using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;

namespace Volk
{
    /// <summary>
    /// Java runtimes embedded in the exe (offline build). Extracted to
    /// %APPDATA%\.volk\java\jre&lt;major&gt; on first use, so the game never needs a Java install.
    /// </summary>
    public static class JavaBoot
    {
        public static bool HasEmbedded(int major)
        {
            return typeof(JavaBoot).Assembly.GetManifestResourceStream("Jre" + major) != null;
        }

        public static string Ensure(int major, Action<int, string> status)
        {
            string target = Path.Combine(Paths.JavaDir, "jre" + major);
            string javaw = Path.Combine(target, @"bin\javaw.exe");
            if (File.Exists(javaw)) return javaw;

            if (!SysInfo.Is64BitOS) return null; // embedded builds are x64

            Stream stream = typeof(JavaBoot).Assembly.GetManifestResourceStream("Jre" + major);
            if (stream == null) return null;

            try
            {
                if (status != null) status(-1, "Unpacking Java " + major + " (first run, one time)...");
                string tmp = target + ".tmp";
                if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
                Directory.CreateDirectory(tmp);

                using (ZipArchive zip = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    string rootPrefix = null;
                    foreach (ZipArchiveEntry e in zip.Entries)
                    {
                        string n = e.FullName.Replace('\\', '/');
                        int slash = n.IndexOf('/');
                        if (slash > 0) { rootPrefix = n.Substring(0, slash + 1); break; }
                    }

                    int total = zip.Entries.Count;
                    int done = 0;
                    int lastPct = -1;
                    foreach (ZipArchiveEntry e in zip.Entries)
                    {
                        done++;
                        if (status != null && total > 0)
                        {
                            int pct = (int)((long)done * 100 / total);
                            if (pct != lastPct && (pct % 2 == 0 || pct == 100))
                            {
                                lastPct = pct;
                                status(pct, "Unpacking Java " + major + "... " + pct + "%");
                            }
                        }

                        if (string.IsNullOrEmpty(e.Name)) continue; // directory entry
                        string rel = e.FullName.Replace('\\', '/');
                        if (rootPrefix != null && rel.StartsWith(rootPrefix)) rel = rel.Substring(rootPrefix.Length);
                        if (rel.Length == 0) continue;
                        string dest = Path.Combine(tmp, rel.Replace('/', Path.DirectorySeparatorChar));
                        Directory.CreateDirectory(Path.GetDirectoryName(dest));
                        try { e.ExtractToFile(dest, true); } catch (IOException) { }
                    }
                }

                if (Directory.Exists(target)) Directory.Delete(target, true);
                Directory.CreateDirectory(Paths.JavaDir);
                Directory.Move(tmp, target);

                string check = Path.Combine(target, @"bin\javaw.exe");
                if (!File.Exists(check)) throw new Exception("javaw.exe missing after unpacking Java " + major);
                if (status != null) status(100, "Java " + major + " ready (unpacked)");
                return check;
            }
            catch (Exception ex)
            {
                Log.Write("JavaBoot.Ensure(" + major + "): " + ex);
                if (status != null) status(-1, "Java unpack failed: " + ex.Message);
                try { if (Directory.Exists(target + ".tmp")) Directory.Delete(target + ".tmp", true); } catch { }
                return null;
            }
            finally
            {
                try { stream.Dispose(); } catch { }
            }
        }
    }
}
