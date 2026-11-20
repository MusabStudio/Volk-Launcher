using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Threading;

namespace Volk
{
    public class LaunchOptions
    {
        public string VersionId;
        public string Username;
        public string GameDir;
        public string JavawPath;
        public int RamMb;
        public bool DownloadSounds;
        public int PresetLevel = 1;
        public bool FastMods = true;
        public int Width;
        public int Height;
    }

    public class Progress
    {
        public Action<string> Log;
        public Action<int, string> Status; // percent (-1 = marquee), text

        public void Say(string s) { if (Log != null) Log(s); }
        public void Step(int pct, string s) { if (Status != null) Status(pct, s); }
    }

    public static class Versions
    {
        const string ManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
        static string ManifestPath { get { return Path.Combine(Paths.CacheDir, "version_manifest_v2.json"); } }

        public static string GetManifest(Progress p)
        {
            Directory.CreateDirectory(Paths.CacheDir);
            bool fresh = false;
            if (File.Exists(ManifestPath))
            {
                TimeSpan age = DateTime.Now - File.GetLastWriteTime(ManifestPath);
                fresh = age.TotalHours < 6;
            }
            if (!fresh)
            {
                try
                {
                    if (p != null) p.Step(-1, "Checking version list...");
                    Http.GetFile(ManifestUrl, ManifestPath, null);
                }
                catch (Exception ex)
                {
                    if (!File.Exists(ManifestPath)) throw;
                    if (p != null) p.Say("Version list refresh failed, using cached copy (" + ex.Message + ")");
                }
            }
            return File.ReadAllText(ManifestPath);
        }

        public class VersionInfo
        {
            public string Id;
            public string Url;
            public string Type;
        }

        public static List<VersionInfo> List(Progress p)
        {
            List<VersionInfo> list = new List<VersionInfo>();
            Dictionary<string, object> root = Json.Parse(GetManifest(p));
            object[] arr = Json.Arr(root["versions"]);
            if (arr == null) return list;
            foreach (object o in arr)
            {
                VersionInfo v = new VersionInfo();
                v.Id = Json.Str(o, "id");
                v.Url = Json.Str(o, "url");
                v.Type = Json.Str(o, "type");
                if (v.Id != null) list.Add(v);
            }
            return list;
        }

        public static string VersionDir(string gameDir, string id)
        {
            return Path.Combine(gameDir, "versions", id);
        }

        public static string JsonPath(string gameDir, string id)
        {
            return Path.Combine(VersionDir(gameDir, id), id + ".json");
        }

        public static string GetVersionJson(string gameDir, string id, Progress p)
        {
            string path = JsonPath(gameDir, id);
            if (File.Exists(path))
            {
                try
                {
                    string cached = File.ReadAllText(path);
                    Json.Parse(cached); // validate
                    return cached;
                }
                catch
                {
                    try { File.Delete(path); } catch { }
                }
            }

            List<VersionInfo> list = List(p);
            VersionInfo found = null;
            foreach (VersionInfo v in list) if (v.Id == id) { found = v; break; }
            if (found == null) throw new Exception("Version '" + id + "' does not exist. Pick a version from the list.");

            Directory.CreateDirectory(VersionDir(gameDir, id));
            if (p != null) p.Step(-1, "Loading version " + id + "...");
            Http.GetFile(found.Url, path, null);
            return File.ReadAllText(path);
        }
    }

    public static class GameFiles
    {
        public static bool NeedsInstall(string gameDir, string id)
        {
            try
            {
                string vj = Versions.JsonPath(gameDir, id);
                if (!File.Exists(vj)) return true;
                Dictionary<string, object> root = Json.Parse(File.ReadAllText(vj));
                string parent = Json.Str(root, "inheritsFrom");
                if (!string.IsNullOrEmpty(parent)) return NeedsInstall(gameDir, parent);
                string jar = Path.Combine(Versions.VersionDir(gameDir, id), id + ".jar");
                return !IsValidJar(jar);
            }
            catch { return true; }
        }

        static bool AllowedByRules(object rulesObj)
        {
            object[] rules = Json.Arr(rulesObj);
            if (rules == null || rules.Length == 0) return true;
            bool allowed = false;
            foreach (object r in rules)
            {
                string action = Json.Str(r, "action");
                Dictionary<string, object> os = Json.ObjAt(r, "os");
                bool matches = true;
                if (os != null)
                {
                    string name = Json.Str(os, "name");
                    if (!string.IsNullOrEmpty(name) && name != "windows") matches = false;
                }
                if (matches) allowed = (action == "allow");
            }
            return allowed;
        }

