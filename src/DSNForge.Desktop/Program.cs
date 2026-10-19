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
            if (args.Length == 2 && args[0] == "--screenshot")
            {
                using (var f = new MainForm())
                {
                    f.Show();
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
