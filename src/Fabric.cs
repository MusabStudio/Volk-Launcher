using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Volk
{
    /// <summary>Fabric loader profile + the usual performance mods from Modrinth.</summary>
    public static class Fabric
    {
        const string Meta = "https://meta.fabricmc.net/v2";

        static readonly string[] PerfMods = new string[]
        {
            "sodium", "lithium", "ferrite-core", "immediatelyfast", "krypton", "fabric-api"
        };

        /// <summary>True when the user has put jars in mods/ that need Fabric.</summary>
        public static bool HasUserMods(string gameDir)
        {
            try
            {
                string modsDir = Path.Combine(gameDir, "mods");
                if (!Directory.Exists(modsDir)) return false;
                return Directory.GetFiles(modsDir, "*.jar").Length > 0;
            }
            catch { return false; }
        }

        public static string LoaderVersion()
        {
            object[] list = Json.ParseArray(Http.GetString(Meta + "/versions/loader"));
            if (list == null) throw new Exception("Fabric loader list is empty");
            foreach (object o in list)
            {
                Dictionary<string, object> d = Json.Obj(o);
                if (d == null) continue;
                object stable;
                if (d.TryGetValue("stable", out stable) && stable is bool && (bool)stable)
                    return Json.Str(o, "version");
            }
            return Json.Str(list[0], "version");
        }

        /// <summary>Creates versions/fabric-loader-X-Y/fabric-loader-X-Y.json and returns the id.</summary>
        public static string EnsureProfile(string gameDir, string mc, Progress p)
        {
            // Reuse a Fabric profile that other launchers already installed for this version
            // (it may use a loader build that this Minecraft version actually supports).
            string found = FindExisting(gameDir, mc);
            if (found != null)
            {
                if (p != null) p.Say("Using existing Fabric profile: " + found);
                return found;
            }

            string loader = LoaderVersion();
            string id = "fabric-loader-" + loader + "-" + mc;
            string dir = Versions.VersionDir(gameDir, id);
            string path = Path.Combine(dir, id + ".json");
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(dir);
                if (p != null) p.Step(-1, "Setting up Fabric " + loader + "...");
                string json = Http.GetString(Meta + "/versions/loader/" + mc + "/" + loader + "/profile/json");
                Json.Parse(json); // validate before saving
                File.WriteAllText(path, json);
                if (p != null) p.Say("Fabric " + loader + " profile created for " + mc);
            }
            return id;
        }

        public static int InstallMods(string gameDir, string mc, Action<string> log)
        {
            string modsDir = Path.Combine(gameDir, "mods");
            Directory.CreateDirectory(modsDir);
            int installed = 0;

            foreach (string slug in PerfMods)
            {
                try
                {
                    if (AlreadyPresent(modsDir, slug))
                    {
                        if (log != null) log(slug + ": already in mods folder - kept");
                        installed++;
                        continue;
                    }

                    string url = "https://api.modrinth.com/v2/project/" + slug + "/version?game_versions=" +
                        Uri.EscapeDataString("[\"" + mc + "\"]") +
                        "&loaders=" + Uri.EscapeDataString("[\"fabric\"]");
                    object[] versions = Json.ParseArray(Http.GetString(url));
                    if (versions == null || versions.Length == 0)
                    {
                        if (log != null) log(slug + ": no build for " + mc + " yet - skipped");
                        continue;
                    }

                    string fileUrl = null;
                    string fileName = null;
                    object[] files = Json.Arr(Json.Obj(versions[0])["files"]);
                    if (files != null)
                    {
                        foreach (object fo in files)
                        {
                            Dictionary<string, object> f = Json.Obj(fo);
                            if (f == null) continue;
                            object prim;
                            bool primary = f.TryGetValue("primary", out prim) && prim is bool && (bool)prim;
                            if (primary || fileUrl == null)
                            {
                                fileUrl = Json.Str(fo, "url");
                                fileName = Json.Str(fo, "filename");
                            }
                            if (primary) break;
                        }
                    }
                    if (string.IsNullOrEmpty(fileUrl) || string.IsNullOrEmpty(fileName)) continue;

                    string dest = Path.Combine(modsDir, fileName);
                    if (!File.Exists(dest))
                    {
                        if (log != null) log("Downloading " + slug + " " + fileName);
                        Http.GetFile(fileUrl, dest, null);
                    }
                    installed++;
                }
                catch (Exception ex)
                {
                    if (log != null) log(slug + " failed: " + ex.Message);
                }
            }
            if (log != null) log("Performance mods: " + installed + "/" + PerfMods.Length + " ready");
            return installed;
        }

        static bool AlreadyPresent(string modsDir, string slug)
        {
            try
            {
                string key = slug.Replace("-", "").ToLowerInvariant();
                foreach (string f in Directory.GetFiles(modsDir, "*.jar"))
                {
                    string n = Path.GetFileNameWithoutExtension(f).Replace("-", "").Replace("_", "").ToLowerInvariant();
                    if (n.StartsWith(key)) return true;
                }
            }
            catch { }
            return false;
        }

        static string FindExisting(string gameDir, string mc)
        {
            try
            {
                string versions = Path.Combine(gameDir, "versions");
                if (!Directory.Exists(versions)) return null;

                string bestId = null;
                DateTime bestTime = DateTime.MinValue;

                foreach (string dir in Directory.GetDirectories(versions))
                {
                    string id = Path.GetFileName(dir);
                    string json = Path.Combine(dir, id + ".json");
                    if (!File.Exists(json)) continue;
                    try
                    {
                        Dictionary<string, object> root = Json.Parse(File.ReadAllText(json));
                        string parent = Json.Str(root, "inheritsFrom");
                        string main = Json.Str(root, "mainClass");
                        if (parent != mc) continue;
                        if (main == null || main.IndexOf("fabric", StringComparison.OrdinalIgnoreCase) < 0) continue;

                        DateTime t = File.GetLastWriteTimeUtc(json);
                        if (t > bestTime) { bestTime = t; bestId = id; }
                    }
                    catch { }
                }
                return bestId;
            }
            catch { return null; }
        }

        public static void ClearMods(string gameDir, Action<string> log)
        {
            try
            {
                string modsDir = Path.Combine(gameDir, "mods");
                if (!Directory.Exists(modsDir)) return;
                foreach (string f in Directory.GetFiles(modsDir, "*.jar"))
                {
                    string n = Path.GetFileName(f).ToLowerInvariant();
                    if (n.StartsWith("sodium") || n.StartsWith("lithium") || n.StartsWith("ferritecore") ||
                        n.StartsWith("immediatelyfast") || n.StartsWith("krypton"))
                    {
                        try { File.Delete(f); } catch { }
                    }
                }
            }
            catch { }
        }
    }
}

