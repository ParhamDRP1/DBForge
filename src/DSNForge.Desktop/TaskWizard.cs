using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DSNForge.Core;

namespace DSNForge.Desktop
{
    internal sealed class TaskWizard : Form
    {
        public TransferStep Step
        {
            get; private set;
        }
        readonly List<ConnectionProfile> profiles; readonly TabControl pages = new TabControl { Dock = DockStyle.Fill };
        readonly ComboBox profile = new ComboBox { Width = 400, DropDownStyle = ComboBoxStyle.DropDownList };
        readonly ComboBox table = new ComboBox { Width = 400, DropDownStyle = ComboBoxStyle.DropDown };
        readonly ComboBox target = new ComboBox { Width = 400, DropDownStyle = ComboBoxStyle.DropDown };
        readonly ComboBox operation = new ComboBox { Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
        readonly TextBox name = Ui.Text(), file = Ui.Text(580), metadataPath = Ui.Text(580), query = Ui.Multiline(), ddl = Ui.Multiline();
        readonly Label connectionInfo = Ui.Label(""), databaseTest = Ui.Label("Database: not tested"), ftpTest = Ui.Label("FTP: not tested"), catalogStatus = Ui.Label("Select a profile, then read catalog or enter a table name.");
        readonly CheckBox create = new CheckBox { Text = "Create target table if missing (review SQL below)", AutoSize = true };
        readonly DataGridView columns = new DataGridView { Dock = DockStyle.Fill, AutoGenerateColumns = true, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells };
        readonly PropertyGrid options = new PropertyGrid { Dock = DockStyle.Fill };
        readonly SecretEditor archive;
        readonly Button back, next; bool reading; bool? dbPassed, ftpPassed;
        string layoutSource;
        public TaskWizard(List<ConnectionProfile> profiles, TransferStep step)
        {
            this.profiles = profiles;
            Step = ConnectionProfile.JsonCopy(step);
            Ui.Setup(this, "Task wizard · " + step.Name, 1200, 850);
            var p1 = Ui.Page(pages, "1 · Connection");
            var p2 = Ui.Page(pages, "2 · Catalog / SQL");
            var p3 = Ui.Page(pages, "3 · Operation / files");
            var p4 = Ui.Page(pages, "4 · CSV layout");
            var p5 = Ui.Page(pages, "5 · DDL / metadata");
            var p6 = Ui.Page(pages, "6 · Review / cards");
            var connection = Ui.Flow();
            connection.Controls.Add(Ui.Label("Connection profile"));
            connection.Controls.Add(profile);
            connection.Controls.Add(Ui.Button("New connection profile", () => { using (var f = new ProfileEditor(new ConnectionProfile())) if (f.ShowDialog(this) == DialogResult.OK) { profiles.Add(f.Profile); ProfileStore.Save(profiles); BindProfiles(f.Profile.Id); } }));
            connection.Controls.Add(Ui.Button("Test database", async () => await Test(false)));
            connection.Controls.Add(databaseTest);
            connection.Controls.Add(Ui.Button("Test FTP", async () => await Test(true)));
            connection.Controls.Add(ftpTest);
            connection.Controls.Add(connectionInfo);
            p1.Controls.Add(connection);
            profile.SelectedIndexChanged += (s, e) => { var c = profile.SelectedItem as ConnectionProfile; dbPassed = ftpPassed = null; databaseTest.Text = "Database: not tested"; ftpTest.Text = "FTP: not tested"; connectionInfo.Text = c == null ? "" : $"\r\nDatabase: {c.Database.Provider} · {c.Database.Host}:{c.Database.Port} · {c.Database.Catalog} · {c.Database.User}\r\nFTP: {c.Ftp.Host}:{c.Ftp.Port} · {c.Ftp.User} · {c.Ftp.Representation} · TLS={c.Ftp.ExplicitTls}\r\nNext does not test the connection."; };
            BindProfiles(Step.ConnectionProfileId);
            var catalog = Ui.Flow();
            operation.Items.AddRange(Enum.GetValues(typeof(Operation)).Cast<object>().ToArray());
            operation.SelectedItem = Step.Operation;
            name.Text = Step.Name;
            table.Text = Step.Table;
            target.Text = Step.TargetTable;
            catalog.Controls.AddRange(new Control[] { Ui.Label("Task name"), name, Ui.Label("Operation"), operation, Ui.Label("Source / LOAD target table"), table, Ui.Label("TRANSFER target"), target });
            catalog.Controls.Add(Ui.Button("Read catalog", async () => await ReadCatalog()));
            catalog.Controls.Add(catalogStatus);
            p2.Controls.Add(query);
            p2.Controls.Add(Ui.Label("Optional UNLOAD SELECT query (blank = whole table). Query results use streamed CSV export."));
            p2.Controls.Add(catalog);
            query.Text = Step.UnloadQuery;
            var files = Ui.Flow();
            file.Text = Step.LocalFile;
            metadataPath.Text = Step.MetadataFile;
            files.Controls.Add(Ui.Label("Data file / ZIP"));
            files.Controls.Add(file);
            files.Controls.Add(Ui.Button("Browse…", Browse));
            files.Controls.Add(Ui.Label("Metadata (.dbmeta)"));
            files.Controls.Add(metadataPath);
            files.Controls.Add(Ui.Button("Read dbmeta / ZIP", async () => await ReadMetadata()));
            options.SelectedObject = new PropertyView(Step, "Name", "Operation", "ConnectionProfileId", "Table", "TargetTable", "LocalFile", "MetadataFile", "UnloadQuery", "CreateTableIfMissing", "CreateScript", "Columns");
            p3.Controls.Add(options);
            p3.Controls.Add(files);
            archive = new SecretEditor("ZIP password (blank runtime prompt can mean no output encryption)", Step.ArchivePassword);
            p3.Controls.Add(archive);
            p4.Controls.Add(columns);
            var layoutBar = Ui.Flow();
            layoutBar.Controls.Add(Ui.Button("Read table / query layout", async () => await ReadLayout()));
            layoutBar.Controls.Add(Ui.Label("Included ✓ selects fields. CodePage=0 inherits LocalCodePage. Trim defaults to None. Mixed encodings must be ASCII-compatible."));
            p4.Controls.Add(layoutBar);
            BindColumns();
            p5.Controls.Add(ddl);
            var ddlBar = Ui.Flow();
            create.Checked = Step.CreateTableIfMissing;
            ddl.Text = Step.CreateScript;
            ddlBar.Controls.Add(create);
            ddlBar.Controls.Add(Ui.Label("Review target names, tablespace and allocation. dbmeta is editable; no DDL runs while editing."));
            p5.Controls.Add(ddlBar);
            var cards = Ui.Multiline();
            p6.Controls.Add(cards);
            var review = Ui.Flow();
            review.Controls.Add(Ui.Button("Refresh review", () => { CaptureValues(); cards.Text = string.IsNullOrWhiteSpace(Step.UnloadQuery) ? (!string.IsNullOrWhiteSpace(Step.MetadataLoadCards) ? Step.MetadataLoadCards : ControlBuilder.Build(Step, "{dataset}")) : MetadataCatalog.SelectQuery(Step.UnloadQuery); }));
            review.Controls.Add(Ui.Button("Apply edited cards", () => { if (!string.IsNullOrWhiteSpace(Step.UnloadQuery)) throw new InvalidOperationException("Edit SQL on the Catalog / SQL page."); if (Step.Input == LoadInput.GeneratedCards) Step.MetadataLoadCards = cards.Text; else { Step.ControlStatements = cards.Text; Step.BatchRecords = 0; } options.Refresh(); }));
            review.Controls.Add(Ui.Label("Native UNLOAD generates LOAD cards. They are saved beside the file and embedded in dbmeta."));
            p6.Controls.Add(review);
            var navigation = Ui.Flow();
            navigation.Dock = DockStyle.Bottom;
            back = Ui.Button("← Back", () => pages.SelectedIndex = Math.Max(0, pages.SelectedIndex - 1));
            next = Ui.Button("Next →", async () => { CaptureValues(); if (profile.SelectedItem == null) throw new InvalidOperationException("Select a connection profile."); pages.SelectedIndex = Math.Min(pages.TabCount - 1, pages.SelectedIndex + 1); if (pages.SelectedIndex == 1 && table.Items.Count == 0) await ReadCatalog(); if (pages.SelectedIndex == 3 && (Step.Columns.Count == 0 || layoutSource != LayoutSource())) await ReadLayout(); });
            navigation.Controls.AddRange(new Control[] { back, next, Ui.Button("Save task", Save), Ui.Button("Cancel", () => Close()) });
            Controls.Add(pages);
            Controls.Add(navigation);
            layoutSource = LayoutSource();
            file.TextChanged += (sender, args) => Step.MetadataApplied = false;
            metadataPath.TextChanged += (sender, args) => Step.MetadataApplied = false;
            Action refreshOperation = () => { var op = (Operation)operation.SelectedItem; query.Enabled = op == Operation.Unload; target.Enabled = op == Operation.Transfer; create.Enabled = op != Operation.Unload; };
            operation.SelectedIndexChanged += (sender, args) => refreshOperation();
            refreshOperation();
            FormClosing += (s, e) => { if (reading) e.Cancel = true; };
        }
        void BindProfiles(string id)
        {
            profile.Items.Clear();
            profile.Items.AddRange(profiles.Cast<object>().ToArray());
            profile.SelectedItem = profiles.FirstOrDefault(p => p.Id == id) ?? profiles.FirstOrDefault();
        }
        void BindColumns()
        {
            columns.DataSource = new BindingList<TextColumn>(Step.Columns);
        }
        string LayoutSource() => (profile.SelectedItem as ConnectionProfile)?.Id + "|" + operation.SelectedItem + "|" + table.Text + "|" + target.Text + "|" + query.Text;
        void CaptureValues()
        {
            columns.EndEdit();
            Step.Name = name.Text.Trim();
            Step.ConnectionProfileId = (profile.SelectedItem as ConnectionProfile)?.Id ?? "";
            Step.Operation = (Operation)operation.SelectedItem;
            Step.Table = table.Text.Trim();
            Step.TargetTable = target.Text.Trim();
            Step.LocalFile = file.Text;
            Step.MetadataFile = metadataPath.Text;
            Step.UnloadQuery = query.Text.Trim();
            Step.CreateTableIfMissing = create.Checked;
            Step.CreateScript = ddl.Text;
            Step.ArchivePassword = archive.CaptureValues();
        }
        void Save()
        {
            if (reading)
                return;
            CaptureValues();
            if (string.IsNullOrWhiteSpace(Step.Name) || string.IsNullOrEmpty(Step.ConnectionProfileId))
                throw new InvalidOperationException("Task name and connection profile are required.");
            MetadataCatalog.Identifier(Step.Table);
            if (Step.Operation == Operation.Transfer)
                MetadataCatalog.Identifier(Step.TargetTable);
            if (!string.IsNullOrWhiteSpace(Step.UnloadQuery))
                MetadataCatalog.SelectQuery(Step.UnloadQuery);
            if ((dbPassed == false || ftpPassed == false) && MessageBox.Show(this, "A test failed. Save the task anyway?", "Connection", MessageBoxButtons.YesNo) != DialogResult.Yes)
                return;
            DialogResult = DialogResult.OK;
            Close();
        }
        async Task Test(bool ftp)
        {
            if (reading)
                return;
            try
            {
                var p = (ConnectionProfile)profile.SelectedItem ?? throw new InvalidOperationException("Select a profile.");
                reading = true;
                string password = (ftp ? p.FtpSecret : p.DatabaseSecret).Resolve();
                if (string.IsNullOrEmpty(password))
                    password = Ui.Prompt(this, ftp ? "FTP password" : "Database password");
                if (password == null)
                    return;
                await Task.Run(() => { if (ftp) { using (var f = new MainframeFtp(p.Ftp, password, null, CancellationToken.None)) { } } else { using (var c = ProviderFactory.Create(p.Database, password)) c.Open(); } });
                if (ftp)
                    ftpPassed = true;
                else
                    dbPassed = true;
                (ftp ? ftpTest : databaseTest).Text = "✓ Connected";
            }
            catch (Exception ex) { if (ftp) ftpPassed = false; else dbPassed = false; (ftp ? ftpTest : databaseTest).Text = "✗ Failed"; MessageBox.Show(this, ex.Message, "Test"); }
            finally { reading = false; }
        }
        async Task ReadCatalog()
        {
            if (reading)
                return;
            try
            {
                var p = (ConnectionProfile)profile.SelectedItem ?? throw new InvalidOperationException("Select a profile.");
                var c = Ui.Secrets(this, p, false);
                reading = true;
                catalogStatus.Text = "Reading catalog…";
                var names = await Task.Run(() => MetadataCatalog.Tables(p.Database, c.Db2Password));
                string original = table.Text, dest = target.Text;
                table.Items.Clear();
                table.Items.AddRange(names.ToArray());
                target.Items.Clear();
                target.Items.AddRange(names.ToArray());
                table.Text = original;
                target.Text = dest;
                catalogStatus.Text = names.Count + " tables. New target names can be typed.";
            }
            catch (Exception ex) { catalogStatus.Text = "Catalog unavailable; manual entry is available."; MessageBox.Show(this, ex.Message, "Catalog"); }
            finally { reading = false; }
        }
        async Task ReadLayout()
        {
            if (reading)
                return;
            try
            {
                CaptureValues();
                var p = (ConnectionProfile)profile.SelectedItem ?? throw new InvalidOperationException("Select a profile.");
                var c = Ui.Secrets(this, p, false);
                reading = true;
                if (!string.IsNullOrWhiteSpace(Step.UnloadQuery))
                    Step.Columns = await Task.Run(() => MetadataCatalog.QueryLayout(p.Database, c.Db2Password, Step.UnloadQuery));
                else
                {
                    var m = await Task.Run(() => MetadataCatalog.Describe(p.Database, c.Db2Password, Step.Operation == Operation.Transfer && !create.Checked ? Step.TargetTable : Step.Table));
                    Step.Columns = m.Columns;
                    if (string.IsNullOrWhiteSpace(ddl.Text))
                        ddl.Text = m.CreateScript;
                }
                BindColumns();
                layoutSource = LayoutSource();
                options.Refresh();
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message + "\r\nYou can import dbmeta or enter the layout manually.", "Layout"); }
            finally { reading = false; }
        }
        void Browse()
        {
            if ((Operation)operation.SelectedItem == Operation.Unload)
            {
                using (var d = new SaveFileDialog { Filter = "CSV data|*.csv|All files|*.*" })
                    if (d.ShowDialog(this) == DialogResult.OK)
                        file.Text = d.FileName;
            }
            else
            {
                using (var d = new OpenFileDialog { Filter = "Data files|*.csv;*.zip;*.bin;*.txt|All files|*.*" })
                    if (d.ShowDialog(this) == DialogResult.OK)
                        file.Text = d.FileName;
            }
        }
        async Task ReadMetadata()
        {
            if (reading)
                return;
            string temp = null;
            try
            {
                string path = metadataPath.Text;
                if (string.IsNullOrWhiteSpace(path) && File.Exists(file.Text))
                {
                    if (ArchiveFiles.IsZip(file.Text))
                    {
                        string password = ArchiveFiles.IsProtected(file.Text) ? Ui.Prompt(this, "ZIP password") : null;
                        if (ArchiveFiles.IsProtected(file.Text) && password == null)
                            return;
                        temp = Path.Combine(Path.GetTempPath(), "DSNForge-" + Guid.NewGuid().ToString("N"));
                        reading = true;
                        path = await Task.Run(() => ArchiveFiles.ExtractMetadata(file.Text, temp, password));
                    }
                    else
                        path = file.Text + ".dbmeta";
                }
                if (!File.Exists(path))
                {
                    using (var d = new OpenFileDialog { Filter = "DB2 metadata|*.dbmeta" })
                    {
                        if (d.ShowDialog(this) != DialogResult.OK)
                            return;
                        path = d.FileName;
                    }
                }
                var m = DbMetadata.Load(path);
                m.Apply(Step);
                ddl.Text = m.CreateScript;
                table.Text = Step.Table;
                BindColumns();
                layoutSource = LayoutSource();
                options.Refresh();
                if (temp == null)
                    metadataPath.Text = path;
                Step.MetadataApplied = true;
                MessageBox.Show(this, "Metadata loaded. Review DDL, layout and target table before saving.\r\n" + string.Join("\r\n", m.Notes), "dbmeta");
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Read metadata"); }
            finally { reading = false; if (temp != null && Directory.Exists(temp)) Directory.Delete(temp, true); }
        }
    }
}
