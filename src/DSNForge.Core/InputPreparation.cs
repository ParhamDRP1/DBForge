using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.VisualBasic.FileIO;
namespace DSNForge.Core
{
    public class PreparedBatch : IDisposable
    {
        public string File
        {
            get; set;
        }
        public long Records
        {
            get; set;
        }
        public int RecordBytes
        {
            get; set;
        }
        public bool Temporary
        {
            get; set;
        }
        public void Dispose()
        {
            if (Temporary && System.IO.File.Exists(File))
                System.IO.File.Delete(File);
        }
    }
    public static class InputPreparation
    {
        static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);
        public static IEnumerable<PreparedBatch> Read(TransferStep s, string work, Action<RunEvent> log, CancellationToken token)
        {
            if (s.Input != LoadInput.CsvToUtf8Fixed)
            {
                if (s.NativeInputHasRdw)
                {
                    foreach (var b in StripRdw(s, work, token))
                        yield return b;
                    yield break;
                }
                if (s.BatchRecords == 0 || s.NativeRecordBytes < 1)
                {
                    long len = new FileInfo(s.LocalFile).Length;
                    if (s.NativeRecordBytes > 0 && len % s.NativeRecordBytes != 0)
                        throw new InvalidDataException("Native file length is not a multiple of record width");
                    yield return new PreparedBatch { File = s.LocalFile, Records = s.NativeRecordBytes > 0 ? len / s.NativeRecordBytes : 0, RecordBytes = s.NativeRecordBytes };
                    yield break;
                }
                using (var input = System.IO.File.OpenRead(s.LocalFile))
                {
                    int index = 0;
                    while (input.Position < input.Length)
                    {
                        token.ThrowIfCancellationRequested();
                        string file = Path.Combine(work, "native-" + (++index) + ".bin");
                        long bytes = Math.Min((long)s.BatchRecords * s.NativeRecordBytes, input.Length - input.Position);
                        if (bytes % s.NativeRecordBytes != 0)
                            throw new InvalidDataException("Truncated native record");
                        using (var output = System.IO.File.Create(file))
                        {
                            var buffer = new byte[1048576];
                            long left = bytes;
                            while (left > 0)
                            {
                                token.ThrowIfCancellationRequested();
                                int n = input.Read(buffer, 0, (int)Math.Min(left, buffer.Length));
                                if (n == 0)
                                    throw new EndOfStreamException();
                                output.Write(buffer, 0, n);
                                left -= n;
                            }
                        }
                        yield return new PreparedBatch { File = file, Records = bytes / s.NativeRecordBytes, RecordBytes = s.NativeRecordBytes, Temporary = true };
                    }
                }
                yield break;
            }
            bool mixed = s.Columns.Any(c => c.CodePage != 0 && c.CodePage != s.LocalCodePage);
            if (mixed && s.Columns.Select(c => c.CodePage == 0 ? s.LocalCodePage : c.CodePage).Any(cp => Encoding.GetEncoding(cp).GetString(new byte[] { 34, 44, 13, 10 }) != "\",\r\n"))
                throw new InvalidDataException("Mixed column encodings require ASCII-compatible encodings");
            var encoding = Encoding.GetEncoding(mixed ? 28591 : s.LocalCodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            using (var input = OpenCsv(s.LocalFile, mixed))
            using (var parser = new TextFieldParser(input, encoding, !mixed))
            {
                parser.TextFieldType = FieldType.Delimited;
                parser.SetDelimiters(s.Delimiter);
                parser.HasFieldsEnclosedInQuotes = true;
                parser.TrimWhiteSpace = false;
                if (s.HasHeader && !parser.EndOfData)
                    parser.ReadFields();
                int batch = 0;
                long total = 0;
                while (!parser.EndOfData)
                {
                    token.ThrowIfCancellationRequested();
                    string path = Path.Combine(work, "csv-" + (++batch) + ".bin");
                    long count = 0;
                    int size = ControlBuilder.RecordBytes(s);
                    using (var output = System.IO.File.Create(path))
                    {
                        while (!parser.EndOfData && (s.BatchRecords == 0 || count < s.BatchRecords))
                        {
                            token.ThrowIfCancellationRequested();
                            string[] values = parser.ReadFields();
                            if (values.Length != s.Columns.Count)
                                throw new InvalidDataException("CSV column count mismatch at record " + (total + 1));
                            if (mixed)
                                for (int i = 0; i < values.Length; i++)
                                    values[i] = Encoding.GetEncoding(s.Columns[i].CodePage == 0 ? s.LocalCodePage : s.Columns[i].CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback).GetString(encoding.GetBytes(values[i]));
                            byte[] record = Pack(s, values);
                            output.Write(record, 0, record.Length);
                            total++;
                            count++;
                            if (total % 10000 == 0)
                                log?.Invoke(new RunEvent { Stage = "Prepare", Records = total, Message = "Prepared " + total.ToString("N0") + " records" });
                        }
                    }
                    yield return new PreparedBatch { File = path, Records = count, RecordBytes = size, Temporary = true };
                }
            }
        }
        static IEnumerable<PreparedBatch> StripRdw(TransferStep s, string work, CancellationToken token)
        {
            if (s.NativeRecordBytes < 1)
                throw new InvalidDataException("RDW import requires a fixed payload width");
            using (var input = File.OpenRead(s.LocalFile))
            {
                int batch = 0;
                while (input.Position < input.Length)
                {
                    string path = Path.Combine(work, "rdw-" + (++batch) + ".bin");
                    long count = 0;
                    using (var output = File.Create(path))
                    {
                        while (input.Position < input.Length && (s.BatchRecords == 0 || count < s.BatchRecords))
                        {
                            token.ThrowIfCancellationRequested();
                            int a = input.ReadByte(), b = input.ReadByte(), c = input.ReadByte(), d = input.ReadByte();
                            if (d < 0 || c != 0 || d != 0)
                                throw new InvalidDataException("Invalid or spanned RDW record");
                            int length = (a << 8) + b - 4;
                            if (length != s.NativeRecordBytes)
                                throw new InvalidDataException("Variable record length differs from NativeRecordBytes; use original dataset for a server-side Transfer");
                            var record = new byte[length];
                            int pos = 0;
                            while (pos < length)
                            {
                                int n = input.Read(record, pos, length - pos);
                                if (n == 0)
                                    throw new EndOfStreamException("Truncated RDW payload");
                                pos += n;
                            }
                            output.Write(record, 0, record.Length);
                            count++;
                        }
                    }
                    yield return new PreparedBatch { File = path, Records = count, RecordBytes = s.NativeRecordBytes, Temporary = true };
                }
            }
        }
        static Stream OpenCsv(string path, bool mixed)
        {
            var stream = File.OpenRead(path);
            if (mixed)
            {
                var bom = new byte[3];
                int n = stream.Read(bom, 0, 3);
                stream.Position = n == 3 && bom[0] == 239 && bom[1] == 187 && bom[2] == 191 ? 3 : 0;
            }
            return stream;
        }
        public static byte[] Pack(TransferStep s, string[] fields)
        {
            if (fields.Length != s.Columns.Count)
                throw new InvalidDataException("Column count mismatch");
            var record = new byte[ControlBuilder.RecordBytes(s)];
            int offset = 0;
            for (int i = 0; i < fields.Length; i++)
            {
                if (!s.Columns[i].Included)
                    continue;
                bool isNull = !string.IsNullOrEmpty(s.CsvNullToken) && fields[i] == s.CsvNullToken;
                if (isNull && !s.Columns[i].Nullable)
                    throw new InvalidDataException("NULL in non-nullable column " + s.Columns[i].Name);
                string text = isNull ? "" : ProjectRunner.Trim(fields[i], s.Columns[i].Trim);
                if (!isNull && !string.IsNullOrEmpty(s.CsvNullToken))
                    text = text.Replace("\\\\", "\\");
                byte[] value = StrictUtf8.GetBytes(text);
                if (value.Length > s.Columns[i].MaxUtf8Bytes)
                    throw new InvalidDataException("Value exceeds UTF-8 byte width for " + s.Columns[i].Name);
                int prefix = ControlBuilder.TextField(s.Columns[i]) ? 2 : 0;
                if (prefix == 2)
                {
                    record[offset] = (byte)(value.Length >> 8);
                    record[offset + 1] = (byte)value.Length;
                }
                else
                    for (int j = 0; j < s.Columns[i].MaxUtf8Bytes; j++)
                        record[offset + j] = 32;
                Buffer.BlockCopy(value, 0, record, offset + prefix, value.Length);
                offset += ControlBuilder.FieldBytes(s.Columns[i]);
                if (!string.IsNullOrEmpty(s.CsvNullToken))
                    record[offset++] = isNull ? (byte)255 : (byte)0;
            }
            return record;
        }
    }
}
