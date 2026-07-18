using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace OPBlocksManager.Services
{
    /// <summary>
    /// Non-elevated half of Install/Remove: relaunches the Manager elevated (UAC)
    /// to do the actual COM registration, waits, and returns the worker's result.
    /// Keeping everything inside the one exe means no PowerShell / external script
    /// dependency in the shipped product.
    /// </summary>
    public sealed class Registrar
    {
        public sealed class Outcome
        {
            public bool Success;
            public bool Cancelled;   // user declined the UAC prompt
            public string Log;
        }

        public Outcome Install(BlockManifest manifest) => Run(
            $"{RegistrationWorker.CmdRegister} {RegistrationWorker.ArgManifest} \"{manifest.ManifestPath}\"");
        public Outcome Remove(BlockManifest manifest) => Run(
            $"{RegistrationWorker.CmdUnregister} {RegistrationWorker.ArgManifest} \"{manifest.ManifestPath}\"");

        /// <summary>Register EVERY family in the block library in ONE elevated step (one UAC prompt).</summary>
        public Outcome InstallAll(string blocksDirectory) => Run(
            $"{RegistrationWorker.CmdRegisterAll} {RegistrationWorker.ArgBlocksDir} \"{blocksDirectory}\"");
        public Outcome RemoveAll(string blocksDirectory) => Run(
            $"{RegistrationWorker.CmdUnregisterAll} {RegistrationWorker.ArgBlocksDir} \"{blocksDirectory}\"");

        private Outcome Run(string commandArgs)
        {
            string resultFile = Path.Combine(Path.GetTempPath(), "opblocks-reg-" + Guid.NewGuid().ToString("N") + ".json");
            string exe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule.FileName;

            var psi = new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = true,
                Verb = "runas", // triggers the UAC elevation prompt
                Arguments = $"{commandArgs} {RegistrationWorker.ArgResult} \"{resultFile}\""
            };

            try
            {
                using var p = Process.Start(psi);
                p.WaitForExit();
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // ERROR_CANCELLED (1223) — user declined elevation.
                return new Outcome { Success = false, Cancelled = true, Log = "Elevation was cancelled." };
            }

            try
            {
                if (File.Exists(resultFile))
                {
                    var result = JsonSerializer.Deserialize<RegistrationResult>(
                        File.ReadAllText(resultFile),
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    File.Delete(resultFile);
                    return new Outcome { Success = result.Success, Log = result.Log };
                }
            }
            catch { /* fall through */ }

            return new Outcome { Success = false, Log = "No result returned from the elevated step." };
        }
    }
}
