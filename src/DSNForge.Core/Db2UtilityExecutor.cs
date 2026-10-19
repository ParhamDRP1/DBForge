using System;
using System.Data;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading;
using System.Data.Common;
namespace DSNForge.Core
{
    public interface IUtilityExecutor
    {
        UtilityResult Execute(Db2Settings settings, string password, string id, string cards, bool preview, Action<RunEvent> log, CancellationToken token);
    }
    public class UtilityResult
    {
        public int ReturnCode
        {
            get; set;
        }
        public double Seconds
        {
            get; set;
        }
        public long Records
        {
            get; set;
        }
    }
    public sealed class Db2UtilityExecutor : IUtilityExecutor
    {
        public static DbConnection CreateConnection(Db2Settings s, string password) => ProviderFactory.Create(s, password);
        public UtilityResult Execute(Db2Settings s, string password, string id, string cards, bool preview, Action<RunEvent> log, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var watch = Stopwatch.StartNew();
            using (var c = CreateConnection(s, password))
            {
                c.Open();
                token.ThrowIfCancellationRequested();
                using (var q = c.CreateCommand())
                {
                    q.CommandText = "CALL SYSPROC.DSNUTILU(?,?,?,?)";
                    q.CommandTimeout = s.UtilityTimeoutSeconds;
                    string[] values = { id, preview ? "PREVIEW" : "NO", ControlBuilder.Normalize(cards) };
                    int[] lengths = { 16, 8, 32704 };
                    for (int i = 0; i < 3; i++)
                    {
                        var p = q.CreateParameter();
                        p.DbType = DbType.String;
                        p.Size = lengths[i];
                        p.Value = values[i];
                        q.Parameters.Add(p);
                    }
                    var result = q.CreateParameter();
                    result.DbType = DbType.Int32;
                    result.Direction = ParameterDirection.Output;
                    q.Parameters.Add(result);
                    long records = 0;
                    using (token.Register(() => { try { q.Cancel(); } catch { } }))
                    using (var r = q.ExecuteReader())
                    {
                        do
                        {
                            while (r.Read())
                            {
                                string text = "";
                                for (int i = 0; i < r.FieldCount; i++)
                                    text += Convert.ToString(r[i]) + " ";
                                log?.Invoke(new RunEvent { Stage = "DSNUTILU", Message = text.Trim() });
                                var m = Regex.Match(text, @"(?:TOTAL NUMBER OF RECORDS LOADED|NUMBER OF RECORDS UNLOADED|NUMBER OF RECORDS)=\s*(\d+)");
                                if (m.Success)
                                    records = Math.Max(records, long.Parse(m.Groups[1].Value));
                            }
                        } while (r.NextResult());
                    }
                    token.ThrowIfCancellationRequested();
                    if (result.Value == null || result.Value == DBNull.Value)
                        throw new InvalidOperationException("DSNUTILU did not return a status; outcome unknown");
                    return new UtilityResult { ReturnCode = Convert.ToInt32(result.Value), Seconds = watch.Elapsed.TotalSeconds, Records = records };
                }
            }
        }
    }
}
