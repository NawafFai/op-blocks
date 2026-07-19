using System;
using System.Collections.Generic;
using System.IO;

namespace OPBlocksManager.Services
{
    /// <summary>
    /// The "Enable in Aspen" one-click feature. Aspen stores the enabled model
    /// libraries (incl. CAPE-OPEN) per case/template — there is no global flag — so
    /// instead of making the user tick Customize ▸ Manage Libraries every time, we
    /// install a ready "ONE PROCESS" user template that already has CAPE-OPEN enabled.
    /// The user then just picks File ▸ New ▸ User ▸ ONE PROCESS and the blocks are in
    /// the palette immediately.
    /// </summary>
    public sealed class AspenTemplateInstaller
    {
        public const string TemplateName = "ONE PROCESS.apt";

        public sealed class Result { public bool Success; public int Count; public string Message; }

        public Result Install()
        {
            string src = ResolveTemplate();
            if (src == null)
                return new Result { Success = false, Message = "ONE PROCESS.apt template not found next to the app." };

            var targets = AspenUserTemplateFolders(createIfMissing: true);
            int n = 0;
            foreach (string dir in targets)
            {
                try { File.Copy(src, Path.Combine(dir, TemplateName), true); n++; }
                catch (Exception ex) { return new Result { Success = false, Count = n, Message = "Copy failed: " + ex.Message }; }
            }
            return new Result
            {
                Success = n > 0,
                Count = n,
                Message = n > 0
                    ? $"Installed to {n} Aspen folder(s). Start saltwater cases from File ▸ New ▸ User ▸ ONE PROCESS — " +
                      "it opens with the CAPE-OPEN blocks already enabled AND a salt-capable method (ELECNRTL), so you skip " +
                      "Manage Libraries and avoid the pure-water steam tables that error on brine. (Aspen enables model " +
                      "libraries per-simulation, so this template is the one-click path.)"
                    : "No Aspen template folder found."
            };
        }

        /// <summary>True if the template is already installed in at least one Aspen folder.</summary>
        public bool IsInstalled()
        {
            foreach (string dir in AspenUserTemplateFolders(createIfMissing: false))
                if (File.Exists(Path.Combine(dir, TemplateName))) return true;
            return false;
        }

        private static List<string> AspenUserTemplateFolders(bool createIfMissing)
        {
            var list = new List<string>();
            string docs = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
            foreach (string root in new[] { "Aspentech", "AspenTech" })
            {
                string baseDir = Path.Combine(docs, root);
                if (Directory.Exists(baseDir))
                    foreach (string d in Directory.GetDirectories(baseDir, "Aspen Plus V*"))
                        if (!list.Contains(d)) list.Add(d);
            }
            if (list.Count == 0 && createIfMissing)
            {
                string d = Path.Combine(docs, "AspenTech", "Aspen Plus V14.0");
                Directory.CreateDirectory(d);
                list.Add(d);
            }
            return list;
        }

        private static string ResolveTemplate()
        {
            string exeDir = AppContext.BaseDirectory;
            string local = Path.Combine(exeDir, "templates", TemplateName);
            if (File.Exists(local)) return local;

            var dir = new DirectoryInfo(exeDir);
            for (int i = 0; i < 8 && dir != null; i++)
            {
                string c = Path.Combine(dir.FullName, "installer", "templates", TemplateName);
                if (File.Exists(c)) return c;
                if (File.Exists(Path.Combine(dir.FullName, "OPBlocks.sln"))) break;
                dir = dir.Parent;
            }
            return null;
        }
    }
}
