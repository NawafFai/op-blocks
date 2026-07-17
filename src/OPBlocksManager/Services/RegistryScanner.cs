using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace OPBlocksManager.Services
{
    /// <summary>
    /// Detects installed host simulators (spec §10 screen 1) and reports whether a
    /// given block CLSID is registered as a CAPE-OPEN Unit Operation.
    /// </summary>
    public sealed class RegistryScanner
    {
        // CapeUnitOperation category id — a block is a usable CO unit only if its
        // CLSID is a member of this category.
        public const string CapeUnitOperationCatid = "{678C09A5-7D66-11D2-A67D-00105A42887F}";

        public List<SimulatorInfo> DetectSimulators()
        {
            var list = new List<SimulatorInfo>();
            DetectAspen(list);
            DetectDwsim(list);
            return list;
        }

        private static void DetectAspen(List<SimulatorInfo> list)
        {
            // The reliable source of the install path is the versioned Program Files
            // folder; the registry key under AspenTech\Aspen Plus confirms the engine
            // version.
            string aspenRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "AspenTech");

            if (Directory.Exists(aspenRoot))
            {
                foreach (string dir in Directory.GetDirectories(aspenRoot, "Aspen Plus V*"))
                {
                    string folder = Path.GetFileName(dir); // "Aspen Plus V14.0"
                    list.Add(new SimulatorInfo
                    {
                        Kind = "Aspen Plus",
                        Name = folder,
                        Version = DescribeAspenFolder(folder),
                        Bitness = "x64",
                        Path = dir,
                        Found = true
                    });
                }
            }

            if (list.Count == 0)
            {
                // No folder found but registry present → report unknown-path install.
                using RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\AspenTech\Aspen Plus");
                if (k != null)
                {
                    foreach (string ver in k.GetSubKeyNames())
                    {
                        if (string.Equals(ver, "CurVer", StringComparison.OrdinalIgnoreCase)) continue;
                        list.Add(new SimulatorInfo
                        {
                            Kind = "Aspen Plus",
                            Name = "Aspen Plus (engine " + ver + ")",
                            Version = MapEngine(ver),
                            Bitness = "x64",
                            Path = "(registered; path not found)",
                            Found = true
                        });
                    }
                }
            }
        }

        private static string DescribeAspenFolder(string folder)
        {
            // "Aspen Plus V14.0" → "V14 (engine 40.0)". Any Aspen Plus version is
            // detected (the caller globs "Aspen Plus V*"); the blocks register as
            // standard CAPE-OPEN units and are version-independent, so this only
            // gives each detected version a friendly label.
            int idx = folder.IndexOf('V');
            string vpart = idx >= 0 ? folder.Substring(idx) : folder;
            if (vpart.StartsWith("V14")) return "V14 (engine 40.0)";
            if (vpart.StartsWith("V12.1")) return "V12.1 (engine 39.0)";
            if (vpart.StartsWith("V12")) return "V12 (engine 38.0)";
            if (vpart.StartsWith("V11")) return "V11 (engine 37.0)";
            if (vpart.StartsWith("V10")) return "V10 (engine 36.0)";
            return vpart; // V13 or any future/other version: show the folder version as-is
        }

        private static string MapEngine(string engine)
        {
            switch (engine)
            {
                case "40.0": return "V14 (engine 40.0)";
                case "39.0": return "V12.1 (engine 39.0)";
                case "38.0": return "V12 (engine 38.0)";
                case "37.0": return "V11 (engine 37.0)";
                default: return "engine " + engine;
            }
        }

        private static void DetectDwsim(List<SimulatorInfo> list)
        {
            string[] candidates =
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DWSIM"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "DWSIM"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "DWSIM")
            };

            foreach (string dir in candidates)
            {
                if (Directory.Exists(dir) &&
                    (File.Exists(Path.Combine(dir, "DWSIM.exe")) || File.Exists(Path.Combine(dir, "CapeOpen.dll"))))
                {
                    list.Add(new SimulatorInfo
                    {
                        Kind = "DWSIM",
                        Name = "DWSIM",
                        Version = "installed",
                        Bitness = File.Exists(Path.Combine(dir, "DWSIM.exe")) ? "x64" : "x64",
                        Path = dir,
                        Found = true
                    });
                    return;
                }
            }
        }

        /// <summary>
        /// Reports registration state of a block CLSID: whether it is a member of the
        /// CapeUnitOperation category in the x64 hive, the x86 (WOW6432) hive, or both.
        /// </summary>
        public RegistrationState GetRegistrationState(string clsid)
        {
            string g = NormalizeClsid(clsid);
            bool x64 = CatidPresent(@"SOFTWARE\Classes\CLSID\" + g);
            bool x86 = CatidPresent(@"SOFTWARE\WOW6432Node\Classes\CLSID\" + g);
            if (x64 && x86) return RegistrationState.Both;
            if (x64 || x86) return RegistrationState.Partial;
            return RegistrationState.None;
        }

        private static bool CatidPresent(string clsidPath)
        {
            using RegistryKey k = Registry.LocalMachine.OpenSubKey(
                clsidPath + @"\Implemented Categories\" + CapeUnitOperationCatid);
            return k != null;
        }

        public static string NormalizeClsid(string clsid)
        {
            if (string.IsNullOrEmpty(clsid)) return clsid;
            clsid = clsid.Trim();
            if (!clsid.StartsWith("{")) clsid = "{" + clsid;
            if (!clsid.EndsWith("}")) clsid = clsid + "}";
            return clsid;
        }
    }

    public enum RegistrationState { None, Partial, Both }
}
