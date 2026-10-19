using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
namespace DSNForge.Core
{
    public enum Operation
    {
        Load, Unload, Transfer
    }
    public enum FtpRepresentation
    {
        Binary, Ascii, Ebcdic
    }
    public enum LoadInput
    {
        NativeFile, CsvToUtf8Fixed, ExistingDataset, GeneratedCards
    }
    public enum UnloadFormat
    {
        Native, Delimited
    }
    public enum LoadDisposition
    {
        Append, Replace
    }
    [TypeConverter(typeof(ExpandableObjectConverter))]
    public class Db2Settings
    {
        public DatabaseProvider Provider { get; set; } = DatabaseProvider.MicrosoftHostIntegration;
        public string ProviderInvariantName { get; set; } = "IBM.Data.DB2";
        public string ProviderAssemblyPath { get; set; } = "";
        public string ConnectionTypeName { get; set; } = "IBM.Data.DB2.DB2Connection";
        [Description("For ODBC/OLE DB/custom providers. Use {host}, {port}, {catalog}, {user}; passwords are injected separately, never put them here.")]
        public string ConnectionTemplate { get; set; } = "";
        public string PasswordKeyword { get; set; } = "Password";
        public string Host { get; set; } = "db2.example.invalid"; public int Port { get; set; } = 446;
        public string Catalog { get; set; } = "DB2LOC"; public string User { get; set; } = "DB2USER";
        public string Schema { get; set; } = "APP"; public string PackageCollection { get; set; } = "APP";
        public int PcCodePage { get; set; } = 1256; public int HostCcsid { get; set; } = 1208;
        public int ConnectTimeoutSeconds { get; set; } = 15; public int UtilityTimeoutSeconds { get; set; } = 3600;
        public string PasswordEnvironmentVariable { get; set; } = "DSNFORGE_DB2_PASSWORD";
        public override string ToString() => Host + " / " + Catalog;
    }
    [TypeConverter(typeof(ExpandableObjectConverter))]
    public class FtpSettings
    {
        public string Host { get; set; } = "db2.example.invalid"; public int Port { get; set; } = 21; public string User { get; set; } = "DB2USER";
        public string PasswordEnvironmentVariable { get; set; } = "DSNFORGE_FTP_PASSWORD";
        public bool ExplicitTls { get; set; } = false; public bool Passive { get; set; } = true;
        [Description("For active FTP; blank uses the local address of the control connection.")]
        public string ActiveAddress { get; set; } = "";
        public FtpRepresentation Representation { get; set; } = FtpRepresentation.Binary;
        public int TimeoutSeconds { get; set; } = 60; public int BufferBytes { get; set; } = 1048576;
        [Description("Additional SITE options, for example RDW or SBDATACONN=(IBM-1047,IBM-1256). Sent after allocation options.")]
        public string SiteOptions { get; set; } = "";
        public int Retries { get; set; } = 3;
        public override string ToString() => Host + " / " + Representation;
    }
    [TypeConverter(typeof(ExpandableObjectConverter))]
    public class DatasetSettings
    {
        [Description("Fully qualified MVS dataset. Tokens: {run} = R + 7 characters; {batch} = B + 6 digits. Keep each qualifier <=8 characters.")]
        public string Name { get; set; } = "MYUSER.DSNFORGE.{run}.{batch}";
        public bool AutoAllocate { get; set; } = true; public bool DeleteAfterSuccess { get; set; } = false;
        public bool AllowOverwrite { get; set; } = false;
        public string RecordFormat { get; set; } = "FB"; public int RecordLength { get; set; } = 80; public int BlockSize { get; set; } = 0;
        public string SpaceUnit { get; set; } = "CYL"; public int PrimarySpace { get; set; } = 5; public int SecondarySpace { get; set; } = 5;
        public string Unit { get; set; } = "SYSDA";
        [Description("Additional TEMPLATE options for UNLOAD; e.g. DATACLAS, STORCLAS. Syntax validated by DSNUTILU PREVIEW.")]
        public string TemplateOptions { get; set; } = "";
        public override string ToString() => Name;
    }
    public class TextColumn
    {
        public bool Included { get; set; } = true;
        public string SqlType { get; set; } = "VARCHAR";
        public bool Nullable { get; set; } = true;
        public string DefaultSql { get; set; } = "";
        [Description("0 inherits file encoding. For mixed-encoding CSV, use ASCII-compatible encodings.")]
        public int CodePage { get; set; } = 0;
        public FieldTrim Trim { get; set; } = FieldTrim.None;
        public string Name { get; set; } = "F01"; public int MaxUtf8Bytes { get; set; } = 32; public override string ToString() => Name + " (" + MaxUtf8Bytes + ")";
    }
    public class TransferStep
    {
        [Browsable(false)] public string Id { get; set; } = Guid.NewGuid().ToString("N");
        [Category("01 · Task")] public string ConnectionProfileId { get; set; } = "";
        [Category("02 · File")] public string MetadataFile { get; set; } = "";
        [Category("02 · File"), Description("Import layout/encoding/header/NULL defaults from dbmeta unless already reviewed in the wizard. Disable for a completely manual layout.")]
        public bool UseMetadataLayout { get; set; } = true;
        [Browsable(false)] public bool MetadataApplied { get; set; }
        [Category("03 · LOAD")]
        public bool CreateTableIfMissing
        {
            get; set;
        }
        [Category("03 · LOAD")] public string CreateScript { get; set; } = "";
        [Category("04 · UNLOAD"), Description("SELECT query exported as CSV through the selected database provider; blank uses DSNUTILU.")]
        public string UnloadQuery { get; set; } = "";
        [Category("02 · File")]
        public bool CompressOutput
        {
            get; set;
        }
        [Browsable(false)] public SecretSetting ArchivePassword { get; set; } = new SecretSetting { Mode = PasswordMode.RuntimePrompt };
        [Category("01 · Task")] public string Name { get; set; } = "New transfer";
        [Category("01 · Task")] public bool Enabled { get; set; } = true;
        [Category("01 · Task")] public Operation Operation { get; set; } = Operation.Load;
        [Category("01 · Task")] public string Table { get; set; } = "APP.TABLE_NAME";
        [Category("01 · Task"), Description("Destination table for a server-side Transfer.")] public string TargetTable { get; set; } = "";
        [Category("02 · File")] public string LocalFile { get; set; } = "";
        [Category("02 · File")] public LoadInput Input { get; set; } = LoadInput.CsvToUtf8Fixed;
        [Category("02 · File")] public int LocalCodePage { get; set; } = 1256;
        [Category("02 · File")] public string Delimiter { get; set; } = ",";
        [Category("02 · File")] public bool HasHeader { get; set; } = false;
        [Category("02 · File"), Description("Empty disables NULL markers. Query exports use \\N and escape literal backslashes.")]
        public string CsvNullToken { get; set; } = "";
        [Category("02 · File"), Description("LOAD rows per utility invocation; 0 = one file. UNLOAD runs once per table. Supported for CSV and fixed native files. Each committed batch is recorded in the run report; no automatic replay after uncertain utility outcomes.")]
        public int BatchRecords { get; set; } = 100000;
        [Category("02 · File"), Description("Fixed native input record width. 0 = opaque file (cannot batch or count records).")]
        public int NativeRecordBytes { get; set; } = 0;
        [Category("02 · File"), Description("Strip four-byte RDW headers from native FTP downloads before uploading fixed records. NativeRecordBytes is the payload size, excluding RDW.")]
        public bool NativeInputHasRdw { get; set; } = false;
        [Category("02 · File")] public List<TextColumn> Columns { get; set; } = new List<TextColumn>();
        [Category("03 · LOAD")] public LoadDisposition Disposition { get; set; } = LoadDisposition.Append;
        [Category("03 · LOAD")] public bool LogWrites { get; set; } = true;
        [Category("03 · LOAD"), Description("LOAD cards generated by UNLOAD; table and input DD are rebound to this task.")]
        public string GeneratedCardsFile { get; set; } = "";
        [Category("06 · Expert"), Description("LOAD cards restored from dbmeta; editable and rebound to the task target at execution.")]
        public string MetadataLoadCards { get; set; } = "";
        [Category("04 · UNLOAD")] public UnloadFormat UnloadFormat { get; set; } = UnloadFormat.Native;
        [Category("04 · UNLOAD")] public string ShrLevel { get; set; } = "REFERENCE";
        [Category("05 · Dataset")] public DatasetSettings Dataset { get; set; } = new DatasetSettings();
        [Category("06 · Expert"), Description("Extra options inserted before INTO TABLE / FROM TABLE. Use full override for arbitrary syntax.")]
        public string UtilityOptions { get; set; } = "";
        [Category("06 · Expert"), Description("Full utility control statements. Tokens: {dataset}, {punch}, {table}, {target}. This replaces generated statements, including TEMPLATE. No SQL WHERE syntax conversion is attempted.")]
        public string ControlStatements { get; set; } = "";
        [Category("06 · Expert")] public bool AcceptReturnCode4 { get; set; } = true;
        public override string ToString() => Name;
    }
    public class TransferProject
    {
        public int FormatVersion { get; set; } = 2; public string Name { get; set; } = "DSNForge project";
        public Db2Settings Db2 { get; set; } = new Db2Settings(); public FtpSettings Ftp { get; set; } = new FtpSettings();
        public List<TransferStep> Steps { get; set; } = new List<TransferStep>();
        public List<ConnectionProfile> Profiles { get; set; } = new List<ConnectionProfile>();
        public string ReportDirectory { get; set; } = "runs";
    }
    public class Credentials
    {
        public string Db2Password
        {
            get; set;
        }
        public string FtpPassword
        {
            get; set;
        }
        public static Credentials FromEnvironment(TransferProject p) => new Credentials { Db2Password = Environment.GetEnvironmentVariable(p.Db2.PasswordEnvironmentVariable), FtpPassword = Environment.GetEnvironmentVariable(p.Ftp.PasswordEnvironmentVariable) };
    }
    public static class ProjectStore
    {
        public static string Json(object value) => new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(value);
        public static void Save(string path, object value)
        {
            if (value is TransferProject project)
                foreach (var profile in project.Profiles)
                    ProfileStore.Validate(profile);
            var full = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            var tmp = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(tmp, Json(value), new UTF8Encoding(false));
            if (File.Exists(full))
                File.Replace(tmp, full, null);
            else
                File.Move(tmp, full);
        }
        public static TransferProject Load(string path)
        {
            var p = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<TransferProject>(File.ReadAllText(path));
            if (p == null || (p.FormatVersion != 1 && p.FormatVersion != 2))
                throw new InvalidDataException("Unsupported project version");
            p.FormatVersion = 2;
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!Path.IsPathRooted(p.ReportDirectory))
                p.ReportDirectory = Path.Combine(dir, p.ReportDirectory);
            foreach (var step in p.Steps)
            {
                if (!string.IsNullOrWhiteSpace(step.LocalFile) && !Path.IsPathRooted(step.LocalFile))
                    step.LocalFile = Path.Combine(dir, step.LocalFile);
                if (!string.IsNullOrWhiteSpace(step.GeneratedCardsFile) && !Path.IsPathRooted(step.GeneratedCardsFile))
                    step.GeneratedCardsFile = Path.Combine(dir, step.GeneratedCardsFile);
                if (!string.IsNullOrWhiteSpace(step.MetadataFile) && !Path.IsPathRooted(step.MetadataFile))
                    step.MetadataFile = Path.Combine(dir, step.MetadataFile);
            }
            return p;
        }
        public static TransferProject Clone(TransferProject p) => new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<TransferProject>(Json(p));
    }
}
