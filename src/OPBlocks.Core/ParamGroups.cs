using System;
using System.Collections.Generic;

namespace OPBlocks.Core
{
    /// <summary>
    /// Logical section headers for the block editor (P3, v1.1.3). Each entry
    /// names the section that STARTS at a given parameter, in declaration order
    /// (sections are contiguous runs of the block's parameter list). Blocks with
    /// few parameters are deliberately absent — a lone header is noise. Validated
    /// by unit test against the real blocks so a renamed parameter cannot leave a
    /// dangling header.
    /// </summary>
    public static class ParamGroups
    {
        // "SectionTitle|FirstParameterName" in parameter-declaration order.
        private static readonly Dictionary<string, string[]> Inputs =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "OP-RO", new[] { "Calculation mode|CalcMode", "Membrane|Area",
                "Operating conditions|AppliedPressure", "Recovery & design targets|MaxRecovery",
                "Energy recovery (ERD)|ERDType" } },
            { "OP-NF", new[] { "Calculation mode|CalcMode", "Membrane|Area",
                "Operating conditions|AppliedPressure", "Recovery & design targets|MaxRecovery" } },
            { "OP-UF", new[] { "Membrane|Area", "Operation|TMP",
                "Separation & fouling|Rejection", "Recovery & pump|MaxRecovery" } },
            { "OP-FO", new[] { "Membrane|Area", "Solution & limits|VantHoffI" } },
            { "OP-PRO", new[] { "Membrane|Area", "Operating conditions|AppliedPressure",
                "Power & limits|TurbineEff" } },
            { "OP-EVAPPOND", new[] { "Pond geometry|Area", "Climate|Irradiance",
                "Brine & model|WaterActivity" } },
            { "OP-MD", new[] { "Membrane|Area", "Operation & model|HotActivity" } },
            { "OP-MED", new[] { "Configuration|NEffects", "Temperatures|TopBrineTemp",
                "Model constants|GorPerEffect" } },
            { "OP-MSF", new[] { "Configuration|NStages", "Temperatures|TopBrineTemp",
                "Brine properties|SpecificHeat" } },
            { "OP-ED", new[] { "Calculation mode|CalcMode", "Stack|CellPairs",
                "Ion transport|CurrentEfficiency", "Design target|TargetRemoval" } },
            { "OP-EDI", new[] { "Stack|CellPairs", "Transport & target|TargetRemoval" } },
            { "OP-CDI", new[] { "Electrodes|SAC", "Operation|CycleTime" } },
            { "OP-DLE", new[] { "Sorbent isotherm|QmaxLi", "Bed & cycle|BedVolume",
                "Selectivity|MgLiSelectivity" } },
            { "OP-GAC", new[] { "Freundlich isotherm|FreundlichK", "Bed & contact|BedMass" } },
            { "OP-PEM", new[] { "Stack|CellArea", "Electrical performance|CellVoltage" } },
            { "OP-AEL", new[] { "Stack|CellArea", "Electrical performance|CellVoltage" } },
            { "OP-UVAOP", new[] { "Dose & kinetics|UVDose", "Water & lamp|UVT" } },
        };

        private static readonly Dictionary<string, string[]> Results =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "OP-RO", new[] { "Performance|Recovery", "Energy|PumpPower",
                "Flux, osmotics & TDS|PermeateFlux", "Design results|RequiredArea" } },
            { "OP-NF", new[] { "Performance|Recovery", "Energy|PumpPower" } },
        };

        /// <summary>
        /// The section title that starts at this parameter, or null. Set
        /// <paramref name="resultsTab"/> for the Results tab's own sections.
        /// </summary>
        public static string SectionStartingAt(string blockCode, string paramName, bool resultsTab)
        {
            string[] defs;
            var map = resultsTab ? Results : Inputs;
            if (blockCode == null || paramName == null || !map.TryGetValue(blockCode, out defs))
                return null;
            foreach (string def in defs)
            {
                int cut = def.IndexOf('|');
                if (cut > 0 && string.Equals(def.Substring(cut + 1), paramName, StringComparison.OrdinalIgnoreCase))
                    return def.Substring(0, cut);
            }
            return null;
        }

        /// <summary>All mapped (blockCode, paramName, resultsTab) triples — for validation tests.</summary>
        public static IEnumerable<Tuple<string, string, bool>> AllMappings()
        {
            foreach (var kv in Inputs)
                foreach (string def in kv.Value)
                    yield return Tuple.Create(kv.Key, def.Substring(def.IndexOf('|') + 1), false);
            foreach (var kv in Results)
                foreach (string def in kv.Value)
                    yield return Tuple.Create(kv.Key, def.Substring(def.IndexOf('|') + 1), true);
        }
    }
}
