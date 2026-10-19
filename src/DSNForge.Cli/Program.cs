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
}
