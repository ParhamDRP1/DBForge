using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DSNForge.Core;
namespace DSNForge.Desktop
{
    public sealed class MainForm : Form
    {
        TransferProject project = new TransferProject(); string projectPath;
        readonly ListView tasks = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, CheckBoxes = true };
        readonly TextBox name = Ui.Text(350), reports = Ui.Text(650); bool binding;
        public MainForm(TransferProject initial = null)
        {
            Ui.Setup(this, "DSNForge · Project studio", 1300, 850);
            DoubleBuffered = true;
            var menu = new MenuStrip();
            var file = new ToolStripMenuItem("Project");
            var connections = new ToolStripMenuItem("Connections");
            file.DropDownItems.Add("New", null, (s, e) => { project = NewProject(); projectPath = null; Bind(); });
            file.DropDownItems.Add("Open…", null, (s, e) => Guard(Open));
            file.DropDownItems.Add("Save…", null, (s, e) => Guard(Save));
            connections.DropDownItems.Add("Manage connection profiles…", null, (s, e) => Guard(() => { using (var manager = new ProfileManager(project.Profiles)) manager.ShowDialog(this); Bind(); }));
            menu.Items.Add(file);
            menu.Items.Add(connections);
            MainMenuStrip = menu;
            var header = new Label { Text = "DSNForge  /  Transfer projects", Dock = DockStyle.Top, Height = 75, Font = new Font("Segoe UI", 23, FontStyle.Bold), ForeColor = Color.White, BackColor = Color.FromArgb(18, 40, 65), Padding = new Padding(20, 14, 0, 0) };
            var settings = Ui.Flow();
            settings.Controls.Add(Ui.Label("Project"));
            settings.Controls.Add(name);
            settings.Controls.Add(Ui.Label("Reports"));
            settings.Controls.Add(reports);
            tasks.Columns.Add("Task", 260);
            tasks.Columns.Add("Operation", 110);
            tasks.Columns.Add("Connection profile", 220);
            tasks.Columns.Add("Table", 280);
            tasks.Columns.Add("Data file", 350);
            tasks.DoubleClick += (s, e) => Guard(Edit);
            tasks.ItemChecked += (s, e) => { if (!binding && e.Item.Tag is TransferStep step) step.Enabled = e.Item.Checked; };
            var bar = Ui.Flow();
            bar.Dock = DockStyle.Bottom;
            bar.Controls.Add(Ui.Button("New task wizard", Add));
            bar.Controls.Add(Ui.Button("Edit task wizard", Edit));
            bar.Controls.Add(Ui.Button("Clone task", () => { var s = Selected; if (s == null) return; var copy = ConnectionProfile.JsonCopy(s); copy.Name += " copy"; copy.Id = Guid.NewGuid().ToString("N"); project.Steps.Add(copy); Bind(); }));
            bar.Controls.Add(Ui.Button("Remove", () => { if (Selected != null) { project.Steps.Remove(Selected); Bind(); } }));
            bar.Controls.Add(Ui.Button("↑", () => MoveTask(-1)));
            bar.Controls.Add(Ui.Button("↓", () => MoveTask(1)));
            bar.Controls.Add(Ui.Button("Validate / preview…", () => Run(true)));
            bar.Controls.Add(Ui.Button("Run wizard…", () => Run(false)));
            Controls.Add(tasks);
            Controls.Add(bar);
            Controls.Add(settings);
            Controls.Add(header);
            Controls.Add(menu);
            project = initial ?? NewProject();
            Bind();
        }
        TransferStep Selected => tasks.SelectedItems.Count == 0 ? null : (TransferStep)tasks.SelectedItems[0].Tag;
        static TransferProject NewProject() => new TransferProject { Profiles = ProfileStore.Load(), ReportDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DSNForge", "runs") };
        void Guard(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "DSNForge", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
        void CaptureValues()
        {
            project.Name = name.Text;
            project.ReportDirectory = reports.Text;
        }
        void Bind()
        {
            binding = true;
            tasks.BeginUpdate();
            tasks.Items.Clear();
            name.Text = project.Name;
            reports.Text = project.ReportDirectory;
            foreach (var s in project.Steps)
                tasks.Items.Add(new ListViewItem(new[] { s.Name, s.Operation.ToString(), project.Profiles.FirstOrDefault(x => x.Id == s.ConnectionProfileId)?.Name ?? "Select profile", s.Table, s.LocalFile }) { Tag = s, Checked = s.Enabled });
            tasks.EndUpdate();
            binding = false;
        }
        void Add()
        {
            CaptureValues();
            using (var wizard = new TaskWizard(project.Profiles, new TransferStep { Name = "Task " + (project.Steps.Count + 1) }))
                if (wizard.ShowDialog(this) == DialogResult.OK)
                {
                    project.Steps.Add(wizard.Step);
                    Bind();
                }
        }
        void Edit()
        {
            if (Selected == null)
                return;
            CaptureValues();
            int index = project.Steps.IndexOf(Selected);
            using (var wizard = new TaskWizard(project.Profiles, Selected))
                if (wizard.ShowDialog(this) == DialogResult.OK)
                {
                    project.Steps[index] = wizard.Step;
                    Bind();
                }
        }
        void MoveTask(int delta)
        {
            var s = Selected;
            if (s == null)
                return;
            int from = project.Steps.IndexOf(s), to = from + delta;
            if (to < 0 || to >= project.Steps.Count)
                return;
            CaptureValues();
            project.Steps.RemoveAt(from);
            project.Steps.Insert(to, s);
            Bind();
            tasks.Items[to].Selected = true;
        }
        void Open()
        {
            using (var d = new OpenFileDialog { Filter = "DSNForge project|*.json" })
                if (d.ShowDialog(this) == DialogResult.OK)
                {
                    project = ProjectStore.Load(d.FileName);
                    ProfileStore.Migrate(project);
                    projectPath = d.FileName;
                    Bind();
                }
        }
        void Save()
        {
            CaptureValues();
            using (var d = new SaveFileDialog { Filter = "DSNForge project|*.json", FileName = projectPath ?? "project.dsnforge.json" })
                if (d.ShowDialog(this) == DialogResult.OK)
                {
                    ProjectStore.Save(d.FileName, project);
                    projectPath = d.FileName;
                }
        }
        void Run(bool preview)
        {
            CaptureValues();
            if (!project.Steps.Any(s => s.Enabled))
                throw new InvalidOperationException("Enable at least one task.");
            using (var wizard = new RunWizard(project, preview))
                wizard.ShowDialog(this);
        }
    }
}
