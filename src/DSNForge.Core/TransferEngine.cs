using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Collections.Generic;
namespace DSNForge.Core
{
    public sealed class TransferEngine
    {
        readonly IUtilityExecutor utility; int running; public event Action<RunEvent> Progress;
        public TransferEngine(IUtilityExecutor utility = null)
        {
            this.utility = utility ?? new Db2UtilityExecutor();
        }
        public Task<RunReport> RunAsync(TransferProject project, Credentials credentials, CancellationToken token) => Task.Run(() => Run(project, credentials, token));
        public Task ValidateRemoteAsync(TransferProject p, Credentials secrets, CancellationToken token) => Task.Run(() => { var issues = Validation.Check(p); if (issues.Count > 0) throw new InvalidDataException(string.Join(Environment.NewLine, issues)); NeedSecrets(p, secrets); int i = 0; foreach (var s in p.Steps.Where(x => x.Enabled)) { token.ThrowIfCancellationRequested(); string ds = ControlBuilder.Dataset(s, "RPREVIEW", ++i); var result = utility.Execute(p.Db2, secrets.Db2Password, "DFPV" + Guid.NewGuid().ToString("N").Substring(0, 10).ToUpperInvariant(), ControlBuilder.Build(s, ds), true, e => Progress?.Invoke(e), token); if (result.ReturnCode > 4) throw new InvalidOperationException("PREVIEW return code " + result.ReturnCode + " for " + s.Name); } Progress?.Invoke(new RunEvent { Stage = "Validate", Message = "DSNUTILU PREVIEW passed. Data conversion, dataset availability and final LOAD outcomes are not verified by PREVIEW." }); }, token);
        static void NeedSecrets(TransferProject p, Credentials c)
        {
            if (string.IsNullOrEmpty(c?.Db2Password))
                throw new InvalidOperationException("DB2 password is missing");
            if (string.IsNullOrEmpty(c.FtpPassword) && p.Steps.Any(s => s.Enabled && (s.Operation != Operation.Load || s.Input != LoadInput.ExistingDataset)))
                throw new InvalidOperationException("FTP password is missing");
        }
        RunReport Run(TransferProject project, Credentials credentials, CancellationToken token)
        {
            if (Interlocked.Exchange(ref running, 1) != 0)
                throw new InvalidOperationException("This engine is already running");
            try
            {
                return RunInternal(ProjectStore.Clone(project), credentials, token);
            }
            finally { Interlocked.Exchange(ref running, 0); }
        }
        RunReport RunInternal(TransferProject p, Credentials secrets, CancellationToken token)
        {
            var issues = Validation.Check(p);
            if (issues.Count > 0)
                throw new InvalidDataException(string.Join(Environment.NewLine, issues));
            NeedSecrets(p, secrets);
            string run = "R" + Guid.NewGuid().ToString("N").Substring(0, 7).ToUpperInvariant();
            string dir = Path.GetFullPath(Path.Combine(p.ReportDirectory, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + run));
            Directory.CreateDirectory(dir);
            var report = new RunReport { Project = p.Name, RunId = run };
            object logGate = new object();
            string current = "Starting";
            var watch = Stopwatch.StartNew();
            Func<string, string> redact = text => { if (text == null) return null; foreach (string secret in new[] { secrets.Db2Password, secrets.FtpPassword }) if (!string.IsNullOrEmpty(secret)) text = text.Replace(secret, "[redacted]"); return text; };
            Action<RunEvent> log = e => { e.Message = redact(e.Message); e.ElapsedSeconds = watch.Elapsed.TotalSeconds; lock (logGate) File.AppendAllText(Path.Combine(dir, "events.jsonl"), ProjectStore.Json(e) + Environment.NewLine); try { Progress?.Invoke(e); } catch { } };
            Action save = () => Reports.Save(dir, report);
            save();
            log(new RunEvent { Stage = "Project", Message = "Run " + run + " | Reports: " + dir });
            using (var pulse = new Timer(_ => log(new RunEvent { Stage = "Heartbeat", Message = current + " · elapsed " + watch.Elapsed.ToString(@"hh\:mm\:ss") }), null, 5000, 5000))
            {
                try
                {
                    int serial = 0;
                    foreach (var step in p.Steps.Where(x => x.Enabled))
                    {
                        token.ThrowIfCancellationRequested();
                        current = step.Name;
                        if (step.Operation == Operation.Load)
                        {
                            if (step.Input == LoadInput.ExistingDataset)
                            {
                                string ds = ControlBuilder.Dataset(step, run, ++serial);
                                var br = NewBatch(report, step, ds);
                                ExecuteUtility(p, secrets, step, ControlBuilder.Build(step, ds), br, dir, log, token);
                                save();
                                Cleanup(p, secrets, step, ds, log, token);
                            }
                            else
                            {
                                int stepBatch = 0;
                                foreach (var batch in InputPreparation.Read(step, dir, log, token))
                                    using (batch)
                                    {
                                        token.ThrowIfCancellationRequested();
                                        stepBatch++;
                                        string ds = ControlBuilder.Dataset(step, run, ++serial);
                                        var br = NewBatch(report, step, ds);
                                        br.Records = batch.Records;
                                        br.Bytes = new FileInfo(batch.File).Length;
                                        save();
                                        Upload(p, secrets, step, batch, ds, br, log, token);
                                        save();
                                        string cards = ControlBuilder.Build(step, ds, false, stepBatch);
                                        ExecuteUtility(p, secrets, step, cards, br, dir, log, token);
                                        save();
                                        Cleanup(p, secrets, step, ds, log, token);
                                    }
                            }
                        }
                        else
                        {
                            string ds = ControlBuilder.Dataset(step, run, ++serial);
                            string punch = ControlBuilder.Punch(ds);
                            var br = NewBatch(report, step, ds);
                            save();
                            using (var ftp = Open(p, secrets, log, token))
                            {
                                bool dataExists = ftp.Exists(ds), punchExists = ftp.Exists(punch);
                                if (step.Dataset.AutoAllocate && (dataExists || punchExists))
                                    throw new IOException("UNLOAD dataset already exists: " + ds + ". Use a unique {run} token.");
                                if (!step.Dataset.AutoAllocate && (!dataExists || !punchExists || !step.Dataset.AllowOverwrite))
                                    throw new IOException("Preallocated UNLOAD requires both datasets and AllowOverwrite=true");
                            }
                            ExecuteUtility(p, secrets, step, ControlBuilder.Build(step, ds, true), br, dir, log, token);
                            save();
                            string cardsPath = Path.Combine(dir, "unload-" + serial + ".load.txt");
                            var clock = Stopwatch.StartNew();
                            using (var ftp = Open(p, secrets, log, token))
                            {
                                ftp.SetType(FtpRepresentation.Ascii);
                                ftp.Download(punch, cardsPath, null);
                            }
                            br.TransferSeconds += clock.Elapsed.TotalSeconds;
                            save();
                            if (step.Operation == Operation.Unload)
                            {
                                clock.Restart();
                                using (var ftp = Open(p, secrets, log, token))
                                {
                                    ftp.Site(p.Ftp.SiteOptions);
                                    ftp.Download(ds, step.LocalFile, (n, t) => { br.Bytes = n; log(new RunEvent { Stage = "Download", Bytes = n, TotalBytes = t, Message = step.Name + " · " + n.ToString("N0") + " bytes" }); });
                                }
                                br.TransferSeconds += clock.Elapsed.TotalSeconds;
                                File.Copy(cardsPath, step.LocalFile + ".load.txt", true);
                                save();
                            }
                            else
                            {
                                var load = new TransferStep { Name = step.Name + " → LOAD", Table = step.TargetTable, Operation = Operation.Load, Input = LoadInput.ExistingDataset, Disposition = step.Disposition, AcceptReturnCode4 = step.AcceptReturnCode4, LogWrites = step.LogWrites };
                                string cards = ControlBuilder.Rebind(File.ReadAllText(cardsPath), load, ds);
                                var dest = NewBatch(report, load, ds);
                                dest.Records = br.Records;
                                save();
                                ExecuteUtility(p, secrets, load, cards, dest, dir, log, token);
                                save();
                            }
                            Cleanup(p, secrets, step, ds, log, token);
                            Cleanup(p, secrets, step, punch, log, token);
                        }
                    }
                    report.Status = report.Batches.Any(x => x.ReturnCode == 4) ? "CompletedWithWarnings" : "Completed";
                }
                catch (Exception ex) { report.Status = token.IsCancellationRequested ? "Cancelled" : "Failed"; report.Error = redact(ex.Message); log(new RunEvent { Stage = current, Level = "ERROR", Message = report.Error }); var unfinished = report.Batches.LastOrDefault(x => x.Status != "Succeeded" && x.Status != "Warning"); if (unfinished != null) { unfinished.Error = report.Error; if (unfinished.Status == "UtilityRunning") unfinished.Status = "OutcomeUnknown — inspect UtilityId before rerun"; else unfinished.Status = report.Status; } }
                finally { report.Finished = DateTimeOffset.Now; save(); log(new RunEvent { Stage = "Project", Message = report.Status + " | " + dir }); }
            }
            return report;
        }
        static BatchResult NewBatch(RunReport r, TransferStep s, string ds)
        {
            var b = new BatchResult { Step = s.Name, Operation = s.Operation, Dataset = ds, Status = "Pending", UtilityId = "DF" + Guid.NewGuid().ToString("N").Substring(0, 12).ToUpperInvariant() };
            r.Batches.Add(b);
            return b;
        }
        MainframeFtp Open(TransferProject p, Credentials c, Action<RunEvent> log, CancellationToken token) => new MainframeFtp(p.Ftp, c.FtpPassword, log, token);
        void ExecuteUtility(TransferProject p, Credentials c, TransferStep s, string cards, BatchResult batch, string dir, Action<RunEvent> log, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            batch.ControlFile = Path.Combine(dir, batch.UtilityId + ".control.txt");
            File.WriteAllText(batch.ControlFile, cards);
            batch.Status = "UtilityRunning";
            ProjectStore.Save(Path.Combine(dir, batch.UtilityId + ".checkpoint.json"), batch);
            log(new RunEvent { Stage = "Utility", Message = "Starting " + batch.UtilityId + " · " + s.Name + ". Server row progress is unavailable until SYSPRINT returns." });
            var result = utility.Execute(p.Db2, c.Db2Password, batch.UtilityId, cards, false, log, token);
            batch.ReturnCode = result.ReturnCode;
            batch.UtilitySeconds = result.Seconds;
            batch.ConfirmedRecords = result.RecordCountKnown || result.Records > 0 ? (long?)result.Records : null;
            batch.RejectedRecords = result.RejectedRecords;
            if (result.Records > 0)
            {
                if (batch.Records > 0 && s.Operation == Operation.Load && batch.Records != result.Records)
                    throw new InvalidDataException("Loaded row count differs from prepared row count");
                batch.Records = result.Records;
            }
            batch.Status = result.ReturnCode == 0 ? "Succeeded" : result.ReturnCode == 4 ? "Warning" : "UtilityFailed";
            ProjectStore.Save(Path.Combine(dir, batch.UtilityId + ".checkpoint.json"), batch);
            if (result.ReturnCode > 4 || result.ReturnCode == 4 && !s.AcceptReturnCode4)
                throw new InvalidOperationException("DSNUTILU return code " + result.ReturnCode + "; inspect SYSPRINT and UtilityId " + batch.UtilityId);
            log(new RunEvent { Stage = "Utility", Level = result.ReturnCode == 4 ? "WARN" : "INFO", Records = batch.Records, Message = "Finished " + batch.UtilityId + " · RC=" + result.ReturnCode + " · records=" + batch.Records + " · seconds=" + result.Seconds.ToString("F2") });
        }
        void Upload(TransferProject p, Credentials c, TransferStep s, PreparedBatch batch, string ds, BatchResult result, Action<RunEvent> log, CancellationToken token)
        {
            var clock = Stopwatch.StartNew();
            bool checkedName = false;
            for (int attempt = 0; ; attempt++)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    using (var ftp = Open(p, c, log, token))
                    {
                        if (!checkedName)
                        {
                            bool exists = ftp.Exists(ds);
                            if (exists && !s.Dataset.AllowOverwrite)
                                throw new InvalidOperationException("Dataset already exists; overwrite is disabled: " + ds);
                            if (!exists && !s.Dataset.AutoAllocate)
                                throw new InvalidOperationException("Dataset does not exist and automatic allocation is off");
                            checkedName = true;
                        }
                        if (s.Dataset.AutoAllocate)
                        {
                            int width = batch.RecordBytes > 0 ? batch.RecordBytes : s.Dataset.RecordLength;
                            ftp.Site("FILETYPE=SEQ RECFM=" + (batch.RecordBytes > 0 ? "FB" : s.Dataset.RecordFormat) + " LRECL=" + width + " BLKSIZE=" + s.Dataset.BlockSize + " " + (s.Dataset.SpaceUnit == "CYL" ? "CYLINDERS" : "TRACKS") + " PRIMARY=" + s.Dataset.PrimarySpace + " SECONDARY=" + s.Dataset.SecondarySpace);
                        }
                        ftp.Site(p.Ftp.SiteOptions);
                        ftp.Upload(batch.File, ds, (n, t) => log(new RunEvent { Stage = "Upload", Bytes = n, TotalBytes = t, Records = batch.Records, BytesPerSecond = clock.Elapsed.TotalSeconds > 0 ? n / clock.Elapsed.TotalSeconds : 0, RemainingSeconds = n > 0 ? (double?)(clock.Elapsed.TotalSeconds * (t - n) / n) : null, Message = s.Name + " · " + n.ToString("N0") + " / " + t.ToString("N0") + " bytes" }));
                        result.TransferSeconds = clock.Elapsed.TotalSeconds;
                        result.Status = "Uploaded";
                        return;
                    }
                }
                catch (Exception ex) when (!(ex is OperationCanceledException) && !(ex is InvalidOperationException) && attempt < p.Ftp.Retries) { token.ThrowIfCancellationRequested(); log(new RunEvent { Level = "WARN", Stage = "FTP retry", Message = ex.Message + " · retry " + (attempt + 1) }); if (token.WaitHandle.WaitOne(Math.Min((attempt + 1) * 5, 60) * 1000)) token.ThrowIfCancellationRequested(); }
            }
        }
        void Cleanup(TransferProject p, Credentials c, TransferStep s, string ds, Action<RunEvent> log, CancellationToken token)
        {
            if (!s.Dataset.DeleteAfterSuccess)
                return;
            try
            {
                using (var ftp = Open(p, c, log, token))
                    ftp.Delete(ds);
                log(new RunEvent { Stage = "Cleanup", Message = "Deleted " + ds });
            }
            catch (Exception ex) { log(new RunEvent { Stage = "Cleanup", Level = "WARN", Message = "Data operation completed; dataset retained: " + ex.Message }); }
        }
    }
}
