using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DSNForge.Core;
namespace DSNForge.Desktop
{
    public class MainForm : Form
    {
        TransferProject project = new TransferProject(); string projectPath; CancellationTokenSource cancellation; bool busy; RunReport lastReport;
        readonly ListBox steps = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, Font = new Font("Segoe UI", 11) };
        readonly PropertyGrid stepProperties = new PropertyGrid { Dock = DockStyle.Fill, HelpVisible = true, ToolbarVisible = true };
        readonly PropertyGrid dbProperties = new PropertyGrid { Dock = DockStyle.Fill }, ftpProperties = new PropertyGrid { Dock = DockStyle.Fill };
        readonly TextBox projectName = new TextBox { Width = 260 }, reportDirectory = new TextBox { Width = 330 }, dbPassword = new TextBox { Width = 220, UseSystemPasswordChar = true }, ftpPassword = new TextBox { Width = 220, UseSystemPasswordChar = true };
        readonly TextBox cards = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", 11) };
        readonly TextBox summary = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Segoe UI", 11) };
        readonly DataGridView layout = new DataGridView { Dock = DockStyle.Fill, AutoGenerateColumns = true, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = Color.White, BorderStyle = BorderStyle.None };
        readonly ListView events = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = false };
        readonly ConcurrentQueue<RunEvent> incoming = new ConcurrentQueue<RunEvent>(); readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 250 };
        readonly ProgressBar progress = new ProgressBar { Dock = DockStyle.Top, Height = 10 }; readonly Label status = new Label { Dock = DockStyle.Fill, Text = "Ready · create or open a transfer project", Padding = new Padding(8), AutoEllipsis = true };
        readonly ToolStrip toolbar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(8), ImageScalingSize = new Size(20, 20) }; readonly List<ToolStripItem> mutating = new List<ToolStripItem>(); ToolStripButton stop;
        readonly TabControl tabs = new TabControl { Dock = DockStyle.Fill }; readonly SplitContainer split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 260, FixedPanel = FixedPanel.Panel1 };
        public MainForm()
        {
            Text = "DSNForge 1.1 · DB2 Data Utility Studio";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            MinimumSize = new Size(1050, 730);
            Size = new Size(1360, 900);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 10);
            BackColor = Color.FromArgb(244, 247, 251);
            DoubleBuffered = true;
            var header = new Panel { Dock = DockStyle.Top, Height = 76, BackColor = Color.FromArgb(18, 40, 65) };
            header.Controls.Add(new Label { Text = "DSNForge", ForeColor = Color.White, Font = new Font("Segoe UI", 24, FontStyle.Bold), AutoSize = true, Location = new Point(20, 10) });
            header.Controls.Add(new Label { Text = "LOAD / UNLOAD  ·  Projects  ·  DSNUTILU", ForeColor = Color.LightSteelBlue, AutoSize = true, Location = new Point(330, 32) });
            AddButton("New", () => NewProject());
            AddButton("Open", () => OpenProject());
            AddButton("Save", () => SaveProject());
            toolbar.Items.Add(new ToolStripSeparator());
            AddButton("Validate", () => ValidateProject());
            AddButton("DB2 Preview", async () => await Execute(true));
            AddButton("Run project", async () => await Execute(false));
            stop = new ToolStripButton("Stop");
            stop.Enabled = false;
            stop.Click += (s, e) => { cancellation?.Cancel(); status.Text = "Cancellation requested. A running server utility may require inspection by UtilityId."; stop.Enabled = false; };
            toolbar.Items.Add(stop);
            toolbar.Items.Add(new ToolStripSeparator());
            AddButton("Export report", () => ExportReport(), false);
            AddButton("Open reports", () => { Directory.CreateDirectory(project.ReportDirectory); Process.Start(Path.GetFullPath(project.ReportDirectory)); }, false);
            var taskHeader = new Label { Dock = DockStyle.Top, Height = 38, Text = "  TRANSFER STEPS", Font = new Font(Font, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft };
            var taskButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 160 };
            foreach (string name in new[] { "+ LOAD", "+ UNLOAD", "+ TRANSFER", "Remove", "↑", "↓" })
            {
                var b = new Button { Text = name, AutoSize = true, FlatStyle = FlatStyle.Flat };
                b.Click += (s, e) => TaskAction(name);
                taskButtons.Controls.Add(b);
            }
            split.Panel1.Controls.Add(steps);
            split.Panel1.Controls.Add(taskHeader);
            split.Panel1.Controls.Add(taskButtons);
            split.Panel2.Controls.Add(tabs);
            var taskPage = Page("Task settings");
            taskPage.Controls.Add(stepProperties);
            var fileBar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42 };
            var choose = new Button { Text = "Choose file…", AutoSize = true };
            choose.Click += (s, e) => ChooseFile();
            fileBar.Controls.Add(choose);
            var readLayout = new Button { Text = "Read table layout", AutoSize = true };
            readLayout.Click += async (s, e) => { if (busy || Selected == null) return; var task = Selected; readLayout.Enabled = false; try { var password = dbPassword.Text.Length > 0 ? dbPassword.Text : Credentials.FromEnvironment(project).Db2Password; var columns = await Task.Run(() => Catalog.ReadTextLayout(project.Db2, password, task.Table)); task.Columns = columns; BindStep(); tabs.SelectedIndex = 2; } catch (Exception ex) { MessageBox.Show(ex.Message, "Table layout"); } finally { readLayout.Enabled = true; } };
            fileBar.Controls.Add(readLayout);
            var tip = new Label { Text = "Edit categorized options below; advanced options accept full utility cards.", AutoSize = true, Padding = new Padding(8) };
            fileBar.Controls.Add(tip);
            taskPage.Controls.Add(fileBar);
            var connection = Page("Connections");
            var connectionsSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 400 };
            connectionsSplit.Panel1.Controls.Add(dbProperties);
            connectionsSplit.Panel2.Controls.Add(ftpProperties);
            connection.Controls.Add(connectionsSplit);
            var passwords = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 64 };
            passwords.Controls.Add(new Label { Text = "DB2 password", AutoSize = true });
            passwords.Controls.Add(dbPassword);
            passwords.Controls.Add(new Label { Text = "FTP password", AutoSize = true });
            passwords.Controls.Add(ftpPassword);
            passwords.Controls.Add(new Label { Text = "Session only · never saved in project files", AutoSize = true });
            connection.Controls.Add(passwords);
            var layoutPage = Page("CSV layout");
            layoutPage.Controls.Add(layout);
            layoutPage.Controls.Add(new Label { Dock = DockStyle.Top, Height = 45, Text = "CSV packing preserves empty strings and whitespace. Define text columns in order; widths are UTF-8 bytes.\nNumeric/date/binary/native layouts: use NativeFile with explicit LOAD cards.", Padding = new Padding(6) });
            var controlPage = Page("Utility cards");
            controlPage.Controls.Add(cards);
            var controlButtons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40 };
            var generate = new Button { Text = "Generate preview", AutoSize = true };
            generate.Click += (s, e) => PreviewCards();
            var apply = new Button { Text = "Use as custom cards", AutoSize = true };
            apply.Click += (s, e) => { if (!busy && Selected != null) { Selected.ControlStatements = cards.Text; stepProperties.Refresh(); } };
            var reset = new Button { Text = "Use generated cards", AutoSize = true };
            reset.Click += (s, e) => { if (!busy && Selected != null) { Selected.ControlStatements = ""; PreviewCards(); stepProperties.Refresh(); } };
            controlButtons.Controls.AddRange(new Control[] { generate, apply, reset });
            controlPage.Controls.Add(controlButtons);
            events.Columns.Add("Time", 155);
            events.Columns.Add("Level", 70);
            events.Columns.Add("Stage", 115);
            events.Columns.Add("Message", 950);
            Page("Live events").Controls.Add(events);
            Page("Report").Controls.Add(summary);
            var projectPage = Page("Project");
            var projectPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(20) };
            projectPanel.Controls.Add(new Label { Text = "Project name", AutoSize = true });
            projectPanel.Controls.Add(projectName);
            projectPanel.Controls.Add(new Label { Text = "Report directory (JSON, CSV, HTML, JSONL)", AutoSize = true });
            projectPanel.Controls.Add(reportDirectory);
            projectPanel.Controls.Add(new Label { Text = "Tasks run in list order. For combined transfers, use a Transfer task or sequence UNLOAD/LOAD tasks.\nAll native utility/FTP SITE switches remain available through expert settings.\nStopping the client does not prove that a server utility rolled back. Inspect its UtilityId before rerunning.", AutoSize = true, MaximumSize = new Size(700, 0) });
            projectPage.Controls.Add(projectPanel);
            var footer = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = Color.White };
            footer.Controls.Add(status);
            footer.Controls.Add(progress);
            Controls.Add(split);
            Controls.Add(footer);
            Controls.Add(toolbar);
            Controls.Add(header);
            toolbar.Dock = DockStyle.Top;
            steps.SelectedIndexChanged += (s, e) => BindStep();
            stepProperties.PropertyValueChanged += (s, e) => { RefreshSteps(); };
            timer.Tick += (s, e) => Drain();
            timer.Start();
            FormClosing += (s, e) => { if (busy) { e.Cancel = true; MessageBox.Show("Stop the operation and wait for its report before closing.", "DSNForge"); } };
            Shown += (s, e) => { split.SplitterDistance = 280; };
            project.Steps.Add(new TransferStep { Name = "Load data" });
            project.ReportDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DSNForge", "runs");
            BindProject();
        }
        TransferStep Selected => steps.SelectedItem as TransferStep;
        TabPage Page(string name)
        {
            var p = new TabPage(name) { BackColor = Color.White, Padding = new Padding(6) };
            tabs.TabPages.Add(p);
            return p;
        }
        void AddButton(string text, Action action, bool mutation = true)
        {
            var b = new ToolStripButton(text);
            b.Click += (s, e) => { try { action(); } catch (Exception ex) { MessageBox.Show(ex.Message, "DSNForge", MessageBoxButtons.OK, MessageBoxIcon.Error); } };
            toolbar.Items.Add(b);
            if (mutation)
                mutating.Add(b);
        }
        void CaptureEdits()
        {
            layout.EndEdit();
            project.Name = projectName.Text;
            project.ReportDirectory = reportDirectory.Text;
        }
        void BindProject()
        {
            dbProperties.SelectedObject = project.Db2;
            ftpProperties.SelectedObject = project.Ftp;
            projectName.Text = project.Name;
            reportDirectory.Text = project.ReportDirectory;
            RefreshSteps();
        }
        void RefreshSteps()
        {
            int index = steps.SelectedIndex;
            steps.DataSource = null;
            steps.DataSource = project.Steps;
            steps.DisplayMember = "Name";
            if (project.Steps.Count > 0)
                steps.SelectedIndex = Math.Max(0, Math.Min(index, project.Steps.Count - 1));
        }
        void BindStep()
        {
            stepProperties.SelectedObject = Selected;
            layout.DataSource = Selected == null ? null : new System.ComponentModel.BindingList<TextColumn>(Selected.Columns);
            cards.Text = "";
        }
        void NewProject()
        {
            if (busy)
                return;
            project = new TransferProject();
            projectPath = null;
            BindProject();
        }
        void OpenProject()
        {
            using (var d = new OpenFileDialog { Filter = "DSNForge project (*.json)|*.json" })
                if (d.ShowDialog() == DialogResult.OK)
                {
                    project = ProjectStore.Load(d.FileName);
                    projectPath = d.FileName;
                    BindProject();
                }
        }
        void SaveProject()
        {
            CaptureEdits();
            using (var d = new SaveFileDialog { Filter = "DSNForge project (*.json)|*.json", FileName = projectPath ?? "project.dsnforge.json" })
                if (d.ShowDialog() == DialogResult.OK)
                {
                    ProjectStore.Save(d.FileName, project);
                    projectPath = d.FileName;
                    status.Text = "Project saved · " + projectPath;
                }
        }
        void TaskAction(string action)
        {
            if (busy)
                return;
            CaptureEdits();
            if (action.StartsWith("+"))
            {
                project.Steps.Add(new TransferStep { Name = action.Substring(2) + " " + (project.Steps.Count + 1), Operation = action.Contains("UNLOAD") ? Operation.Unload : action.Contains("TRANSFER") ? Operation.Transfer : Operation.Load });
                RefreshSteps();
                steps.SelectedIndex = project.Steps.Count - 1;
            }
            else if (Selected != null)
            {
                int i = steps.SelectedIndex;
                if (action == "Remove")
                    project.Steps.RemoveAt(i);
                else
                {
                    int j = i + (action == "↑" ? -1 : 1);
                    if (j >= 0 && j < project.Steps.Count)
                    {
                        var item = project.Steps[i];
                        project.Steps.RemoveAt(i);
                        project.Steps.Insert(j, item);
                    }
                }
                RefreshSteps();
            }
        }
        void ChooseFile()
        {
            if (busy || Selected == null)
                return;
            if (Selected.Operation == Operation.Unload)
            {
                using (var d = new SaveFileDialog())
                    if (d.ShowDialog() == DialogResult.OK)
                        Selected.LocalFile = d.FileName;
            }
            else
            {
                using (var d = new OpenFileDialog())
                    if (d.ShowDialog() == DialogResult.OK)
                        Selected.LocalFile = d.FileName;
            }
            stepProperties.Refresh();
        }
        void PreviewCards()
        {
            CaptureEdits();
            if (Selected != null)
            {
                string ds = ControlBuilder.Dataset(Selected, "RPREVIEW", 1);
                cards.Text = ControlBuilder.Build(Selected, ds).Replace(ds, "{dataset}").Replace(ControlBuilder.Punch(ds), "{punch}");
            }
        }
        void ValidateProject()
        {
            CaptureEdits();
            var errors = Validation.Check(project);
            MessageBox.Show(errors.Count == 0 ? "Local validation passed. DB2 Preview checks utility syntax on the server." : string.Join(Environment.NewLine, errors), "Validation", MessageBoxButtons.OK, errors.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        async Task Execute(bool preview)
        {
            if (busy)
                return;
            CaptureEdits();
            var errors = Validation.Check(project);
            if (errors.Count > 0)
            {
                MessageBox.Show(string.Join(Environment.NewLine, errors), "Validation");
                return;
            }
            busy = true;
            foreach (var b in mutating)
                b.Enabled = false;
            stepProperties.Enabled = dbProperties.Enabled = ftpProperties.Enabled = layout.Enabled = false;
            stop.Enabled = true;
            progress.Style = ProgressBarStyle.Marquee;
            events.Items.Clear();
            tabs.SelectedIndex = 4;
            cancellation = new CancellationTokenSource();
            lastReport = null;
            var engine = new TransferEngine();
            engine.Progress += e => incoming.Enqueue(e);
            var secrets = Credentials.FromEnvironment(project);
            if (dbPassword.Text.Length > 0)
                secrets.Db2Password = dbPassword.Text;
            if (ftpPassword.Text.Length > 0)
                secrets.FtpPassword = ftpPassword.Text;
            try
            {
                if (preview)
                {
                    await engine.ValidateRemoteAsync(ProjectStore.Clone(project), secrets, cancellation.Token);
                    status.Text = "DB2 PREVIEW passed";
                }
                else
                {
                    lastReport = await engine.RunAsync(project, secrets, cancellation.Token);
                    summary.Text = "Status: " + lastReport.Status + Environment.NewLine + "Run: " + lastReport.RunId + Environment.NewLine + "Started: " + lastReport.Started + Environment.NewLine + "Finished: " + lastReport.Finished + Environment.NewLine + "Error: " + lastReport.Error + Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, lastReport.Batches.Select(x => x.Step + " | " + x.Status + " | RC=" + x.ReturnCode + " | rows=" + x.Records.ToString("N0") + " | FTP=" + x.TransferSeconds.ToString("F2") + "s | Utility=" + x.UtilitySeconds.ToString("F2") + "s"));
                    status.Text = lastReport.Status;
                }
            }
            catch (Exception ex) { incoming.Enqueue(new RunEvent { Level = "ERROR", Message = ex.Message }); status.Text = ex.Message; }
            finally { Drain(); busy = false; foreach (var b in mutating) b.Enabled = true; stepProperties.Enabled = dbProperties.Enabled = ftpProperties.Enabled = layout.Enabled = true; stop.Enabled = false; progress.Style = ProgressBarStyle.Continuous; progress.Value = lastReport?.Status.StartsWith("Completed") == true ? 100 : 0; cancellation.Dispose(); cancellation = null; }
        }
        void Drain()
        {
            events.BeginUpdate();
            int n = 0;
            RunEvent e;
            while (n++ < 300 && incoming.TryDequeue(out e))
            {
                if (e.Level == "TRACE")
                    continue;
                var item = new ListViewItem(new[] { e.Time.ToString("yyyy-MM-dd HH:mm:ss"), e.Level, e.Stage, e.Message });
                if (e.Level == "ERROR")
                    item.ForeColor = Color.Firebrick;
                else if (e.Level == "WARN")
                    item.ForeColor = Color.DarkGoldenrod;
                events.Items.Add(item);
                if (events.Items.Count > 5000)
                    events.Items.RemoveAt(0);
                if (e.TotalBytes > 0)
                {
                    progress.Style = ProgressBarStyle.Continuous;
                    progress.Value = (int)Math.Min(100, e.Bytes * 100 / e.TotalBytes);
                    status.Text = e.Message + " | " + progress.Value + "%" + (e.BytesPerSecond > 0 ? " | " + (e.BytesPerSecond / 1048576).ToString("F2") + " MiB/s" : "") + (e.RemainingSeconds.HasValue ? " | ETA " + TimeSpan.FromSeconds(e.RemainingSeconds.Value).ToString(@"hh\:mm\:ss") : "");
                }
                else if (e.Stage == "Utility")
                {
                    progress.Style = ProgressBarStyle.Marquee;
                    status.Text = e.Message;
                }
            }
            if (events.Items.Count > 0)
                events.Items[events.Items.Count - 1].EnsureVisible();
            events.EndUpdate();
        }
        void ExportReport()
        {
            if (lastReport == null)
            {
                MessageBox.Show("Run a project first.");
                return;
            }
            using (var d = new FolderBrowserDialog())
                if (d.ShowDialog() == DialogResult.OK)
                {
                    Reports.Save(d.SelectedPath, lastReport);
                    Process.Start(d.SelectedPath);
                }
        }
    }
}
