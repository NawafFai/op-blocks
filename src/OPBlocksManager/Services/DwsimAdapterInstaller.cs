using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OPBlocksManager.Services
{
    /// <summary>
    /// The "Enable in DWSIM" one-click feature. Unlike Aspen (COM registration), the
    /// NATIVE DWSIM adapter is a plain .NET plug-in: DWSIM discovers it by scanning
    /// every DLL in its <c>unitops\</c> folder (Assembly.LoadFile) and taking the
    /// exported <c>IExternalUnitOperation</c> types. So "enabling" = copying the
    /// adapter (<c>OPBlocks.DWSIM.dll</c>) plus its <c>OPBlocks.*</c> / <c>CapeOpen</c>
    /// dependencies into <c>%LOCALAPPDATA%\DWSIM\unitops\</c>; "removing" = deleting
    /// exactly those files. This is the mechanism proven live 2026-07-18 (the native
    /// OP blocks appear in the palette WITH icons under an OP-BLOCKS grouping).
    ///
    /// No elevation is needed — the DWSIM per-user folder is user-writable. DWSIM must
    /// be closed during the copy or the DLLs are locked; the result reports that.
    /// </summary>
    public sealed class DwsimAdapterInstaller
    {
        public const string AdapterDll = "OPBlocks.DWSIM.dll";

        public sealed class Result { public bool Success; public int Count; public string Message; }

        /// <summary>Copy the adapter + dependencies into the DWSIM unitops folder.</summary>
        public Result Install()
        {
            string src = ResolveAdapterSource();
            if (src == null)
                return new Result { Success = false, Message = "Native DWSIM adapter not found (looked for a 'dwsim' folder next to the app and in the build output)." };

            string dwsim = ResolveDwsimDir();
            if (dwsim == null)
                return new Result { Success = false, Message = "No DWSIM install found. Install DWSIM (or use \"Enable in Aspen\"), then Refresh." };

            List<string> payload = PayloadFiles(src);
            if (!payload.Any(p => Path.GetFileName(p).Equals(AdapterDll, StringComparison.OrdinalIgnoreCase)))
                return new Result { Success = false, Message = "Adapter DLL (" + AdapterDll + ") is missing from the source folder." };

            string unitops = Path.Combine(dwsim, "unitops");
            try { Directory.CreateDirectory(unitops); }
            catch (Exception ex) { return new Result { Success = false, Message = "Could not create the unitops folder: " + ex.Message }; }

            int n = 0;
            foreach (string file in payload)
            {
                try { File.Copy(file, Path.Combine(unitops, Path.GetFileName(file)), true); n++; }
                catch (IOException)
                {
                    return new Result
                    {
                        Success = false,
                        Count = n,
                        Message = "A DWSIM plug-in file is locked — close DWSIM if it is running, then try again."
                    };
                }
                catch (Exception ex) { return new Result { Success = false, Count = n, Message = "Copy failed: " + ex.Message }; }
            }

            return new Result
            {
                Success = n > 0,
                Count = n,
                Message = "Native DWSIM blocks enabled (" + n + " file(s) → " + unitops +
                          "). Restart DWSIM: the OP blocks appear in the palette under OP-BLOCKS, with icons."
            };
        }

        /// <summary>Delete exactly the files this installer deploys from the unitops folder.</summary>
        public Result Remove()
        {
            string dwsim = ResolveDwsimDir();
            if (dwsim == null)
                return new Result { Success = false, Message = "No DWSIM install found." };

            string unitops = Path.Combine(dwsim, "unitops");
            if (!Directory.Exists(unitops))
                return new Result { Success = true, Count = 0, Message = "Nothing to remove — DWSIM has no unitops folder." };

            int n = 0;
            foreach (string name in ManagedFileNames(unitops))
            {
                string p = Path.Combine(unitops, name);
                if (!File.Exists(p)) continue;
                try { File.Delete(p); n++; }
                catch (IOException)
                {
                    return new Result { Success = false, Count = n, Message = "A file is locked — close DWSIM, then try again." };
                }
                catch (Exception ex) { return new Result { Success = false, Count = n, Message = "Remove failed: " + ex.Message }; }
            }

            return new Result
            {
                Success = true,
                Count = n,
                Message = n > 0
                    ? "Native DWSIM blocks removed (" + n + " file(s)). Restart DWSIM to clear them from the palette."
                    : "Native DWSIM blocks were not installed."
            };
        }

        /// <summary>True if the adapter DLL is present in the DWSIM unitops folder.</summary>
        public bool IsInstalled()
        {
            string dwsim = ResolveDwsimDir();
            if (dwsim == null) return false;
            return File.Exists(Path.Combine(dwsim, "unitops", AdapterDll));
        }

        /// <summary>True if a native DWSIM adapter is available to deploy on this machine.</summary>
        public bool AdapterAvailable() => ResolveAdapterSource() != null;

        // ---- resolution helpers ----

        /// <summary>The files copied into unitops: the adapter, its OPBlocks.* deps, and CapeOpen.dll.</summary>
        private static List<string> PayloadFiles(string src)
        {
            var files = Directory.GetFiles(src, "OPBlocks*.dll", SearchOption.TopDirectoryOnly).ToList();
            string cape = ResolveCapeOpen(src);
            if (cape != null) files.Add(cape);
            return files;
        }

        /// <summary>Names this installer owns in unitops (so Remove never touches unrelated plug-ins).</summary>
        private static IEnumerable<string> ManagedFileNames(string unitops)
        {
            foreach (string f in Directory.GetFiles(unitops, "OPBlocks*.dll", SearchOption.TopDirectoryOnly))
                yield return Path.GetFileName(f);
            yield return "CapeOpen.dll";
        }

        /// <summary>
        /// Folder holding the built net48 adapter DLLs. Deployed: &lt;exeDir&gt;\dwsim.
        /// Dev fallback: the adapter project's build output (src\OPBlocks.DWSIM\bin\&lt;cfg&gt;).
        /// </summary>
        private static string ResolveAdapterSource()
        {
            string exeDir = AppContext.BaseDirectory;

            string local = Path.Combine(exeDir, "dwsim");
            if (HasAdapter(local)) return local;

            var dir = new DirectoryInfo(exeDir);
            for (int i = 0; i < 8 && dir != null; i++)
            {
                foreach (string cfg in new[] { "Release", "Debug" })
                {
                    string c = Path.Combine(dir.FullName, "src", "OPBlocks.DWSIM", "bin", cfg);
                    if (HasAdapter(c)) return c;
                }
                if (File.Exists(Path.Combine(dir.FullName, "OPBlocks.sln"))) break;
                dir = dir.Parent;
            }
            return null;
        }

        private static bool HasAdapter(string dir) =>
            Directory.Exists(dir) && File.Exists(Path.Combine(dir, AdapterDll));

        /// <summary>CapeOpen.dll — beside the adapter, else libs\, else any blocks\ family folder.</summary>
        private static string ResolveCapeOpen(string src)
        {
            string beside = Path.Combine(src, "CapeOpen.dll");
            if (File.Exists(beside)) return beside;

            string exeDir = AppContext.BaseDirectory;
            var dir = new DirectoryInfo(exeDir);
            for (int i = 0; i < 8 && dir != null; i++)
            {
                string libs = Path.Combine(dir.FullName, "libs", "CapeOpen", "CapeOpen.dll");
                if (File.Exists(libs)) return libs;
                string blocks = Path.Combine(dir.FullName, "blocks");
                if (Directory.Exists(blocks))
                {
                    string hit = Directory.GetFiles(blocks, "CapeOpen.dll", SearchOption.AllDirectories).FirstOrDefault();
                    if (hit != null) return hit;
                }
                if (File.Exists(Path.Combine(dir.FullName, "OPBlocks.sln"))) break;
                dir = dir.Parent;
            }
            return null;
        }

        private static string ResolveDwsimDir()
        {
            string[] candidates =
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DWSIM"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "DWSIM"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "DWSIM")
            };
            foreach (string dir in candidates)
                if (Directory.Exists(dir) &&
                    (File.Exists(Path.Combine(dir, "DWSIM.exe")) || File.Exists(Path.Combine(dir, "CapeOpen.dll"))))
                    return dir;
            return null;
        }
    }
}
