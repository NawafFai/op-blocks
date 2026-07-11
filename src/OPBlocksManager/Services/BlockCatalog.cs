using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace OPBlocksManager.Services
{
    /// <summary>
    /// Loads block manifests (.opblocks.json) from the block library folder. Each
    /// manifest describes one block-family DLL and the blocks it contains — this is
    /// the single source of truth the Manager uses; it never needs to load the
    /// (net48) block DLLs into its own (net8) process.
    /// </summary>
    public sealed class BlockCatalog
    {
        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        /// <summary>
        /// Resolves the block library folder. In a deployed install this is
        /// <c>&lt;exeDir&gt;\blocks</c>; during development it falls back to the
        /// repository's <c>blocks\</c> folder next to the solution.
        /// </summary>
        public static string ResolveBlocksDirectory()
        {
            string exeDir = AppContext.BaseDirectory;

            string local = Path.Combine(exeDir, "blocks");
            if (HasManifests(local)) return local;

            // Dev fallback: walk up looking for a solution-level blocks\ folder.
            var dir = new DirectoryInfo(exeDir);
            for (int i = 0; i < 8 && dir != null; i++)
            {
                string candidate = Path.Combine(dir.FullName, "blocks");
                if (HasManifests(candidate)) return candidate;
                if (File.Exists(Path.Combine(dir.FullName, "OPBlocks.sln"))) break;
                dir = dir.Parent;
            }
            return local; // may not exist yet; Load returns empty
        }

        private static bool HasManifests(string dir)
        {
            return Directory.Exists(dir) &&
                   Directory.GetFiles(dir, "*.opblocks.json", SearchOption.AllDirectories).Length > 0;
        }

        /// <summary>Folder holding the equipment icon SVGs (&lt;exeDir&gt;\icons, or repo icons\ in dev).</summary>
        public static string ResolveIconsDirectory()
        {
            string exeDir = AppContext.BaseDirectory;
            string local = Path.Combine(exeDir, "icons");
            if (Directory.Exists(local) && Directory.GetFiles(local, "*.svg").Length > 0) return local;
            var dir = new DirectoryInfo(exeDir);
            for (int i = 0; i < 8 && dir != null; i++)
            {
                string c = Path.Combine(dir.FullName, "icons");
                if (Directory.Exists(c)) return c;
                if (File.Exists(Path.Combine(dir.FullName, "OPBlocks.sln"))) break;
                dir = dir.Parent;
            }
            return local;
        }

        public List<BlockManifest> Load(string blocksDirectory)
        {
            var result = new List<BlockManifest>();
            if (!Directory.Exists(blocksDirectory)) return result;

            foreach (string path in Directory.GetFiles(blocksDirectory, "*.opblocks.json", SearchOption.AllDirectories))
            {
                try
                {
                    var manifest = JsonSerializer.Deserialize<BlockManifest>(File.ReadAllText(path), JsonOpts);
                    if (manifest == null) continue;
                    manifest.ManifestPath = path;
                    manifest.ManifestDirectory = Path.GetDirectoryName(path);
                    manifest.DllPath = ResolveDll(manifest);
                    result.Add(manifest);
                }
                catch
                {
                    // Skip malformed manifests rather than fail the whole catalog.
                }
            }
            return result;
        }

        private static string ResolveDll(BlockManifest m)
        {
            if (string.IsNullOrEmpty(m.Dll)) return null;
            string beside = Path.Combine(m.ManifestDirectory, m.Dll);
            return File.Exists(beside) ? beside : m.Dll;
        }
    }
}
