using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Volk
{
    static class SetupProgram
    {
        [STAThread]
        static void Main(string[] args)
        {
            bool quiet = false;
            bool preload = false;
            foreach (string a in args)
            {
                if (a == "/quiet" || a == "/s") quiet = true;
                if (a == "/preload") preload = true;
            }

            try { Log.Init(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (quiet)
            {
                Installer.Install("Player", preload, delegate (int pct, string t) { Console.WriteLine(pct + "% " + t); },
                                  delegate (string s) { Console.WriteLine(s); });
                return;            }
            Application.Run(new SetupForm());
        }
    }

    public static class Installer
    {
        public static string TargetDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Volk"); }
        }

        public static string LauncherExe
        {
            get { return Path.Combine(TargetDir, "VolkLauncher.exe"); }
        }

        public static void Install(string nickname, bool preload, Action<int, string> status, Action<string> log)
        {
            Directory.CreateDirectory(TargetDir);

            // 1. launcher
            if (status != null) status(-1, "Installing launcher...");
            using (Stream s = typeof(Installer).Assembly.GetManifestResourceStream("Launcher"))
            {
                if (s == null) throw new Exception("Installer is damaged (launcher payload missing).");
                using (FileStream fs = File.Create(LauncherExe)) s.CopyTo(fs);
            }
            if (log != null) log("Launcher -> " + LauncherExe);

            // 2. Java 8 + Java 21 (embedded in this setup)
            string j21 = JavaBoot.Ensure(21, status);
            string j8 = JavaBoot.Ensure(8, status);
            if (j21 != null && log != null) log("Java 21 -> " + j21);
            if (j8 != null && log != null) log("Java 8  -> " + j8);

            // 3. settings tuned for a weak PC
            Settings st = Settings.Load();
            st.Username = string.IsNullOrEmpty(nickname) ? "Player" : nickname;
            st.VersionId = "1.21.11";
            st.RamMb = st.RamMb > 2048 ? 1536 : st.RamMb;
            st.PresetLevel = 2;
            st.FastMods = true;
            st.Width = 640;
            st.Height = 360;
            st.JavaPath = "";
            st.Save();
            if (log != null) log("Settings written (1.21.11, Potato, 640x360)");

            // 4. shortcuts
            try
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Volk");
                Directory.CreateDirectory(startMenu);
                CreateShortcut(Path.Combine(desktop, "Volk Launcher.lnk"), LauncherExe, TargetDir);
                CreateShortcut(Path.Combine(startMenu, "Volk Launcher.lnk"), LauncherExe, TargetDir);
                if (log != null) log("Shortcuts created (Desktop + Start Menu)");
            }
            catch (Exception ex) { if (log != null) log("Shortcut failed: " + ex.Message); }

            // 5. optional: download the game now so the first PLAY is instant
            if (preload)
            {
                LaunchOptions o = new LaunchOptions();
                o.Username = st.Username;
                o.VersionId = "1.21.11";
                o.GameDir = Paths.DefaultGameDir;
                o.RamMb = st.RamMb;
                o.PresetLevel = 2;
                o.FastMods = true;
                o.Width = 640;
                o.Height = 360;
                o.JavawPath = j21;

                Progress p = new Progress();
                p.Status = status;
                p.Log = log;

                string fab = Fabric.EnsureProfile(o.GameDir, "1.21.11", p);
                o.VersionId = fab;
                GameFiles.Install(o, p);
                ModScanner.PrepareFor(o.GameDir, "1.21.11", log);
                Fabric.InstallMods(o.GameDir, "1.21.11", log);
                GameFiles.ApplySpeedPreset(o.GameDir, 2);
                if (log != null) log("Minecraft 1.21.11 + Sodium installed");
            }

            if (status != null) status(100, "Install complete");
        }

        public static void CreateShortcut(string lnkPath, string target, string workDir)
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) throw new Exception("WScript.Shell unavailable");
            object shell = Activator.CreateInstance(shellType);
            object sc = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
            Type t = sc.GetType();
            t.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { target });
            t.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { workDir });
            t.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { "Volk Launcher" });
            t.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { target + ",0" });
            t.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
        }
    }

    public class SetupForm : Form
    {
        static readonly Color Bg = Color.FromArgb(22, 22, 28);
        static readonly Color Panel = Color.FromArgb(30, 30, 38);
        static readonly Color Fg = Color.FromArgb(232, 232, 240);
        static readonly Color Muted = Color.FromArgb(138, 138, 153);
        static readonly Color Accent = Color.FromArgb(108, 92, 231);
        static readonly Color Play = Color.FromArgb(46, 204, 113);

        TextBox _name;
        CheckBox _preload;
        Button _go;
        ProgressBar _bar;
        Label _status;
        TextBox _log;
        bool _busy;

        public SetupForm()
        {
            Text = "Volk Setup";
            ClientSize = new Size(560, 430);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Bg;
            ForeColor = Fg;
            Font = new Font("Segoe UI", 9f);

            Panel head = new Panel();
            head.Dock = DockStyle.Top;
            head.Height = 76;
            head.Paint += delegate (object s, PaintEventArgs e)
            {
                using (LinearGradientBrush b = new LinearGradientBrush(new Rectangle(0, 0, head.Width, head.Height), Color.FromArgb(38, 32, 68), Bg, LinearGradientMode.Vertical))
                    e.Graphics.FillRectangle(b, 0, 0, head.Width, head.Height);
                using (SolidBrush a = new SolidBrush(Accent)) e.Graphics.FillRectangle(a, 0, head.Height - 3, head.Width, 3);
                using (Font f = new Font("Segoe UI Semibold", 22f))
                using (SolidBrush b = new SolidBrush(Fg)) e.Graphics.DrawString("VOLK", f, b, 20, 8);
                using (Font f = new Font("Segoe UI", 9.5f))
                using (SolidBrush b = new SolidBrush(Muted)) e.Graphics.DrawString("SETUP  ·  installs Java 8 + Java 21 + launcher", f, b, 122, 26);
            };
            Controls.Add(head);

            Panel body = new Panel();
            body.Dock = DockStyle.Fill;
            body.Padding = new Padding(20, 14, 20, 14);
            Controls.Add(body);
            body.BringToFront();

            Label l1 = new Label();
            l1.Text = "Nickname (3-16 letters)";
            l1.ForeColor = Muted;
            l1.Location = new Point(0, 4);
            l1.AutoSize = true;
            body.Controls.Add(l1);

            _name = new TextBox();
            _name.Text = "Player";
            _name.BackColor = Panel;
            _name.ForeColor = Fg;
            _name.BorderStyle = BorderStyle.FixedSingle;
            _name.Font = new Font("Segoe UI", 10f);
            _name.Location = new Point(0, 24);
            _name.Width = 300;
            body.Controls.Add(_name);

            _preload = new CheckBox();
            _preload.Text = "Also download Minecraft 1.21.11 + Sodium now (about 250 MB)";
            _preload.ForeColor = Fg;
            _preload.FlatStyle = FlatStyle.Flat;
            _preload.BackColor = Bg;
            _preload.Location = new Point(0, 58);
            _preload.AutoSize = true;
            body.Controls.Add(_preload);

            Label l2 = new Label();
            l2.Text = "Installs to: " + Installer.TargetDir;
            l2.ForeColor = Color.FromArgb(100, 100, 115);
            l2.Location = new Point(0, 86);
            l2.AutoSize = true;
            body.Controls.Add(l2);

            _go = new Button();
            _go.Text = "INSTALL & PLAY";
            _go.Location = new Point(0, 110);
            _go.Size = new Size(520, 52);
            _go.FlatStyle = FlatStyle.Flat;
            _go.FlatAppearance.BorderSize = 0;
            _go.BackColor = Play;
            _go.ForeColor = Color.FromArgb(10, 30, 18);
            _go.Font = new Font("Segoe UI Semibold", 14f);
            _go.Cursor = Cursors.Hand;
            _go.Click += delegate { Start(); };
            body.Controls.Add(_go);

            _bar = new ProgressBar();
            _bar.Location = new Point(0, 172);
            _bar.Size = new Size(520, 8);
            _bar.Style = ProgressBarStyle.Continuous;
            body.Controls.Add(_bar);

            _status = new Label();
            _status.Text = "Ready to install.";
            _status.ForeColor = Muted;
            _status.Location = new Point(0, 184);
            _status.Size = new Size(520, 18);
            body.Controls.Add(_status);

            _log = new TextBox();
            _log.Multiline = true;
            _log.ScrollBars = ScrollBars.Vertical;
            _log.ReadOnly = true;
            _log.BackColor = Color.FromArgb(16, 16, 20);
            _log.ForeColor = Color.FromArgb(160, 180, 170);
            _log.BorderStyle = BorderStyle.None;
            _log.Font = new Font("Consolas", 8.25f);
            _log.Location = new Point(0, 208);
            _log.Size = new Size(520, 170);
            body.Controls.Add(_log);
        }

        void Start()
        {
            if (_busy) return;
            _busy = true;
            _go.Enabled = false;
            _go.Text = "INSTALLING...";
            _go.BackColor = Color.FromArgb(60, 90, 75);

            string name = Sanitize(_name.Text);
            bool preload = _preload.Checked;

            Thread t = new Thread(delegate ()
            {
                try
                {
                    Installer.Install(name, preload,
                        delegate (int pct, string text) { Ui(delegate { SetProgress(pct, text); }); },
                        delegate (string line) { Ui(delegate { Append(line); }); });

                    Ui(delegate
                    {
                        _status.Text = "Done. Starting Volk Launcher...";
                        _bar.Value = 100;
                        _go.Text = "STARTED";
                    });
                    Thread.Sleep(600);
                    Process.Start(Installer.LauncherExe);
                    Ui(delegate { Close(); });
                }
                catch (Exception ex)
                {
                    Log.Write("Setup: " + ex);
                    Ui(delegate
                    {
                        Append("ERROR: " + ex.Message);
                        _status.Text = "Install failed: " + ex.Message;
                        _bar.Style = ProgressBarStyle.Continuous;
                        _bar.Value = 0;
                        _go.Enabled = true;
                        _go.Text = "RETRY";
                        _go.BackColor = Play;
                        _busy = false;
                        MessageBox.Show(this, "Install failed:\n\n" + ex.Message, "Volk Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    });
                }
            });
            t.IsBackground = true;
            t.Start();
        }

        void SetProgress(int pct, string text)
        {
            if (pct < 0) _bar.Style = ProgressBarStyle.Marquee;
            else { _bar.Style = ProgressBarStyle.Continuous; _bar.Value = Math.Min(100, Math.Max(0, pct)); }
            _status.Text = text;
        }

        void Append(string line)
        {
            if (_log.TextLength > 60000) _log.Text = _log.Text.Substring(_log.TextLength - 30000);
            _log.AppendText(line + Environment.NewLine);
        }

        void Ui(Action a)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { try { BeginInvoke(a); } catch { } } else a();
        }

        static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "Player";
            StringBuilder sb = new StringBuilder();
            foreach (char c in s.Trim()) if (char.IsLetterOrDigit(c) || c == '_') sb.Append(c);
            string r = sb.ToString();
            if (r.Length > 16) r = r.Substring(0, 16);
            return r.Length < 3 ? "Player" : r;
        }
    }
}
