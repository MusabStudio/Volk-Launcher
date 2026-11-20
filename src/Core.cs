using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Volk
{
    public static class Log
    {
        static readonly object Gate = new object();
        public static string FilePath;

        public static void Init()
        {
            try
            {
                Directory.CreateDirectory(Paths.Root);
                FilePath = Path.Combine(Paths.Root, "launcher.log");
            }
            catch { }
        }

        public static void Write(string msg)
        {
            try
            {
                lock (Gate)
                {
                    if (FilePath == null) Init();
                    if (FilePath == null) return;
                    File.AppendAllText(FilePath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg + Environment.NewLine);
                }
            }
            catch { }
        }
    }

    public static class Paths
    {
        public static string Root { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".volk"); } }
        public static string JavaDir { get { return Path.Combine(Root, "java"); } }
        public static string CacheDir { get { return Path.Combine(Root, "cache"); } }
        public static string ConfigFile { get { return Path.Combine(Root, "config.txt"); } }

        public static string DefaultGameDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft"); }
        }
    }

    public class Settings
    {
        public string Username = "Player";
        public string VersionId = "1.8.9";
        public string GameDir = Paths.DefaultGameDir;
        public string JavaPath = "";
        public int RamMb = -1;   // -1 = auto (half of installed RAM, capped)
        public int PresetLevel = -1; // -1 = auto, 0 off, 1 smooth, 2 potato
        public bool DownloadSounds = true;
        public string HorizonPath = "";
        public bool FastMods = true;
        public int Width = 0;    // 0 = auto
        public int Height = 0;   // 0 = auto

        public static Settings Load()
        {
            Settings s = new Settings();
            try
            {
                if (File.Exists(Paths.ConfigFile))
                {
                    foreach (string line in File.ReadAllLines(Paths.ConfigFile))
                    {
                        int i = line.IndexOf('=');
                        if (i <= 0) continue;
                        string k = line.Substring(0, i).Trim();
                        string v = line.Substring(i + 1).Trim();
                        switch (k)
                        {
                            case "username": s.Username = v; break;
                            case "version": s.VersionId = v; break;
                            case "gamedir": s.GameDir = v; break;
                            case "java": s.JavaPath = v; break;
                            case "ram": s.RamMb = ParseInt(v, s.RamMb); break;
                            case "preset": s.PresetLevel = v == "1" ? 1 : 0; break;
                            case "presetlevel": s.PresetLevel = ParseInt(v, s.PresetLevel); break;
                            case "horizon": s.HorizonPath = v; break;
                            case "fastmods": s.FastMods = v == "1"; break;
                            case "sounds": s.DownloadSounds = v == "1"; break;
                            case "width": s.Width = ParseInt(v, s.Width); break;
                            case "height": s.Height = ParseInt(v, s.Height); break;
                        }
                    }
                }
            }
            catch (Exception ex) { Log.Write("Settings.Load: " + ex.Message); }
            if (string.IsNullOrEmpty(s.GameDir)) s.GameDir = Paths.DefaultGameDir;

            // One-time migration: older builds defaulted sounds off, which makes the game silent.
            try
            {
                string marker = Path.Combine(Paths.Root, "sounds-on.marker");
                if (!File.Exists(marker))
                {
                    s.DownloadSounds = true;
                    Directory.CreateDirectory(Paths.Root);
                    File.WriteAllText(marker, "1");
                }
            }
            catch { }

            // Auto defaults: a 4 GB laptop must not get a 32 GB desktop's settings, and vice versa.            if (s.RamMb <= 0) s.RamMb = SysInfo.RecommendedRam();
            if (s.PresetLevel < 0) s.PresetLevel = SysInfo.TotalRamMb >= 8192 ? 1 : 2;
            if (s.Width <= 0 || s.Height <= 0)
            {
                if (SysInfo.TotalRamMb >= 8192) { s.Width = 1280; s.Height = 720; }
                else { s.Width = 640; s.Height = 360; }
            }
            return s;
        }

        static int ParseInt(string v, int fallback)
        {
            int r;
            if (int.TryParse(v, out r)) return r;
            return fallback;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Paths.Root);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("username=" + Username);
                sb.AppendLine("version=" + VersionId);
                sb.AppendLine("gamedir=" + GameDir);
                sb.AppendLine("java=" + JavaPath);
                sb.AppendLine("ram=" + RamMb);
                sb.AppendLine("preset=" + (PresetLevel > 0 ? "1" : "0"));
                sb.AppendLine("presetlevel=" + PresetLevel);
                sb.AppendLine("horizon=" + HorizonPath);
                sb.AppendLine("fastmods=" + (FastMods ? "1" : "0"));
                sb.AppendLine("sounds=" + (DownloadSounds ? "1" : "0"));
                sb.AppendLine("width=" + Width);
                sb.AppendLine("height=" + Height);
                File.WriteAllText(Paths.ConfigFile, sb.ToString());
            }
            catch (Exception ex) { Log.Write("Settings.Save: " + ex.Message); }
        }
    }

    public static class SysInfo
    {
        [StructLayout(LayoutKind.Sequential)]
        struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        public static long TotalRamMb
        {
            get
            {
                try
                {
                    MEMORYSTATUSEX m = new MEMORYSTATUSEX();
                    m.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
                    if (GlobalMemoryStatusEx(ref m)) return (long)(m.ullTotalPhys / (1024 * 1024));
                }
                catch { }
                return 4096;
            }
        }

        public static long FreeRamMb
        {
            get
            {
                try
                {
                    MEMORYSTATUSEX m = new MEMORYSTATUSEX();
                    m.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
                    if (GlobalMemoryStatusEx(ref m)) return (long)(m.ullAvailPhys / (1024 * 1024));
                }
                catch { }
                return 1024;
            }
        }

        public static bool Is64BitOS { get { return Environment.Is64BitOperatingSystem; } }

        public static string OsName
        {
            get
            {
                Version v = Environment.OSVersion.Version;
                if (v.Major == 6 && v.Minor == 2) return "Windows 8 (6.2)";
                if (v.Major == 6 && v.Minor == 3) return "Windows 8.1 (6.3)";
                if (v.Major == 6 && v.Minor == 1) return "Windows 7";
                if (v.Major == 10) return "Windows 10/11";
                return "Windows " + v.Major + "." + v.Minor;
            }
        }

        public static string CpuName
        {
            get
            {
                try
                {
                    using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                    {
                        if (k != null)
                        {
                            object o = k.GetValue("ProcessorNameString");
                            if (o != null) return o.ToString().Trim();
                        }
                    }
                }
                catch { }
                return "Unknown CPU";
            }
        }

        public static int Cores { get { return Environment.ProcessorCount; } }

        // Best default heap for small machines: about half of free RAM, capped.
        public static int RecommendedRam()
        {
            long total = TotalRamMb;
            if (total <= 3072) return 1024;
            if (total <= 4096) return 1536;
            if (total <= 6144) return 2048;
            if (total <= 8192) return 3072;
            return 4096;
        }
    }

    public static class Http
    {
        static bool _init;

        public static void Init()
        {
            if (_init) return;
            _init = true;
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2
            }
            catch { }
            try { ServicePointManager.DefaultConnectionLimit = 12; } catch { }
        }

        public static string GetString(string url)
        {
            Init();
            Exception last = null;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    using (WebClient wc = NewClient())
                    {
                        return wc.DownloadString(url);
                    }
                }
                catch (Exception ex) { last = ex; Thread.Sleep(400 * (attempt + 1)); }
            }
            throw new Exception("Download failed: " + url + " (" + (last == null ? "?" : last.Message) + ")");
        }

        public static void GetFile(string url, string dest, Action<long, long> progress)
        {
            Init();
            Exception last = null;
            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            string part = dest + ".part";
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    using (WebClient wc = NewClient())
                    {
                        if (progress != null) wc.DownloadProgressChanged += delegate (object s, DownloadProgressChangedEventArgs e) { progress(e.BytesReceived, e.TotalBytesToReceive); };
                        wc.DownloadFile(url, part);
                    }
                    if (File.Exists(dest)) File.Delete(dest);
                    File.Move(part, dest);
                    return;
                }
                catch (Exception ex)
                {
                    last = ex;
                    try { File.Delete(part); } catch { }
                    Thread.Sleep(400 * (attempt + 1));
                }
            }
            throw new Exception("Download failed: " + url + " (" + (last == null ? "?" : last.Message) + ")");
        }

        static WebClient NewClient()
        {
            WebClient wc = new WebClient();
            wc.Headers.Add("User-Agent", "VolkLauncher/1.0 (Windows)");
            wc.Proxy = null;
            return wc;
        }
    }

    public static class Json
    {
        public static Dictionary<string, object> Obj(object o)
        {
            Dictionary<string, object> d = o as Dictionary<string, object>;
            if (d != null) return d;
            System.Collections.IDictionary any = o as System.Collections.IDictionary;
            if (any != null)
            {
                Dictionary<string, object> copy = new Dictionary<string, object>();
                foreach (System.Collections.DictionaryEntry e in any) copy[Convert.ToString(e.Key)] = e.Value;
                return copy;
            }
            return null;
        }

        public static object[] Arr(object o)
        {
            object[] a = o as object[];
            if (a != null) return a;
            System.Collections.IEnumerable seq = o as System.Collections.IEnumerable;
            if (seq != null && !(o is string))
            {
                List<object> list = new List<object>();
                foreach (object item in seq) list.Add(item);
                return list.ToArray();
            }
            return null;
        }

        public static string Str(object o, string key)
        {
            Dictionary<string, object> d = Obj(o);
            if (d == null) return null;
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return null;
            return v.ToString();
        }

        public static Dictionary<string, object> ObjAt(object o, string key)
        {
            Dictionary<string, object> d = Obj(o);
            if (d == null) return null;
            object v;
            if (!d.TryGetValue(key, out v)) return null;
            return Obj(v);
        }

        public static Dictionary<string, object> Parse(string text)
        {
            System.Web.Script.Serialization.JavaScriptSerializer ser = new System.Web.Script.Serialization.JavaScriptSerializer();
            ser.MaxJsonLength = int.MaxValue;
            ser.RecursionLimit = 256;
            return ser.Deserialize<Dictionary<string, object>>(text);
        }

        public static object[] ParseArray(string text)
        {
            System.Web.Script.Serialization.JavaScriptSerializer ser = new System.Web.Script.Serialization.JavaScriptSerializer();
            ser.MaxJsonLength = int.MaxValue;
            ser.RecursionLimit = 256;
            return Arr(ser.DeserializeObject(text));
        }
    }

    public static class JavaFinder
    {
        public static string Find(Settings s)
        {
            return FindFor(s, null);
        }

        // Required Java major for a Minecraft version.
        public static int RequiredJava(string versionId)
        {
            if (string.IsNullOrEmpty(versionId)) return 8;
            try
            {
                string[] p = versionId.Split('.', '-');
                int major, minor = 0;
                if (!int.TryParse(p[0], out major)) return 8;
                if (p.Length > 1) int.TryParse(p[1], out minor);
                if (major > 1) return 21;          // 26.x and newer scheme
                if (minor <= 16) return 8;         // 1.16.5 and older
                if (minor <= 20) return 17;        // 1.17 - 1.20.4
                return 21;                          // 1.20.5+
            }
            catch { return 8; }
        }

        public static List<string> FindAll(Settings s)
        {
            lock (CacheGate)
            {
                if (_allCache != null && (DateTime.UtcNow - _allCacheTime).TotalSeconds < 30)
                    return new List<string>(_allCache);
            }
            List<string> result = FindAllUncached(s);
            lock (CacheGate)
            {
                _allCache = new List<string>(result);
                _allCacheTime = DateTime.UtcNow;
            }
            return result;
        }

        static List<string> FindAllUncached(Settings s)
        {
            List<string> candidates = new List<string>();

            if (!string.IsNullOrEmpty(s.JavaPath) && File.Exists(s.JavaPath)) candidates.Add(s.JavaPath);

            string volkJava = FindIn(Paths.JavaDir);
            if (volkJava != null) candidates.Add(volkJava);

            foreach (string root in new string[] {
                @"C:\Program Files\Java",
                @"C:\Program Files (x86)\Java",
                @"C:\Program Files\Eclipse Adoptium",
                @"C:\Program Files\Eclipse Foundation",
                @"C:\Program Files\Zulu",
                @"C:\Program Files\BellSoft",
                @"C:\Program Files\Microsoft",
                @"C:\Program Files (x86)\Eclipse Adoptium",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Eclipse Adoptium")
            })
            {
                string j = FindIn(root);
                if (j != null) candidates.Add(j);
            }

            foreach (string key in new string[] {
                @"SOFTWARE\JavaSoft\Java Runtime Environment",
                @"SOFTWARE\JavaSoft\Java Development Kit",
                @"SOFTWARE\WOW6432Node\JavaSoft\Java Runtime Environment",
                @"SOFTWARE\WOW6432Node\JavaSoft\Java Development Kit" })
            {
                string j = FromRegistry(key);
                if (j != null) candidates.Add(j);
            }

            // Runtimes bundled by other launchers / clients. Reusing one of these means no download.
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            string[] bundled = new string[] {
                Path.Combine(Paths.DefaultGameDir, "runtime"),     // official launcher (java-runtime-delta = 21)
                Path.Combine(appData, ".fastclient"),
                Path.Combine(appData, ".lunarclient"),
                Path.Combine(appData, ".tlauncher"),
                Path.Combine(appData, ".sklauncher"),
                Path.Combine(appData, ".atlauncher"),
                Path.Combine(appData, "PrismLauncher"),
                Path.Combine(appData, "com.modrinth.theseus"),
                Path.Combine(localAppData, @"Programs\FastClient"),
                Path.Combine(localAppData, @"Programs\Lunar Client"),
                Path.Combine(pf, "FastClient"),
                Path.Combine(pf86, "FastClient")
            };
            foreach (string root in bundled)
            {
                string j = FindDeep(root, 5);
                if (j != null) candidates.Add(j);
            }

            string jh = Environment.GetEnvironmentVariable("JAVA_HOME");
            if (!string.IsNullOrEmpty(jh))
            {
                string p = Path.Combine(jh, @"bin\javaw.exe");
                if (File.Exists(p)) candidates.Add(p);
            }

            // de-duplicate (case-insensitive)
            List<string> unique = new List<string>();
            foreach (string c in candidates)
            {
                bool seen = false;
                foreach (string u in unique) if (string.Equals(u, c, StringComparison.OrdinalIgnoreCase)) { seen = true; break; }
                if (!seen) unique.Add(c);
            }
            return unique;
        }

        public static string FindFor(Settings s, string versionId)
        {
            List<string> all = FindAll(s);
            if (all.Count == 0) return null;

            int need = RequiredJava(versionId);
            bool is64 = Environment.Is64BitOperatingSystem;

            string best = null;
            int bestScore = int.MinValue;
            int bestMajor = -1;

            foreach (string c in all)
            {
                int major = MajorVersion(c);
                bool x86 = c.IndexOf("Program Files (x86)", StringComparison.OrdinalIgnoreCase) >= 0;
                if (x86 && is64) continue; // never prefer 32-bit java on 64-bit windows

                int score = 0;
                if (major >= need) score += 1000 - (major - need); // closest adequate version first
                else score += 500 - (need - major) * 10;           // otherwise closest below
                if (major == need) score += 200;
                if (versionId == null && major == 8) score += 300;  // default = java 8 for label
                if (c.IndexOf(".volk", StringComparison.OrdinalIgnoreCase) >= 0) score += 20;

                if (score > bestScore) { bestScore = score; best = c; bestMajor = major; }
            }

            if (best == null) best = all[0];
            return best;
        }


        static string FindIn(string root)
        {
            try
            {
                if (!Directory.Exists(root)) return null;
                // direct (root is a jre/jdk home)
                string direct = Path.Combine(root, @"bin\javaw.exe");
                if (File.Exists(direct)) return direct;

                string[] dirs = Directory.GetDirectories(root);
                Array.Sort(dirs);
                Array.Reverse(dirs);
                foreach (string d in dirs)
                {
                    string p = Path.Combine(d, @"bin\javaw.exe");
                    if (File.Exists(p)) return p;
                    string p2 = Path.Combine(d, @"jre\bin\javaw.exe");
                    if (File.Exists(p2)) return p2;
                }
            }
            catch { }
            return null;
        }

        /// <summary>Bounded recursive search for bin\javaw.exe (launcher-bundled runtimes).</summary>
        static string FindDeep(string root, int depth)
        {
            try
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return null;
                string direct = Path.Combine(root, @"bin\javaw.exe");
                if (File.Exists(direct)) return direct;
                if (depth <= 0) return null;
                foreach (string d in Directory.GetDirectories(root))
                {
                    string r = FindDeep(d, depth - 1);
                    if (r != null) return r;
                }
            }
            catch { }
            return null;
        }

        static string FromRegistry(string key)
        {
            try
            {
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(key))
                {
                    if (k == null) return null;
                    string cur = k.GetValue("CurrentVersion") as string;
                    if (cur == null) return null;
                    using (Microsoft.Win32.RegistryKey v = k.OpenSubKey(cur))
                    {
                        if (v == null) return null;
                        string home = v.GetValue("JavaHome") as string;
                        if (home == null) return null;
                        string p = Path.Combine(home, @"bin\javaw.exe");
                        if (File.Exists(p)) return p;
                    }
                }
            }
            catch { }
            return null;
        }

        // Caching matters: every Describe() starts a java process, and this runs on the UI thread.
        static readonly Dictionary<string, string> DescCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, int> MajorCache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        static readonly object CacheGate = new object();
        static List<string> _allCache;
        static DateTime _allCacheTime = DateTime.MinValue;

        public static void Invalidate()
        {
            lock (CacheGate)
            {
                DescCache.Clear();
                MajorCache.Clear();
                _allCache = null;
                _allCacheTime = DateTime.MinValue;
            }
        }

        public static string Describe(string javawPath)
        {
            if (string.IsNullOrEmpty(javawPath)) return "not found";
            lock (CacheGate)
            {
                string cached;
                if (DescCache.TryGetValue(javawPath, out cached)) return cached;
            }
            string result = DescribeUncached(javawPath);
            lock (CacheGate) { DescCache[javawPath] = result; }
            return result;
        }

        static string DescribeUncached(string javawPath)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(javawPath.Replace("javaw.exe", "java.exe"), "-version");
                psi.RedirectStandardError = true;
                psi.RedirectStandardOutput = true;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                using (Process p = Process.Start(psi))
                {
                    string err = p.StandardError.ReadToEnd();
                    p.WaitForExit(4000);
                    string first = err.Split('\n')[0].Trim();
                    if (first.Length > 0) return first;
                }
            }
            catch { }
            return Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(javawPath))) ?? javawPath;
        }

        public static bool IsJava8OrOlder(string javawPath)
        {
            return MajorVersion(javawPath) <= 8;
        }

        public static int MajorVersion(string javawPath)
        {
            if (string.IsNullOrEmpty(javawPath)) return 8;
            lock (CacheGate)
            {
                int cached;
                if (MajorCache.TryGetValue(javawPath, out cached)) return cached;
            }
            int value = MajorVersionUncached(javawPath);
            lock (CacheGate) { MajorCache[javawPath] = value; }
            return value;
        }

        static int MajorVersionUncached(string javawPath)
        {
            string d = Describe(javawPath);
            if (string.IsNullOrEmpty(d)) return 8;
            int q1 = d.IndexOf('"');
            if (q1 >= 0)
            {
                int q2 = d.IndexOf('"', q1 + 1);
                if (q2 > q1)
                {
                    string v = d.Substring(q1 + 1, q2 - q1 - 1);
                    string[] parts = v.Split('.');
                    int n;
                    if (parts.Length > 0 && int.TryParse(parts[0], out n))
                    {
                        if (n == 1 && parts.Length > 1 && int.TryParse(parts[1], out n)) return n;
                        return n;
                    }
                }
            }
            if (d.IndexOf("1.8", StringComparison.OrdinalIgnoreCase) >= 0) return 8;
            return 8;
        }
    }
}

