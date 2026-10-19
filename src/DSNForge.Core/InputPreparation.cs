using System;
using System.Collections.Generic;
using System.IO;
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
            var encoding = Encoding.GetEncoding(s.LocalCodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            using (var parser = new TextFieldParser(s.LocalFile, encoding, true))
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
        public static byte[] Pack(TransferStep s, string[] fields)
        {
            if (fields.Length != s.Columns.Count)
                throw new InvalidDataException("Column count mismatch");
            var record = new byte[ControlBuilder.RecordBytes(s)];
            int offset = 0;
            for (int i = 0; i < fields.Length; i++)
            {
                byte[] value = new UTF8Encoding(false, true).GetBytes(fields[i]);
                if (value.Length > s.Columns[i].MaxUtf8Bytes)
                    throw new InvalidDataException("Value exceeds UTF-8 byte width for " + s.Columns[i].Name);
                record[offset] = (byte)(value.Length >> 8);
                record[offset + 1] = (byte)value.Length;
                Buffer.BlockCopy(value, 0, record, offset + 2, value.Length);
                offset += s.Columns[i].MaxUtf8Bytes + 2;
            }
            return record;
        }
    }
}
