using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using DSNForge.Core;
using ICSharpCode.SharpZipLib.Zip;

static class WizardTests
{
    sealed class RecordingUtility : IUtilityExecutor
    {
        public List<string> Calls = new List<string>();
        public UtilityResult Execute(Db2Settings settings, string password, string id, string cards, bool preview, Action<RunEvent> log, CancellationToken token)
        {
            Calls.Add(settings.Host + ":" + password);
            return new UtilityResult { ReturnCode = 0, Records = 4, RecordCountKnown = true };
        }
    }
    public static int Run(string directory)
    {
        int count = 0;
        Action<bool, string> check = (ok, text) => { if (!ok) throw new Exception(text); count++; };
        var secret = new SecretSetting { Mode = PasswordMode.Manual };
        secret.Store("Synthetic-test-secret-123");
        check(secret.Resolve() == "Synthetic-test-secret-123", "DPAPI roundtrip");
        check(!ProjectStore.Json(secret).Contains("Synthetic-test-secret-123"), "No cleartext password in serialized secret");
        var profile = new ConnectionProfile { Name = "Test", DatabaseSecret = secret };
        var clone = profile.Clone("Clone");
        clone.Database.Host = "different.invalid";
        check(clone.Id != profile.Id && clone.Database.Host != profile.Database.Host, "Clone has independent id and settings");
        var project = new TransferProject { Profiles = new List<ConnectionProfile> { profile }, Steps = new List<TransferStep> { new TransferStep { ConnectionProfileId = profile.Id } } };
        string path = Path.Combine(directory, "profiles-project.json");
        ProjectStore.Save(path, project);
        check(ProjectStore.Load(path).Profiles[0].DatabaseSecret.Resolve() == "Synthetic-test-secret-123", "Encrypted project restore");
        profile.Database.ConnectionTemplate = "Provider=DB2OLEDB.1;Password=plaintext";
        bool failed = false;
        try
        {
            ProjectStore.Save(path, project);
        }
        catch (InvalidDataException) { failed = true; }
        check(failed, "Connection template cannot save a plaintext password");
        profile.Database.ConnectionTemplate = "";
        check(MetadataCatalog.SelectQuery("SELECT 'DELETE' AS A FROM SYSIBM.SYSDUMMY1;").EndsWith("WITH UR"), "Safe query accepts literal and appends isolation");
        failed = false;
        try
        {
            MetadataCatalog.SelectQuery("SELECT * FROM FINAL TABLE (DELETE FROM APP.T)");
        }
        catch (InvalidDataException) { failed = true; }
        check(failed, "Data changing SELECT rejected");
        check(MetadataCatalog.SplitStatements("CREATE TABLE APP.T (A VARCHAR(20) DEFAULT 'a;b');CREATE TABLE APP.U (A INT);").Count() == 2, "DDL splitter preserves semicolons in literals");
        var step = new TransferStep { Columns = new List<TextColumn> { new TextColumn { Name = "A", MaxUtf8Bytes = 20, Trim = FieldTrim.Both }, new TextColumn { Name = "B", Included = false } } };
        var packed = InputPreparation.Pack(step, new[] { " value ", "skip" });
        check(packed.Length == 22 && packed[1] == 5, "Selection and Trim affect bytes and width");
        step.Columns[0].Nullable = true;
        step.CsvNullToken = "\\N";
        packed = InputPreparation.Pack(step, new[] { "\\N", "skip" });
        check(packed[22] == 255 && packed[1] == 0, "NULL indicator distinct from empty");
        packed = InputPreparation.Pack(step, new[] { "", "skip" });
        check(packed[22] == 0, "Empty string is not NULL");
        packed = InputPreparation.Pack(step, new[] { "\\\\N", "skip" });
        check(Encoding.UTF8.GetString(packed, 2, 2) == "\\N" && packed[22] == 0, "Literal NULL token escapes roundtrip");
        check(ControlBuilder.Build(step, "MYUSER.TEST.B000001").Contains("NULLIF(23:23)=X'FF'"), "LOAD cards match NULL indicator position");
        step.Columns[0].SqlType = "DECIMAL(10,2)";
        packed = InputPreparation.Pack(step, new[] { "12.50", "skip" });
        check(Encoding.UTF8.GetString(packed, 0, 5) == "12.50" && ControlBuilder.Build(step, "MYUSER.TEST.B000001").Contains("DECIMAL EXTERNAL(20)"), "Typed numeric field uses external representation");
        string csv = Path.Combine(directory, "archive.csv");
        File.WriteAllText(csv, "001,فارسی\r\n", Encoding.UTF8);
        var meta = new DbMetadata { SourceTable = "APP.T", Columns = step.Columns, CreateScript = "CREATE TABLE APP.T (A VARCHAR(20));", NullToken = "\\N", LoadCards = "LOAD DATA INDDN SYSREC RESUME YES INTO TABLE APP.T (A POSITION(1) VARCHAR)" };
        ProjectStore.Save(csv + ".dbmeta", meta);
        ArchiveFiles.Compress(csv, "synthetic-zip-password", false, CancellationToken.None);
        check(ArchiveFiles.IsZip(csv + ".zip") && ArchiveFiles.IsProtected(csv + ".zip"), "AES ZIP detected by content");
        failed = false;
        try
        {
            ArchiveFiles.Extract(csv + ".zip", Path.Combine(directory, "wrong-key"), "wrong", CancellationToken.None);
        }
        catch (ZipException) { failed = true; }
        check(failed, "Wrong ZIP password detected");
        string unpacked = ArchiveFiles.Extract(csv + ".zip", Path.Combine(directory, "correct-key"), "synthetic-zip-password", CancellationToken.None);
        check(File.ReadAllBytes(csv).SequenceEqual(File.ReadAllBytes(unpacked)), "Encrypted ZIP preserves bytes");
        check(DbMetadata.Load(unpacked + ".dbmeta").LoadCards == meta.LoadCards, "ZIP includes metadata and cards");
        string previewDir = Path.Combine(directory, "metadata-only");
        string previewMeta = ArchiveFiles.ExtractMetadata(csv + ".zip", previewDir, "synthetic-zip-password");
        check(DbMetadata.Load(previewMeta).SourceTable == "APP.T" && Directory.GetFiles(previewDir).Length == 1, "Wizard previews metadata without extracting the data file");
        var imported = new TransferStep();
        DbMetadata.Load(unpacked + ".dbmeta").Apply(imported);
        check(imported.Input == LoadInput.GeneratedCards && imported.CsvNullToken == "\\N", "Metadata restores LOAD mode and NULL policy");
        using (var z = new ZipOutputStream(File.Create(Path.Combine(directory, "traversal.zip"))))
        {
            z.PutNextEntry(new ZipEntry("../escape.csv"));
            z.WriteByte(65);
            z.CloseEntry();
        }
        failed = false;
        try
        {
            ArchiveFiles.Extract(Path.Combine(directory, "traversal.zip"), Path.Combine(directory, "safe"), null, CancellationToken.None);
        }
        catch (InvalidDataException) { failed = true; }
        check(failed && !File.Exists(Path.Combine(directory, "escape.csv")), "ZIP path traversal rejected");
        var first = new ConnectionProfile { Name = "First" };
        first.Database.Host = "first.invalid";
        var second = first.Clone("Second");
        second.Database.Host = "second.invalid";
        var runProject = new TransferProject { ReportDirectory = Path.Combine(directory, "profile-runs"), Profiles = new List<ConnectionProfile> { first, second } };
        runProject.Steps.Add(new TransferStep { Name = "First task", ConnectionProfileId = first.Id, Input = LoadInput.ExistingDataset, Table = "APP.T", BatchRecords = 0 });
        runProject.Steps.Add(new TransferStep { Name = "Second task", ConnectionProfileId = second.Id, Input = LoadInput.ExistingDataset, Table = "APP.T", BatchRecords = 0 });
        var runOptions = new RunOptions();
        runOptions.Credentials[first.Id] = new Credentials { Db2Password = "first-secret" };
        runOptions.Credentials[second.Id] = new Credentials { Db2Password = "second-secret" };
        var adapter = new RecordingUtility();
        var report = new ProjectRunner(adapter).Run(runProject, runOptions, false, CancellationToken.None);
        check(report.Status == "Completed" && report.Batches.Count == 2, "Profile-aware runner completes ordered tasks");
        check(adapter.Calls.SequenceEqual(new[] { "first.invalid:first-secret", "second.invalid:second-secret" }), "Each task routes to its selected profile and credential");
        check(ProjectRunner.ReportText(report).Contains("Confirmed inserted rows: 8"), "Confirmed LOAD totals exclude preparation estimates");
        runOptions.ProfileOverrides[first.Id] = second;
        adapter = new RecordingUtility();
        report = new ProjectRunner(adapter).Run(runProject, runOptions, true, CancellationToken.None);
        check(report.Status == "PreviewPassed" && adapter.Calls[0] == "second.invalid:first-secret", "Runtime override affects selected profile without mutating saved project");
        check(first.Database.Host == "first.invalid", "Project remains unchanged by run overrides");
        string mixedFile = Path.Combine(directory, "mixed.csv");
        File.WriteAllBytes(mixedFile, new byte[] { 239, 187, 191 }.Concat(Encoding.UTF8.GetBytes("\"فارسی\";")).Concat(Encoding.GetEncoding(1256).GetBytes("\"سلام\"\r\n")).ToArray());
        var mixed = new TransferStep { LocalFile = mixedFile, LocalCodePage = 65001, Delimiter = ";", Columns = new List<TextColumn> { new TextColumn { Name = "A", MaxUtf8Bytes = 40, CodePage = 65001 }, new TextColumn { Name = "B", MaxUtf8Bytes = 40, CodePage = 1256 } } };
        foreach (var part in InputPreparation.Read(mixed, directory, null, CancellationToken.None))
            using (part)
            {
                var bytes = File.ReadAllBytes(part.File);
                check(Encoding.UTF8.GetString(bytes, 2, bytes[1]) == "فارسی" && Encoding.UTF8.GetString(bytes, 44, bytes[43]) == "سلام", "Mixed UTF8/1256 CSV with BOM preserves each field");
            }
        return count;
    }
}
