using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace OPBlocksManager.Services
{
    /// <summary>
    /// P5 — makes the OP Blocks palette appear in EVERY new Aspen simulation with
    /// zero manual steps. Aspen keeps the list of loaded model libraries per user
    /// under HKCU\Software\AspenTech\Aspen Plus\&lt;engine&gt;\Libraries as numbered
    /// subkeys (Display Name / Path / Active / Position); a fresh case loads every
    /// entry whose Active = 1. "Enable in Aspen" used to only drop a template the
    /// user had to pick by hand — this registers the deployed "ONE PROCESS.apm"
    /// as an Active library so the palette is simply there.
    ///
    /// IMPORTANT: Aspen REWRITES this key from its in-memory list when it closes,
    /// so the write only sticks when Aspen is not running. <see cref="AspenRunning"/>
    /// lets the caller tell the user to close Aspen first (same rule as DWSIM).
    /// </summary>
    public sealed class AspenLibraryRegistrar
    {
        // Aspen shows the .apm's LIBRARY name in Manage Libraries ("ONE PROCESS");
        // "OP Blocks" is the palette CATEGORY/tab inside it. Use the library name so
        // a fresh registration matches (and updates) an entry the user added by hand,
        // instead of creating a confusing duplicate.
        public const string DisplayName = "ONE PROCESS";
        private const string DefaultAspenKey = @"Software\AspenTech\Aspen Plus";

        // Overridable so the registry mechanics can be exercised against a sandbox
        // key in a test without an Aspen install; production uses the default path.
        private readonly string _aspenKey;

        public AspenLibraryRegistrar(string aspenKeyOverride = null)
        {
            _aspenKey = string.IsNullOrEmpty(aspenKeyOverride) ? DefaultAspenKey : aspenKeyOverride;
        }

        public sealed class Result { public bool Success; public string Message; }

        /// <summary>True while Aspen Plus (GUI or engine) is running — it will clobber our write on exit.</summary>
        public static bool AspenRunning()
        {
            foreach (string n in new[] { "AspenPlus", "apmain", "apwn" })
                if (Process.GetProcessesByName(n).Length > 0) return true;
            return false;
        }

        /// <summary>Registers ONE PROCESS.apm as an Active library under every detected Aspen engine.</summary>
        public Result Enable()
        {
            string apm = ResolvePalette();
            if (apm == null)
                return new Result { Success = false, Message = "ONE PROCESS.apm palette not found next to the app." };

            List<string> engines = AspenEngineVersions();
            if (engines.Count == 0)
                return new Result { Success = false, Message = "No Aspen Plus installation was found under HKCU." };

            int done = 0;
            foreach (string ver in engines)
            {
                try { if (WriteLibraryEntry(ver, apm)) done++; }
                catch (Exception ex) { return new Result { Success = false, Message = "Registry write failed: " + ex.Message }; }
            }

            string note = AspenRunning()
                ? " NOTE: Aspen is open — close and reopen it for the palette to load (Aspen rewrites its " +
                  "library list on exit, so the change takes effect on the next launch)."
                : " Open Aspen and the OP Blocks palette tab is already there in any new simulation.";
            return new Result
            {
                Success = done > 0,
                Message = done > 0
                    ? $"OP Blocks palette activated for {done} Aspen version(s)." + note
                    : "Could not activate the palette on any Aspen version."
            };
        }

        /// <summary>Removes our library entry from every Aspen engine (leaves Aspen's own entries intact).</summary>
        public Result Disable()
        {
            int removed = 0;
            foreach (string ver in AspenEngineVersions())
            {
                try { if (RemoveLibraryEntry(ver)) removed++; }
                catch { /* best-effort cleanup */ }
            }
            return new Result
            {
                Success = true,
                Message = removed > 0
                    ? $"OP Blocks palette entry removed from {removed} Aspen version(s)."
                    : "No OP Blocks palette entry was present."
            };
        }

        /// <summary>True if our library entry is Active on at least one Aspen engine.</summary>
        public bool IsEnabled()
        {
            foreach (string ver in AspenEngineVersions())
            {
                using RegistryKey libs = Registry.CurrentUser.OpenSubKey($@"{_aspenKey}\{ver}\Libraries");
                if (libs == null) continue;
                foreach (string idx in libs.GetSubKeyNames())
                    using (RegistryKey e = libs.OpenSubKey(idx))
                        if (e != null && IsOurs(e) && Convert.ToInt32(e.GetValue("Active", 0)) == 1)
                            return true;
            }
            return false;
        }

        // ---- registry mechanics -------------------------------------------------

        private bool WriteLibraryEntry(string ver, string apmPath)
        {
            using RegistryKey libs = Registry.CurrentUser.CreateSubKey($@"{_aspenKey}\{ver}\Libraries");
            if (libs == null) return false;

            // Reuse our existing slot (idempotent) or the next free numbered index.
            string slot = null;
            int maxPos = -1;
            foreach (string idx in libs.GetSubKeyNames())
            {
                using RegistryKey e = libs.OpenSubKey(idx);
                if (e == null) continue;
                maxPos = Math.Max(maxPos, Convert.ToInt32(e.GetValue("Position", 0)));
                if (IsOurs(e)) slot = idx;
            }
            if (slot == null)
            {
                int n = 1;
                var names = new HashSet<string>(libs.GetSubKeyNames(), StringComparer.OrdinalIgnoreCase);
                while (names.Contains(n.ToString())) n++;
                slot = n.ToString();
            }

            using RegistryKey entry = libs.CreateSubKey(slot);
            entry.SetValue("Display Name", DisplayName, RegistryValueKind.String);
            entry.SetValue("Path", apmPath, RegistryValueKind.String);
            entry.SetValue("Active", 1, RegistryValueKind.DWord);
            // keep whatever position it had; a brand-new entry goes to the end
            if (entry.GetValue("Position") == null)
                entry.SetValue("Position", maxPos + 1, RegistryValueKind.DWord);
            return true;
        }

        private bool RemoveLibraryEntry(string ver)
        {
            using RegistryKey libs = Registry.CurrentUser.OpenSubKey($@"{_aspenKey}\{ver}\Libraries", writable: true);
            if (libs == null) return false;
            bool any = false;
            foreach (string idx in libs.GetSubKeyNames().ToArray())
            {
                using (RegistryKey e = libs.OpenSubKey(idx))
                    if (e == null || !IsOurs(e)) continue;
                libs.DeleteSubKeyTree(idx, throwOnMissingSubKey: false);
                any = true;
            }
            return any;
        }

        /// <summary>
        /// Recognises our entry — including one the user added by hand — by the
        /// library name ("ONE PROCESS" or the "OP Blocks" category) or by any
        /// ONE PROCESS*.apm path (covers older ONE PROCESS.staged.apm registrations).
        /// Never matches Aspen's own libraries, so their entries are untouched.
        /// </summary>
        private static bool IsOurs(RegistryKey entry)
        {
            string name = entry.GetValue("Display Name") as string ?? "";
            string path = entry.GetValue("Path") as string ?? "";
            return name.Equals("ONE PROCESS", StringComparison.OrdinalIgnoreCase)
                || name.Equals("OP Blocks", StringComparison.OrdinalIgnoreCase)
                || path.IndexOf(@"\ONE PROCESS", StringComparison.OrdinalIgnoreCase) >= 0
                   && path.EndsWith(".apm", StringComparison.OrdinalIgnoreCase);
        }

        private List<string> AspenEngineVersions()
        {
            var vers = new List<string>();
            using RegistryKey k = Registry.CurrentUser.OpenSubKey(_aspenKey);
            if (k != null)
                foreach (string sub in k.GetSubKeyNames())
                    if (!string.Equals(sub, "CurVer", StringComparison.OrdinalIgnoreCase) &&
                        sub.Length > 0 && (char.IsDigit(sub[0])))
                        vers.Add(sub);
            return vers;
        }

        /// <summary>Locates the deployed ONE PROCESS.apm (staged beside the app; dev fallback installer\aspen).</summary>
        private static string ResolvePalette()
        {
            string exeDir = AppContext.BaseDirectory;
            foreach (string rel in new[] { @"aspen\ONE PROCESS.apm", @"templates\ONE PROCESS.apm" })
            {
                string p = Path.Combine(exeDir, rel);
                if (File.Exists(p)) return p;
            }
            var dir = new DirectoryInfo(exeDir);
            for (int i = 0; i < 8 && dir != null; i++)
            {
                string c = Path.Combine(dir.FullName, "installer", "aspen", "ONE PROCESS.apm");
                if (File.Exists(c)) return c;
                if (File.Exists(Path.Combine(dir.FullName, "OPBlocks.sln"))) break;
                dir = dir.Parent;
            }
            return null;
        }
    }
}
