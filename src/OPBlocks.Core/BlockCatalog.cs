using System;
using System.Collections.Generic;

namespace OPBlocks.Core
{
    /// <summary>
    /// The SINGLE source of block identity shown to users (P2, v1.1.3): full
    /// human name, family, and a one-line "typical use" hint per block code.
    /// Consumed by the Aspen editor header, the DWSIM adapter (palette name,
    /// flowsheet banner, editor header) and the registration metadata writers,
    /// so a block introduces itself identically everywhere. Names match the
    /// packaged .opblocks.json manifests — keep them in lock-step.
    /// </summary>
    public static class BlockCatalog
    {
        public sealed class Info
        {
            public readonly string FullName;
            public readonly string Family;
            public readonly string TypicalUse;
            internal Info(string fullName, string family, string typicalUse)
            { FullName = fullName; Family = family; TypicalUse = typicalUse; }
        }

        private static readonly Dictionary<string, Info> Map =
            new Dictionary<string, Info>(StringComparer.OrdinalIgnoreCase)
        {
            // ---- Desalination — thermal & evaporation ----
            { "OP-EVAPPOND", new Info("Solar Evaporation Pond", "Desalination",
                "Brine disposal / concentration by climate-driven evaporation.") },
            { "OP-MD", new Info("Membrane Distillation (DCMD)", "Desalination",
                "Hot brine (50-80 C) to pure distillate across a membrane.") },
            { "OP-MED", new Info("Multi-Effect Distillation", "Desalination",
                "Thermal seawater desalination; GOR ~ 0.85 x effects.") },
            { "OP-MSF", new Info("Multi-Stage Flash", "Desalination",
                "Once-through flash desalination; PR ~ 10 at 24 stages.") },
            { "OP-MVC", new Info("Mechanical Vapour Compression", "Desalination",
                "Electrically-driven evaporation; SEC 8-16 kWh/m3.") },
            // ---- Desalination — membranes ----
            { "OP-RO", new Info("Reverse Osmosis", "Desalination",
                "Seawater/brackish desalination at 40-70 bar; use ELECNRTL for brines.") },
            { "OP-NF", new Info("Nanofiltration", "Desalination",
                "Softening / multivalent-ion removal at 5-15 bar.") },
            { "OP-UF", new Info("Ultrafiltration", "Desalination",
                "Colloid & macromolecule removal at 0.5-3 bar; salts pass.") },
            { "OP-FO", new Info("Forward Osmosis", "Desalination",
                "Osmotically-driven water transfer into a strong draw solution.") },
            { "OP-PRO", new Info("Pressure-Retarded Osmosis", "Desalination",
                "Salinity-gradient power; peak power at dP = dPi/2.") },
            // ---- Electrochemical ----
            { "OP-ED", new Info("Electrodialysis", "Electrochemical",
                "Brackish desalination by Faradaic ion transport (set V, R, cell pairs).") },
            { "OP-EDI", new Info("Electrodeionization", "Electrochemical",
                "Ultrapure-water polishing after RO (feeds below ~500 ppm).") },
            { "OP-CDI", new Info("Capacitive Deionization", "Electrochemical",
                "Brackish polishing on capacitive electrodes (SAC-limited).") },
            { "OP-CHLORALK", new Info("Chlor-Alkali Membrane Cell", "Electrochemical",
                "Cl2 / H2 / NaOH from (near-)saturated brine electrolysis.") },
            { "OP-IX", new Info("Ion Exchange Column", "Electrochemical",
                "Ca/Mg softening on cation resin; Na passes.") },
            // ---- Lithium & sorption ----
            { "OP-DLE", new Info("Direct Lithium Extraction", "Lithium & Sorption",
                "Li capture from brine on a selective sorbent (Langmuir + LDF).") },
            { "OP-SX", new Info("Solvent Extraction (Mixer-Settler)", "Lithium & Sorption",
                "Counter-current metal extraction (Kremser cascade).") },
            { "OP-GAC", new Info("Activated Carbon Adsorption", "Lithium & Sorption",
                "Trace-organics removal; Freundlich capacity and bed life.") },
            { "OP-CRYST", new Info("Crystallizer", "Lithium & Sorption",
                "Salt crystallization from (near-)saturated liquor (solubility-limited).") },
            { "OP-PPT", new Info("Chemical Precipitation Reactor", "Lithium & Sorption",
                "Reagent softening / metals removal; sludge leaves wet.") },
            // ---- Energy & gas ----
            { "OP-PEM", new Info("PEM Electrolyzer", "Energy & Gas",
                "H2 from water electrolysis; SEC = 26.59 x V / eff kWh/kg.") },
            { "OP-AEL", new Info("Alkaline Electrolyzer", "Energy & Gas",
                "H2 from alkaline water electrolysis (industrial ranges).") },
            { "OP-FC", new Info("PEM Fuel Cell", "Energy & Gas",
                "Power from H2/air; efficiency = V / 1.253 (LHV).") },
            { "OP-RPB", new Info("Rotating Packed Bed (HiGee)", "Energy & Gas",
                "Intensified CO2 absorption; NTU = k x sqrt(RPM).") },
            { "OP-UVAOP", new Info("UV / Advanced Oxidation", "Energy & Gas",
                "Trace-contaminant destruction by UV dose (Bolton EEO).") },
            // ---- Demo ----
            { "OP-MIXER-DEMO", new Info("Demo Mixer", "Demo",
                "Adiabatic stream mixer (framework demo block).") },
        };

        /// <summary>Identity for a block code; never null (unknown codes get a stub).</summary>
        public static Info For(string blockCode)
        {
            Info i;
            if (blockCode != null && Map.TryGetValue(blockCode, out i)) return i;
            return new Info(blockCode ?? "OP block", "ONE PROCESS", null);
        }

        /// <summary>"OP-RO — Reverse Osmosis" — the display title used everywhere.</summary>
        public static string DisplayTitle(string blockCode)
        {
            Info i = For(blockCode);
            return i.FullName == blockCode ? blockCode : blockCode + " — " + i.FullName;
        }

        /// <summary>Every catalogued code (for coverage tests).</summary>
        public static IEnumerable<string> Codes { get { return Map.Keys; } }
    }
}