namespace Volk
{
    public static class GpuInfo
    {
        public static string Name = "";
        public static string Driver = "";
        public static bool Loaded;

        public static void Load()
        {
            if (Loaded) return;
            Loaded = true;
            try
            {
                using (System.Management.ManagementObjectSearcher searcher =
                    new System.Management.ManagementObjectSearcher("SELECT Name, DriverVersion FROM Win32_VideoController"))
                {
                    string fallback = null;
                    foreach (System.Management.ManagementObject o in searcher.Get())
                    {
                        string n = o["Name"] as string;
                        if (string.IsNullOrEmpty(n)) continue;
                        if (n.IndexOf("Basic Display", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            n.IndexOf("Basic Render", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            fallback = n;
                            continue;
                        }
                        Name = n;
                        Driver = o["DriverVersion"] as string;
                        break;
                    }
                    if (string.IsNullOrEmpty(Name) && fallback != null) Name = fallback;
                }
            }
            catch { }
        }

        /// <summary>Rough verdict for OpenGL 3.3 / Vulkan capability.</summary>
        public static string Verdict()
        {
            if (string.IsNullOrEmpty(Name)) return "unknown";
            string n = Name.ToLowerInvariant();
            if (n.Contains("basic display") || n.Contains("basic render") || n.Contains("standard vga"))
                return "NO DRIVER - software rendering (this alone causes ~15 FPS)";
            if (n.Contains("gma") || n.Contains("945") || n.Contains("965") ||
                n.Contains("g45") || n.Contains("g43") || n.Contains("g41") || n.Contains("g31") ||
                n.Contains("g35") || n.Contains("g33") || n.Contains("q45") || n.Contains("q43") ||
                n.Contains("q35") || n.Contains("q33") || n.Contains("gl40") || n.Contains("gm45") ||
                n.Contains("gm43") || n.Contains("gs45") || n.Contains("express chipset") ||
                n.Contains("4 series") || n.Contains("3 series"))
                return "too old - OpenGL 2.1 only, no Vulkan/DX12 (26.x impossible)";
            if (n.Contains("geforce 6") || n.Contains("geforce 7") || n.Contains("radeon x") ||
                n.Contains("radeon 9") || n.Contains("radeon hd 2"))
                return "very old - OpenGL 2.x, no Vulkan";
            if (n.Contains("geforce 8") || n.Contains("geforce 9") || n.Contains("gt 2") ||
                n.Contains("gt 3") || n.Contains("gt 4") || n.Contains("gt 5") || n.Contains("gt 6") ||
                n.Contains("gtx") || n.Contains("radeon hd 3") || n.Contains("radeon hd 4") ||
                n.Contains("radeon hd 5") || n.Contains("radeon hd 6") || n.Contains("radeon hd 7") ||
                n.Contains("radeon r") || n.Contains("intel hd") || n.Contains("uhd"))
                return "OpenGL 3.3 capable (Vulkan maybe not) - Horizon can use OpenGL";
            return "unknown - check the OpenGL version in-game (F3)";
        }

        public static bool TooOldForModern()
        {
            string v = Verdict();
            return v.StartsWith("too old") || v.StartsWith("very old") || v.StartsWith("NO DRIVER");
        }
    }
}




