using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
namespace DSNForge.Core
{
    public class RunEvent
    {
        public DateTimeOffset Time { get; set; } = DateTimeOffset.Now; public string Level { get; set; } = "INFO"; public string Stage { get; set; } = ""; public string Message { get; set; } = ""; public long Bytes
        {
            get; set;
        }
        public long TotalBytes
        {
            get; set;
        }
        public long Records
        {
            get; set;
        }
        public double ElapsedSeconds
        {
            get; set;
        }
        public double BytesPerSecond
        {
            get; set;
        }
        public double? RemainingSeconds
        {
            get; set;
        }
    }
    public class BatchResult
    {
        public Operation Operation
        {
            get; set;
        }
        public long? ConfirmedRecords
        {
            get; set;
        }
        public long? RejectedRecords
        {
            get; set;
        }
        public string Step
        {
            get; set;
        }
        public string Dataset
        {
            get; set;
        }
        public string UtilityId
        {
            get; set;
        }
        public string Status
        {
            get; set;
        }
        public int ReturnCode { get; set; } = -1; public long Records
        {
            get; set;
        }
        public long Bytes
        {
            get; set;
        }
        public double TransferSeconds
        {
            get; set;
        }
        public double UtilitySeconds
        {
            get; set;
        }
        public double BytesPerSecond => TransferSeconds > 0 ? Bytes / TransferSeconds : 0; public double RecordsPerUtilitySecond => UtilitySeconds > 0 ? Records / UtilitySeconds : 0; public string ControlFile
        {
            get; set;
        }
        public string Error
        {
            get; set;
        }
    }
    public class RunReport
    {
        public string Project
        {
            get; set;
        }
        public string RunId
        {
            get; set;
        }
        public DateTimeOffset Started { get; set; } = DateTimeOffset.Now; public DateTimeOffset? Finished
        {
            get; set;
        }
        public string Status { get; set; } = "Running"; public List<BatchResult> Batches { get; set; } = new List<BatchResult>(); public string Error
        {
            get; set;
        }
    }
    public static class Reports
    {
        public static void Save(string dir, RunReport report)
        {
            Directory.CreateDirectory(dir);
            ProjectStore.Save(Path.Combine(dir, "report.json"), report);
            var b = new StringBuilder("step,dataset,utility_id,status,return_code,records,bytes,transfer_seconds,utility_seconds,error\r\n");
            foreach (var x in report.Batches)
                b.AppendLine(string.Join(",", new[] { x.Step, x.Dataset, x.UtilityId, x.Status, x.ReturnCode.ToString(), x.Records.ToString(), x.Bytes.ToString(), x.TransferSeconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture), x.UtilitySeconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture), x.Error }.Select(Csv)));
            File.WriteAllText(Path.Combine(dir, "report.csv"), b.ToString(), new UTF8Encoding(true));
            var html = "<meta charset='utf-8'><title>DSNForge report</title><style>body{font:16px Segoe UI;margin:40px;background:#f4f7fb}table{border-collapse:collapse;background:white}td,th{padding:10px;border:1px solid #ccd5df}h1{color:#124c73}</style><h1>DSNForge · " + H(report.Project) + "</h1><p>" + H(report.Status) + " | " + report.Started + " → " + report.Finished + "</p><p>" + H(report.Error) + "</p><table><tr><th>Task</th><th>Dataset</th><th>Status</th><th>RC</th><th>Rows</th><th>Bytes</th><th>FTP sec</th><th>Utility sec</th></tr>";
            foreach (var x in report.Batches)
                html += "<tr><td>" + H(x.Step) + "</td><td>" + H(x.Dataset) + "</td><td>" + H(x.Status) + "</td><td>" + x.ReturnCode + "</td><td>" + x.Records + "</td><td>" + x.Bytes + "</td><td>" + x.TransferSeconds.ToString("F2") + "</td><td>" + x.UtilitySeconds.ToString("F2") + "</td></tr>";
            File.WriteAllText(Path.Combine(dir, "report.html"), html + "</table>", Encoding.UTF8);
        }
        static string Csv(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\""; static string H(string s) => System.Net.WebUtility.HtmlEncode(s ?? "");
    }
    public static class ControlBuilder
    {
        public static string Dataset(TransferStep s, string run, int batch) => s.Dataset.Name.Replace("{run}", run).Replace("{batch}", "B" + batch.ToString("D6"));
        public static string Punch(string data)
        {
            if (data == "{dataset}") return "{punch}";
            if (string.IsNullOrEmpty(data))
                return "";
            var i = data.LastIndexOf('.');
            var last = data.Substring(i + 1);
            return data.Substring(0, i + 1) + (last.Length == 0 ? "P" : (last.StartsWith("P", StringComparison.OrdinalIgnoreCase) ? "Q" : "P") + last.Substring(1));
        }
        public static string Normalize(string cards) => Regex.Replace(cards, @"\r\n?|\n", " ").Trim();
        public static bool TextField(TextColumn c) => c.SqlType.StartsWith("CHAR", StringComparison.OrdinalIgnoreCase) || c.SqlType.StartsWith("VARCHAR", StringComparison.OrdinalIgnoreCase);
        public static int FieldBytes(TextColumn c) => c.MaxUtf8Bytes + (TextField(c) ? 2 : 0);
        public static int RecordBytes(TransferStep s) => s.Columns.Where(x => x.Included).Sum(x => checked(FieldBytes(x) + (string.IsNullOrEmpty(s.CsvNullToken) ? 0 : 1)));
        static string FieldType(TextColumn c)
        {
            if (TextField(c))
                return "VARCHAR MIXED";
            string type = c.SqlType.Split('(')[0].Trim().ToUpperInvariant();
            if (type == "SMALLINT" || type == "BIGINT")
                type = "INTEGER";
            if (type == "REAL" || type == "DOUBLE")
                type = "FLOAT";
            if (!new[] { "INTEGER", "DECIMAL", "FLOAT", "DATE", "TIME", "TIMESTAMP" }.Contains(type))
                throw new InvalidDataException("Use native cards for unsupported CSV type " + c.SqlType);
            return type + " EXTERNAL(" + c.MaxUtf8Bytes + ")";
        }
        public static string Build(TransferStep s, string dataset, bool unload = false, int batch = 1)
        {
            var punch = Punch(dataset);
            if (!string.IsNullOrWhiteSpace(s.ControlStatements))
                return Normalize(s.ControlStatements.Replace("{dataset}", dataset).Replace("{punch}", punch).Replace("{table}", s.Table).Replace("{target}", s.TargetTable));
            if (unload || s.Operation != Operation.Load)
            {
                string allocation = " UNIT " + s.Dataset.Unit + " SPACE(" + s.Dataset.PrimarySpace + "," + s.Dataset.SecondarySpace + ") " + s.Dataset.SpaceUnit + " " + s.Dataset.TemplateOptions;
                if (!s.Dataset.AutoAllocate)
                    allocation = "";
                string disposition = s.Dataset.AutoAllocate ? "NEW,CATLG,DELETE" : "SHR,KEEP,KEEP";
                return Normalize("TEMPLATE DFOUT DSN '" + dataset + "' DISP(" + disposition + ")" + allocation + " TEMPLATE DFPUNCH DSN '" + punch + "' DISP(" + disposition + ")" + allocation + " UNLOAD DATA UNLDDN DFOUT PUNCHDDN DFPUNCH SHRLEVEL " + s.ShrLevel + " " + (s.UnloadFormat == UnloadFormat.Delimited ? "FORMAT DELIMITED UNICODE " : "") + s.UtilityOptions + " FROM TABLE " + s.Table);
            }
            string fields = "";
            if (s.Input == LoadInput.CsvToUtf8Fixed)
            {
                int pos = 1;
                var f = new List<string>();
                foreach (var col in s.Columns.Where(x => x.Included))
                {
                    int indicator = pos + FieldBytes(col);
                    f.Add(col.Name + " POSITION(" + pos + ") " + FieldType(col) + (string.IsNullOrEmpty(s.CsvNullToken) ? "" : " NULLIF(" + indicator + ":" + indicator + ")=X'FF'"));
                    pos += FieldBytes(col) + (string.IsNullOrEmpty(s.CsvNullToken) ? 0 : 1);
                }
                fields = " (" + string.Join(",", f) + ")";
            }
            if (s.Input == LoadInput.GeneratedCards)
                return Rebind(string.IsNullOrWhiteSpace(s.MetadataLoadCards) ? File.ReadAllText(s.GeneratedCardsFile) : s.MetadataLoadCards, s, dataset, batch);
            return Normalize("TEMPLATE DFIN DSN '" + dataset + "' DISP(SHR,KEEP,KEEP) LOAD DATA INDDN DFIN " + (s.Disposition == LoadDisposition.Replace && batch == 1 ? "REPLACE" : "RESUME YES") + " LOG " + (s.LogWrites ? "YES" : "NO") + " " + (s.Input == LoadInput.CsvToUtf8Fixed ? "UNICODE " : "") + s.UtilityOptions + " INTO TABLE " + s.Table + fields);
        }
        public static string Rebind(string cards, TransferStep s, string dataset, int batch = 1)
        {
            cards = Normalize(cards);
            cards = Regex.Replace(cards, @"\bLOG\s+(YES|NO)\b", "LOG " + (s.LogWrites ? "YES" : "NO"), RegexOptions.IgnoreCase);
            if (Regex.Matches(cards, @"\bINTO\s+TABLE\b", RegexOptions.IgnoreCase).Count != 1)
                throw new InvalidDataException("Generated cards must contain exactly one INTO TABLE");
            cards = Regex.Replace(cards, @"\bINTO\s+TABLE\s+(?:""[^""]+""|[\w#$@]+)\s*\.\s*(?:""[^""]+""|[\w#$@]+)", "INTO TABLE " + s.Table, RegexOptions.IgnoreCase);
            cards = Regex.Replace(cards, @"\bINDDN\s+\w+", "INDDN DFIN", RegexOptions.IgnoreCase);
            cards = Regex.Replace(cards, @"\bRESUME\s+(YES|NO)\b|\bREPLACE\b", s.Disposition == LoadDisposition.Replace && batch == 1 ? "REPLACE" : "RESUME YES", RegexOptions.IgnoreCase);
            return "TEMPLATE DFIN DSN '" + dataset + "' DISP(SHR,KEEP,KEEP) " + cards;
        }
    }
    public static class Validation
    {
        public static List<string> Check(TransferProject p)
        {
            var e = new List<string>();
            if (p.Db2 == null || p.Ftp == null || p.Steps == null)
            {
                e.Add("Project is missing settings");
                return e;
            }
            if (string.IsNullOrWhiteSpace(p.Db2.Host) || string.IsNullOrWhiteSpace(p.Db2.User))
                e.Add("DB2 host/user required");
            if (p.Ftp.BufferBytes < 4096 || p.Ftp.BufferBytes > 16777216)
                e.Add("FTP buffer must be 4KB–16MB");
            if (p.Ftp.TimeoutSeconds < 1 || p.Db2.UtilityTimeoutSeconds < 1)
                e.Add("Timeouts must be positive");
            if (p.Ftp.Retries < 0 || p.Ftp.Retries > 20)
                e.Add("FTP retries must be 0–20");
            if (p.Steps.Count(x => x.Enabled) == 0)
                e.Add("Select at least one task");
            foreach (var s in p.Steps.Where(x => x.Enabled))
            {
                string prefix = s.Name + ": ";
                if (!Regex.IsMatch(s.Table ?? "", @"^[A-Za-z][\w#$@]*\.[A-Za-z][\w#$@]*$"))
                    e.Add(prefix + "Use SCHEMA.TABLE identifiers");
                if (s.Operation == Operation.Transfer && !Regex.IsMatch(s.TargetTable ?? "", @"^[A-Za-z][\w#$@]*\.[A-Za-z][\w#$@]*$"))
                    e.Add(prefix + "Transfer target required");
                string ds = ControlBuilder.Dataset(s, "R1234567", 1);
                if (!ValidDataset(ds) || !ValidDataset(ControlBuilder.Punch(ds)))
                    e.Add(prefix + "Invalid dataset name (1–8 chars per qualifier; max 44)");
                if (s.NativeInputHasRdw && s.NativeRecordBytes < 1)
                    e.Add(prefix + "RDW input needs a fixed payload width");
                if (s.Operation == Operation.Load && s.Input == LoadInput.GeneratedCards && p.Ftp.Representation != FtpRepresentation.Binary)
                    e.Add(prefix + "Native generated LOAD cards require binary FTP");
                if (s.Operation == Operation.Load && s.BatchRecords > 0 && !string.IsNullOrWhiteSpace(s.ControlStatements))
                    e.Add(prefix + "Custom control statements require BatchRecords=0; use GeneratedCards for native batching");
                if (s.BatchRecords < 0)
                    e.Add(prefix + "Batch records cannot be negative");
                if (s.Dataset.PrimarySpace < 1 || s.Dataset.SecondarySpace < 1)
                    e.Add(prefix + "Allocation space must be positive");
                if (s.Dataset.RecordLength < 1 || s.Dataset.RecordLength > 32760)
                    e.Add(prefix + "LRECL out of range");
                if (!new[] { "FB", "VB", "F", "V", "U" }.Contains(s.Dataset.RecordFormat))
                    e.Add(prefix + "Invalid RECFM");
                if (!new[] { "CYL", "TRK" }.Contains(s.Dataset.SpaceUnit))
                    e.Add(prefix + "Space unit must be CYL or TRK");
                if (s.Operation == Operation.Load && s.Input != LoadInput.ExistingDataset && !File.Exists(s.LocalFile))
                    e.Add(prefix + "Input file does not exist");
                if (s.Operation == Operation.Unload && string.IsNullOrWhiteSpace(s.LocalFile))
                    e.Add(prefix + "Output file required");
                if (s.Operation == Operation.Unload && File.Exists(s.LocalFile) && !s.Dataset.AllowOverwrite)
                    e.Add(prefix + "Output file already exists");
                if (s.Operation == Operation.Load && s.Input == LoadInput.CsvToUtf8Fixed)
                {
                    if (!s.Columns.Any(x => x.Included))
                        e.Add(prefix + "CSV column layout required");
                    if (s.Columns.Any(x => x.MaxUtf8Bytes < 1 || x.MaxUtf8Bytes > 32704 || !Regex.IsMatch(x.Name ?? "", @"^[A-Za-z][\w#$@]*$")))
                        e.Add(prefix + "Invalid column layout");
                    if (s.Columns.Select(x => x.Name.ToUpperInvariant()).Distinct().Count() != s.Columns.Count)
                        e.Add(prefix + "Duplicate columns");
                    if (ControlBuilder.RecordBytes(s) > 32760)
                        e.Add(prefix + "Fixed record exceeds 32760 bytes");
                    if (p.Ftp.Representation != FtpRepresentation.Binary)
                        e.Add(prefix + "UTF-8 packed CSV requires binary FTP");
                    if (s.BatchRecords > 0 && !s.Dataset.Name.Contains("{batch}"))
                        e.Add(prefix + "Batching requires {batch} in dataset name");
                }
                if (s.Operation == Operation.Load && s.Input != LoadInput.CsvToUtf8Fixed && s.BatchRecords > 0 && s.Input != LoadInput.ExistingDataset && !s.Dataset.Name.Contains("{batch}"))
                    e.Add(prefix + "Batching requires {batch} in dataset name");
                if (s.Operation == Operation.Load && s.Input == LoadInput.NativeFile && s.BatchRecords > 0 && s.NativeRecordBytes < 1)
                    e.Add(prefix + "Native batching needs a fixed record width; otherwise set BatchRecords=0");
                if (s.Input == LoadInput.GeneratedCards && string.IsNullOrWhiteSpace(s.MetadataLoadCards) && !File.Exists(s.GeneratedCardsFile))
                    e.Add(prefix + "Generated LOAD cards file missing");
                if (s.Operation == Operation.Load && s.Input == LoadInput.NativeFile && string.IsNullOrWhiteSpace(s.ControlStatements))
                    e.Add(prefix + "Native file requires explicit LOAD cards; no layout is guessed");
                try
                {
                    Encoding.GetEncoding(s.LocalCodePage);
                    var cards = ControlBuilder.Build(s, ds);
                    if (Encoding.UTF8.GetByteCount(cards) > 32704)
                        e.Add(prefix + "DSNUTILU control statement exceeds 32704 UTF-8 bytes");
                }
                catch (Exception ex) { e.Add(prefix + ex.Message); }
            }
            return e;
        }
        public static bool ValidDataset(string name) => name != null && name.Length <= 44 && name.Split('.').All(x => Regex.IsMatch(x, @"^[A-Za-z#$@][A-Za-z0-9#$@-]{0,7}$"));
    }
}