namespace Volk
{
    /// <summary>Reads what the user dropped into mods/ (Fabric or Forge) without loading it.</summary>
    public static class ModScanner
    {
        public class Mod
        {
            public string File;
            public string Name;
            public string Id;
            public string Version;
            public string Loader;      // "fabric" or "forge"
            public string NeedsMc;     // declared minecraft dependency, if any
        }

        public static List<Mod> Scan(string gameDir)
        {
            List<Mod> list = new List<Mod>();
            try
            {
                string modsDir = Path.Combine(gameDir, "mods");
                if (!Directory.Exists(modsDir)) return list;
                foreach (string jar in Directory.GetFiles(modsDir, "*.jar")) list.Add(Read(jar));
            }
            catch { }
            return list;
        }

        /// <summary>Moves jars that declare a different Minecraft major.minor out of mods/, so they cannot crash the launch.</summary>
        public static int PrepareFor(string gameDir, string mcVersion, Action<string> log)
        {
            int moved = 0, restored = 0;
            try
            {
                string modsDir = Path.Combine(gameDir, "mods");
                string disabled = Path.Combine(gameDir, "mods.disabled");
                if (!Directory.Exists(modsDir)) Directory.CreateDirectory(modsDir);

                // 1. bring back mods that belong to the version we are launching now
                if (Directory.Exists(disabled))
                {
                    foreach (string jar in Directory.GetFiles(disabled, "*.jar"))
                    {
                        Mod m = Read(jar);
                        if (string.IsNullOrEmpty(m.NeedsMc) || !Compatible(m.NeedsMc, mcVersion)) continue;
                        try
                        {
                            string dest = Path.Combine(modsDir, Path.GetFileName(jar));
                            if (File.Exists(dest)) File.Delete(dest);
                            File.Move(jar, dest);
                            restored++;
                            if (log != null) log("restored: " + m.Name + " (" + m.NeedsMc + ")");
                        }
                        catch { }
                    }
                }

                // 2. take out mods for other versions (kept, not deleted)
                foreach (string jar in Directory.GetFiles(modsDir, "*.jar"))
                {
                    Mod m = Read(jar);
                    if (string.IsNullOrEmpty(m.NeedsMc) || Compatible(m.NeedsMc, mcVersion)) continue;
                    try
                    {
                        Directory.CreateDirectory(disabled);
                        string dest = Path.Combine(disabled, Path.GetFileName(jar));
                        if (File.Exists(dest)) File.Delete(dest);
                        File.Move(jar, dest);
                        moved++;
                        if (log != null) log("stashed for later: " + m.Name + " (needs " + m.NeedsMc + ")");
                    }
                    catch { }
                }

                if (restored > 0 && log != null) log(restored + " mod(s) restored for " + mcVersion);
                if (moved > 0 && log != null)
                    log(moved + " mod(s) for another Minecraft version stashed in " + disabled + " (they come back automatically)");
            }
            catch { }
            return moved;
        }

