using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Authentication;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Diagnostics;
namespace DSNForge.Core
{
    public sealed class FtpFailure : IOException
    {
        public int Code
        {
            get;
        }
        public FtpFailure(string response) : base(response) { int n; int.TryParse(response.Substring(0, Math.Min(3, response.Length)), out n); Code = n; }
    }
    public sealed class MainframeFtp : IDisposable
    {
        readonly FtpSettings options; readonly Action<RunEvent> log; readonly CancellationToken token; TcpClient control; Stream channel; StreamReader reader; StreamWriter writer; CancellationTokenRegistration cancel;
        public MainframeFtp(FtpSettings options, string password, Action<RunEvent> log, CancellationToken token)
        {
            this.options = options;
            this.log = log;
            this.token = token;
            try
            {
                control = Connect(options.Host, options.Port);
                cancel = token.Register(() => { try { control.Close(); } catch { } });
                channel = control.GetStream();
                Readers();
                Expect(Read(), 2);
                if (options.ExplicitTls)
                {
                    Expect(Command("AUTH TLS"), 2);
                    var ssl = new SslStream(channel, false);
                    ssl.AuthenticateAsClient(options.Host, null, SslProtocols.Tls12, false);
                    channel = ssl;
                    Readers();
                }
                string user = Command("USER " + Safe(options.User));
                if (user.StartsWith("331"))
                    Expect(Command("PASS " + Safe(password), true), 2);
                else
                    Expect(user, 2);
                if (options.ExplicitTls)
                {
                    Expect(Command("PBSZ 0"), 2);
                    Expect(Command("PROT P"), 2);
                }
                SetType(options.Representation);
            }
            catch { Dispose(); throw; }
        }
        void Readers()
        {
            reader = new StreamReader(channel, Encoding.ASCII, false, 4096, true);
            writer = new StreamWriter(channel, Encoding.ASCII, 4096, true) { NewLine = "\r\n", AutoFlush = true };
        }
        TcpClient Connect(string host, int port)
        {
            var c = new TcpClient();
            try
            {
                var t = c.ConnectAsync(host, port);
                if (!t.Wait(options.TimeoutSeconds * 1000, token))
                    throw new TimeoutException("FTP connect timeout");
                t.GetAwaiter().GetResult();
                c.ReceiveTimeout = options.TimeoutSeconds * 1000;
                c.SendTimeout = options.TimeoutSeconds * 1000;
                c.SendBufferSize = options.BufferBytes;
                c.ReceiveBufferSize = options.BufferBytes;
                c.NoDelay = true;
                return c;
            }
            catch { c.Close(); throw; }
        }
        static string Safe(string s)
        {
            if (s == null || s.IndexOfAny(new[] { '\r', '\n' }) >= 0)
                throw new ArgumentException("Invalid FTP argument");
            return s;
        }
        string Read()
        {
            token.ThrowIfCancellationRequested();
            string first = reader.ReadLine();
            if (first == null)
                throw new IOException("FTP connection closed");
            var b = new StringBuilder(first);
            if (first.Length > 3 && first[3] == '-')
            {
                string end = first.Substring(0, 3) + " ";
                string l;
                do
                {
                    l = reader.ReadLine();
                    if (l == null)
                        throw new IOException("Incomplete FTP reply");
                    b.AppendLine().Append(l);
                    if (b.Length > 1048576)
                        throw new IOException("FTP reply too large");
                } while (!l.StartsWith(end));
            }
            return b.ToString();
        }
        string Command(string command, bool secret = false)
        {
            token.ThrowIfCancellationRequested();
            writer.WriteLine(command);
            string reply = Read();
            log?.Invoke(new RunEvent { Stage = "FTP", Level = reply[0] >= '4' && !command.StartsWith("NLST ") && !command.StartsWith("SIZE ") ? "WARN" : "TRACE", Message = secret ? "FTP authentication response " + reply.Substring(0, 3) : reply });
            return reply;
        }
        static void Expect(string s, int hundred)
        {
            if (s.Length < 3 || s[0] != (char)('0' + hundred))
                throw new FtpFailure(s);
        }
        public void SetType(FtpRepresentation type) => Expect(Command("TYPE " + (type == FtpRepresentation.Binary ? "I" : type == FtpRepresentation.Ascii ? "A" : "E")), 2);
        public void Site(string options)
        {
            if (!string.IsNullOrWhiteSpace(options))
                Expect(Command("SITE " + Safe(options)), 2);
        }
        public void Delete(string name)
        {
            if (!Validation.ValidDataset(name))
                throw new ArgumentException("Invalid dataset");
            Expect(Command("DELE '" + name + "'"), 2);
        }
        public bool Exists(string name)
        {
            try
            {
                using (var m = new MemoryStream())
                {
                    Transfer("NLST '" + name + "'", m, false, 0);
                    return m.Length > 0;
                }
            }
            catch (FtpFailure ex) { if (ex.Code == 550) return false; throw; }
        }
        public void Upload(string file, string name, Action<long, long> progress)
        {
            using (var f = File.OpenRead(file))
                Transfer("STOR '" + name + "'", f, true, f.Length, progress);
        }
        public void Download(string name, string file, Action<long, long> progress)
        {
            long size = 0;
            string reply = Command("SIZE '" + name + "'");
            if (reply.StartsWith("213 "))
                long.TryParse(reply.Substring(4).Trim(), out size);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file)));
            string part = file + ".partial";
            using (var f = new FileStream(part, FileMode.Create, FileAccess.Write))
                Transfer("RETR '" + name + "'", f, false, size, progress);
            if (File.Exists(file))
                File.Replace(part, file, null);
            else
                File.Move(part, file);
        }
        void Transfer(string command, Stream local, bool upload, long total, Action<long, long> progress = null)
        {
            token.ThrowIfCancellationRequested();
            TcpClient data = null;
            TcpListener listener = null;
            try
            {
                if (options.Passive)
                {
                    string reply = Command("PASV");
                    Expect(reply, 2);
                    var m = Regex.Match(reply, @"\((\d+),(\d+),(\d+),(\d+),(\d+),(\d+)\)");
                    if (!m.Success)
                        throw new IOException("Invalid PASV response");
                    data = Connect(options.Host, int.Parse(m.Groups[5].Value) * 256 + int.Parse(m.Groups[6].Value));
                }
                else
                {
                    var localIp = ((IPEndPoint)control.Client.LocalEndPoint).Address;
                    listener = new TcpListener(localIp, 0);
                    listener.Start();
                    int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                    string address = string.IsNullOrWhiteSpace(options.ActiveAddress) ? localIp.ToString() : options.ActiveAddress;
                    Expect(Command("PORT " + address.Replace('.', ',') + "," + (port / 256) + "," + (port % 256)), 2);
                }
                string start = Command(command);
                Expect(start, 1);
                if (data == null)
                {
                    var task = listener.AcceptTcpClientAsync();
                    if (!task.Wait(options.TimeoutSeconds * 1000, token))
                        throw new TimeoutException("Active FTP accept timeout");
                    data = task.Result;
                    data.ReceiveTimeout = options.TimeoutSeconds * 1000;
                    data.SendTimeout = options.TimeoutSeconds * 1000;
                }
                var captured = data;
                using (token.Register(() => { try { captured.Close(); } catch { } }))
                {
                    Stream network = data.GetStream();
                    if (options.ExplicitTls)
                    {
                        var ssl = new SslStream(network, false);
                        ssl.AuthenticateAsClient(options.Host, null, SslProtocols.Tls12, false);
                        network = ssl;
                    }
                    byte[] buffer = new byte[options.BufferBytes];
                    long n = 0;
                    var pulse = Stopwatch.StartNew();
                    int count;
                    Stream input = upload ? local : network, output = upload ? network : local;
                    while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        token.ThrowIfCancellationRequested();
                        output.Write(buffer, 0, count);
                        n += count;
                        if (pulse.ElapsedMilliseconds >= 200)
                        {
                            progress?.Invoke(n, total);
                            pulse.Restart();
                        }
                    }
                    output.Flush();
                    progress?.Invoke(n, total);
                    network.Dispose();
                    data.Close();
                    data = null;
                    Expect(Read(), 2);
                }
            }
            catch { token.ThrowIfCancellationRequested(); throw; }
            finally { data?.Close(); listener?.Stop(); }
        }
        public void Dispose()
        {
            cancel.Dispose();
            try
            {
                channel?.Dispose();
            }
            catch { }
            control?.Close();
        }
    }
}
