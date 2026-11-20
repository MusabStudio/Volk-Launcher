using System;
using System.IO;
using System.Threading;
using Volk;

class TestMain
{
    static void Main(string[] args)
    {
        Log.Init();
        string version = args.Length > 0 ? args[0] : "1.8.9";
        bool installOnly = args.Length > 1 && args[1] == "install";

        LaunchOptions o = new LaunchOptions();
        o.Username = "TestPlayer";
        o.VersionId = version;
        o.GameDir = Path.Combine(Paths.Root, "testgame");
        o.RamMb = 1536;
        o.DownloadSounds = false;
        o.PresetLevel = 1;
        o.Width = 854;
        o.Height = 480;

        Progress p = new Progress();
        p.Log = delegate (string s) { Console.WriteLine(s); };
        p.Status = delegate (int pct, string text) { Console.WriteLine("  [" + pct + "%] " + text); };

        try
        {
            string java = JavaFinder.Find(new Settings());
            if (java == null || JavaFinder.MajorVersion(java) > 8)
            {
                Console.WriteLine("Java 8 required for old MC - downloading Java 8...");
                java = JavaInstaller.DownloadJava8(delegate (int pct, string t) { Console.WriteLine("  [" + pct + "%] " + t); });
            }
            o.JavawPath = java;
            Console.WriteLine("JAVA = " + java + "  (" + JavaFinder.Describe(java) + ") major=" + JavaFinder.MajorVersion(java));

            Console.WriteLine("NeedsInstall = " + GameFiles.NeedsInstall(o.GameDir, version));
            GameFiles.Install(o, p);
            GameFiles.ApplySpeedPreset(o.GameDir, 1);
            Console.WriteLine("INSTALL OK");

            if (installOnly) { Console.WriteLine("install-only done"); return; }

            System.Diagnostics.Process proc = Launcher.Play(o, p, delegate (string s) { Console.WriteLine("GAME> " + s); });
            Console.WriteLine("STARTED PID " + proc.Id);
            bool exited = proc.WaitForExit(75000);
            if (!exited)
            {
                Console.WriteLine("RESULT: still running after 75s -> LAUNCH OK");
                try { proc.Kill(); } catch { }
            }
            else
            {
                Console.WriteLine("RESULT: exited early, code " + proc.ExitCode + " -> LAUNCH FAILED");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("EXCEPTION: " + ex);
        }
    }
}

