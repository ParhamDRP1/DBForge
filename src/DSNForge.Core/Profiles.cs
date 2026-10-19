using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace DSNForge.Core
{
    public enum DatabaseProvider
    {
        MicrosoftHostIntegration, OleDb, Odbc, RegisteredFactory, CustomAssembly
    }
    public enum PasswordMode
    {
        Password_Environment_Variable, Manual, RuntimePrompt
    }
    public enum FieldTrim
    {
        None, Both, Start, End
    }
    public class SecretSetting
    {
        public PasswordMode Mode { get; set; } = PasswordMode.Password_Environment_Variable;
        public string EnvironmentVariable { get; set; } = "";
        public string ProtectedValue { get; set; } = "";
        public void Store(string value)
        {
            ProtectedValue = Protect(value);
        }
        public string Resolve() => Mode == PasswordMode.Password_Environment_Variable ? Environment.GetEnvironmentVariable(EnvironmentVariable) : Mode == PasswordMode.Manual ? Unprotect(ProtectedValue) : null;
        public static string Protect(string value) => string.IsNullOrEmpty(value) ? "" : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Encoding.UTF8.GetBytes("DSNForge.v2"), DataProtectionScope.CurrentUser));
        public static string Unprotect(string value) => string.IsNullOrEmpty(value) ? "" : Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), Encoding.UTF8.GetBytes("DSNForge.v2"), DataProtectionScope.CurrentUser));
    }
    public class ConnectionProfile
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "New connection";
        public Db2Settings Database { get; set; } = new Db2Settings();
        public FtpSettings Ftp { get; set; } = new FtpSettings();
        public SecretSetting DatabaseSecret { get; set; } = new SecretSetting { EnvironmentVariable = "DSNFORGE_DB2_PASSWORD" };
        public SecretSetting FtpSecret { get; set; } = new SecretSetting { EnvironmentVariable = "DSNFORGE_FTP_PASSWORD" };
        public override string ToString() => Name;
        public Credentials Resolve() => new Credentials { Db2Password = DatabaseSecret.Resolve(), FtpPassword = FtpSecret.Resolve() };
        public ConnectionProfile Clone(string name)
        {
            var p = JsonCopy(this);
            p.Id = Guid.NewGuid().ToString("N");
            p.Name = name;
            return p;
        }
        public static T JsonCopy<T>(T value) => new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<T>(ProjectStore.Json(value));
    }
    public static class ProfileStore
    {
        public class RuntimeProfile
        {
            public ConnectionProfile Profile
            {
                get; set;
            }
            public Credentials Credentials
            {
                get; set;
            }
        }
        public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DSNForge");
        public static List<ConnectionProfile> Load()
        {
            var path = Path.Combine(DirectoryPath, "profiles.json");
            return File.Exists(path) ? new JavaScriptSerializer().Deserialize<List<ConnectionProfile>>(File.ReadAllText(path)) : new List<ConnectionProfile>();
        }
        public static void Save(IEnumerable<ConnectionProfile> profiles)
        {
            var list = Load();
            foreach (var p in profiles)
            {
                Validate(p);
                list.RemoveAll(x => x.Id == p.Id);
                list.Add(p);
            }
            ProjectStore.Save(Path.Combine(DirectoryPath, "profiles.json"), list);
        }
        public static void Validate(ConnectionProfile profile)
        {
            var b = new System.Data.Common.DbConnectionStringBuilder { ConnectionString = profile.Database.ConnectionTemplate ?? "" };
            foreach (string key in b.Keys)
                if (key.Equals("Password", StringComparison.OrdinalIgnoreCase) || key.Equals("Pwd", StringComparison.OrdinalIgnoreCase) || key.Equals(profile.Database.PasswordKeyword, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Remove password fields from the connection template; use Manual encrypted / Environment / Runtime Prompt.");
            if (string.IsNullOrWhiteSpace(profile.Name))
                throw new InvalidDataException("Profile name is required.");
        }
        public static void Remember(string profile, Credentials c) => ProjectStore.Save(Path.Combine(DirectoryPath, "runtime-" + SafeId(profile) + ".json"), SecretSetting.Protect(ProjectStore.Json(c)));
        public static void Remember(ConnectionProfile profile, Credentials c) => ProjectStore.Save(Path.Combine(DirectoryPath, "runprofile-" + SafeId(profile.Id) + ".json"), SecretSetting.Protect(ProjectStore.Json(new RuntimeProfile { Profile = profile, Credentials = c })));
        public static RuntimeProfile RecallProfile(string id)
        {
            var path = Path.Combine(DirectoryPath, "runprofile-" + SafeId(id) + ".json");
            return File.Exists(path) ? new JavaScriptSerializer().Deserialize<RuntimeProfile>(SecretSetting.Unprotect(new JavaScriptSerializer().Deserialize<string>(File.ReadAllText(path)))) : null;
        }
        public static Credentials Recall(string profile)
        {
            var path = Path.Combine(DirectoryPath, "runtime-" + SafeId(profile) + ".json");
            if (!File.Exists(path))
                return null;
            return new JavaScriptSerializer().Deserialize<Credentials>(SecretSetting.Unprotect(new JavaScriptSerializer().Deserialize<string>(File.ReadAllText(path))));
        }
        static string SafeId(string id) => new string(id.Where(char.IsLetterOrDigit).ToArray());
        public static void Migrate(TransferProject p)
        {
            if (p.Profiles.Count != 0)
                return;
            var profile = new ConnectionProfile { Name = "Imported project connection", Database = p.Db2, Ftp = p.Ftp };
            profile.DatabaseSecret.EnvironmentVariable = p.Db2.PasswordEnvironmentVariable;
            profile.FtpSecret.EnvironmentVariable = p.Ftp.PasswordEnvironmentVariable;
            p.Profiles.Add(profile);
            foreach (var s in p.Steps)
                s.ConnectionProfileId = profile.Id;
        }
    }
}
