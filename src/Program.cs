using System;
using System.IO;
using System.Windows.Forms;

namespace Volk
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            try { Log.Init(); } catch { }
            AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e)
            {
                Log.Write("FATAL: " + (e.ExceptionObject == null ? "?" : e.ExceptionObject.ToString()));
            };
            Application.ThreadException += delegate (object s, System.Threading.ThreadExceptionEventArgs e)
            {
                Log.Write("UI ERROR: " + e.Exception);
            };
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                Log.Write("CRASH: " + ex);
                try
                {
                    MessageBox.Show("Volk Launcher crashed:\n\n" + ex.Message + "\n\nLog: " + Path.Combine(Paths.Root, "launcher.log"),
                        "Volk Launcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                catch { }
            }
        }
    }
}
