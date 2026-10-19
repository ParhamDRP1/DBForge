using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DSNForge.Core
{
    public class RunOptions
    {
        public Dictionary<string, ConnectionProfile> ProfileOverrides { get; set; } = new Dictionary<string, ConnectionProfile>();
        public Dictionary<string, Credentials> Credentials { get; set; } = new Dictionary<string, Credentials>();
        public Dictionary<string, string> ArchivePasswords { get; set; } = new Dictionary<string, string>();
        public Func<string, string> RequestArchivePassword
        {
            get; set;
        }
    }
    public sealed class ProjectRunner
    {
        readonly IUtilityExecutor utility;
        public ProjectRunner(IUtilityExecutor utility = null)
        {
            this.utility = utility;
        }
        public event Action<RunEvent> Progress;
        public Task<RunReport> RunAsync(TransferProject project, RunOptions options, bool preview, CancellationToken token) => Task.Run(() => Run(project, options, preview, token));
        public RunReport Run(TransferProject project, RunOptions options, bool preview, CancellationToken token)
        {
            var p = ProjectStore.Clone(project);
            ProfileStore.Migrate(p);
            var report = new RunReport { Project = p.Name, RunId = "PROJECT-" + Guid.NewGuid().ToString("N").Substring(0, 10) };
            string directory = Path.Combine(p.ReportDirectory, report.RunId);
            Directory.CreateDirectory(directory);
            var secrets = new List<string>();
            object gate = new object();
            Action<RunEvent> emit = e => { lock (gate) { foreach (var secret in secrets.Where(x => !string.IsNullOrEmpty(x))) e.Message = (e.Message ?? "").Replace(secret, "[redacted]"); File.AppendAllText(Path.Combine(directory, "events.jsonl"), ProjectStore.Json(e) + Environment.NewLine); } Progress?.Invoke(e); };
            var watch = Stopwatch.StartNew();
            string phase = "Preparing";
            using (var pulse = new Timer(_ => emit(new RunEvent { Stage = "Heartbeat", Message = phase + " · " + watch.Elapsed.ToString(@"hh\:mm\:ss") }), null, 5000, 5000))
                try
                {
                    foreach (var s in p.Steps.Where(x => x.Enabled))
                    {
                        token.ThrowIfCancellationRequested();
                        phase = s.Name;
                        var profile = p.Profiles.FirstOrDefault(x => x.Id == s.ConnectionProfileId) ?? throw new InvalidDataException("Missing profile for " + s.Name);
                        if (options.ProfileOverrides.TryGetValue(profile.Id, out var replacement))
                            profile = replacement;
                        var c = options.Credentials.TryGetValue(s.ConnectionProfileId, out var supplied) ? supplied : profile.Resolve();
                        lock (gate)
                        {
                            secrets.Add(c.Db2Password);
                            secrets.Add(c.FtpPassword);
                        }
                        string archivePassword = options.ArchivePasswords.TryGetValue(s.Id, out var ap) || options.ArchivePasswords.TryGetValue(s.Name, out ap) ? ap : s.ArchivePassword.Resolve();
                        lock (gate)
                            secrets.Add(archivePassword);
                        string work = Path.Combine(directory, "work-" + Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(work);
                        try
                        {
                            emit(new RunEvent { Stage = "Task", Message = s.Name + " · profile " + profile.Name });
                            if (s.Operation == Operation.Unload && s.UnloadFormat == UnloadFormat.Delimited && string.IsNullOrWhiteSpace(s.UnloadQuery) && string.IsNullOrWhiteSpace(s.ControlStatements))
                                s.UnloadQuery = "SELECT * FROM " + MetadataCatalog.Identifier(s.Table);
                            if (s.Operation == Operation.Unload && s.UnloadFormat == UnloadFormat.Native && string.IsNullOrWhiteSpace(s.UnloadQuery) && s.Columns.Any(x => !x.Included || x.CodePage != 0 || x.Trim != FieldTrim.None))
                                throw new InvalidDataException("Per-column transformations require Delimited CSV or a SELECT query; native UNLOAD preserves the utility record layout.");
                            if (s.Operation == Operation.Load && s.Input != LoadInput.ExistingDataset)
                            {
                                if (ArchiveFiles.IsZip(s.LocalFile))
                                {
                                    while (true)
                                    {
                                        token.ThrowIfCancellationRequested();
                                        string extraction = Path.Combine(work, Guid.NewGuid().ToString("N"));
                                        try
                                        {
                                            s.LocalFile = ArchiveFiles.Extract(s.LocalFile, extraction, archivePassword, token);
                                            break;
                                        }
                                        catch (ICSharpCode.SharpZipLib.Zip.ZipException) when (ArchiveFiles.IsProtected(s.LocalFile) && options.RequestArchivePassword != null)
                                        {
                                            archivePassword = options.RequestArchivePassword("ZIP password missing or incorrect: " + Path.GetFileName(s.LocalFile));
                                            if (archivePassword == null)
                                                throw new OperationCanceledException();
                                            lock (gate)
                                                secrets.Add(archivePassword);
                                        }
                                    }
                                }
                                string metaPath = string.IsNullOrWhiteSpace(s.MetadataFile) ? s.LocalFile + ".dbmeta" : s.MetadataFile;
                                if (File.Exists(metaPath))
                                {
                                    var meta = DbMetadata.Load(metaPath);
                                meta.ApplyDefaults(s);
                                    if (string.IsNullOrWhiteSpace(s.CreateScript))
                                        s.CreateScript = meta.CreateScript;
                                    if (!string.IsNullOrWhiteSpace(meta.LoadCards))
                                    {
                                        s.Input = LoadInput.GeneratedCards;
                                        s.BatchRecords = 0;
                                        if (string.IsNullOrWhiteSpace(s.MetadataLoadCards))
                                            s.MetadataLoadCards = meta.LoadCards;
                                    }
                                }
                            }
                            if (string.IsNullOrWhiteSpace(s.UnloadQuery))
                            {
                                var preflight = new TransferProject { Db2 = profile.Database, Ftp = profile.Ftp, Steps = new List<TransferStep> { s } };
                                var errors = Validation.Check(preflight);
                                if (errors.Count > 0)
                                    throw new InvalidDataException(string.Join(Environment.NewLine, errors));
                            }
                            else
                                MetadataCatalog.SelectQuery(s.UnloadQuery);
                            if (!preview && s.Operation != Operation.Unload)
                                MetadataCatalog.CreateIfMissing(profile.Database, c.Db2Password, s, token);
                            DbMetadata metadata = null;
                            if (s.Operation == Operation.Unload)
                            {
                                if (string.IsNullOrWhiteSpace(s.UnloadQuery))
                                    metadata = MetadataCatalog.Describe(profile.Database, c.Db2Password, s.Table);
                                else
                                {
                                    try
                                    {
                                        metadata = MetadataCatalog.Describe(profile.Database, c.Db2Password, s.Table);
                                    }
                                    catch (Exception ex) { metadata = new DbMetadata { SourceTable = s.Table }; emit(new RunEvent { Stage = "Metadata", Level = "WARN", Message = "Source table metadata unavailable for query output: " + ex.Message }); }
                                }
                                metadata.Query = s.UnloadQuery;
                                metadata.CodePage = s.LocalCodePage;
                                metadata.Delimiter = s.Delimiter;
                                metadata.HasHeader = s.HasHeader;
                            }
                            if (!string.IsNullOrWhiteSpace(s.UnloadQuery))
                            {
                                if (s.Operation != Operation.Unload)
                                    throw new InvalidDataException("SELECT export is supported only for UNLOAD.");
                                if (preview)
                                    MetadataCatalog.QueryLayout(profile.Database, c.Db2Password, s.UnloadQuery);
                                else
                                    report.Batches.Add(ExportQuery(profile.Database, c.Db2Password, s, metadata, emit, token));
                            }
                            else
                            {
                                var single = new TransferProject { Name = p.Name + " / " + s.Name, Db2 = profile.Database, Ftp = profile.Ftp, ReportDirectory = directory, Steps = new List<TransferStep> { s } };
                                var engine = new TransferEngine(utility);
                                engine.Progress += emit;
                                if (preview)
                                    engine.ValidateRemoteAsync(single, c, token).GetAwaiter().GetResult();
                                else
                                {
                                    var result = engine.RunAsync(single, c, token).GetAwaiter().GetResult();
                                    report.Batches.AddRange(result.Batches);
                                    if (!result.Status.StartsWith("Completed"))
                                        throw new InvalidOperationException(result.Error ?? result.Status);
                                    if (metadata != null)
                                    {
                                        metadata.Native = s.UnloadFormat == UnloadFormat.Native;
                                        metadata.NativeRecordBytes = s.NativeRecordBytes;
                                        metadata.NativeInputHasRdw = s.NativeInputHasRdw;
                                        metadata.Dataset = ConnectionProfile.JsonCopy(s.Dataset);
                                        metadata.CodePage = metadata.Native ? s.LocalCodePage : 65001;
                                        metadata.HasHeader = false;
                                        metadata.Delimiter = ",";
                                        metadata.LoadCards = File.ReadAllText(s.LocalFile + ".load.txt");
                                        metadata.UnloadCards = ControlBuilder.Build(s, "{dataset}");
                                        ProjectStore.Save(s.LocalFile + ".dbmeta", metadata);
                                    }
                                }
                            }
                            if (!preview && s.Operation == Operation.Unload && s.CompressOutput)
                            {
                                emit(new RunEvent { Stage = "ZIP", Message = "Compressing " + s.LocalFile });
                                ArchiveFiles.Compress(s.LocalFile, archivePassword, s.Dataset.AllowOverwrite, token);
                            }
                        }
                        finally { if (Directory.Exists(work)) Directory.Delete(work, true); }
                    }
                    report.Status = preview ? "PreviewPassed" : report.Batches.Any(b => b.ReturnCode == 4) ? "CompletedWithWarnings" : "Completed";
                }
                catch (Exception ex)
                {
                    report.Status = ex is OperationCanceledException || token.IsCancellationRequested ? "Cancelled" : "Failed";
                    string error = ex.Message;
                    lock (gate)
                        foreach (var secret in secrets.Where(x => !string.IsNullOrEmpty(x)))
                            error = error.Replace(secret, "[redacted]");
                    report.Error = error;
                    emit(new RunEvent { Stage = phase, Level = "ERROR", Message = error });
                }
                finally { report.Finished = DateTimeOffset.Now; Reports.Save(directory, report); File.WriteAllText(Path.Combine(directory, "summary.txt"), ReportText(report), Encoding.UTF8); }
            return report;
        }
        public static string ReportText(RunReport r)
        {
            double seconds = ((r.Finished ?? DateTimeOffset.Now) - r.Started).TotalSeconds;
            long confirmed = r.Batches.Sum(b => b.ConfirmedRecords ?? 0);
            var loads = r.Batches.Where(b => b.Operation == Operation.Load).ToList();
            string inserted = loads.Count == 0 ? "0" : loads.Any(b => !b.ConfirmedRecords.HasValue) ? "Unknown (inspect SYSPRINT)" : loads.Sum(b => b.ConfirmedRecords.Value).ToString("N0");
            string rejected = r.Batches.Any(b => !b.RejectedRecords.HasValue) ? "Unknown (inspect SYSPRINT)" : r.Batches.Sum(b => b.RejectedRecords ?? 0).ToString("N0");
            return $"Project: {r.Project}\r\nStatus: {r.Status}\r\nStarted: {r.Started}\r\nFinished: {r.Finished}\r\nTotal rows reported/prepared across operations: {r.Batches.Sum(b => b.Records):N0}\r\nConfirmed inserted rows: {inserted}\r\nConfirmed exported rows: {r.Batches.Where(b => b.Operation != Operation.Load).Sum(b => b.ConfirmedRecords ?? 0):N0}\r\nRejected rows: {rejected}\r\nFailed batches: {r.Batches.Count(b => b.ReturnCode > 4 || !string.IsNullOrEmpty(b.Error))}\r\nTotal seconds: {seconds:F2}\r\nConfirmed rows/second: {(seconds > 0 ? confirmed / seconds : 0):F2}\r\nConfirmed rows/minute: {(seconds > 0 ? confirmed * 60 / seconds : 0):F2}\r\nError: {r.Error}\r\n" + string.Join("\r\n", r.Batches.Select(b => $"{b.Step} | {b.Operation} | {b.Status} | RC={b.ReturnCode} | rows={b.Records} | {b.Error}"));
        }
        static BatchResult ExportQuery(Db2Settings settings, string password, TransferStep step, DbMetadata metadata, Action<RunEvent> log, CancellationToken token)
        {
            var clock = Stopwatch.StartNew();
            long count = 0;
            var outputPath = Path.GetFullPath(step.LocalFile);
            if (File.Exists(outputPath) && !step.Dataset.AllowOverwrite)
                throw new IOException("Output exists; enable AllowOverwrite.");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            string temp = outputPath + "." + Guid.NewGuid().ToString("N") + ".partial";
            try
            {
                using (var c = ProviderFactory.Create(settings, password))
                {
                    c.Open();
                    using (var q = c.CreateCommand())
                    {
                        q.CommandText = MetadataCatalog.SelectQuery(step.UnloadQuery);
                        q.CommandTimeout = settings.UtilityTimeoutSeconds;
                        using (token.Register(() => { try { q.Cancel(); } catch { } }))
                        using (var reader = q.ExecuteReader(CommandBehavior.SequentialAccess))
                        using (var output = File.Create(temp, 1048576))
                        {
                            var discovered = MetadataCatalog.ReaderLayout(reader);
                            var columns = step.Columns.Count == 0 ? discovered : step.Columns;
                            var encodings = columns.Select(col => Encoding.GetEncoding(col.CodePage == 0 ? step.LocalCodePage : col.CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)).ToArray();
                            if (!columns.Any(x => x.Included))
                                throw new InvalidDataException("Select at least one output column.");
                            bool mixed = columns.Any(x => x.CodePage != 0 && x.CodePage != step.LocalCodePage);
                            if (mixed && (step.Delimiter.Any(x => x > 127) || columns.Select(x => x.CodePage == 0 ? step.LocalCodePage : x.CodePage).Any(cp => Encoding.GetEncoding(cp).GetString(new byte[] { 34, 44, 13, 10 }) != "\",\r\n")))
                                throw new InvalidDataException("Mixed CSV encodings require ASCII-compatible code pages and delimiters.");
                            if (columns.Count != reader.FieldCount || columns.Where((x, i) => !x.Name.Equals(reader.GetName(i), StringComparison.OrdinalIgnoreCase)).Any())
                                throw new InvalidDataException("Query columns changed; refresh CSV layout.");
                            metadata.Columns = ConnectionProfile.JsonCopy(columns.Where(x => x.Included).ToList());
                            string existing = metadata.CreateScript ?? "";
                            int tableAt = existing.IndexOf("CREATE TABLE ", StringComparison.OrdinalIgnoreCase);
                            string location = "";
                            if (tableAt >= 0)
                            {
                                var match = System.Text.RegularExpressions.Regex.Match(existing.Substring(tableAt), @"\) IN ([\w#$@]+\.[\w#$@]+);\s*$");
                                if (match.Success)
                                    location = " IN " + match.Groups[1].Value;
                            }
                            metadata.CreateScript = (tableAt > 0 ? existing.Substring(0, tableAt) : "") + MetadataCatalog.CreateTable(step.Table, metadata.Columns, location);
                            metadata.NullToken = "\\N";
                            metadata.Notes.Add("Query result DDL reflects output columns; choose the destination tablespace in the editable script.");
                            if (step.HasHeader)
                                WriteCsv(output, columns.Select(x => x.Name).ToArray(), columns, step, encodings);
                            while (reader.Read())
                            {
                                token.ThrowIfCancellationRequested();
                                var fields = new string[reader.FieldCount];
                                for (int i = 0; i < fields.Length; i++)
                                    fields[i] = reader.IsDBNull(i) ? "\\N" : CsvValue(reader[i], columns[i].SqlType).Replace("\\", "\\\\");
                                WriteCsv(output, fields, columns, step, encodings);
                                count++;
                                if (count % 10000 == 0)
                                    log(new RunEvent { Stage = "Query export", Records = count, Message = count.ToString("N0") + " rows · " + (count / clock.Elapsed.TotalSeconds).ToString("F0") + " rows/s" });
                            }
                        }
                    }
                }
                if (File.Exists(outputPath))
                    File.Replace(temp, outputPath, null);
                else
                    File.Move(temp, outputPath);
                ProjectStore.Save(outputPath + ".dbmeta", metadata);
                return new BatchResult { Step = step.Name, Operation = Operation.Unload, Status = "Succeeded", ReturnCode = 0, Records = count, ConfirmedRecords = count, RejectedRecords = 0, Bytes = new FileInfo(outputPath).Length, TransferSeconds = clock.Elapsed.TotalSeconds };
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        public static string Trim(string value, FieldTrim trim) => trim == FieldTrim.Both ? value.Trim() : trim == FieldTrim.Start ? value.TrimStart() : trim == FieldTrim.End ? value.TrimEnd() : value;
        static string CsvValue(object value, string sqlType)
        {
            if (value is DateTime date)
                return date.ToString(sqlType == "DATE" ? "yyyy-MM-dd" : sqlType == "TIME" ? "HH.mm.ss" : "yyyy-MM-dd-HH.mm.ss.ffffff", System.Globalization.CultureInfo.InvariantCulture);
            if (value is byte[])
                throw new InvalidDataException("Binary values require native UNLOAD; CSV conversion is not guessed.");
            return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
        }
        static void WriteCsv(Stream output, string[] fields, IList<TextColumn> columns, TransferStep step, Encoding[] encodings)
        {
            bool first = true;
            for (int i = 0; i < fields.Length; i++)
            {
                var col = columns[i];
                if (!col.Included)
                    continue;
                var encoding = encodings[i];
                var bytes = encoding.GetBytes((first ? "" : step.Delimiter) + "\"" + Trim(fields[i], col.Trim).Replace("\"", "\"\"") + "\"");
                output.Write(bytes, 0, bytes.Length);
                first = false;
            }
            byte[] newline = Encoding.GetEncoding(step.LocalCodePage).GetBytes("\r\n");
            output.Write(newline, 0, newline.Length);
        }
    }
}
