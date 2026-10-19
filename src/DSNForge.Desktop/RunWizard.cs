using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DSNForge.Core;
namespace DSNForge.Desktop
{
    internal sealed class RunWizard : Form
    {
        sealed class CredentialRow
        {
            public string Id;
            public ComboBox Profile;
            public TextBox DbUser, FtpUser, DbPassword, FtpPassword;
            public CheckBox Remember;
        }
        readonly TransferProject project; readonly bool preview; readonly List<CredentialRow> rows = new List<CredentialRow>();
        readonly TabControl pages = new TabControl { Dock = DockStyle.Fill };
        readonly ListView events = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true };
        readonly TextBox report = Ui.Multiline(); readonly Label status = Ui.Label("Ready");
        readonly ProgressBar progress = new ProgressBar { Dock = DockStyle.Top, Height = 18 };
        readonly ConcurrentQueue<RunEvent> incoming = new ConcurrentQueue<RunEvent>();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 250 };
        readonly Button start, stop; CancellationTokenSource cancel; bool running; RunReport result;
        readonly Dictionary<TransferStep, TextBox> zipPasswords = new Dictionary<TransferStep, TextBox>();
        readonly List<string> eventLines = new List<string>();
        public RunWizard(TransferProject p, bool preview)
        {
            project = ProjectStore.Clone(p);
            ProfileStore.Migrate(project);
            this.preview = preview;
            Ui.Setup(this, preview ? "Run wizard · PREVIEW" : "Run wizard · Execute project", 1250, 850);
            var setup = Ui.Page(pages, "1 · Profiles / runtime overrides");
            var execution = Ui.Page(pages, "2 · Progress / errors");
            var summary = Ui.Page(pages, "3 · Final report");
            var form = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            form.Controls.Add(Ui.Label("Overrides apply to this run. Remember stores credentials encrypted for this Windows user."));
            foreach (var id in project.Steps.Where(s => s.Enabled).Select(s => s.ConnectionProfileId).Distinct())
            {
                var original = project.Profiles.FirstOrDefault(x => x.Id == id) ?? project.Profiles.First();
                var row = new CredentialRow { Id = id, Profile = new ComboBox { Width = 260, DropDownStyle = ComboBoxStyle.DropDownList }, DbUser = Ui.Text(150), FtpUser = Ui.Text(150), DbPassword = Ui.Text(170), FtpPassword = Ui.Text(170), Remember = new CheckBox { Text = "Remember encrypted", AutoSize = true } };
                row.DbPassword.UseSystemPasswordChar = row.FtpPassword.UseSystemPasswordChar = true;
                row.Profile.Items.AddRange(project.Profiles.Cast<object>().ToArray());
                row.Profile.SelectedItem = original;
                Action populate = () => { var selected = (ConnectionProfile)row.Profile.SelectedItem; row.DbUser.Text = selected.Database.User; row.FtpUser.Text = selected.Ftp.User; Credentials c = null; try { var saved = ProfileStore.RecallProfile(selected.Id); c = saved?.Credentials ?? selected.Resolve(); if (saved != null) { row.DbUser.Text = saved.Profile.Database.User; row.FtpUser.Text = saved.Profile.Ftp.User; row.Remember.Checked = true; } } catch (System.Security.Cryptography.CryptographicException) { } row.DbPassword.Text = c?.Db2Password ?? ""; row.FtpPassword.Text = c?.FtpPassword ?? ""; };
                row.Profile.SelectedIndexChanged += (s, e) => populate();
                populate();
                rows.Add(row);
                var group = new GroupBox { Text = original.Name, Width = 1130, Height = 175 };
                var fields = Ui.Flow();
                fields.Controls.AddRange(new Control[] { Ui.Label("Override profile"), row.Profile, Ui.Label("DB user"), row.DbUser, Ui.Label("DB password"), row.DbPassword, Ui.Label("FTP user"), row.FtpUser, Ui.Label("FTP password"), row.FtpPassword, row.Remember });
                group.Controls.Add(fields);
                form.Controls.Add(group);
            }
            foreach (var task in project.Steps.Where(s => s.Enabled))
            {
                form.Controls.Add(Ui.Label(task.Name + " · runtime parameters"));
                var taskOptions = new PropertyGrid { Width = 1110, Height = 260, SelectedObject = new PropertyView(task, "ConnectionProfileId", "Name"), HelpVisible = true };
                taskOptions.PropertyValueChanged += (sender, args) => { string key = args.ChangedItem.PropertyDescriptor?.Name; if (key == "LocalFile" || key == "MetadataFile") task.MetadataApplied = false; };
                form.Controls.Add(taskOptions);
                var zip = Ui.Text(300);
                zip.UseSystemPasswordChar = true;
                try
                {
                    zip.Text = task.ArchivePassword.Resolve() ?? "";
                }
                catch (System.Security.Cryptography.CryptographicException) { }
                var line = Ui.Flow();
                line.Width = 1100;
                line.Controls.Add(Ui.Label("ZIP password (LOAD prompts if incorrect; blank UNLOAD = no encryption)"));
                line.Controls.Add(zip);
                form.Controls.Add(line);
                zipPasswords[task] = zip;
            }
            setup.Controls.Add(form);
            events.Columns.Add("Time", 155);
            events.Columns.Add("Level", 75);
            events.Columns.Add("Stage", 125);
            events.Columns.Add("Details / error code", 790);
            execution.Controls.Add(events);
            execution.Controls.Add(progress);
            execution.Controls.Add(status);
            status.Dock = DockStyle.Top;
            status.AutoSize = false;
            status.Height = 55;
            report.ReadOnly = true;
            summary.Controls.Add(report);
            var bar = Ui.Flow();
            bar.Dock = DockStyle.Bottom;
            start = Ui.Button(preview ? "Start PREVIEW" : "Start project", async () => await Execute());
            stop = Ui.Button("Stop", () => { cancel?.Cancel(); status.Text = "Stopping… inspect server UtilityId before rerun."; stop.Enabled = false; });
            stop.Enabled = false;
            bar.Controls.Add(start);
            bar.Controls.Add(stop);
            bar.Controls.Add(Ui.Button("Save text report", SaveText));
            bar.Controls.Add(Ui.Button("Close", () => Close()));
            Controls.Add(pages);
            Controls.Add(bar);
            timer.Tick += (s, e) => Drain();
            timer.Start();
            FormClosing += (s, e) => { if (running) { e.Cancel = true; MessageBox.Show(this, "Stop the operation and wait for its report before closing."); } };
            FormClosed += (s, e) => timer.Dispose();
        }
        async Task Execute()
        {
            if (running)
                return;
            var options = new RunOptions();
            foreach (var row in rows)
            {
                var p = ConnectionProfile.JsonCopy((ConnectionProfile)row.Profile.SelectedItem);
                p.Database.User = row.DbUser.Text;
                p.Ftp.User = row.FtpUser.Text;
                var credentials = new Credentials { Db2Password = row.DbPassword.Text, FtpPassword = row.FtpPassword.Text };
                if (string.IsNullOrEmpty(credentials.Db2Password))
                {
                    credentials.Db2Password = Ui.Prompt(this, p.Name + " · database password");
                    if (credentials.Db2Password == null)
                        return;
                }
                bool needFtp = project.Steps.Any(s => s.Enabled && s.ConnectionProfileId == row.Id && string.IsNullOrWhiteSpace(s.UnloadQuery) && !(s.Operation == Operation.Unload && s.UnloadFormat == UnloadFormat.Delimited) && (s.Operation != Operation.Load || s.Input != LoadInput.ExistingDataset));
                if (!preview && needFtp && string.IsNullOrEmpty(credentials.FtpPassword))
                {
                    credentials.FtpPassword = Ui.Prompt(this, p.Name + " · FTP password");
                    if (credentials.FtpPassword == null)
                        return;
                }
                options.ProfileOverrides[row.Id] = p;
                options.Credentials[row.Id] = credentials;
                if (row.Remember.Checked)
                    ProfileStore.Remember(p, credentials);
            }
            foreach (var pair in zipPasswords)
                options.ArchivePasswords[pair.Key.Id] = pair.Value.Text;
            options.RequestArchivePassword = message => (string)Invoke(new Func<string>(() => Ui.Prompt(this, message)));
            running = true;
            start.Enabled = false;
            stop.Enabled = true;
            pages.TabPages[0].Enabled = false;
            pages.SelectedIndex = 1;
            events.Items.Clear();
            eventLines.Clear();
            cancel = new CancellationTokenSource();
            progress.Style = ProgressBarStyle.Marquee;
            try
            {
                var runner = new ProjectRunner();
                runner.Progress += e => incoming.Enqueue(e);
                result = await runner.RunAsync(project, options, preview, cancel.Token);
                report.Text = ProjectRunner.ReportText(result);
                pages.SelectedIndex = 2;
            }
            catch (Exception ex) { report.Text = ex.Message; incoming.Enqueue(new RunEvent { Level = "ERROR", Message = ex.Message }); pages.SelectedIndex = 2; }
            finally { Drain(); running = false; stop.Enabled = false; pages.TabPages[0].Enabled = true; start.Enabled = true; progress.Style = ProgressBarStyle.Continuous; progress.Value = result?.Status.StartsWith("Completed") == true ? 100 : 0; cancel.Dispose(); cancel = null; }
        }
        void Drain()
        {
            if (incoming.IsEmpty)
                return;
            events.BeginUpdate();
            int count = 0;
            while (count++ < 300 && incoming.TryDequeue(out var e))
            {
                if (e.Level == "TRACE")
                    continue;
                eventLines.Add($"{e.Time:yyyy-MM-dd HH:mm:ss} | {e.Level} | {e.Stage} | {e.Message}");
                var item = new ListViewItem(new[] { e.Time.ToString("yyyy-MM-dd HH:mm:ss"), e.Level, e.Stage, e.Message });
                if (e.Level == "ERROR")
                    item.ForeColor = System.Drawing.Color.Firebrick;
                events.Items.Add(item);
                if (events.Items.Count > 5000)
                    events.Items.RemoveAt(0);
                status.Text = e.Message;
                if (e.TotalBytes > 0)
                {
                    progress.Style = ProgressBarStyle.Continuous;
                    progress.Value = (int)Math.Min(100, (double)e.Bytes * 100 / e.TotalBytes);
                    status.Text += $" · {progress.Value}%" + (e.RemainingSeconds.HasValue ? " · ETA " + TimeSpan.FromSeconds(e.RemainingSeconds.Value) : "");
                }
            }
            if (events.Items.Count > 0)
                events.Items[events.Items.Count - 1].EnsureVisible();
            events.EndUpdate();
        }
        void SaveText()
        {
            using (var d = new SaveFileDialog { Filter = "Text report|*.txt", FileName = "DSNForge-report.txt" })
                if (d.ShowDialog(this) == DialogResult.OK)
                    File.WriteAllText(d.FileName, report.Text + "\r\n\r\n" + string.Join("\r\n", eventLines), System.Text.Encoding.UTF8);
        }
    }
}
