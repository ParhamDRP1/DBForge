using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Reflection;

namespace DSNForge.Core
{
    /// <summary>Loads the customer-installed Microsoft DB2 provider only for live DB2 calls.</summary>
    public static class ProviderFactory
    {
        public static DbConnection Create(Db2Settings settings, string password)
        {
            if (settings.Provider != DatabaseProvider.MicrosoftHostIntegration)
            {
                DbConnection connection;
                switch (settings.Provider)
                {
                    case DatabaseProvider.OleDb:
                        connection = new System.Data.OleDb.OleDbConnection();
                        break;
                    case DatabaseProvider.Odbc:
                        connection = new System.Data.Odbc.OdbcConnection();
                        break;
                    case DatabaseProvider.CustomAssembly:
                        connection = (DbConnection)Activator.CreateInstance(Assembly.LoadFrom(settings.ProviderAssemblyPath).GetType(settings.ConnectionTypeName, true));
                        break;
                    default:
                        connection = DbProviderFactories.GetFactory(settings.ProviderInvariantName).CreateConnection();
                        break;
                }
                var b = new DbConnectionStringBuilder { ConnectionString = settings.ConnectionTemplate };
                foreach (string key in new System.Collections.Generic.List<string>(Keys(b)))
                {
                    if (key.Equals("pwd", StringComparison.OrdinalIgnoreCase) || key.Equals("password", StringComparison.OrdinalIgnoreCase) || key.Equals(settings.PasswordKeyword, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Remove passwords from the connection template; use the profile password setting.");
                    b[key] = Convert.ToString(b[key]).Replace("{host}", settings.Host).Replace("{port}", settings.Port.ToString()).Replace("{catalog}", settings.Catalog).Replace("{user}", settings.User);
                }
                b[settings.PasswordKeyword] = password ?? "";
                connection.ConnectionString = b.ConnectionString;
                return connection;
            }
            string explicitPath = Environment.GetEnvironmentVariable("DSNFORGE_DB2_PROVIDER");
            Assembly assembly = null;
            if (!string.IsNullOrWhiteSpace(explicitPath))
            {
                assembly = Assembly.LoadFrom(Path.GetFullPath(explicitPath));
            }
            else
            {
                foreach (string folder in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
                {
                    var path = Path.Combine(folder, "Microsoft Host Integration Server", "System", "Microsoft.HostIntegration.MsDb2Client.dll");
                    if (File.Exists(path))
                    {
                        assembly = Assembly.LoadFrom(path);
                        break;
                    }
                }
            }
            if (assembly == null)
                throw new InvalidOperationException("Microsoft DB2 provider is not installed. Install Host Integration Server or set DSNFORGE_DB2_PROVIDER to the installed Microsoft.HostIntegration.MsDb2Client.dll. Offline validation and project editing do not require it.");
            var builderType = assembly.GetType("Microsoft.HostIntegration.MsDb2Client.MsDb2ConnectionStringBuilder", true);
            var builder = Activator.CreateInstance(builderType);
            var values = new Dictionary<string, object> {
                {"NetworkAddress",settings.Host}, {"NetworkPort",settings.Port}, {"InitialCatalog",settings.Catalog},
                {"UserId",settings.User}, {"Password",password}, {"DataSource",settings.Host},
                {"NetworkTransportLibrary","TCPIP"}, {"PackageCollection",settings.PackageCollection},
                {"DefaultSchema",settings.Schema}, {"DefaultQualifier",settings.Schema},
                {"PCCodePage",settings.PcCodePage}, {"BinaryCodePage",settings.PcCodePage},
                {"Authentication","Server"}, {"HostCCSID",settings.HostCcsid},
                {"ConnectionPooling",false}, {"ConnectTimeout",settings.ConnectTimeoutSeconds}
            };
            foreach (var entry in values)
                builderType.GetProperty(entry.Key).SetValue(builder, entry.Value, null);
            var platform = builderType.GetProperty("DBMSPlatform");
            platform.SetValue(builder, Enum.Parse(platform.PropertyType, "DB2MVS"), null);
            var connectionType = assembly.GetType("Microsoft.HostIntegration.MsDb2Client.MsDb2Connection", true);
            return (DbConnection)Activator.CreateInstance(connectionType, new object[] { ((DbConnectionStringBuilder)builder).ConnectionString });
        }
        static IEnumerable<string> Keys(DbConnectionStringBuilder b)
        {
            foreach (string k in b.Keys)
                yield return k;
        }
    }
}