        public static Mod Read(string jar)
        {
            Mod m = new Mod();
            m.File = Path.GetFileName(jar);
            m.Name = Path.GetFileNameWithoutExtension(jar);
            m.Loader = "unknown";
            try
            {
                using (System.IO.Compression.ZipArchive zip = System.IO.Compression.ZipFile.OpenRead(jar))
                {
                    System.IO.Compression.ZipArchiveEntry e = zip.GetEntry("fabric.mod.json");
                    if (e != null)
                    {
                        using (System.IO.StreamReader sr = new System.IO.StreamReader(e.Open()))
                        {
                            Dictionary<string, object> root = Json.Parse(sr.ReadToEnd());
                            m.Loader = "fabric";
                            string n = Json.Str(root, "name");
                            string id = Json.Str(root, "id");
                            string v = Json.Str(root, "version");
                            if (!string.IsNullOrEmpty(n)) m.Name = n;
                            if (!string.IsNullOrEmpty(id)) m.Id = id;
                            if (!string.IsNullOrEmpty(v)) m.Version = v;
                            Dictionary<string, object> dep = Json.ObjAt(root, "depends");
                            if (dep != null && dep.ContainsKey("minecraft"))
                            {
                                object dv = dep["minecraft"];
                                string dsv = dv as string;
                                if (dsv == null)
                                {
                                    object[] dvArr = Json.Arr(dv);
                                    if (dvArr != null && dvArr.Length > 0 && dvArr[0] != null) dsv = dvArr[0].ToString();
                                }
                                m.NeedsMc = dsv;
                            }
                        }
                    }
                    else if (zip.GetEntry("META-INF/mods.toml") != null || zip.GetEntry("mcmod.info") != null)
                    {
                        m.Loader = "forge";
                    }
                    else if (zip.GetEntry("META-INF/MANIFEST.MF") != null)
                    {
                        m.Loader = "old";
                    }
                }
            }
            catch { }
            return m;
        }

        /// <summary>Compares Fabric-style ranges (">=26.1 &lt;27", "~26.3", "1.21.11", "*") with a version.</summary>
        public static bool Compatible(string need, string mcVersion)
        {
            if (string.IsNullOrEmpty(need)) return true;
            need = need.Trim();
            if (need == "*" || need.Length == 0) return true;

            int[] v = ParseVersion(mcVersion);
            if (v == null) return true;

            foreach (string group in need.Split(new string[] { "||" }, StringSplitOptions.None))
            {
                if (EvalGroup(group, v)) return true;
            }
            return false;
        }

