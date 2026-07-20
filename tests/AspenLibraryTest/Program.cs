using System;
using System.IO;
using Microsoft.Win32;
using OPBlocksManager.Services;

// P5 regression — exercises the REAL AspenLibraryRegistrar against a SANDBOX
// registry root (never the live Aspen key), proving: write schema, idempotency,
// Aspen-entry preservation, IsEnabled, and clean removal. Exit 0 = all pass.
class P5Test
{
    const string Sandbox = @"Software\OPBlocksTest\Aspen Plus";
    static int fails = 0;

    static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + what);
        if (!ok) fails++;
    }

    static RegistryKey Entry(string ver, string idx)
        => Registry.CurrentUser.OpenSubKey($@"{Sandbox}\{ver}\Libraries\{idx}");

    static int Main()
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\OPBlocksTest", false); } catch { }

        // seed: engine 40.0 with an existing ASPEN-OWNED library (must survive) + CurVer (must be skipped)
        using (var e = Registry.CurrentUser.CreateSubKey($@"{Sandbox}\40.0\Libraries\1"))
        {
            e.SetValue("Display Name", "Ultrafiltration", RegistryValueKind.String);
            e.SetValue("Path", @"C:\Aspen\Examples\Ultrafiltration.apm", RegistryValueKind.String);
            e.SetValue("Active", 0, RegistryValueKind.DWord);
            e.SetValue("Position", 0, RegistryValueKind.DWord);
        }
        Registry.CurrentUser.CreateSubKey($@"{Sandbox}\CurVer").Dispose();

        // stage a ONE PROCESS.apm next to this exe so ResolvePalette finds it
        string aspenDir = Path.Combine(AppContext.BaseDirectory, "aspen");
        Directory.CreateDirectory(aspenDir);
        string repoApm = null;
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            string c = Path.Combine(d.FullName, "installer", "aspen", "ONE PROCESS.apm");
            if (File.Exists(c)) { repoApm = c; break; }
        }
        if (repoApm == null)
        {
            Console.WriteLine("SKIP: repo ONE PROCESS.apm not found walking up from " + AppContext.BaseDirectory);
            return 0;
        }
        File.Copy(repoApm, Path.Combine(aspenDir, "ONE PROCESS.apm"), true);

        var reg = new AspenLibraryRegistrar(Sandbox);

        var r1 = reg.Enable();
        Check(r1.Success, "Enable() succeeds: " + r1.Message);
        using (var ours = Entry("40.0", "2"))
        {
            Check(ours != null, "our entry created at a new slot");
            if (ours != null)
            {
                Check((ours.GetValue("Display Name") as string) == "OP Blocks", "Display Name = OP Blocks");
                Check(((string)ours.GetValue("Path")).EndsWith(@"aspen\ONE PROCESS.apm"), "Path -> deployed apm");
                Check(ours.GetValueKind("Active") == RegistryValueKind.DWord && (int)ours.GetValue("Active") == 1, "Active = 1 (DWORD)");
                Check(ours.GetValueKind("Position") == RegistryValueKind.DWord, "Position is a DWORD");
            }
        }
        using (var aspen = Entry("40.0", "1"))
            Check(aspen != null && (string)aspen.GetValue("Display Name") == "Ultrafiltration"
                  && (int)aspen.GetValue("Active") == 0, "Aspen's own library left intact");

        reg.Enable();
        using (var libs = Registry.CurrentUser.OpenSubKey($@"{Sandbox}\40.0\Libraries"))
        {
            int mine = 0;
            foreach (var idx in libs.GetSubKeyNames())
                using (var e = libs.OpenSubKey(idx))
                    if ((e.GetValue("Display Name") as string) == "OP Blocks") mine++;
            Check(mine == 1, "Enable() is idempotent (no duplicate entry)");
        }

        Check(reg.IsEnabled(), "IsEnabled() = true after Enable");

        var rd = reg.Disable();
        Check(rd.Success, "Disable() succeeds: " + rd.Message);
        Check(Entry("40.0", "2") == null, "our entry removed");
        using (var aspen = Entry("40.0", "1"))
            Check(aspen != null, "Aspen's own library still present after Disable");
        Check(!reg.IsEnabled(), "IsEnabled() = false after Disable");

        try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\OPBlocksTest", false); } catch { }

        Console.WriteLine(fails == 0 ? "\nALL P5 CHECKS PASSED" : $"\n{fails} FAILURE(S)");
        return fails == 0 ? 0 : 1;
    }
}
