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
            int moved = 0;
            try
            {
                string modsDir = Path.Combine(gameDir, "mods");
                if (!Directory.Exists(modsDir)) return 0;
                string disabled = Path.Combine(gameDir, "mods.disabled");
                foreach (string jar in Directory.GetFiles(modsDir, "*.jar"))
                {
                    Mod m = Read(jar);
                    if (string.IsNullOrEmpty(m.NeedsMc)) continue;
                    if (Compatible(m.NeedsMc, mcVersion)) continue;
                    try
                    {
                        Directory.CreateDirectory(disabled);
                        string dest = Path.Combine(disabled, Path.GetFileName(jar));
                        if (File.Exists(dest)) File.Delete(dest);
                        File.Move(jar, dest);
                        moved++;
                        if (log != null) log("moved to mods.disabled: " + m.Name + " (needs " + m.NeedsMc + ")");
                    }
                    catch { }
                }
                if (moved > 0 && log != null)
                    log(moved + " mod(s) for another Minecraft version moved to " + disabled);
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

        /// <summary>Compares only the major.minor ("1.21.11" and "&gt;=1.21 &lt;1.22" are compatible).</summary>
        public static bool Compatible(string need, string mcVersion)
        {
            if (string.IsNullOrEmpty(need)) return true;
            need = need.Trim();
            if (need == "*") return true;
            string mcKey = MajorMinor(mcVersion);
            if (string.IsNullOrEmpty(mcKey)) return true;
            foreach (string tok in need.Split(new char[] { ' ', ',', '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (MajorMinor(tok) == mcKey) return true;
            }
            return false;
        }

        static string MajorMinor(string v)
        {
            System.Text.RegularExpressions.Match m =
                System.Text.RegularExpressions.Regex.Match(v, @"\d+\.\d+");
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
