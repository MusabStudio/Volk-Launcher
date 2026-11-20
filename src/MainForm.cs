using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Volk
{
    public class MainForm : Form
    {
        // palette
        static readonly Color Bg = Color.FromArgb(22, 22, 28);
        static readonly Color Panel = Color.FromArgb(30, 30, 38);
        static readonly Color PanelHi = Color.FromArgb(40, 40, 50);
        static readonly Color Fg = Color.FromArgb(232, 232, 240);
        static readonly Color Muted = Color.FromArgb(138, 138, 153);
        static readonly Color Accent = Color.FromArgb(108, 92, 231);
        static readonly Color Play = Color.FromArgb(46, 204, 113);
        static readonly Color PlayHover = Color.FromArgb(60, 224, 130);
        static readonly Color Warn = Color.FromArgb(243, 156, 18);

        Settings _s;
        TextBox _user;
        ComboBox _version;
        TrackBar _ram;
        Label _ramLabel;
        ComboBox _presetCombo;
        ComboBox _sizeCombo;
        CheckBox _fastMods;
        CheckBox _sounds;
        Label _javaLabel;
        Button _javaBtn;
        Button _play;
        Button _install;
        ProgressBar _bar;
        Label _status;
        TextBox _log;
        Label _sysLabel;
        bool _busy;

        public MainForm()
        {
            _s = Settings.Load();
            GpuInfo.Load();
            BuildUi();
            LoadVersionsAsync();
            RefreshJavaLabel();
            ShowSystemInfo();
            CheckBrokenDlls();
        }

        void CheckBrokenDlls()
        {
            try
            {
                string p = Path.Combine(_s.GameDir, "opengl32.dll");
                if (!File.Exists(p)) return;

                // On a GPU that cannot do OpenGL 3.2 this file is usually Mesa (software OpenGL)
                // and is the only reason modern versions start at all - never delete it.
                if (GpuInfo.TooOldForModern())
                {
                    AppendLog("Note: software OpenGL (opengl32.dll) found in the game folder - kept, it is needed on this GPU.");
                    return;
                }

                DialogResult r = MessageBox.Show(this,
                    "A fake opengl32.dll was found in your Minecraft folder:\n\n" + p +
                    "\n\nYour GPU supports hardware OpenGL, so this file forces the slow software renderer\n" +
                    "and is a very common cause of ~15 FPS.\n\nDelete it now?",
                    "Volk Launcher - graphics problem found", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (r == DialogResult.Yes)
                {
                    try { File.Delete(p); SetStatus(100, "Removed fake opengl32.dll"); } catch { }
                }
            }
            catch { }
        }

        void BuildUi()
        {
            Text = "Volk Launcher";
            BackColor = Bg;
            ForeColor = Fg;
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(760, 620);
            MinimumSize = new Size(700, 560);
            StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true;

            // ---- header ----
            Panel header = new Panel();
            header.Dock = DockStyle.Top;
            header.Height = 88;
            header.Paint += delegate (object s, PaintEventArgs e) { PaintHeader(e.Graphics, header.ClientSize); };
            Controls.Add(header);

            // ---- body ----
            Panel body = new Panel();
            body.Dock = DockStyle.Fill;
            body.Padding = new Padding(24, 14, 24, 14);
            body.BackColor = Bg;
            Controls.Add(body);
            body.BringToFront();

            TableLayoutPanel grid = new TableLayoutPanel();
            grid.Dock = DockStyle.Top;
            grid.AutoSize = false;
            grid.Height = 250;
            grid.ColumnCount = 2;
            grid.RowCount = 5;
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            grid.BackColor = Bg;
            body.Controls.Add(grid);

            grid.Controls.Add(MakeLabel("Nickname"), 0, 0);
            _user = new TextBox();
            _user.Text = _s.Username;
            _user.BackColor = Panel;
            _user.ForeColor = Fg;
            _user.BorderStyle = BorderStyle.FixedSingle;
            _user.Font = new Font("Segoe UI", 10f);
            _user.Dock = DockStyle.Fill;
            _user.Margin = new Padding(0, 4, 0, 4);
            grid.Controls.Add(_user, 1, 0);

            grid.Controls.Add(MakeLabel("Version"), 0, 1);
            _version = new ComboBox();
            _version.DropDownStyle = ComboBoxStyle.DropDownList;
            _version.FlatStyle = FlatStyle.Flat;
            _version.BackColor = Panel;
            _version.ForeColor = Fg;
            _version.Font = new Font("Segoe UI", 10f);
            _version.Dock = DockStyle.Fill;
            _version.Margin = new Padding(0, 4, 0, 4);
            _version.Items.AddRange(new object[] { "1.8.9", "1.12.2", "1.7.10", "1.16.5", "1.21.8", "26.3" });
            _version.SelectedItem = _s.VersionId;
            if (_version.SelectedIndex < 0) _version.SelectedIndex = 0;
            _version.SelectedIndexChanged += delegate { RefreshJavaLabel(); };
            grid.Controls.Add(_version, 1, 1);

            grid.Controls.Add(MakeLabel("Memory"), 0, 2);
            Panel ramPanel = new Panel();
            ramPanel.Dock = DockStyle.Fill;
            ramPanel.Margin = new Padding(0);
            _ram = new TrackBar();
            _ram.Minimum = 512;
            int maxRam = (int)Math.Max(1024, Math.Min(4096, SysInfo.TotalRamMb / 2));
            maxRam = (maxRam / 256) * 256;
            if (maxRam < 1024) maxRam = 1024;
            _ram.Maximum = maxRam;
            _ram.SmallChange = 256;
            _ram.LargeChange = 512;
            _ram.TickStyle = TickStyle.None;
            _ram.Dock = DockStyle.Top;
            _ram.Value = Math.Min(Math.Max(_s.RamMb, _ram.Minimum), _ram.Maximum);
            _ram.ValueChanged += delegate { UpdateRamLabel(); };
            _ramLabel = new Label();
            _ramLabel.ForeColor = Muted;
            _ramLabel.Dock = DockStyle.Bottom;
            _ramLabel.Height = 16;
            ramPanel.Controls.Add(_ramLabel);
            ramPanel.Controls.Add(_ram);
            grid.Controls.Add(ramPanel, 1, 2);
            UpdateRamLabel();

            grid.Controls.Add(MakeLabel("Preset"), 0, 3);
            Panel opts = new Panel();
            opts.Dock = DockStyle.Fill;
            opts.Margin = new Padding(0);
            _presetCombo = new ComboBox();
            _presetCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _presetCombo.FlatStyle = FlatStyle.Flat;
            _presetCombo.BackColor = Panel;
            _presetCombo.ForeColor = Fg;
            _presetCombo.Location = new Point(0, 6);
            _presetCombo.Width = 190;
            _presetCombo.Items.AddRange(new object[] { "Potato (2 chunks, max FPS)", "Smooth (4 chunks)", "Off (my settings)" });
            _presetCombo.SelectedIndex = _s.PresetLevel >= 2 ? 0 : (_s.PresetLevel == 1 ? 1 : 2);
            _sounds = MakeCheck("Download sounds (+150 MB)", _s.DownloadSounds);
            _sounds.Location = new Point(206, 8);
            _sounds.AutoSize = true;
            _sizeCombo = new ComboBox();
            _sizeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _sizeCombo.FlatStyle = FlatStyle.Flat;
            _sizeCombo.BackColor = Panel;
            _sizeCombo.ForeColor = Fg;
            _sizeCombo.Location = new Point(400, 6);
            _sizeCombo.Width = 140;
            _sizeCombo.Items.AddRange(new object[] { "640x360 (fastest)", "854x480", "1280x720" });
            _sizeCombo.SelectedIndex = _s.Width <= 640 ? 0 : (_s.Width <= 900 ? 1 : 2);

            opts.Controls.Add(_presetCombo);
            opts.Controls.Add(_sounds);
            opts.Controls.Add(_sizeCombo);

            _fastMods = MakeCheck("Fast mods", _s.FastMods);
            _fastMods.Location = new Point(552, 8);
            _fastMods.AutoSize = true;
            opts.Controls.Add(_fastMods);
            grid.Controls.Add(opts, 1, 3);

            grid.Controls.Add(MakeLabel("Java"), 0, 4);
            Panel javaRow = new Panel();
            javaRow.Dock = DockStyle.Fill;
            javaRow.Margin = new Padding(0, 4, 0, 0);
            _javaLabel = new Label();
            _javaLabel.ForeColor = Muted;
            _javaLabel.AutoEllipsis = true;
            _javaLabel.Dock = DockStyle.Fill;
            Button javaBtn = MakeSmallButton("Download Java 8");
            javaBtn.Dock = DockStyle.Right;
            javaBtn.Width = 130;
            javaBtn.Click += delegate { DownloadJava(); };
            _javaBtn = javaBtn;
            Button pickBtn = MakeSmallButton("Browse");
            pickBtn.Dock = DockStyle.Right;
            pickBtn.Width = 74;
            pickBtn.Click += delegate { BrowseJava(); };
            javaRow.Controls.Add(_javaLabel);
            javaRow.Controls.Add(pickBtn);
            javaRow.Controls.Add(javaBtn);
            grid.Controls.Add(javaRow, 1, 4);

            // ---- play button ----
            _play = new Button();
            _play.Text = "PLAY";
            _play.Dock = DockStyle.Top;
            _play.Height = 58;
            _play.FlatStyle = FlatStyle.Flat;
            _play.FlatAppearance.BorderSize = 0;
            _play.BackColor = Play;
            _play.ForeColor = Color.FromArgb(10, 30, 18);
            _play.Font = new Font("Segoe UI Semibold", 16f);
            _play.Cursor = Cursors.Hand;
            _play.MouseEnter += delegate { if (!_busy) _play.BackColor = PlayHover; };
            _play.MouseLeave += delegate { if (!_busy) _play.BackColor = Play; };
            _play.Click += delegate { OnPlay(); };

            Panel playWrap = new Panel();
            playWrap.Dock = DockStyle.Top;
            playWrap.Height = 70;
            playWrap.Padding = new Padding(0, 8, 0, 4);
            playWrap.Controls.Add(_play);
            body.Controls.Add(playWrap);
            playWrap.BringToFront();

            // small actions under play
            Panel actions = new Panel();
            actions.Dock = DockStyle.Top;
            actions.Height = 32;
            Button openBtn = MakeSmallButton("Open folder");
            openBtn.Location = new Point(0, 0);
            openBtn.Width = 110;
            openBtn.Click += delegate { OpenFolder(); };
            _install = MakeSmallButton("Repair / reinstall files");
            _install.Location = new Point(120, 0);
            _install.Width = 170;
            _install.Click += delegate { OnInstall(); };
            Button infoBtn = MakeSmallButton("Copy system info");
            infoBtn.Location = new Point(286, 0);
            infoBtn.Width = 120;
            infoBtn.Click += delegate { CopySystemInfo(); };
            Button modsBtn = MakeSmallButton("Mods folder");
            modsBtn.Location = new Point(414, 0);
            modsBtn.Width = 105;
            modsBtn.Click += delegate { OpenModsFolder(); };
            Button horizonBtn = MakeSmallButton("Horizon 26.3 (OpenGL)");
            horizonBtn.Location = new Point(527, 0);
            horizonBtn.Width = 160;
            horizonBtn.Click += delegate { LaunchHorizon(); };
            actions.Controls.Add(openBtn);
            actions.Controls.Add(_install);
            actions.Controls.Add(infoBtn);
            actions.Controls.Add(modsBtn);
            actions.Controls.Add(horizonBtn);
            body.Controls.Add(actions);
            actions.BringToFront();

            // ---- progress ----
            Panel prog = new Panel();
            prog.Dock = DockStyle.Top;
            prog.Height = 40;
            prog.Padding = new Padding(0, 6, 0, 0);
            _bar = new ProgressBar();
            _bar.Dock = DockStyle.Top;
            _bar.Height = 8;
            _bar.Style = ProgressBarStyle.Continuous;
            _status = new Label();
            _status.Dock = DockStyle.Bottom;
            _status.Height = 18;
            _status.ForeColor = Muted;
            _status.Text = "Ready.";
            prog.Controls.Add(_bar);
            prog.Controls.Add(_status);
            body.Controls.Add(prog);
            prog.BringToFront();

            // ---- log ----
            _log = new TextBox();
            _log.Multiline = true;
            _log.ScrollBars = ScrollBars.Vertical;
            _log.ReadOnly = true;
            _log.BackColor = Color.FromArgb(16, 16, 20);
            _log.ForeColor = Color.FromArgb(160, 180, 170);
            _log.BorderStyle = BorderStyle.None;
            _log.Font = new Font("Consolas", 8.25f);
            _log.Dock = DockStyle.Fill;
            body.Controls.Add(_log);

            _sysLabel = new Label();
            _sysLabel.Dock = DockStyle.Bottom;
            _sysLabel.Height = 34;
            _sysLabel.ForeColor = Color.FromArgb(100, 100, 115);
            _sysLabel.Text = "Ready.";
            body.Controls.Add(_sysLabel);
        }

        void PaintHeader(Graphics g, Size size)
        {
            using (LinearGradientBrush b = new LinearGradientBrush(new Rectangle(0, 0, size.Width, size.Height), Color.FromArgb(38, 32, 68), Bg, LinearGradientMode.Vertical))
                g.FillRectangle(b, 0, 0, size.Width, size.Height);
            using (SolidBrush accent = new SolidBrush(Accent))
                g.FillRectangle(accent, 0, size.Height - 3, size.Width, 3);
            using (Font f = new Font("Segoe UI Semibold", 26f))
            using (SolidBrush b = new SolidBrush(Fg))
                g.DrawString("VOLK", f, b, 22, 12);
            using (Font f = new Font("Segoe UI", 10f))
            using (SolidBrush b = new SolidBrush(Muted))
                g.DrawString("LAUNCHER   ·   fast start, low RAM, Windows 8 friendly", f, b, 128, 36);
        }

        Label MakeLabel(string text)
        {
            Label l = new Label();
            l.Text = text;
            l.ForeColor = Muted;
            l.TextAlign = ContentAlignment.MiddleLeft;
            l.Dock = DockStyle.Fill;
            l.Margin = new Padding(0, 4, 8, 4);
            return l;
        }

        CheckBox MakeCheck(string text, bool value)
        {
            VolkCheck c = new VolkCheck();
            c.Text = text;
            c.Checked = value;
            c.BackColor = Bg;
            c.ForeColor = Fg;
            return c;
        }

        Button MakeSmallButton(string text)
        {
            Button b = new Button();
            b.Text = text;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.BorderColor = PanelHi;
            b.BackColor = Panel;
            b.ForeColor = Fg;
            b.Font = new Font("Segoe UI", 8.5f);
            b.Cursor = Cursors.Hand;
            b.Height = 26;
            return b;
        }

        void UpdateRamLabel()
        {
            _ramLabel.Text = _ram.Value + " MB  (system RAM: " + SysInfo.TotalRamMb + " MB, free: " + SysInfo.FreeRamMb + " MB)";
        }

        void ShowSystemInfo()
        {
            string java = JavaFinder.Find(_s);
            string gpu = string.IsNullOrEmpty(GpuInfo.Name) ? "GPU: unknown" : ("GPU: " + GpuInfo.Name + "  -  " + GpuInfo.Verdict());
            _sysLabel.Text = string.Format("{0}  ·  {1}  ·  {2} MB RAM  ·  {3} cores  ·  Java: {4}{5}{6}",
                SysInfo.OsName, Shorten(SysInfo.CpuName, 26), SysInfo.TotalRamMb, SysInfo.Cores,
                java == null ? "none" : JavaFinder.Describe(java), Environment.NewLine, gpu);
            if (!SysInfo.Is64BitOS)
            {
                _sysLabel.ForeColor = Warn;
                _sysLabel.Text = "32-bit Windows: Minecraft is limited to ~1 GB memory." + Environment.NewLine + _sysLabel.Text;
            }
            else if (GpuInfo.TooOldForModern())
            {
                _sysLabel.ForeColor = Warn;
            }
        }

        static string Shorten(string s, int n)
        {
            if (s == null) return "";
            return s.Length <= n ? s : s.Substring(0, n - 1) + "…";
        }

        void RefreshJavaLabel()
        {
            string v = (_version.SelectedItem as string) ?? _s.VersionId;
            string java = JavaFinder.FindFor(_s, v);
            int need = JavaFinder.RequiredJava(v);
            if (java == null)
            {
                _javaLabel.Text = "not found — click 'Download Java " + need + "'";
                _javaLabel.ForeColor = Warn;
                _javaBtn.Text = "Download Java " + need;
            }
            else
            {
                int have = JavaFinder.MajorVersion(java);
                _javaLabel.Text = "Java " + have + "  ·  " + JavaFinder.Describe(java);
                if (have < need)
                {
                    _javaLabel.Text = "Java " + have + " found, but " + v + " needs Java " + need + "+";
                    _javaLabel.ForeColor = Warn;
                    _javaBtn.Text = "Get Java " + need;
                }
                else
                {
                    _javaLabel.ForeColor = Muted;
                    _javaBtn.Text = "Download Java " + need;
                }
            }
        }

        void LoadVersionsAsync()
        {
            Thread t = new Thread(delegate ()
            {
                try
                {
                    List<Versions.VersionInfo> list = Versions.List(null);
                    if (list.Count == 0) return;
                    Ui(delegate
                    {
                        string cur = _version.SelectedItem as string;
                        _version.BeginUpdate();
                        _version.Items.Clear();
                        foreach (Versions.VersionInfo v in list)
                            if (v.Type == "release" || v.Id == "1.8.9" || v.Id == "1.12.2")
                                _version.Items.Add(v.Id);
                        _version.EndUpdate();
                        if (cur != null && _version.Items.Contains(cur)) _version.SelectedItem = cur;
                        else if (_version.Items.Contains(_s.VersionId)) _version.SelectedItem = _s.VersionId;
                        else if (_version.Items.Count > 0) _version.SelectedIndex = 0;
                    });
                }
                catch (Exception ex) { Log.Write("LoadVersions: " + ex.Message); }
            });
            t.IsBackground = true;
            t.Start();
        }

        void Ui(Action a)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(a); } catch { }
            }
            else a();
        }

        void AppendLog(string line)
        {
            Ui(delegate
            {
                if (_log.TextLength > 120000) _log.Text = _log.Text.Substring(_log.TextLength - 60000);
                _log.AppendText(line + Environment.NewLine);
            });
        }

        void SetStatus(int pct, string text)
        {
            Ui(delegate
            {
                if (pct < 0) { _bar.Style = ProgressBarStyle.Marquee; }
                else { _bar.Style = ProgressBarStyle.Continuous; _bar.Value = Math.Min(100, Math.Max(0, pct)); }
                _status.Text = text;
            });
        }

        void SetBusy(bool busy, string playText)
        {
            Ui(delegate
            {
                _busy = busy;
                _play.Enabled = !busy;
                _install.Enabled = !busy;
                _play.Text = playText ?? (busy ? "WORKING..." : "PLAY");
                _play.BackColor = busy ? Color.FromArgb(60, 90, 75) : Play;
            });
        }

        LaunchOptions BuildOptions(string actionWord)
        {
            string name = (_user.Text ?? "").Trim();
            if (name.Length < 3) { name = "Player"; }
            name = Sanitize(name);
            _user.Text = name;

            LaunchOptions o = new LaunchOptions();
            o.Username = name;
            o.VersionId = (_version.SelectedItem as string) ?? _s.VersionId;
            o.GameDir = _s.GameDir;
            o.RamMb = _ram.Value;
            o.PresetLevel = _presetCombo.SelectedIndex == 0 ? 2 : (_presetCombo.SelectedIndex == 1 ? 1 : 0);
            o.FastMods = _fastMods.Checked;
            if (_sizeCombo.SelectedIndex == 0) { o.Width = 640; o.Height = 360; }
            else if (_sizeCombo.SelectedIndex == 2) { o.Width = 1280; o.Height = 720; }
            else { o.Width = 854; o.Height = 480; }
            o.DownloadSounds = _sounds.Checked;
            o.Width = _s.Width;
            o.Height = _s.Height;

            string java = JavaFinder.FindFor(_s, o.VersionId);
            o.JavawPath = java;

            _s.Username = name;
            _s.VersionId = o.VersionId;
            _s.RamMb = o.RamMb;
            _s.PresetLevel = o.PresetLevel;
            _s.FastMods = o.FastMods;
            _s.Width = o.Width;
            _s.Height = o.Height;
            _s.DownloadSounds = o.DownloadSounds;
            _s.Save();
            return o;
        }

        static string Sanitize(string s)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in s)
                if (char.IsLetterOrDigit(c) || c == '_') sb.Append(c);
            string r = sb.ToString();
            if (r.Length > 16) r = r.Substring(0, 16);
            if (r.Length == 0) r = "Player";
            return r;
        }

        void OnPlay()
        {
            if (_busy) return;
            LaunchOptions o = BuildOptions("play");

            if (string.IsNullOrEmpty(o.JavawPath))
            {
                int need = JavaFinder.RequiredJava(o.VersionId);
                bool embedded = JavaBoot.HasEmbedded(need);
                string text = embedded
                    ? "Java " + need + " is included inside this launcher.\n\nUnpack it now? (one time, ~1 minute, no internet needed)"
                    : "Java was not found on this computer.\n\nMinecraft cannot start without Java.\nDownload Java " + need + " automatically now? (about 45 MB)";
                DialogResult r = MessageBox.Show(this, text, "Volk Launcher", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (r == DialogResult.Yes) DownloadJava();
                return;
            }

            int haveMajor = JavaFinder.MajorVersion(o.JavawPath);
            int needMajor = JavaFinder.RequiredJava(o.VersionId);
            if (haveMajor < needMajor)
            {
                bool embedded = JavaBoot.HasEmbedded(needMajor);
                string text = embedded
                    ? "Minecraft " + o.VersionId + " needs Java " + needMajor + " (this PC has Java " + haveMajor + ").\n\nJava " + needMajor + " is included inside this launcher.\nUnpack it now? (one time, ~1 minute)"
                    : "Minecraft " + o.VersionId + " needs Java " + needMajor + " or newer, but this PC has Java " + haveMajor + ".\n\nDownload Java " + needMajor + " now? (about 45 MB)";
                DialogResult r = MessageBox.Show(this, text, "Volk Launcher", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (r == DialogResult.Yes) DownloadJava();
                return;
            }

            if (IsModern(o.VersionId) && !ConfirmModern(o.VersionId)) return;
            SetBusy(true, "STARTING...");
            Thread t = new Thread(delegate () { RunPlay(o); });
            t.IsBackground = true;
            t.Start();
        }

        static bool IsModern(string id)
        {
            try
            {
                string[] p = id.Split('.');
                int major = int.Parse(p[0]);
                int minor = p.Length > 1 ? int.Parse(p[1]) : 0;
                if (major > 1) return true;
                return minor >= 17;
            }
            catch { return false; }
        }

        bool ConfirmModern(string id)
        {
            string msg =
                "Version " + id + " needs a GPU with Vulkan or DirectX 12 and 4+ GB of usable RAM.\n\n" +
                "On an old Core 2 Duo / integrated graphics PC it will crash on startup or show a black screen.\n\n" +
                "If it fails, use version 1.8.9 or 1.12.2 instead - they run on this hardware.\n\nTry anyway?";
            return MessageBox.Show(this, msg, "Old hardware warning", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
        }

        void RunPlay(LaunchOptions o)
        {
            Progress p = new Progress();
            p.Log = AppendLog;
            p.Status = SetStatus;
            try
            {
                p.Step(-1, "Checking files...");
                string displayVersion = o.VersionId;

                List<ModScanner.Mod> userMods = ModScanner.Scan(o.GameDir);
                if (userMods.Count > 0)
                {
                    p.Say("Found " + userMods.Count + " mod(s) in the mods folder");
                    ModScanner.Report(userMods, displayVersion, AppendLog);

                    // Always quarantine jars that cannot load on this version - otherwise Fabric
                    // refuses to start at all. Nothing is deleted, and they come back automatically.
                    ModScanner.PrepareFor(o.GameDir, displayVersion, AppendLog);
                    if (!o.FastMods)
                        p.Say("Fast mods is off - no mods added, your own mods are used as they are");
                }

                bool needFabric = IsModern(o.VersionId) && (o.FastMods || userMods.Count > 0);

                if (needFabric)
                {
                    if (userMods.Count > 0 && !o.FastMods)
                        p.Say("Mods detected - switching this version to Fabric");
                    p.Step(-1, "Preparing Fabric + performance mods for " + displayVersion + "...");
                    string fabricId = Fabric.EnsureProfile(o.GameDir, o.VersionId, p);
                    o.VersionId = fabricId;
                    p.Say("Using " + fabricId);
                    GameFiles.Install(o, p);
                    if (o.FastMods) Fabric.InstallMods(o.GameDir, displayVersion, AppendLog);
                }
                else if (GameFiles.NeedsInstall(o.GameDir, o.VersionId))
                {
                    p.Say("Installing " + o.VersionId + " files...");
                    GameFiles.Install(o, p);
                }

                if (o.PresetLevel > 0) GameFiles.ApplySpeedPreset(o.GameDir, o.PresetLevel);
                Directory.CreateDirectory(Path.Combine(o.GameDir, "logs"));

                bool boosted = o.PresetLevel > 0;
                if (boosted) PowerBoost.Enable(AppendLog);

                p.Step(-1, "Starting Minecraft " + o.VersionId + "...");
                p.Say("Java: " + JavaFinder.Describe(o.JavawPath) + "  (" + o.JavawPath + ")");
                Process proc = Launcher.Play(o, p, AppendLog);
                try { proc.PriorityClass = ProcessPriorityClass.High; p.Say("Process priority -> High"); } catch { }
                p.Say("Minecraft started (PID " + proc.Id + ")");
                SetStatus(100, "Running - " + displayVersion);
                Ui(delegate { _play.Text = "RUNNING"; });

                proc.WaitForExit();
                if (boosted) PowerBoost.Restore(AppendLog);
                p.Say("Minecraft exited with code " + proc.ExitCode);
                if (proc.ExitCode != 0)
                {
                    p.Say("If it closed instantly, check the log above and: " + Path.Combine(o.GameDir, "logs", "volk-latest.log"));
                    SetStatus(-1, "Game exited (code " + proc.ExitCode + ") - see log");
                }
                else SetStatus(100, "Game closed");
            }
            catch (Exception ex)
            {
                Log.Write(ex.ToString());
                p.Say("ERROR: " + ex.Message);
                SetStatus(-1, "Error: " + ex.Message);
            }
            finally
            {
                SetBusy(false, "PLAY");
            }
        }

        void OnInstall()
        {
            if (_busy) return;
            LaunchOptions o = BuildOptions("install");
            SetBusy(true, "INSTALLING...");
            Thread t = new Thread(delegate ()
            {
                Progress p = new Progress();
                p.Log = AppendLog;
                p.Status = SetStatus;
                try
                {
                    p.Say("Installing files for " + o.VersionId + " into " + o.GameDir);
                    GameFiles.Install(o, p);
                    SetStatus(100, "Files installed");
                }
                catch (Exception ex)
                {
                    Log.Write(ex.ToString());
                    p.Say("ERROR: " + ex.Message);
                    SetStatus(-1, "Error: " + ex.Message);
                }
                finally { SetBusy(false, "PLAY"); }
            });
            t.IsBackground = true;
            t.Start();
        }

        void DownloadJava()
        {
            if (_busy) return;
            string v = (_version.SelectedItem as string) ?? _s.VersionId;
            int need = JavaFinder.RequiredJava(v);
            SetBusy(true, "JAVA...");
            Thread t = new Thread(delegate ()
            {
                try
                {
                    string javaw = JavaInstaller.DownloadJava(need, delegate (int pct, string text) { SetStatus(pct, text); });
                    _s.JavaPath = "";
                    _s.Save();
                    AppendLog("Java " + need + " installed: " + javaw);
                    Ui(delegate { RefreshJavaLabel(); });
                    SetStatus(100, "Java " + need + " installed");
                }
                catch (Exception ex)
                {
                    Log.Write(ex.ToString());
                    AppendLog("Java download failed: " + ex.Message);
                    SetStatus(-1, "Java download failed");
                    Ui(delegate
                    {
                        MessageBox.Show(this,
                            "Could not download Java automatically.\n\n" + ex.Message +
                            "\n\nManual option: install 'Eclipse Temurin JRE " + need + " (x64)' from adoptium.net, then press Browse.",
                            "Volk Launcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    });
                }
                finally { SetBusy(false, "PLAY"); }
            });
            t.IsBackground = true;
            t.Start();
        }

        void BrowseJava()
        {
            OpenFileDialog d = new OpenFileDialog();
            d.Filter = "Java (javaw.exe)|javaw.exe|All files (*.*)|*.*";
            d.Title = "Select your Java 8 javaw.exe";
            if (d.ShowDialog(this) == DialogResult.OK)
            {
                _s.JavaPath = d.FileName;
                _s.Save();
                RefreshJavaLabel();
            }
        }

        void OpenFolder()
        {
            try
            {
                Directory.CreateDirectory(_s.GameDir);
                Process.Start("explorer.exe", "\"" + _s.GameDir + "\"");
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message); }
        }

        void LaunchHorizon()
        {
            try
            {
                if (GpuInfo.TooOldForModern())
                {
                    DialogResult r = MessageBox.Show(this,
                        "This PC's graphics cannot run Minecraft 26.3 at any speed.\n\n" +
                        "GPU: " + GpuInfo.Name + "\n" + GpuInfo.Verdict() + "\n\n" +
                        "26.3 needs OpenGL 3.3 or Vulkan. OpenGL 2.1 hardware cannot be fixed by any launcher, flag or driver.\n\n" +
                        "The fastest thing this PC can do is 1.8.9 with the Potato preset (2 chunks, 640x360).\n\n" +
                        "Switch to Potato now?",
                        "26.3 is not possible on this GPU", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                    if (r == DialogResult.Yes)
                    {
                        _presetCombo.SelectedIndex = 0;
                        _sizeCombo.SelectedIndex = 0;
                        SetStatus(100, "Potato preset selected - press PLAY");
                    }
                    return;
                }

                string exe = _s.HorizonPath;
                if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
                {
                    string gpu = string.IsNullOrEmpty(GpuInfo.Name) ? "unknown GPU" : (GpuInfo.Name + " - " + GpuInfo.Verdict());
                    if (MessageBox.Show(this,
                        "Starts Minecraft 26.3 through Horizon (Rust) with the OpenGL renderer forced.\n\n" +
                        "Requirements:\n" +
                        "  - horizon-client.exe already installed on this PC (with 26.3 resources prepared)\n" +
                        "  - a GPU with OpenGL 3.3\n\n" +
                        "This PC: " + gpu + "\n\nSelect horizon-client.exe now?",
                        "Horizon 26.3 via OpenGL", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;

                    OpenFileDialog d = new OpenFileDialog();
                    d.Filter = "Horizon client (horizon-client.exe)|horizon-client.exe|All files (*.*)|*.*";
                    d.Title = "Select horizon-client.exe";
                    if (d.ShowDialog(this) != DialogResult.OK) return;
                    exe = d.FileName;
                    _s.HorizonPath = exe;
                    _s.Save();
                }

                ProcessStartInfo psi = new ProcessStartInfo(exe);
                psi.WorkingDirectory = Path.GetDirectoryName(exe);
                psi.Arguments = "--render-distance 4";
                psi.UseShellExecute = false;
                psi.EnvironmentVariables["HORIZON_GRAPHICS_API"] = "opengl";
                psi.EnvironmentVariables["HORIZON_NO_DISCORD"] = "1";
                Process.Start(psi);
                AppendLog("Started " + exe + " with HORIZON_GRAPHICS_API=opengl");
                SetStatus(100, "Horizon started with the OpenGL renderer");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not start Horizon:\n\n" + ex.Message, "Volk Launcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        void OpenModsFolder()
        {
            try
            {
                string mods = Path.Combine(_s.GameDir, "mods");
                Directory.CreateDirectory(mods);
                Process.Start("explorer.exe", "\"" + mods + "\"");
                int n = Directory.GetFiles(mods, "*.jar").Length;
                SetStatus(100, "Mods folder: " + n + " jar(s) - paste .jar files here");
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message); }
        }

        void CopySystemInfo()
        {
            string java = JavaFinder.Find(_s);
            string info = string.Format(
                "Volk Launcher 1.0{0}OS: {1} ({2}-bit){3}CPU: {4} ({5} cores){6}RAM: {7} MB total, {8} MB free{9}GPU: {10} (driver {11}) - {12}{13}Java: {14} ({15}){16}Game dir: {17}",
                Environment.NewLine, SysInfo.OsName, SysInfo.Is64BitOS ? 64 : 32, Environment.NewLine,
                SysInfo.CpuName, SysInfo.Cores, Environment.NewLine, SysInfo.TotalRamMb, SysInfo.FreeRamMb,
                Environment.NewLine, GpuInfo.Name, GpuInfo.Driver, GpuInfo.Verdict(),
                Environment.NewLine, java ?? "not found", java == null ? "-" : JavaFinder.Describe(java),
                Environment.NewLine, _s.GameDir);
            try { Clipboard.SetText(info); SetStatus(100, "System info copied to clipboard"); } catch { }
        }
    }
}

namespace Volk
{
    /// <summary>Dark-theme checkbox drawn manually (Win8 flat checkbox is invisible on dark backgrounds).</summary>
    public class VolkCheck : CheckBox
    {
        static readonly Color BoxOn = Color.FromArgb(108, 92, 231);
        static readonly Color BoxOff = Color.FromArgb(42, 42, 52);
        static readonly Color BorderOff = Color.FromArgb(74, 74, 92);

        public VolkCheck()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            BackColor = Color.FromArgb(22, 22, 28);
            ForeColor = Color.FromArgb(232, 232, 240);
            Font = new Font("Segoe UI", 9f);
            Cursor = Cursors.Hand;
            Height = 22;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            int size = 15;
            int y = (Height - size) / 2;
            Rectangle box = new Rectangle(0, y, size, size);

            using (SolidBrush b = new SolidBrush(Checked ? BoxOn : BoxOff)) g.FillRectangle(b, box);
            using (Pen p = new Pen(Checked ? BoxOn : BorderOff)) g.DrawRectangle(p, box);

            if (Checked)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (Pen p = new Pen(Color.White, 1.9f))
                {
                    g.DrawLine(p, box.X + 3f, box.Y + 7.5f, box.X + 6f, box.Y + 10.5f);
                    g.DrawLine(p, box.X + 6f, box.Y + 10.5f, box.X + 11.5f, box.Y + 4f);
                }
                g.SmoothingMode = SmoothingMode.Default;
            }

            if (Focused)
            {
                using (Pen p = new Pen(Color.FromArgb(80, 108, 92, 231))) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            }

            Rectangle textRect = new Rectangle(size + 8, 0, Math.Max(0, Width - size - 8), Height);
            TextRenderer.DrawText(g, Text, Font, textRect, ForeColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }

        protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        public override Size GetPreferredSize(Size proposedSize)
        {
            Size s = TextRenderer.MeasureText(Text, Font);
            return new Size(s.Width + 30, 22);
        }
    }
}





