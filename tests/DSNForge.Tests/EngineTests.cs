using System;
using System.IO;
using System.Threading;
using DSNForge.Core;
public static class EngineTests
{
    sealed class FakeUtility : IUtilityExecutor
    {
        public int Calls; public bool Throw; public int Code;
        public UtilityResult Execute(Db2Settings s, string password, string id, string cards, bool preview, Action<RunEvent> log, CancellationToken token)
        {
            Calls++;
            if (Throw)
                throw new IOException("Connection lost after submission");
            return new UtilityResult { ReturnCode = Code, Records = 10 };
        }
    }
    public static int Run(string dir)
    {
        int n = 0;
        Action<bool, string> check = (ok, text) => { if (!ok) throw new Exception(text); n++; };
        var project = new TransferProject { ReportDirectory = Path.Combine(dir, "runs") };
        project.Steps.Add(new TransferStep { Name = "mock", Input = LoadInput.ExistingDataset, Table = "APP.TEST", BatchRecords = 0 });
        var fake = new FakeUtility { Throw = true };
        var report = new TransferEngine(fake).RunAsync(project, new Credentials { Db2Password = "test" }, CancellationToken.None).GetAwaiter().GetResult();
        check(report.Status == "Failed", "Failure recorded");
        check(fake.Calls == 1, "Uncertain LOAD must never auto replay");
        check(report.Batches[0].Status.StartsWith("OutcomeUnknown"), "Ambiguous outcome explicit");
        fake = new FakeUtility { Code = 8 };
        report = new TransferEngine(fake).RunAsync(project, new Credentials { Db2Password = "test" }, CancellationToken.None).GetAwaiter().GetResult();
        check(report.Status == "Failed" && report.Batches[0].ReturnCode == 8, "Utility code captured");
        fake = new FakeUtility { Code = 4 };
        report = new TransferEngine(fake).RunAsync(project, new Credentials { Db2Password = "test" }, CancellationToken.None).GetAwaiter().GetResult();
        check(report.Status == "CompletedWithWarnings", "Return code 4 warning");
        using (var c = new CancellationTokenSource())
        {
            c.Cancel();
            report = new TransferEngine(new FakeUtility()).RunAsync(project, new Credentials { Db2Password = "test" }, c.Token).GetAwaiter().GetResult();
            check(report.Status == "Cancelled", "Cancellation reported");
        }
        var input = new TransferStep { Input = LoadInput.GeneratedCards, NativeInputHasRdw = true, NativeRecordBytes = 3, BatchRecords = 1, LocalFile = Path.Combine(dir, "rdw.bin") };
        File.WriteAllBytes(input.LocalFile, new byte[] { 0, 7, 0, 0, 65, 66, 67, 0, 7, 0, 0, 49, 50, 51 });
        int batches = 0;
        foreach (var b in InputPreparation.Read(input, dir, null, CancellationToken.None))
            using (b)
            {
                check(new FileInfo(b.File).Length == 3 && b.Records == 1, "RDW removed without payload change");
                batches++;
            }
        check(batches == 2, "RDW batching");
        return n;
    }
}