        public static void Install(LaunchOptions o, Progress p)
        {
            string id = o.VersionId;
            string gameDir = o.GameDir;
            string vdir = Versions.VersionDir(gameDir, id);
            Directory.CreateDirectory(vdir);

            string jsonText = Versions.GetVersionJson(gameDir, id, p);
            Dictionary<string, object> vj = Json.Parse(jsonText);

            // Modded / OptiFine versions build on a parent version: install the parent first.
            string parentId = Json.Str(vj, "inheritsFrom");
            if (!string.IsNullOrEmpty(parentId) && NeedsInstall(gameDir, parentId))
            {
                p.Say("Installing parent version " + parentId + " first...");
                LaunchOptions po = new LaunchOptions();
                po.VersionId = parentId;
                po.Username = o.Username;
                po.GameDir = gameDir;
                po.JavawPath = o.JavawPath;
                po.RamMb = o.RamMb;
                po.DownloadSounds = o.DownloadSounds;
                po.PresetLevel = o.PresetLevel;
                po.Width = o.Width;
                po.Height = o.Height;
                Install(po, p);
            }

            // --- client jar ---
            string jarPath = Path.Combine(vdir, id + ".jar");
            Dictionary<string, object> dl = Json.ObjAt(vj, "downloads");
            string clientUrl = null;
            if (dl != null) clientUrl = Json.Str(dl, "client");
            // downloads.client is an object in newer JSON
            if (dl != null)
            {
                Dictionary<string, object> c = Json.ObjAt(dl, "client");
                if (c != null) clientUrl = Json.Str(c, "url");
            }

            if (!IsValidJar(jarPath))
            {
                if (!string.IsNullOrEmpty(clientUrl))
                {
                    p.Step(-1, "Downloading game " + id + "...");
                    Http.GetFile(clientUrl, jarPath, null);
                }
                else
                {
                    p.Say("No client jar in version JSON (modded version) - assuming inheritsFrom parent.");
                }
            }

            // --- libraries ---
            string libRoot = Path.Combine(gameDir, "libraries");
            string nativeDir = Path.Combine(vdir, "natives");
            object[] libs = Json.Arr(vj["libraries"]);
            int done = 0;
            int total = libs == null ? 0 : libs.Length;
            if (libs != null)
            {
                foreach (object lib in libs)
                {
                    done++;
                    if (!AllowedByRules(GetKey(lib, "rules"))) continue;

                    string name = Json.Str(lib, "name");
                    Dictionary<string, object> d = Json.ObjAt(lib, "downloads");
                    Dictionary<string, object> artifact = Json.ObjAt(d, "artifact");
                    Dictionary<string, object> classifiers = Json.ObjAt(d, "classifiers");

                    bool isNative = false;
                    string nativeKey = null;
                    Dictionary<string, object> natives = Json.ObjAt(lib, "natives");
                    if (natives != null) nativeKey = Json.Str(natives, "windows");
                    if (name != null && name.IndexOf("natives", StringComparison.OrdinalIgnoreCase) >= 0) isNative = true;

                    if (artifact != null)
                    {
                        string rel = Json.Str(artifact, "path");
                        string url = Json.Str(artifact, "url");
                        string dest = Path.Combine(libRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                        if (!File.Exists(dest) && !string.IsNullOrEmpty(url))
                        {
                            p.Step(-1, "Libraries " + done + "/" + total);
                            Http.GetFile(url, dest, null);
                        }
                        if (isNative && nativeKey == null) ExtractNatives(dest, nativeDir);
                    }
                    else
                    {
                        // Fabric / Forge style: "url" is a Maven repository root, no downloads block
                        string baseUrl = Json.Str(lib, "url");
                        string rel = Launcher.MavenPath(name);
                        if (!string.IsNullOrEmpty(baseUrl) && !string.IsNullOrEmpty(rel))
                        {
                            string dest = Path.Combine(libRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                            if (!File.Exists(dest))
                            {
                                p.Step(-1, "Libraries " + done + "/" + total);
                                Http.GetFile(baseUrl.TrimEnd('/') + "/" + rel, dest, null);
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(nativeKey) && classifiers != null)
                    {
                        Dictionary<string, object> cls = Json.ObjAt(classifiers, nativeKey);
                        if (cls != null)
                        {
                            string rel = Json.Str(cls, "path");
                            string url = Json.Str(cls, "url");
                            string dest = Path.Combine(libRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                            if (!File.Exists(dest) && !string.IsNullOrEmpty(url))
                            {
                                p.Step(-1, "Libraries " + done + "/" + total);
                                Http.GetFile(url, dest, null);
                            }
                            ExtractNatives(dest, nativeDir);
                        }
                    }
                }
            }

            // --- assets ---
            Dictionary<string, object> ai = Json.ObjAt(vj, "assetIndex");
            if (ai != null)
            {
                string aid = Json.Str(ai, "id");
                string aurl = Json.Str(ai, "url");
                string indexPath = Path.Combine(gameDir, "assets", "indexes", aid + ".json");
                if (!File.Exists(indexPath))
                {
                    p.Step(-1, "Downloading asset index...");
                    Http.GetFile(aurl, indexPath, null);
                }

                Dictionary<string, object> index = Json.Parse(File.ReadAllText(indexPath));
                Dictionary<string, object> objects = Json.ObjAt(index, "objects");
                List<KeyValuePair<string, Dictionary<string, object>>> queue = new List<KeyValuePair<string, Dictionary<string, object>>>();
                if (objects != null)
                {
                    foreach (KeyValuePair<string, object> kv in objects)
                    {
                        string key = kv.Key;
                        if (!o.DownloadSounds && key.StartsWith("minecraft/sounds/", StringComparison.OrdinalIgnoreCase)) continue;
                        Dictionary<string, object> ent = Json.Obj(kv.Value);
                        if (ent == null) continue;
                        string hash = Json.Str(ent, "hash");
                        if (string.IsNullOrEmpty(hash)) continue;
                        string rel = "objects/" + hash.Substring(0, 2) + "/" + hash;
                        string dest = Path.Combine(gameDir, "assets", rel.Replace('/', Path.DirectorySeparatorChar));
                        if (File.Exists(dest)) continue;
                        queue.Add(new KeyValuePair<string, Dictionary<string, object>>(dest, ent));
                    }
                }

                int n = 0;
                int qcount = queue.Count;
                object gate = new object();
                int degree = SysInfo.Cores >= 4 ? 4 : 2;
                object locker = new object();
                List<Exception> errors = new List<Exception>();

                using (Semaphore doneSignal = new Semaphore(0, qcount + 1))
                {
                    // simple throttled worker pool
                    List<Thread> workers = new List<Thread>();
                    int next = 0;
                    for (int w = 0; w < degree; w++)
                    {
                        Thread t = new Thread(delegate ()
                        {
                            while (true)
                            {
                                int i;
                                lock (locker) { i = next; next++; }
                                if (i >= qcount) break;
                                string dest = queue[i].Key;
                                string hash = Json.Str(queue[i].Value, "hash");
                                try
                                {
                                    Http.GetFile("https://resources.download.minecraft.net/" + hash.Substring(0, 2) + "/" + hash, dest, null);
                                }
                                catch (Exception ex)
                                {
                                    lock (errors) errors.Add(ex);
                                }
                                lock (gate)
                                {
                                    n++;
                                    if (n % 5 == 0 || n == qcount)
                                    {
                                        int pct = qcount == 0 ? 100 : (int)((long)n * 100 / qcount);
                                        if (p != null) p.Step(pct, "Assets " + n + "/" + qcount);
                                    }
                                }
                            }
                        });
                        t.IsBackground = true;
                        t.Start();
                        workers.Add(t);
                    }
                    foreach (Thread t in workers) t.Join();
                }
                if (errors.Count > 0) p.Say(errors.Count + " asset file(s) failed to download (game may miss sounds/textures).");
            }

            p.Step(100, "Files ready");
        }

        static object GetKey(object lib, string key)
        {
            Dictionary<string, object> d = Json.Obj(lib);
            object v;
            if (d != null && d.TryGetValue(key, out v)) return v;
            return null;
        }

        public static bool IsValidJar(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;
                if (new FileInfo(path).Length < 1024) return false;
                using (FileStream fs = File.OpenRead(path))
                {
                    int a = fs.ReadByte();
                    int b = fs.ReadByte();
                    return a == 'P' && b == 'K';
                }
            }
            catch { return false; }
        }

        static void ExtractNatives(string jarPath, string nativeDir)
        {
            try
            {
                Directory.CreateDirectory(nativeDir);
                using (ZipArchive zip = ZipFile.OpenRead(jarPath))
                {
                    foreach (ZipArchiveEntry e in zip.Entries)
                    {
                        string name = e.Name;
                        if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".so", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase))
                        {
                            string dest = Path.Combine(nativeDir, name);
                            if (File.Exists(dest)) continue;
                            try { e.ExtractToFile(dest, true); }
                            catch { }
                        }
                    }
                }
            }
            catch (Exception ex) { Log.Write("ExtractNatives " + jarPath + ": " + ex.Message); }
        }

        public static void ApplySpeedPreset(string gameDir, int level)
        {
            try
            {
                string opt = Path.Combine(gameDir, "options.txt");
                Dictionary<string, string> kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (File.Exists(opt))
                {
                    foreach (string line in File.ReadAllLines(opt))
                    {
                        int i = line.IndexOf(':');
                        if (i <= 0) continue;
                        kv[line.Substring(0, i)] = line.Substring(i + 1);
                    }
                }
                if (level >= 2) { Set(kv, "renderDistance", "2"); Set(kv, "guiScale", "1"); Set(kv, "maxFramerate", "120"); }
                else
                {
                    Set(kv, "renderDistance", "4");
                    Set(kv, "guiScale", "2");
                    Set(kv, "maxFramerate", "60");
                }
                Set(kv, "graphics", "0");
                Set(kv, "fancyGraphics", "false");
                Set(kv, "smoothLighting", "false");
                Set(kv, "ao", "false");
                Set(kv, "ambientOcclusion", "false");
                Set(kv, "particles", "2");
                Set(kv, "clouds", "false");
                Set(kv, "entityShadows", "false");
                Set(kv, "mipmapLevels", "0");
                Set(kv, "vsync", "false");
                Set(kv, "useVbo", "true");
                Set(kv, "fov", "0.0");
                Set(kv, "gamma", "0.5");
                Set(kv, "anaglyph3d", "false");

                // Audio: only fix sliders that are missing or at zero - never fight the player's mix.
                SetAudio(kv, "soundCategory_master", "1.0");
                SetAudio(kv, "soundCategory_voice", "1.0");
                SetAudio(kv, "soundCategory_player", "1.0");
                SetAudio(kv, "soundCategory_music", "0.5");

                StringBuilder sb = new StringBuilder();
                foreach (KeyValuePair<string, string> e in kv) sb.AppendLine(e.Key + ":" + e.Value);
                File.WriteAllText(opt, sb.ToString());
            }
            catch (Exception ex) { Log.Write("ApplySpeedPreset: " + ex.Message); }
        }

        static void Set(Dictionary<string, string> kv, string k, string v)
        {
            kv[k] = v;
        }

        /// <summary>Writes a sound slider only when it is missing or set to 0 (a very common "no sound" cause).</summary>
        static void SetAudio(Dictionary<string, string> kv, string k, string v)
        {
            string cur;
            if (kv.TryGetValue(k, out cur))
            {
                float f;
                if (float.TryParse(cur, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out f) && f > 0.001f)
                    return; // keep the player's value
            }
            kv[k] = v;
        }
    }

    public static class Launcher
    {
        public static string OfflineUuid(string name)
        {
            try
            {
                using (System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create())
                {
                    byte[] b = md5.ComputeHash(Encoding.UTF8.GetBytes("OfflinePlayer:" + name));
                    b[6] = (byte)((b[6] & 0x0f) | 0x30);
                    b[8] = (byte)((b[8] & 0x3f) | 0x80);
                    string hex = BitConverter.ToString(b).Replace("-", "").ToLowerInvariant();
                    return hex.Substring(0, 8) + "-" + hex.Substring(8, 4) + "-" + hex.Substring(12, 4) + "-" + hex.Substring(16, 4) + "-" + hex.Substring(20, 12);
                }
            }
            catch { return "00000000-0000-0000-0000-000000000000"; }
        }

        public static Process Play(LaunchOptions o, Progress p, Action<string> gameOutput)
        {
            string id = o.VersionId;
            string gameDir = o.GameDir;
            string vdir = Versions.VersionDir(gameDir, id);
            string jsonText = File.ReadAllText(Versions.JsonPath(gameDir, id));
            Dictionary<string, object> vj = Json.Parse(jsonText);

            // parent merge (OptiFine / Forge style)
            Dictionary<string, object> merged = vj;
            string parentId = Json.Str(vj, "inheritsFrom");
            if (!string.IsNullOrEmpty(parentId))
            {
                string parentJson = Versions.GetVersionJson(gameDir, parentId, p);
                Dictionary<string, object> parent = Json.Parse(parentJson);
                merged = Merge(parent, vj, gameDir);
                p.Say("Merged parent version " + parentId);
            }

            string mainClass = Json.Str(merged, "mainClass");
            if (string.IsNullOrEmpty(mainClass)) throw new Exception("Version has no main class.");

            // classpath
            List<string> cp = new List<string>();
            object[] libs = Json.Arr(merged["libraries"]);
            string libRoot = Path.Combine(gameDir, "libraries");
            if (libs != null)
            {
                foreach (object lib in libs)
                {
                    Dictionary<string, object> d = Json.Obj(lib);
                    string name = Json.Str(lib, "name");
                    if (name != null && name.IndexOf("natives", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    if (!Allowed(lib)) continue;
                    Dictionary<string, object> dl = Json.ObjAt(lib, "downloads");
                    Dictionary<string, object> art = Json.ObjAt(dl, "artifact");
                    string rel = art != null ? Json.Str(art, "path") : MavenPath(name);
                    if (string.IsNullOrEmpty(rel)) continue;
                    string full = Path.Combine(libRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(full)) cp.Add(full);
                }
            }
            string clientJar = Path.Combine(vdir, id + ".jar");
            if (!File.Exists(clientJar))
            {
                string pj = Path.Combine(Versions.VersionDir(gameDir, parentId == null ? id : parentId), (parentId == null ? id : parentId) + ".jar");
                if (File.Exists(pj)) clientJar = pj;
            }
            cp.Add(clientJar);

            string nativeDir = Path.Combine(Versions.VersionDir(gameDir, string.IsNullOrEmpty(parentId) ? id : parentId), "natives");
            Directory.CreateDirectory(nativeDir);

            // args
            List<string> jvm = new List<string>();
            List<string> gameArgs = new List<string>();

            Dictionary<string, object> arguments = Json.ObjAt(merged, "arguments");
            if (arguments != null)
            {
                AppendArgs(Json.Arr(Get(arguments, "jvm")), jvm, o, cp, nativeDir, gameDir, id);
                AppendArgs(Json.Arr(Get(arguments, "game")), gameArgs, o, cp, nativeDir, gameDir, id);
            }
            else
            {
                string legacy = Json.Str(merged, "minecraftArguments");
                if (legacy != null)
                {
                    foreach (string tok in SplitArgs(legacy)) gameArgs.Add(Subst(tok, o, cp, nativeDir, gameDir, id));
                }
                // legacy versions have no arguments block: set a small, fast window ourselves
                if (o.Width > 0)
                {
                    gameArgs.Add("--width");
                    gameArgs.Add(o.Width.ToString());
                    gameArgs.Add("--height");
                    gameArgs.Add(o.Height.ToString());
                }
            }

            string javaExe = string.IsNullOrEmpty(o.JavawPath) ? "javaw.exe" : o.JavawPath;

            bool argsHaveCp = false;
            foreach (string a in jvm) { if (a == "-cp" || a == "-classpath") { argsHaveCp = true; break; } }

            int javaMajor = JavaFinder.MajorVersion(o.JavawPath);

            StringBuilder cmd = new StringBuilder();
            cmd.Append("-Xmx").Append(o.RamMb).Append("M ");
            cmd.Append("-Xms").Append(Math.Min(o.RamMb, 512)).Append("M ");
            cmd.Append("-Dminecraft.launcher.brand=Volk ");
            cmd.Append("-Dminecraft.launcher.version=1.0 ");
            if (!argsHaveCp)
            {
                // legacy versions (1.12.2 and older) ship no arguments block
                if (javaMajor <= 8) cmd.Append("-XX:+UseConcMarkSweepGC -XX:+CMSIncrementalMode -XX:-UseAdaptiveSizePolicy ");
                else cmd.Append("-XX:+UseG1GC -XX:MaxGCPauseMillis=60 ");
                cmd.Append("-Djava.library.path=\"").Append(nativeDir).Append("\" ");
                cmd.Append("-cp \"").Append(string.Join(";", cp.ToArray())).Append("\" ");
            }
            foreach (string a in jvm) cmd.Append(QuoteTok(a)).Append(' ');
            cmd.Append(mainClass).Append(' ');
            foreach (string a in gameArgs) cmd.Append(QuoteTok(a)).Append(' ');

            string gameLog = Path.Combine(gameDir, "logs", "volk-latest.log");
            Directory.CreateDirectory(Path.GetDirectoryName(gameLog));
            if (File.Exists(gameLog)) File.Delete(gameLog);

            ProcessStartInfo psi = new ProcessStartInfo(javaExe, cmd.ToString());
            psi.WorkingDirectory = gameDir;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;

            p.Say("Command: " + javaExe + " " + cmd.ToString());
            Process proc = new Process();
            proc.StartInfo = psi;
            proc.OutputDataReceived += delegate (object s, DataReceivedEventArgs e) { if (e.Data != null) OnLine(e.Data, gameOutput, gameLog); };
            proc.ErrorDataReceived += delegate (object s, DataReceivedEventArgs e) { if (e.Data != null) OnLine(e.Data, gameOutput, gameLog); };
            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            return proc;
        }

        public static string QuoteTok(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            if (s.StartsWith("\"")) return s;
            bool needs = false;
            foreach (char c in s) { if (char.IsWhiteSpace(c)) { needs = true; break; } }
            return needs ? ("\"" + s + "\"") : s;
        }

        static void OnLine(string line, Action<string> sink, string logFile)
        {
            try { if (sink != null) sink(line); } catch { }
            try { File.AppendAllText(logFile, line + Environment.NewLine); } catch { }
        }

        static Dictionary<string, object> Merge(Dictionary<string, object> parent, Dictionary<string, object> child, string gameDir)
        {
            Dictionary<string, object> result = new Dictionary<string, object>(parent);
            foreach (KeyValuePair<string, object> kv in child) result[kv.Key] = kv.Value;

            // libraries: union, child wins on same name prefix
            List<object> libs = new List<object>();
            Dictionary<string, object> seen = new Dictionary<string, object>();
            object[] pl = Json.Arr(Get(parent, "libraries"));
            object[] cl = Json.Arr(Get(child, "libraries"));
            if (pl != null) foreach (object l in pl) { string n = Json.Str(l, "name"); if (n != null) { seen[n] = l; } }
            if (cl != null)
            {
                foreach (object l in cl)
                {
                    string n = Json.Str(l, "name");
                    string group = n;
                    if (n != null)
                    {
                        int i = n.LastIndexOf(':');
                        if (i > 0) group = n.Substring(0, i);
                    }
                    if (group != null)
                    {
                        List<string> remove = new List<string>();
                        foreach (string k in seen.Keys) if (k.StartsWith(group + ":")) remove.Add(k);
                        foreach (string k in remove) seen.Remove(k);
                    }
                    if (n != null) seen[n] = l;
                }
            }
            foreach (object l in seen.Values) libs.Add(l);
            result["libraries"] = libs.ToArray();

            // arguments: parent first, child appended (Fabric adds jvm args and no game args)
            object parArgs = Get(parent, "arguments");
            object chiArgs = Get(child, "arguments");
            if (parArgs != null || chiArgs != null)
            {
                Dictionary<string, object> mergedArgs = new Dictionary<string, object>();
                mergedArgs["jvm"] = Concat(Json.Arr(Get(parArgs, "jvm")), Json.Arr(Get(chiArgs, "jvm")));
                mergedArgs["game"] = Concat(Json.Arr(Get(parArgs, "game")), Json.Arr(Get(chiArgs, "game")));
                result["arguments"] = mergedArgs;
                result.Remove("minecraftArguments");
            }
            else
            {
                if (Get(child, "minecraftArguments") == null && Get(parent, "minecraftArguments") != null) result["minecraftArguments"] = Get(parent, "minecraftArguments");
            }
            if (Get(child, "assetIndex") == null && Get(parent, "assetIndex") != null) result["assetIndex"] = Get(parent, "assetIndex");
            if (Get(child, "assets") == null && Get(parent, "assets") != null) result["assets"] = Get(parent, "assets");
            return result;
        }

        static object[] Concat(object[] a, object[] b)
        {
            List<object> list = new List<object>();
            if (a != null) list.AddRange(a);
            if (b != null) list.AddRange(b);
            return list.ToArray();
        }

        static object Get(object o, string key)
        {
            Dictionary<string, object> d = Json.Obj(o);
            object v;
            if (d != null && d.TryGetValue(key, out v)) return v;
            return null;
        }

        static bool Allowed(object lib)
        {
            object rules = Get(lib, "rules");
            object[] arr = Json.Arr(rules);
            if (arr == null || arr.Length == 0) return true;
            bool allowed = false;
            foreach (object r in arr)
            {
                Dictionary<string, object> rd = Json.Obj(r);
                if (rd == null) continue;
                if (rd.ContainsKey("features")) continue; // we have no optional features (demo / quickplay / custom res)
                string action = Json.Str(r, "action");
                Dictionary<string, object> os = Json.ObjAt(r, "os");
                bool matches = true;
                if (os != null)
                {
                    string name = Json.Str(os, "name");
                    if (!string.IsNullOrEmpty(name) && name != "windows") matches = false;
                }
                if (matches) allowed = action == "allow";
            }
            return allowed;
        }

        public static string MavenPath(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string[] parts = name.Split(':');
            if (parts.Length < 3) return null;
            string group = parts[0].Replace('.', '/');
            string artifact = parts[1];
            string version = parts[2];
            string classifier = parts.Length > 3 ? "-" + parts[3] : "";
            return group + "/" + artifact + "/" + version + "/" + artifact + "-" + version + classifier + ".jar";
        }

        static string Split(string s, char sep, int idx) { return s; }

        static List<string> SplitArgs(string s)
        {
            List<string> list = new List<string>();
            StringBuilder cur = new StringBuilder();
            bool quote = false;
            foreach (char c in s)
            {
                if (c == '"') { quote = !quote; continue; }
                if (!quote && char.IsWhiteSpace(c)) { if (cur.Length > 0) { list.Add(cur.ToString()); cur.Length = 0; } continue; }
                cur.Append(c);
            }
            if (cur.Length > 0) list.Add(cur.ToString());
            return list;
        }

        static void AppendArgs(object[] arr, List<string> target, LaunchOptions o, List<string> cp, string nativeDir, string gameDir, string id)
        {
            if (arr == null) return;
            foreach (object item in arr)
            {
                if (item is string) { target.Add(Subst((string)item, o, cp, nativeDir, gameDir, id)); continue; }
                Dictionary<string, object> d = Json.Obj(item);
                if (d == null) continue;
                if (!Allowed(item)) continue;
                object value = Get(item, "value");
                if (value is string) target.Add(Subst((string)value, o, cp, nativeDir, gameDir, id));
                else
                {
                    object[] vals = Json.Arr(value);
                    if (vals != null) foreach (object v in vals) if (v is string) target.Add(Subst((string)v, o, cp, nativeDir, gameDir, id));
                }
            }
        }

        static string Subst(string s, LaunchOptions o, List<string> cp, string nativeDir, string gameDir, string id)
        {
            string uuid = OfflineUuid(o.Username);
            s = s.Replace("${auth_player_name}", o.Username);
            s = s.Replace("${version_name}", id);
            s = s.Replace("${game_directory}", gameDir);
            s = s.Replace("${assets_root}", Path.Combine(gameDir, "assets"));
            s = s.Replace("${game_assets}", Path.Combine(gameDir, "assets", "virtual", "legacy"));
            s = s.Replace("${assets_index_name}", AssetIndexName(gameDir, id));
            s = s.Replace("${auth_uuid}", uuid);
            s = s.Replace("${auth_access_token}", "0");
            s = s.Replace("${auth_session}", "0");
            s = s.Replace("${auth_xuid}", "0");
            s = s.Replace("${xuid}", "0");
            s = s.Replace("${clientid}", "");
            s = s.Replace("${user_type}", "legacy");
            s = s.Replace("${user_properties}", "{}");
            s = s.Replace("${version_type}", "release");
            s = s.Replace("${classpath}", string.Join(";", cp.ToArray()));
            s = s.Replace("${natives_directory}", nativeDir);
            s = s.Replace("${launcher_name}", "Volk");
            s = s.Replace("${launcher_version}", "1.0");
            s = s.Replace("${classpath_separator}", ";");
            s = s.Replace("${path_separator}", ";");
            s = s.Replace("${library_directory}", Path.Combine(gameDir, "libraries"));
            s = s.Replace("${resolution_width}", o.Width.ToString());
            s = s.Replace("${resolution_height}", o.Height.ToString());
            s = s.Replace("${quickPlayPath}", "");
            s = s.Replace("${quickPlaySingleplayer}", "");
            s = s.Replace("${quickPlayMultiplayer}", "");
            s = s.Replace("${quickPlayRealms}", "");
            return s;
        }

        static string AssetIndexName(string gameDir, string id)
        {
            try
            {
                string current = id;
                for (int depth = 0; depth < 6; depth++)
                {
                    string path = Versions.JsonPath(gameDir, current);
                    if (!File.Exists(path)) break;
                    Dictionary<string, object> vj = Json.Parse(File.ReadAllText(path));
                    Dictionary<string, object> ai = Json.ObjAt(vj, "assetIndex");
                    if (ai != null)
                    {
                        string found = Json.Str(ai, "id");
                        if (!string.IsNullOrEmpty(found)) return found;
                    }
                    string a = Json.Str(vj, "assets");
                    if (!string.IsNullOrEmpty(a)) return a;
                    string parent = Json.Str(vj, "inheritsFrom");
                    if (string.IsNullOrEmpty(parent)) break;
                    current = parent;
                }
            }
            catch { }
            return "legacy";
        }
    }

    public static class JavaInstaller
    {
        public static string DownloadJava8(Action<int, string> status)
        {
            return DownloadJava(8, status);
        }

        public static string DownloadJava(int major, Action<int, string> status)
        {
            // 1. runtime embedded in the exe (offline build) - instant, no internet
            string embedded = JavaBoot.Ensure(major, status);
            if (embedded != null)
            {
                if (status != null) Log.Write("Using embedded Java " + major + ": " + embedded);
                return embedded;
            }

            bool x64 = SysInfo.Is64BitOS;
            string arch = x64 ? "x64" : "x32";
            string url = "https://api.adoptium.net/v3/binary/latest/" + major + "/ga/windows/" + arch + "/jre/hotspot/normal/eclipse";
            Directory.CreateDirectory(Paths.JavaDir);
            string zip = Path.Combine(Paths.Root, "jre" + major + ".zip");
            if (status != null) status(-1, "Downloading Java " + major + " (" + arch + ")...");
            Http.GetFile(url, zip, delegate (long got, long total)
            {
                if (status != null && total > 0) status((int)(got * 100 / total), "Java " + major + ": " + (got / 1048576) + " / " + (total / 1048576) + " MB");
            });

            if (status != null) status(-1, "Extracting Java " + major + "...");
            string tmp = Path.Combine(Paths.Root, "jre_tmp_" + major);
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            ZipFile.ExtractToDirectory(zip, tmp);

            string[] dirs = Directory.GetDirectories(tmp);
            string home = dirs.Length > 0 ? dirs[0] : tmp;
            string target = Path.Combine(Paths.JavaDir, "jre" + major);
            if (Directory.Exists(target)) Directory.Delete(target, true);
            Directory.CreateDirectory(Paths.JavaDir);
            Directory.Move(home, target);
            try { Directory.Delete(tmp, true); } catch { }
            try { File.Delete(zip); } catch { }

            string javaw = Path.Combine(target, @"bin\javaw.exe");
            if (!File.Exists(javaw)) throw new Exception("Java extracted but javaw.exe not found.");
            if (status != null) status(100, "Java " + major + " ready");
            return javaw;
        }
    }
}


namespace Volk
{
    /// <summary>Real FPS wins on a weak laptop: power plan + process priority.</summary>
    public static class PowerBoost
    {
        static string _previous;

        public static string ActiveScheme()
        {
            try
            {
                System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo("powercfg.exe", "-getactivescheme");
                psi.RedirectStandardOutput = true;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                using (System.Diagnostics.Process p = System.Diagnostics.Process.Start(psi))
                {
                    string outp = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(4000);
                    int i = outp.IndexOf("GUID:");
                    if (i < 0) return null;
                    string rest = outp.Substring(i + 5).Trim();
                    int j = rest.IndexOf(' ');
                    string guid = j > 0 ? rest.Substring(0, j) : rest;
                    return guid.Trim();
                }
            }
            catch { return null; }
        }

        public static void Enable(Action<string> log)
        {
            try
            {
                _previous = ActiveScheme();
                Run("-setactive SCHEME_MIN");
                if (log != null) log("Power plan -> High performance");
            }
            catch (Exception ex) { if (log != null) log("powercfg failed: " + ex.Message); }
        }

        public static void Restore(Action<string> log)
        {
            try
            {
                if (string.IsNullOrEmpty(_previous)) return;
                Run("-setactive " + _previous);
                if (log != null) log("Power plan restored");
                _previous = null;
            }
            catch { }
        }

        static void Run(string args)
        {
            System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo("powercfg.exe", args);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            using (System.Diagnostics.Process p = System.Diagnostics.Process.Start(psi))
            {
                p.WaitForExit(8000);
            }
        }
    }
}


namespace Volk
{
    /// <summary>Audio diagnostics: does Windows have a device, and are the sound files on disk?</summary>
    public static class AudioCheck
    {
        public static string Devices()
        {
            try
            {
                using (System.Management.ManagementObjectSearcher s =
                    new System.Management.ManagementObjectSearcher("SELECT Name, Status FROM Win32_SoundDevice"))
                {
                    System.Text.StringBuilder sb = new System.Text.StringBuilder();
                    foreach (System.Management.ManagementObject o in s.Get())
                    {
                        string n = o["Name"] as string;
                        if (string.IsNullOrEmpty(n)) continue;
                        if (sb.Length > 0) sb.Append(" | ");
                        sb.Append(n.Trim());
                    }
                    return sb.Length == 0 ? "" : sb.ToString();
                }
            }
            catch { return null; }
        }

        public static void SoundStats(string gameDir, string versionId, out int have, out int total)
        {
            have = 0; total = 0;
            try
            {
                string indexId = null;
                string cur = versionId;
                for (int depth = 0; depth < 6 && indexId == null; depth++)
                {
                    string path = Versions.JsonPath(gameDir, cur);
                    if (!File.Exists(path)) break;
                    Dictionary<string, object> vj = Json.Parse(File.ReadAllText(path));
                    Dictionary<string, object> ai = Json.ObjAt(vj, "assetIndex");
                    if (ai != null) indexId = Json.Str(ai, "id");
                    if (indexId == null) indexId = Json.Str(vj, "assets");
                    if (indexId == null) cur = Json.Str(vj, "inheritsFrom");
                    if (string.IsNullOrEmpty(cur)) break;
                }
                if (string.IsNullOrEmpty(indexId)) return;

                string indexPath = Path.Combine(gameDir, "assets", "indexes", indexId + ".json");
                if (!File.Exists(indexPath)) return;

                Dictionary<string, object> index = Json.Parse(File.ReadAllText(indexPath));
                Dictionary<string, object> objects = Json.ObjAt(index, "objects");
                if (objects == null) return;

                foreach (KeyValuePair<string, object> kv in objects)
                {
                    if (!kv.Key.StartsWith("minecraft/sounds/", StringComparison.OrdinalIgnoreCase)) continue;
                    total++;
                    Dictionary<string, object> e = Json.Obj(kv.Value);
                    string hash = e == null ? null : Json.Str(e, "hash");
                    if (string.IsNullOrEmpty(hash)) continue;
                    string file = Path.Combine(gameDir, "assets", "objects", hash.Substring(0, 2), hash);
                    if (File.Exists(file)) have++;
                }
            }
            catch { }
        }
    }
}
