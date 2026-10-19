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
    }
}
