using System;
using System.IO;
using System.Drawing;
using System.Windows.Forms;
namespace DSNForge.Desktop
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, e) => MessageBox.Show(e.Exception.Message, "DSNForge", MessageBoxButtons.OK, MessageBoxIcon.Error);
            if (args.Length >= 2 && (args[0] == "--screenshot" || args[0] == "--wizard-screenshot" || args[0] == "--profile-screenshot" || args[0] == "--run-screenshot"))
            {
                var profile = new DSNForge.Core.ConnectionProfile { Name = "Example mainframe" };
                var project = new DSNForge.Core.TransferProject { Profiles = new System.Collections.Generic.List<DSNForge.Core.ConnectionProfile> { profile } };
                project.Steps.Add(new DSNForge.Core.TransferStep { ConnectionProfileId = profile.Id, Name = "Example unload", Operation = DSNForge.Core.Operation.Unload });
                using (Form f = args[0] == "--wizard-screenshot" ? (Form)new TaskWizard(project.Profiles, project.Steps[0]) : args[0] == "--profile-screenshot" ? new ProfileEditor(profile) : args[0] == "--run-screenshot" ? new RunWizard(project, false) : new MainForm(project))
                {
                    f.Show();
                    Application.DoEvents();
                    if (args.Length == 3)
                        foreach (Control control in f.Controls)
                            if (control is TabControl tabs)
                                tabs.SelectedIndex = int.Parse(args[2]);
                    Application.DoEvents();
                    using (var b = new Bitmap(f.Width, f.Height))
                    {
                        f.DrawToBitmap(b, new Rectangle(Point.Empty, f.Size));
                        b.Save(args[1]);
                    }
                    f.Close();
                }
                return;
            }
            Application.Run(new MainForm());
        }
    }
}
