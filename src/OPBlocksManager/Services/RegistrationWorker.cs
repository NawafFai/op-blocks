using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace OPBlocksManager.Services
{
    /// <summary>
    /// The elevated half of Install/Remove. Runs in a second, UAC-elevated instance
    /// of the Manager (command line <c>--register|--unregister --manifest &lt;path&gt;
    /// --result &lt;path&gt;</c>). It drives the .NET Framework RegAsm for BOTH the x64
    /// and x86 hives — a missing bitness is the #1 "block not in palette" cause
    /// (spec §3) — then corrects the CapeDescription metadata the library shifts, and
    /// writes a JSON result the non-elevated UI reads.
    /// </summary>
    public static class RegistrationWorker
    {
        private const string RegAsm64 = @"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe";
        private const string RegAsm32 = @"C:\Windows\Microsoft.NET\Framework\v4.0.30319\RegAsm.exe";

        public const string CmdRegister = "--register";
        public const string CmdUnregister = "--unregister";
        public const string ArgManifest = "--manifest";
        public const string ArgResult = "--result";

        public static bool IsWorkerInvocation(string[] args)
        {
            return args != null && (args.Contains(CmdRegister) || args.Contains(CmdUnregister));
        }

        /// <summary>Entry point for the elevated instance. Returns a process exit code.</summary>
        public static int Run(string[] args)
        {
            bool unregister = args.Contains(CmdUnregister);
            string manifestPath = ArgValue(args, ArgManifest);
            string resultPath = ArgValue(args, ArgResult);
            var log = new StringBuilder();
            bool success = false;

            try
            {
                if (string.IsNullOrEmpty(manifestPath) || !File.Exists(manifestPath))
                    throw new FileNotFoundException("Manifest not found: " + manifestPath);

                var manifest = JsonSerializer.Deserialize<BlockManifest>(
                    File.ReadAllText(manifestPath),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                string dll = Path.Combine(Path.GetDirectoryName(manifestPath), manifest.Dll);
                if (!File.Exists(dll)) throw new FileNotFoundException("Block DLL not found: " + dll);

                foreach (var (regasm, bit) in new[] { (RegAsm64, "x64"), (RegAsm32, "x86") })
                {
                    if (!File.Exists(regasm)) { log.AppendLine($"[{bit}] RegAsm not found: {regasm}"); continue; }
                    string args2 = unregister ? $"\"{dll}\" /unregister" : $"\"{dll}\" /codebase";
                    int code = RunProcess(regasm, args2, log, bit);
                    if (code != 0) log.AppendLine($"[{bit}] RegAsm exit code {code}");
                }

                if (!unregister)
                    FixMetadata(manifest, log);

                success = true;
                log.AppendLine(unregister ? "Unregistered." : "Registered in x64 + x86 hives.");
            }
            catch (Exception ex)
            {
                log.AppendLine("ERROR: " + ex.Message);
                success = false;
            }

            WriteResult(resultPath, new RegistrationResult { Success = success, Log = log.ToString() });
            return success ? 0 : 1;
        }

        private static int RunProcess(string exe, string arguments, StringBuilder log, string bit)
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            string stdout = p.StandardOutput.ReadToEnd();
            string stderr = p.StandardError.ReadToEnd();
            p.WaitForExit();
            foreach (string line in (stdout + stderr).Split('\n'))
            {
                string t = line.Trim();
                if (t.Length > 0 && !t.StartsWith("Microsoft") && !t.StartsWith("Copyright") && !t.StartsWith("for Microsoft"))
                    log.AppendLine($"[{bit}] {t}");
            }
            return p.ExitCode;
        }

        /// <summary>
        /// Writes the correct CapeDescription values (the library puts VendorURL into
        /// the CapeVersion slot and leaves VendorURL empty) into both hives.
        /// </summary>
        private static void FixMetadata(BlockManifest manifest, StringBuilder log)
        {
            foreach (BlockDef b in manifest.Blocks)
            {
                string g = RegistryScanner.NormalizeClsid(b.Clsid);
                foreach (string clsidBase in new[]
                         {
                             @"SOFTWARE\Classes\CLSID\",
                             @"SOFTWARE\WOW6432Node\Classes\CLSID\"
                         })
                {
                    string cdPath = clsidBase + g + @"\CapeDescription";
                    using RegistryKey cd = Registry.LocalMachine.OpenSubKey(cdPath, writable: true);
                    if (cd == null) continue;
                    if (!string.IsNullOrEmpty(b.CapeVersion)) cd.SetValue("CapeVersion", b.CapeVersion, RegistryValueKind.String);
                    if (b.VendorUrl != null) cd.SetValue("VendorURL", b.VendorUrl, RegistryValueKind.String);
                    log.AppendLine($"metadata fixed for {b.Code}");
                }
            }
        }

        private static void WriteResult(string path, RegistrationResult result)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                File.WriteAllText(path, JsonSerializer.Serialize(result,
                    new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { /* best effort */ }
        }

        private static string ArgValue(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            return null;
        }
    }
}
