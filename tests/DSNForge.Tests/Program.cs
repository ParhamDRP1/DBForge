using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Threading;
using DSNForge.Core;
class Tests
{
    static int n; static void Assert(bool ok, string message)
    {
        if (!ok)
            throw new Exception(message);
        n++;
    }
    static int Main(string[] args)
    {
        string dir = Path.Combine(Path.GetTempPath(), "DSNForge-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            var s = new TransferStep { Table = "APP.TEST", Columns = new System.Collections.Generic.List<TextColumn> { new TextColumn { Name = "F01", MaxUtf8Bytes = 20 }, new TextColumn { Name = "F02", MaxUtf8Bytes = 20 } } };
            byte[] b = InputPreparation.Pack(s, new[] { " فارسی ", "001" });
            Assert(b[1] == Encoding.UTF8.GetByteCount(" فارسی "), "UTF8 prefix");
            Assert(Encoding.UTF8.GetString(b, 2, b[1]) == " فارسی ", "Persian whitespace roundtrip");
            Assert(b[23] == 3, "second length prefix");
            bool throws = false;
            try
            {
                InputPreparation.Pack(s, new[] { new string('x', 21), "a" });
            }
            catch (InvalidDataException) { throws = true; }
            Assert(throws, "No truncation");
            string cards = ControlBuilder.Build(s, "MYUSER.TEST.B000001");
            Assert(cards.Contains("VARCHAR MIXED"), "Unicode MIXED");
            s.Disposition = LoadDisposition.Replace;
            Assert(ControlBuilder.Build(s, "MYUSER.TEST.B000001", false, 1).Contains(" REPLACE "), "First replace");
            Assert(ControlBuilder.Build(s, "MYUSER.TEST.B000002", false, 2).Contains("RESUME YES"), "Later append");
            Assert(Validation.ValidDataset("MYUSER.TEST.B000001"), "Dataset valid");
            Assert(!Validation.ValidDataset("MYUSER.TOOLONGGG"), "Qualifier width");
            Assert(!Validation.ValidDataset("MYUSER.X' DELETE"), "Dataset injection");
            s.LocalFile = Path.Combine(dir, "input.csv");
            File.WriteAllText(s.LocalFile, "\" فارسی \";\"a;b\"\r\n\"\";001\r\n\"line\nline\";x\r\n", new UTF8Encoding(true));
            s.LocalCodePage = 65001;
            s.Delimiter = ";";
            s.BatchRecords = 2;
            long total = 0;
            int batch = 0;
            foreach (var part in InputPreparation.Read(s, dir, null, CancellationToken.None))
                using (part)
                {
                    total += part.Records;
                    batch++;
                    Assert(new FileInfo(part.File).Length == part.RecordBytes * part.Records, "Fixed records");
                }
            Assert(total == 3 && batch == 2, "CSV quoted delimiter, empty, multiline and batching");
            var p = new TransferProject();
            p.Steps.Add(s);
            string file = Path.Combine(dir, "project.json");
            ProjectStore.Save(file, p);
            var restored = ProjectStore.Load(file);
            Assert(restored.Steps[0].Columns.Count == 2, "Project roundtrip");
            Assert(!File.ReadAllText(file).Contains("Db2Password"), "No embedded secrets");
            Assert(Validation.Check(restored).Count == 0, "Valid project");
            var report = new RunReport { Project = "<script>", Status = "Completed" };
            Reports.Save(dir, report);
            Assert(!File.ReadAllText(Path.Combine(dir, "report.html")).Contains("<script>"), "HTML escaping");
            var generated = "LOAD DATA INDDN SYSREC RESUME NO INTO TABLE APP.OLD (F01 POSITION(1) VARCHAR)";
            Assert(ControlBuilder.Rebind(generated, s, "MYUSER.TEST.B000001").Contains("INTO TABLE APP.TEST"), "Rebind table");
            Assert(ControlBuilder.Rebind(generated, s, "MYUSER.TEST.B000001").Contains("INDDN DFIN"), "Rebind DD");
            n += EngineTests.Run(dir);
            Console.WriteLine("PASS " + n + " assertions");
            return 0;
        }
        catch (Exception e) { Console.WriteLine(e); return 1; }
    }
}
