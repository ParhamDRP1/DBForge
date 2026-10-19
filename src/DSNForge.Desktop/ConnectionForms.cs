using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DSNForge.Core;

namespace DSNForge.Desktop
{
    internal static class Ui
    {
        public static Button Button(string title, Action action)
        {
            var b = new Button { Text = title, AutoSize = true, Margin = new Padding(5), MinimumSize = new Size(100, 30) };
            b.Click += (s, e) => { try { action(); } catch (Exception ex) { MessageBox.Show(b.FindForm(), ex.Message, "DSNForge", MessageBoxButtons.OK, MessageBoxIcon.Error); } };
            return b;
        }
        public static FlowLayoutPanel Flow() => new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(8) };
        public static Label Label(string text) => new Label { Text = text, AutoSize = true, Margin = new Padding(5, 10, 5, 5) };
        public static TextBox Text(int width = 320) => new TextBox { Width = width, Margin = new Padding(5) };
        public static TextBox Multiline() => new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", 10) };
        public static TabPage Page(TabControl tabs, string name)
        {
            var page = new TabPage(name) { Padding = new Padding(10) };
            tabs.TabPages.Add(page);
            return page;
        }
        public static void Setup(Form f, string title, int width = 1050, int height = 760)
        {
            f.Text = title;
            f.Size = new Size(width, height);
            f.MinimumSize = new Size(780, 580);
            f.Font = new Font("Segoe UI", 10);
            f.StartPosition = FormStartPosition.CenterParent;
            f.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        public static string Prompt(IWin32Window owner, string title, bool secret = true)
        {
            using (var f = new Form { Text = title, Size = new Size(560, 180), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false })
            {
                var text = new TextBox { Left = 20, Top = 20, Width = 500, UseSystemPasswordChar = secret };
                var ok = new Button { Text = "OK", Left = 320, Top = 70, DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "Cancel", Left = 420, Top = 70, DialogResult = DialogResult.Cancel };
                f.Controls.AddRange(new Control[] { text, ok, cancel });
                f.AcceptButton = ok;
                f.CancelButton = cancel;
                return f.ShowDialog(owner) == DialogResult.OK ? text.Text : null;
            }
        }
        public static Credentials Secrets(IWin32Window owner, ConnectionProfile profile, bool ftp = true)
        {
            Credentials c;
            try
            {
                c = profile.Resolve();
            }
            catch (System.Security.Cryptography.CryptographicException) { c = new Credentials(); }
            if (string.IsNullOrEmpty(c.Db2Password))
                c.Db2Password = Prompt(owner, profile.Name + " · Database password");
            if (ftp && string.IsNullOrEmpty(c.FtpPassword))
                c.FtpPassword = Prompt(owner, profile.Name + " · FTP password");
            if (c.Db2Password == null || ftp && c.FtpPassword == null)
                throw new OperationCanceledException();
            return c;
        }
    }
    internal sealed class PropertyView : System.ComponentModel.CustomTypeDescriptor
    {
        readonly object value; readonly string[] excluded;
        public PropertyView(object value, params string[] excluded)
        {
            this.value = value;
            this.excluded = excluded;
        }
        public override object GetPropertyOwner(System.ComponentModel.PropertyDescriptor pd) => value;
        public override System.ComponentModel.PropertyDescriptorCollection GetProperties() => GetProperties(null);
        public override System.ComponentModel.PropertyDescriptorCollection GetProperties(Attribute[] attributes) => new System.ComponentModel.PropertyDescriptorCollection(System.ComponentModel.TypeDescriptor.GetProperties(value).Cast<System.ComponentModel.PropertyDescriptor>().Where(p => !excluded.Contains(p.Name) && p.IsBrowsable).ToArray());
    }
    internal sealed class SecretEditor : UserControl
    {
        readonly ComboBox mode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
        readonly TextBox value = Ui.Text(300); readonly Label hint = Ui.Label("");
        SecretSetting original;
        public SecretEditor(string name, SecretSetting secret)
        {
            original = ConnectionProfile.JsonCopy(secret);
            AutoSize = true;
            Dock = DockStyle.Top;
            var f = Ui.Flow();
            f.Controls.Add(Ui.Label(name));
            mode.Items.AddRange(Enum.GetValues(typeof(PasswordMode)).Cast<object>().ToArray());
            mode.SelectedItem = secret.Mode;
            f.Controls.AddRange(new Control[] { mode, value, hint });
            Controls.Add(f);
            mode.SelectedIndexChanged += (s, e) => RefreshMode();
            RefreshMode();
            value.Text = secret.Mode == PasswordMode.Password_Environment_Variable ? secret.EnvironmentVariable : "";
        }
        void RefreshMode()
        {
            var m = (PasswordMode)mode.SelectedItem;
            value.UseSystemPasswordChar = m == PasswordMode.Manual;
            value.Enabled = m != PasswordMode.RuntimePrompt;
            value.Text = "";
            hint.Text = m == PasswordMode.Manual ? "Encrypted for this Windows user; blank keeps saved password" : m == PasswordMode.RuntimePrompt ? "Ask at run time" : "Environment variable name";
        }
        public SecretSetting CaptureValues()
        {
            var result = ConnectionProfile.JsonCopy(original);
            result.Mode = (PasswordMode)mode.SelectedItem;
            if (result.Mode == PasswordMode.Manual)
            {
                if (value.Text.Length > 0)
                    result.Store(value.Text);
                if (string.IsNullOrEmpty(result.ProtectedValue))
                    throw new InvalidOperationException("Enter the Manual password.");
            }
            else
            {
                result.ProtectedValue = "";
                if (result.Mode == PasswordMode.Password_Environment_Variable)
                {
                    result.EnvironmentVariable = value.Text;
                    if (string.IsNullOrWhiteSpace(value.Text))
                        throw new InvalidOperationException("Enter the password environment variable name.");
                }
            }
            return result;
        }
    }
    internal sealed class ProfileEditor : Form
    {
        public ConnectionProfile Profile
        {
            get; private set;
        }
        readonly TextBox name = Ui.Text(); readonly SecretEditor dbSecret, ftpSecret;
        readonly Label dbStatus = Ui.Label("Database: not tested"), ftpStatus = Ui.Label("FTP: not tested");
        bool? dbPassed, ftpPassed; bool testing;
        public ProfileEditor(ConnectionProfile profile)
        {
            Profile = ConnectionProfile.JsonCopy(profile);
            Ui.Setup(this, "Connection profile · Database + FTP");
            var tabs = new TabControl { Dock = DockStyle.Fill };
            var db = new PropertyGrid { Dock = DockStyle.Fill, SelectedObject = new PropertyView(Profile.Database, "Provider", "PasswordEnvironmentVariable") };
            var ftp = new PropertyGrid { Dock = DockStyle.Fill, SelectedObject = new PropertyView(Profile.Ftp, "PasswordEnvironmentVariable") };
            var dbPage = Ui.Page(tabs, "1 · Database provider");
            var ftpPage = Ui.Page(tabs, "2 · FTP");
            dbSecret = new SecretEditor("Database password", Profile.DatabaseSecret);
            ftpSecret = new SecretEditor("FTP password", Profile.FtpSecret);
            dbPage.Controls.Add(db);
            dbPage.Controls.Add(dbSecret);
            var providerLine = Ui.Flow();
            var provider = new ComboBox { Width = 320, DropDownStyle = ComboBoxStyle.DropDownList };
            provider.Items.AddRange(Enum.GetValues(typeof(DatabaseProvider)).Cast<object>().ToArray());
            provider.SelectedItem = Profile.Database.Provider;
            provider.SelectedIndexChanged += (sender, args) => { Profile.Database.Provider = (DatabaseProvider)provider.SelectedItem; dbPassed = null; dbStatus.Text = "Database: not tested"; db.Refresh(); };
            providerLine.Controls.Add(Ui.Label("Database provider / driver"));
            providerLine.Controls.Add(provider);
            providerLine.Controls.Add(Ui.Button("Installed ADO.NET factories", () => { var rows = System.Data.Common.DbProviderFactories.GetFactoryClasses().Rows.Cast<System.Data.DataRow>(); MessageBox.Show(this, string.Join("\r\n", rows.Select(row => row["InvariantName"] + " · " + row["Name"])), "Registered providers"); }));
            dbPage.Controls.Add(providerLine);
            ftpPage.Controls.Add(ftp);
            ftpPage.Controls.Add(ftpSecret);
            db.PropertyValueChanged += (s, e) => { dbPassed = null; dbStatus.Text = "Database: changed; not tested"; };
            ftp.PropertyValueChanged += (s, e) => { ftpPassed = null; ftpStatus.Text = "FTP: changed; not tested"; };
            var top = Ui.Flow();
            name.Text = Profile.Name;
            top.Controls.Add(Ui.Label("Profile name"));
            top.Controls.Add(name);
            var bottom = Ui.Flow();
            bottom.Dock = DockStyle.Bottom;
            bottom.Controls.Add(Ui.Button("Test database", async () => await Test(false)));
            bottom.Controls.Add(dbStatus);
            bottom.Controls.Add(Ui.Button("Test FTP", async () => await Test(true)));
            bottom.Controls.Add(ftpStatus);
            bottom.Controls.Add(Ui.Button("Save profile", Save));
            bottom.Controls.Add(Ui.Button("Cancel", () => Close()));
            Controls.Add(tabs);
            Controls.Add(top);
            Controls.Add(bottom);
            FormClosing += (s, e) => { if (testing) e.Cancel = true; };
        }
        void CaptureValues()
        {
            Profile.Name = name.Text.Trim();
            if (Profile.Name.Length == 0)
                throw new InvalidOperationException("Enter a profile name.");
            Profile.DatabaseSecret = dbSecret.CaptureValues();
            Profile.FtpSecret = ftpSecret.CaptureValues();
        }
        async Task Test(bool ftp)
        {
            if (testing)
                return;
            try
            {
                CaptureValues();
                testing = true;
                string password = (ftp ? Profile.FtpSecret : Profile.DatabaseSecret).Resolve();
                if (string.IsNullOrEmpty(password))
                    password = Ui.Prompt(this, ftp ? "FTP password" : "Database password");
                if (password == null)
                    return;
                (ftp ? ftpStatus : dbStatus).Text = "Testing…";
                await Task.Run(() => { if (ftp) { using (var f = new MainframeFtp(Profile.Ftp, password, null, CancellationToken.None)) { } } else { using (var c = ProviderFactory.Create(Profile.Database, password)) c.Open(); } });
                if (ftp)
                    ftpPassed = true;
                else
                    dbPassed = true;
                (ftp ? ftpStatus : dbStatus).Text = "✓ Connected";
            }
            catch (Exception ex) { if (ftp) ftpPassed = false; else dbPassed = false; (ftp ? ftpStatus : dbStatus).Text = "✗ Failed"; MessageBox.Show(this, ex.Message, "Connection test"); }
            finally { testing = false; }
        }
        void Save()
        {
            if (testing)
                return;
            CaptureValues();
            ProfileStore.Validate(Profile);
            if ((dbPassed == false || ftpPassed == false) && MessageBox.Show(this, "A connection test failed. Save this profile anyway?", "Save profile", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
    internal sealed class ProfileManager : Form
    {
        readonly List<ConnectionProfile> profiles; readonly ListBox list = new ListBox { Dock = DockStyle.Fill };
        public ProfileManager(List<ConnectionProfile> profiles)
        {
            this.profiles = profiles;
            Ui.Setup(this, "Connections · Profile manager", 900, 600);
            var buttons = Ui.Flow();
            buttons.Dock = DockStyle.Bottom;
            buttons.Controls.Add(Ui.Button("New profile", () => Edit(new ConnectionProfile(), false)));
            buttons.Controls.Add(Ui.Button("Edit", () => { if (list.SelectedItem is ConnectionProfile p) Edit(p, true); }));
            buttons.Controls.Add(Ui.Button("Clone with name", () => { if (list.SelectedItem is ConnectionProfile p) { string name = Ui.Prompt(this, "New profile name", false); if (!string.IsNullOrWhiteSpace(name)) Edit(p.Clone(name), false); } }));
            buttons.Controls.Add(Ui.Button("Save profiles", () => { ProfileStore.Save(profiles); MessageBox.Show(this, "Profiles saved. Manual passwords use Windows user encryption."); }));
            buttons.Controls.Add(Ui.Button("Close", () => Close()));
            Controls.Add(list);
            Controls.Add(buttons);
            RefreshList();
        }
        void RefreshList()
        {
            list.DataSource = null;
            list.DataSource = profiles;
        }
        void Edit(ConnectionProfile p, bool replace)
        {
            using (var f = new ProfileEditor(p))
                if (f.ShowDialog(this) == DialogResult.OK)
                {
                    if (profiles.Any(x => x.Id != p.Id && x.Name.Equals(f.Profile.Name, StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidOperationException("Profile name already exists.");
                    if (replace)
                        profiles[profiles.IndexOf(p)] = f.Profile;
                    else
                        profiles.Add(f.Profile);
                    ProfileStore.Save(profiles);
                    RefreshList();
                }
        }
    }
}
