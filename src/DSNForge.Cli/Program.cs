using System;
using System.IO;
using System.Threading;
using DSNForge.Core;
class Program
{
    static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (Exception e) { Console.Error.WriteLine(e.Message); return 1; }
    }
    static int Run(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("DSNForge CLI\n  new <project.json>\n  validate <project.json>\n  preview <project.json>\n  run <project.json>\nPasswords: DSNFORGE_DB2_PASSWORD / DSNFORGE_FTP_PASSWORD. Ctrl+C requests cancellation; interrupted utilities may still be active on the server.");
            return 2;
        }
        if (args[0] == "new")
        {
            if (File.Exists(args[1]))
                throw new IOException("Project already exists");
            var example = new TransferProject { Name = "Example CSV load" };
            example.Steps.Add(new TransferStep { Name = "Load accounts", Table = "APP.EXAMPLE", LocalFile = "input.csv", Columns = new System.Collections.Generic.List<TextColumn> { new TextColumn { Name = "F01", MaxUtf8Bytes = 32 }, new TextColumn { Name = "F02", MaxUtf8Bytes = 128 } } });
            ProjectStore.Save(args[1], example);
            return 0;
        }
        var p = ProjectStore.Load(args[1]);
        if (p.Profiles.Count > 0)
            return RunProfiles(args[0], p);
        var errors = Validation.Check(p);
        foreach (var e in errors)
            Console.Error.WriteLine("ERROR " + e);
        if (errors.Count > 0)
            return 2;
        if (args[0] == "validate")
        {
            Console.WriteLine("Local validation passed. Use preview for DB2 syntax validation.");
            return 0;
        }
        using (var cancel = new CancellationTokenSource())
        {
            Console.CancelKeyPress += (s, e) => { e.Cancel = true; cancel.Cancel(); };
            var engine = new TransferEngine();
            engine.Progress += e => { if (e.Level != "TRACE") Console.WriteLine(e.Time.ToString("yyyy-MM-dd HH:mm:ss") + " | " + e.Level + " | " + e.Stage + " | " + e.Message); };
            if (args[0] == "preview")
            {
                engine.ValidateRemoteAsync(p, Credentials.FromEnvironment(p), cancel.Token).GetAwaiter().GetResult();
                return 0;
            }
            if (args[0] != "run")
                throw new ArgumentException("Unknown command");
            var report = engine.RunAsync(p, Credentials.FromEnvironment(p), cancel.Token).GetAwaiter().GetResult();
            Console.WriteLine(report.Status);
            return report.Status.StartsWith("Completed") ? 0 : 1;
        }
    }
    static string Password(string title)
    {
        if (Console.IsInputRedirected)
            throw new InvalidOperationException("Runtime password required; configure an environment variable for unattended execution.");
        Console.Write(title + ": ");
        var value = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(true);
            if (key.Key == ConsoleKey.Enter)
                break;
            if (key.Key == ConsoleKey.Escape)
                throw new OperationCanceledException();
            if (key.Key == ConsoleKey.Backspace)
            {
                if (value.Length > 0)
                    value.Length--;
            }
            else if (!char.IsControl(key.KeyChar))
                value.Append(key.KeyChar);
        }
        Console.WriteLine();
        return value.ToString();
    }
    static int RunProfiles(string command, TransferProject p)
    {
        if (command == "validate")
        {
            foreach (var profile in p.Profiles)
                ProfileStore.Validate(profile);
            foreach (var s in p.Steps)
            {
                MetadataCatalog.Identifier(s.Table);
                if (!p.Profiles.Exists(x => x.Id == s.ConnectionProfileId))
                    throw new InvalidDataException("Task profile not found: " + s.Name);
                if (!string.IsNullOrWhiteSpace(s.UnloadQuery))
                    MetadataCatalog.SelectQuery(s.UnloadQuery);
            }
            Console.WriteLine("Profile/task structure passed. Use preview for server validation; input layout is validated after ZIP/dbmeta preparation.");
            return 0;
        }
        if (command != "run" && command != "preview")
            throw new ArgumentException("Unknown command");
        var options = new RunOptions { RequestArchivePassword = Password };
        foreach (var profile in p.Profiles)
        {
            var c = profile.Resolve();
            if (string.IsNullOrEmpty(c.Db2Password))
                c.Db2Password = Password(profile.Name + " database password");
            bool ftp = p.Steps.Exists(s => s.Enabled && s.ConnectionProfileId == profile.Id && string.IsNullOrEmpty(s.UnloadQuery) && !(s.Operation == Operation.Unload && s.UnloadFormat == UnloadFormat.Delimited) && (s.Operation != Operation.Load || s.Input != LoadInput.ExistingDataset));
            if (command == "run" && ftp && string.IsNullOrEmpty(c.FtpPassword))
                c.FtpPassword = Password(profile.Name + " FTP password");
            options.Credentials[profile.Id] = c;
        }
        using (var cancel = new CancellationTokenSource())
        {
            Console.CancelKeyPress += (s, e) => { e.Cancel = true; cancel.Cancel(); };
            var runner = new ProjectRunner();
            runner.Progress += e => { if (e.Level != "TRACE") Console.WriteLine($"{e.Time:O} | {e.Level} | {e.Stage} | {e.Message}"); };
            var result = runner.Run(p, options, command == "preview", cancel.Token);
            Console.WriteLine(ProjectRunner.ReportText(result));
            return result.Status.StartsWith("Completed") || result.Status == "PreviewPassed" ? 0 : 1;
        }
    }
}
