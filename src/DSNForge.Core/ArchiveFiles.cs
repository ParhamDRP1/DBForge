using System;
using System.IO;
using System.Linq;
using System.Threading;
using ICSharpCode.SharpZipLib.Zip;

namespace DSNForge.Core
{
    public static class ArchiveFiles
    {
        public static bool IsZip(string path)
        {
            using (var f = File.OpenRead(path))
                return f.ReadByte() == 0x50 && f.ReadByte() == 0x4b && f.ReadByte() == 3 && f.ReadByte() == 4;
        }
        public static bool IsProtected(string path)
        {
            using (var z = new ZipFile(path))
                return z.Cast<ZipEntry>().Any(e => e.IsCrypted);
        }
        public static string Extract(string path, string directory, string password, CancellationToken token)
        {
            Directory.CreateDirectory(directory);
            using (var z = new ZipFile(path))
            {
                z.Password = password;
                var entries = z.Cast<ZipEntry>().Where(e => e.IsFile).ToList();
                var data = entries.Where(e => !e.Name.EndsWith(".dbmeta", StringComparison.OrdinalIgnoreCase) && !e.Name.EndsWith(".load.txt", StringComparison.OrdinalIgnoreCase)).ToList();
                if (data.Count != 1)
                    throw new InvalidDataException("ZIP must contain one data file, optional .dbmeta and .load.txt.");
                foreach (var e in entries)
                {
                    if (e.Name != Path.GetFileName(e.Name) || e.Name.Contains("\\") || e.Name.Contains(":"))
                        throw new InvalidDataException("ZIP paths must be flat file names.");
                    var target = Path.Combine(directory, e.Name);
                    var checksum = new ICSharpCode.SharpZipLib.Checksum.Crc32();
                    using (var input = z.GetInputStream(e))
                    using (var output = new FileStream(target, FileMode.CreateNew))
                        Copy(input, output, token, checksum);
                    if ((e.AESKeySize == 0 || e.Crc != 0) && e.Crc >= 0 && checksum.Value != e.Crc)
                        throw new InvalidDataException("ZIP checksum failed: " + e.Name);
                }
                return Path.Combine(directory, data[0].Name);
            }
        }
        public static string ExtractMetadata(string path, string directory, string password)
        {
            Directory.CreateDirectory(directory);
            using (var zip = new ZipFile(path))
            {
                zip.Password = password;
                var entries = zip.Cast<ZipEntry>().Where(e => e.IsFile && e.Name.EndsWith(".dbmeta", StringComparison.OrdinalIgnoreCase)).ToList();
                if (entries.Count != 1)
                    throw new InvalidDataException("ZIP must contain exactly one .dbmeta file for metadata preview.");
                var entry = entries[0];
                if (entry.Size > 16 * 1024 * 1024)
                    throw new InvalidDataException("Metadata preview exceeds 16 MiB.");
                string file = Path.Combine(directory, "preview.dbmeta");
                using (var input = zip.GetInputStream(entry))
                using (var output = File.Create(file))
                    Copy(input, output, CancellationToken.None);
                return file;
            }
        }
        public static void Compress(string path, string password, bool overwrite, CancellationToken token)
        {
            string destination = path + ".zip", temp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            if (File.Exists(destination) && !overwrite)
                throw new IOException("Output archive already exists: " + destination);
            try
            {
                using (var z = new ZipOutputStream(File.Create(temp)))
                {
                    z.SetLevel(6);
                    z.Password = password;
                    foreach (string file in new[] { path, path + ".dbmeta", path + ".load.txt" }.Where(File.Exists))
                    {
                        z.PutNextEntry(new ZipEntry(Path.GetFileName(file)) { Size = new FileInfo(file).Length, AESKeySize = string.IsNullOrEmpty(password) ? 0 : 256 });
                        using (var input = File.OpenRead(file))
                            Copy(input, z, token);
                        z.CloseEntry();
                    }
                    z.Finish();
                }
                if (File.Exists(destination))
                    File.Replace(temp, destination, null);
                else
                    File.Move(temp, destination);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        static void Copy(Stream input, Stream output, CancellationToken token, ICSharpCode.SharpZipLib.Checksum.Crc32 checksum = null)
        {
            var buffer = new byte[1048576];
            int n;
            while ((n = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                token.ThrowIfCancellationRequested();
                checksum?.Update(new ArraySegment<byte>(buffer, 0, n));
                output.Write(buffer, 0, n);
            }
        }
    }
}
