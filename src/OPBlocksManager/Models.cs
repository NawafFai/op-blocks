using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace OPBlocksManager
{
    /// <summary>A detected host simulator (Aspen Plus install or DWSIM).</summary>
    public sealed class SimulatorInfo
    {
        public string Kind { get; set; }       // "Aspen Plus" | "DWSIM"
        public string Name { get; set; }        // e.g. "Aspen Plus V14.0"
        public string Version { get; set; }     // e.g. "V14 (engine 40.0)"
        public string Bitness { get; set; }     // "x64" | "x86"
        public string Path { get; set; }        // install directory
        public bool Found { get; set; }
    }

    // ---- Block manifest (.opblocks.json): the single source of truth the Manager
    //      trusts for a block family. Ships next to the family DLL. ----

    public sealed class BlockManifest
    {
        public string Family { get; set; }
        public string Dll { get; set; }
        public List<BlockDef> Blocks { get; set; } = new List<BlockDef>();

        // Filled in after load — not part of the JSON on disk.
        [JsonIgnore] public string ManifestPath { get; set; }
        [JsonIgnore] public string ManifestDirectory { get; set; }
        [JsonIgnore] public string DllPath { get; set; }
    }

    public sealed class BlockDef
    {
        public string Code { get; set; }         // "OP-MIXER-DEMO"
        public string Name { get; set; }         // "Demo Mixer"
        public string Clsid { get; set; }        // GUID (no braces)
        public string Category { get; set; }     // "ONE PROCESS"
        public string Milestone { get; set; }
        public string Description { get; set; }
        public string CapeVersion { get; set; }
        public string VendorUrl { get; set; }
    }

    /// <summary>Result written by the elevated registration worker.</summary>
    public sealed class RegistrationResult
    {
        public bool Success { get; set; }
        public string Log { get; set; }
    }
}