        static bool EvalGroup(string group, int[] v)
        {
            bool any = false;
            foreach (string raw in group.Split(new char[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (raw == "*") continue;
                any = true;
                if (!EvalClause(raw, v)) return false;
            }
            return any;
        }

        static bool EvalClause(string token, int[] v)
        {
            string t = token.Trim();
            while (t.EndsWith("-")) t = t.Substring(0, t.Length - 1); // fabric pre-release marker

            string op = "";
            foreach (string candidate in new string[] { ">=", "<=", "==", ">", "<", "~", "^", "=" })
            {
                if (t.StartsWith(candidate)) { op = candidate; t = t.Substring(candidate.Length).Trim(); break; }
            }

            int[] need = ParseVersion(t);
            if (need == null) return true; // unknown syntax: do not block

            int cmp = Compare(v, need);
            switch (op)
            {
                case ">=": return cmp >= 0;
                case "<=": return cmp <= 0;
                case ">": return cmp > 0;
                case "<": return cmp < 0;
                case "=":
                case "==": return cmp == 0;
                case "~":
                {
                    if (cmp < 0) return false;
                    int[] upper = (int[])need.Clone();
                    int bump = upper.Length >= 2 ? upper.Length - 1 : 0; // ~26.3 -> <26.4, ~1.21.11 -> <1.21.12
                    upper[bump] = upper[bump] + 1;
                    for (int i = bump + 1; i < upper.Length; i++) upper[i] = 0;
                    return Compare(v, upper) < 0;
                }
                case "^":
                {
                    if (cmp < 0) return false;
                    int[] upper = (int[])need.Clone();
                    upper[0] = upper[0] + 1;
                    for (int i = 1; i < upper.Length; i++) upper[i] = 0;
                    return Compare(v, upper) < 0;
                }
                default:
                {
                    // plain "1.21.11" / "26.3": component prefix match
                    if (v.Length < need.Length) return false;
                    for (int i = 0; i < need.Length; i++) if (v[i] != need[i]) return false;
                    return true;
                }
            }
        }

        static int Compare(int[] a, int[] b)
        {
            int n = Math.Max(a.Length, b.Length);
            for (int i = 0; i < n; i++)
            {
                int x = i < a.Length ? a[i] : 0;
                int y = i < b.Length ? b[i] : 0;
                if (x != y) return x < y ? -1 : 1;
            }
            return 0;
        }

        static int[] ParseVersion(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            string[] parts = s.Split('.');
            List<int> nums = new List<int>();
            foreach (string p in parts)
            {
                int n;
                if (!int.TryParse(p, out n)) break;
                nums.Add(n);
            }
            return nums.Count > 0 ? nums.ToArray() : null;
        }

        static string MajorMinor(string v)
        {
            System.Text.RegularExpressions.Match m =
                System.Text.RegularExpressions.Regex.Match(v ?? "", @"\d+\.\d+");
            return m.Success ? m.Value : null;
        }

        /// <summary>Logs a friendly summary; returns true if something looks incompatible.</summary>
        public static bool Report(List<Mod> mods, string mcVersion, Action<string> log)
        {
            if (mods == null || mods.Count == 0) return false;

            int fabric = 0, forge = 0, other = 0, suspicious = 0;
            foreach (Mod m in mods)
            {
                if (m.Loader == "fabric") fabric++;
                else if (m.Loader == "forge") forge++;
                else other++;

                if (!string.IsNullOrEmpty(m.NeedsMc) && !Matches(m.NeedsMc, mcVersion))
                {
                    suspicious++;
                    if (log != null) log("mod warning: " + m.Name + " wants Minecraft " + m.NeedsMc + " (you run " + mcVersion + ")");
                }
            }

            if (log != null)
                log("Mods folder: " + mods.Count + " jar(s) - " + fabric + " Fabric, " + forge + " Forge, " + other + " unknown");

            // Tell the user clearly when the folder was built for another version.
            Dictionary<string, int> byVersion = new Dictionary<string, int>();
            foreach (Mod m in mods)
            {
                string k = MajorMinor(m.NeedsMc);
                if (string.IsNullOrEmpty(k)) continue;
                if (!byVersion.ContainsKey(k)) byVersion[k] = 0;
                byVersion[k]++;
            }
            string mcKey = MajorMinor(mcVersion);
            int best = 0;
            string bestKey = null;
            foreach (KeyValuePair<string, int> kv in byVersion)
            {
                if (kv.Value > best) { best = kv.Value; bestKey = kv.Key; }
            }
            if (bestKey != null && mcKey != null && bestKey != mcKey && log != null)
                log("hint: " + best + " mod(s) in mods/ are for Minecraft " + bestKey + " - select version " + bestKey + " in the dropdown to play with them");

            if (forge > 0 && mcVersion.IndexOf("1.1", StringComparison.Ordinal) == 0 && mcVersion != "1.12.2")
            {
                if (log != null) log("Forge mods need Forge, and this launcher starts Fabric. Fabric will ignore them (harmless).");
            }
            return suspicious > 0;
        }

        /// <summary>Very small check for ranges like ">=1.20", "~1.21.11", "*", "1.21.x".</summary>
        static bool Matches(string need, string mc)
        {
            if (string.IsNullOrEmpty(need)) return true;
            need = need.Trim();
            if (need == "*") return true;
            if (need.IndexOf(mc, StringComparison.OrdinalIgnoreCase) >= 0) return true;

            // "1.21.x" style
            string[] mcParts = mc.Split('.');
            if (mcParts.Length >= 2)
            {
                string prefix = mcParts[0] + "." + mcParts[1];
                if (need.IndexOf(prefix, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            // unknown syntax: do not nag
            if (need.IndexOfAny(new char[] { '>', '<', '=', '~', '^', ' ' }) < 0) return true;
            return false;
        }

        static bool AlreadyPresent(string modsDir, string slug)
        {
            try
            {
                string key = slug.Replace("-", "").ToLowerInvariant();
                foreach (string f in Directory.GetFiles(modsDir, "*.jar"))
                {
                    string n = Path.GetFileNameWithoutExtension(f).Replace("-", "").Replace("_", "").ToLowerInvariant();
                    if (n.StartsWith(key)) return true;
                }
            }
            catch { }
            return false;
        }
    }
}
