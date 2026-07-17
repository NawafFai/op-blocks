using System;

namespace OPBlocks.Core
{
    /// <summary>
    /// Pure ultrafiltration engine — arrays and doubles only; the single source of
    /// truth for OP-UF physics shared by the CAPE-OPEN block and the tests.
    ///
    /// What makes UF NOT nanofiltration/RO (the physics this block must respect):
    ///  • UF separates by SIZE EXCLUSION (pores ~2-100 nm; MWCO ~1-500 kDa). It has
    ///    NO osmotic barrier for dissolved salts: ions pass essentially freely and
    ///    the permeate TDS ≈ feed TDS. Only macromolecules/colloids are rejected.
    ///  • The flux law is pure Darcy (pressure-driven, no osmotic term):
    ///        Jw = Lp * TMP * FF        [L/m2/h]
    ///    with Lp the membrane permeability, TMP the trans-membrane pressure and
    ///    FF a fouling derating factor (gel/cake layer), the standard practical
    ///    form of the resistance-in-series model at fixed fouling state.
    ///
    /// The block classifies each component: dissolved SALTS pass with the water
    /// (passage 1); all other non-water species ("macro" solutes: proteins, humics,
    /// colloids, oil) are rejected with the configured rejection. The engine takes
    /// the per-component passage array so the classification travels with the block.
    ///
    /// Validity: TMP 0.1-5 bar, Lp 20-1000 L/m2/h/bar (polymeric UF), FF 0.2-1.
    ///
    /// References:
    ///  • M. Cheryan, "Ultrafiltration and Microfiltration Handbook," 2nd ed.
    ///    (1998), ch. 4 — Darcy flux, fouling resistances, MWCO.
    ///  • R. W. Baker, "Membrane Technology and Applications," 3rd ed. (2012),
    ///    ch. 6 — UF transport and size exclusion.
    ///  • J. C. Crittenden et al. (MWH), "Water Treatment: Principles and Design,"
    ///    3rd ed. (2012), ch. 12 — membrane filtration practice.
    /// </summary>
    public static class UfModel
    {
        public const double WaterMwKgMol = 0.0180153;   // kg/mol
        private const double AtmPa = 101325.0;

        public const double TypMaxTmpBar = 5.0;         // polymeric UF hydraulic limit
        public const double HighRecovery = 0.97;        // above → concentrate too small

        public struct Spec
        {
            public double AreaM2;
            public double PermLp;          // L/m2/h/bar
            public double TmpBar;
            public double FoulingFactor;   // 0..1
            public double MaxRecoveryPct;  // %
            public double PumpEffPct;      // %
        }

        public struct Split
        {
            public double Recovery;        // permeate water / feed water
            public double NaturalRecovery; // before the MaxRecovery cap
            public bool RecoveryCapped;
            public double FluxLMH;
            public double[] PermMol, ConcMol;
            public double TdsPermPpm, TdsConcPpm, TdsFeedPpm, MacroRemovalPct;
        }

        public struct Energy { public double PumpKW; public double SEC; }

        /// <summary>
        /// UF split. <paramref name="passage"/> = per-component fraction that passes
        /// to permeate at unit recovery (1 for dissolved salts, 1−rejection for
        /// macro solutes, ignored for water).
        /// </summary>
        public static Split Solve(Spec s, double[] f, int wi, double[] passage, double[] mwGmol,
                                  bool[] isMacro)
        {
            int n = f.Length;
            double feedWaterMol = wi >= 0 ? f[wi] : 0.0;
            double flux = Math.Max(0.0, s.PermLp * s.TmpBar * ProcessOps.Clamp(s.FoulingFactor, 0, 1));
            double permWaterMol = flux * s.AreaM2 / 3600.0 / WaterMwKgMol;
            double naturalRec = feedWaterMol > 0 ? Math.Min(permWaterMol / feedWaterMol, 0.999999) : 0.0;
            double maxRec = ProcessOps.Clamp(s.MaxRecoveryPct, 0, 100) / 100.0;
            double recovery = Math.Min(naturalRec, maxRec);

            var split = new Split
            {
                FluxLMH = flux,
                NaturalRecovery = naturalRec,
                Recovery = recovery,
                RecoveryCapped = naturalRec > maxRec + 1e-12,
                PermMol = new double[n],
                ConcMol = new double[n],
            };
            var frac = new double[n];
            for (int i = 0; i < n; i++)
                frac[i] = (i == wi) ? recovery : ProcessOps.Clamp(passage != null && i < passage.Length ? passage[i] : 1.0, 0, 1) * recovery;
            ProcessOps.SplitFlows(f, frac, split.PermMol, split.ConcMol);

            if (mwGmol != null)
            {
                double permKg = 0, concKg = 0, feedKg = 0, permSalt = 0, concSalt = 0, feedSalt = 0;
                double macroFeed = 0, macroPerm = 0;
                for (int i = 0; i < n; i++)
                {
                    double kg = mwGmol[i];
                    permKg += split.PermMol[i] * kg; concKg += split.ConcMol[i] * kg; feedKg += f[i] * kg;
                    if (i != wi)
                    {
                        permSalt += split.PermMol[i] * kg; concSalt += split.ConcMol[i] * kg; feedSalt += f[i] * kg;
                        if (isMacro != null && i < isMacro.Length && isMacro[i]) { macroFeed += f[i]; macroPerm += split.PermMol[i]; }
                    }
                }
                split.TdsPermPpm = permKg > 1e-30 ? permSalt / permKg * 1e6 : 0.0;
                split.TdsConcPpm = concKg > 1e-30 ? concSalt / concKg * 1e6 : 0.0;
                split.TdsFeedPpm = feedKg > 1e-30 ? feedSalt / feedKg * 1e6 : 0.0;
                // observed macro removal = 1 − (macro in permeate / macro in feed)
                split.MacroRemovalPct = macroFeed > 1e-30 ? (1.0 - macroPerm / macroFeed) * 100.0 : 0.0;
            }
            return split;
        }

        /// <summary>Feed pump raises the feed by TMP; SEC on permeate volume.</summary>
        public static Energy CalcEnergy(Spec s, double feedM3s, double permM3s)
        {
            double dpPa = Math.Max(0.0, s.TmpBar) * 1e5;
            double eta = s.PumpEffPct > 0 ? s.PumpEffPct / 100.0 : 0.0;
            double pumpKW = eta > 0 ? dpPa * feedM3s / eta / 1000.0 : 0.0;
            double permM3h = permM3s * 3600.0;
            return new Energy { PumpKW = pumpKW, SEC = permM3h > 1e-12 ? pumpKW / permM3h : 0.0 };
        }

        /// <summary>
        /// True when a component id looks like a DISSOLVED SALT / small ion, which
        /// UF passes freely (no osmotic barrier). Everything else non-water is
        /// treated as a macro solute subject to the size-exclusion rejection.
        /// </summary>
        public static bool IsDissolvedSalt(string compId)
        {
            if (string.IsNullOrEmpty(compId)) return false;
            string id = compId.ToUpperInvariant();
            string[] keys = { "NACL", "KCL", "MGCL", "CACL", "MGSO4", "CASO4", "NA2SO4", "K2SO4",
                              "NAHCO3", "CACO3", "MGCO3", "NA+", "K+", "MG+", "CA+", "CL-", "SO4",
                              "HCO3", "NO3", "LICL", "SODIUM", "CHLORIDE", "POTASSIUM" };
            foreach (string k in keys) if (id.IndexOf(k, StringComparison.Ordinal) >= 0) return true;
            return false;
        }
    }
}
