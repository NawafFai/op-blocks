using System;

namespace OPBlocks.Core
{
    /// <summary>
    /// The pure nanofiltration engine — no CAPE-OPEN, no host objects, just arrays
    /// and doubles. It is the SINGLE source of truth for OP-NF physics: the
    /// CAPE-OPEN block (<c>OPBlocks.Desalination.Nanofiltration</c>) and the
    /// validation tests both call it, so the two can never drift. Correctness is
    /// pinned independently by physical-range unit tests against published NF data.
    ///
    /// What makes NF NOT reverse osmosis (the physics the factory must survive):
    ///  • NF is a "leaky" membrane. Its rejection is SELECTIVE — multivalent ions
    ///    (Mg2+, Ca2+, SO4 2-) are rejected almost completely while monovalent ions
    ///    (Na+, Cl-) pass substantially. The block therefore carries TWO rejections
    ///    and passes a per-component salt-passage array to the engine.
    ///  • Because solute partially permeates, the membrane does not "feel" the full
    ///    osmotic pressure. The driving force uses a REFLECTION COEFFICIENT sigma
    ///    (Spiegler-Kedem irreversible-thermodynamics form), 0 &lt; sigma &lt;= 1:
    ///        Jw = A * (dP - sigma * dPi_avg).
    ///    sigma = 1 recovers the RO (solution-diffusion) limit; NF membranes run
    ///    sigma ~ 0.9-0.99.
    ///  • Osmotic driving force uses the AVERAGE of the feed and concentrate osmotic
    ///    pressures, dPi_avg = 1/2 (pi_feed + pi_conc), so pi rises with recovery and
    ///    the module self-limits (a single-point feed-pi model misses this).
    ///
    /// Rating mode solves the implicit recovery ↔ pi_avg coupling by a deterministic
    /// bisection (bit-identical every run). Design mode inverts the model: given a
    /// target recovery and a design flux it returns the required area and pressure.
    /// NF is a low-pressure process, so no energy-recovery device is modelled; the
    /// feed pump raises the whole feed from atmospheric intake.
    ///
    /// References:
    ///  • O. Kedem, A. Katchalsky, "Thermodynamic analysis of the permeability of
    ///    biological membranes to non-electrolytes," Biochim. Biophys. Acta 27
    ///    (1958) 229-246 — the Kedem-Katchalsky flux equations.
    ///  • K. S. Spiegler, O. Kedem, "Thermodynamics of hyperfiltration (reverse
    ///    osmosis): criteria for efficient membranes," Desalination 1 (1966)
    ///    311-326 — the reflection coefficient sigma.
    ///  • A. W. Mohammad et al., "Nanofiltration membranes review: recent advances
    ///    and future prospects," Desalination 356 (2015) 226-254.
    ///  • R. W. Baker, Membrane Technology and Applications, 3rd ed. (2012), ch. 5.
    /// </summary>
    public static class NfModel
    {
        public enum Mode { Rating, Design }

        public const double WaterMwKgMol = 0.0180153;   // kg/mol
        private const double AtmPa = 101325.0;

        // engineering-advisory thresholds (documented; warnings only, never block)
        public const double MembraneMaxBar = 41.0;       // typical NF element hydraulic limit
        public const double LowConcRecovery = 0.90;      // recovery above → very low reject flow

        public struct Spec
        {
            public Mode CalcMode;
            public double AreaM2;
            public double WaterPermA;        // L/m2/h/bar
            public double ReflectionSigma;   // - (0..1)
            public double AppliedBar;        // bar (Rating input)
            public double VantHoffI;         // -
            public double PumpEffPct;        // %
            public double MaxRecoveryPct;    // %
            public double TargetRecoveryPct; // % (Design)
            public double DesignFluxLMH;     // L/m2/h (Design)
        }

        public struct Split
        {
            public double Recovery;          // fraction of feed water to permeate
            public double NaturalRecovery;   // before the MaxRecovery cap
            public bool RecoveryCapped;
            public double FluxLMH;           // achieved permeate water flux
            public double NdpBar;            // net driving pressure (applied − sigma·pi_avg)
            public double PiFeedBar, PiConcBar, PiAvgBar;
            public double AppliedBarUsed;    // Rating: input; Design: required
            public double RequiredAreaM2;    // Design output (= AreaM2 in Rating)
            public double RequiredPressureBar; // Design output (= AppliedBar in Rating)
            public double[] PermMol, ConcMol;  // mol/s (host scale), per component
            public double TdsPermPpm, TdsConcPpm, TdsFeedPpm, SaltRejObsPct;
            public int Iterations;
        }

        public struct Energy
        {
            public double PumpKW;
            public double SEC;               // kWh/m3 (permeate basis)
        }

        /// <summary>
        /// Solves the NF membrane balance. <paramref name="saltPass"/> is the
        /// fraction of each component that passes to permeate at unit recovery
        /// (0 for water — water is handled by the recovery itself; the caller sets
        /// each solute's passage from its monovalent/multivalent rejection).
        /// <paramref name="piFeedBar"/> is the feed osmotic pressure the caller
        /// obtained (package water activity if it has one, else van 't Hoff); the
        /// concentrate osmotic pressure is evaluated internally on the concentrate
        /// composition by the same routine, so any feed is handled.
        /// </summary>
        public static Split Solve(Spec s, double[] f, int wi, double[] saltPass, double[] mwGmol,
                                  double tK, double piFeedBar)
        {
            int n = f.Length;
            double feedWaterMol = wi >= 0 ? f[wi] : 0.0;
            double sigma = Clamp(s.ReflectionSigma, 0.0, 1.0);
            double maxRec = Clamp(s.MaxRecoveryPct, 0, 100) / 100.0;

            double recovery, naturalRec;
            int iters = 0;
            if (s.CalcMode == Mode.Design)
            {
                double target = Clamp(s.TargetRecoveryPct, 0, 100) / 100.0;
                naturalRec = target;
                recovery = Math.Min(target, maxRec);
            }
            else
            {
                naturalRec = SolveRatingRecovery(s, f, wi, saltPass, sigma, tK, piFeedBar, feedWaterMol, out iters);
                recovery = Math.Min(naturalRec, maxRec);
            }

            var split = new Split
            {
                PiFeedBar = piFeedBar,
                Iterations = iters,
                PermMol = new double[n],
                ConcMol = new double[n],
            };
            var frac = new double[n];
            for (int i = 0; i < n; i++) frac[i] = (i == wi) ? recovery : PassOf(saltPass, i) * recovery;
            ProcessOps.SplitFlows(f, frac, split.PermMol, split.ConcMol);

            double piConcFinal = ProcessOps.OsmoticPressureBar(null, split.ConcMol, wi, s.VantHoffI, tK, null);
            double piAvgFinal = 0.5 * (piFeedBar + piConcFinal);
            split.PiConcBar = piConcFinal;
            split.PiAvgBar = piAvgFinal;

            double permWaterMolFinal = wi >= 0 ? split.PermMol[wi] : 0.0;
            split.NaturalRecovery = naturalRec;
            split.Recovery = recovery;
            split.RecoveryCapped = naturalRec > maxRec + 1e-12;

            if (s.CalcMode == Mode.Design)
            {
                double jw = Math.Max(0.0, s.DesignFluxLMH);
                split.FluxLMH = jw;
                split.NdpBar = s.WaterPermA > 0 ? jw / s.WaterPermA : 0.0;
                split.RequiredPressureBar = sigma * piAvgFinal + split.NdpBar;
                split.RequiredAreaM2 = jw > 0 ? permWaterMolFinal * WaterMwKgMol * 3600.0 / jw : 0.0;
                split.AppliedBarUsed = split.RequiredPressureBar;
            }
            else
            {
                split.FluxLMH = s.AreaM2 > 0 ? permWaterMolFinal * WaterMwKgMol * 3600.0 / s.AreaM2 : 0.0;
                split.NdpBar = Math.Max(0.0, s.AppliedBar - sigma * piAvgFinal);
                split.RequiredAreaM2 = s.AreaM2;
                split.RequiredPressureBar = s.AppliedBar;
                split.AppliedBarUsed = s.AppliedBar;
            }

            if (mwGmol != null)
            {
                double permKg = 0, concKg = 0, feedKg = 0, permSalt = 0, concSalt = 0, feedSalt = 0;
                for (int i = 0; i < n; i++)
                {
                    double kg = mwGmol[i];
                    permKg += split.PermMol[i] * kg; concKg += split.ConcMol[i] * kg; feedKg += f[i] * kg;
                    if (i != wi)
                    {
                        permSalt += split.PermMol[i] * kg; concSalt += split.ConcMol[i] * kg; feedSalt += f[i] * kg;
                    }
                }
                split.TdsPermPpm = permKg > 1e-30 ? permSalt / permKg * 1e6 : 0.0;
                split.TdsConcPpm = concKg > 1e-30 ? concSalt / concKg * 1e6 : 0.0;
                split.TdsFeedPpm = feedKg > 1e-30 ? feedSalt / feedKg * 1e6 : 0.0;
                split.SaltRejObsPct = split.TdsFeedPpm > 1e-12 ? (1.0 - split.TdsPermPpm / split.TdsFeedPpm) * 100.0 : 0.0;
            }
            return split;
        }

        private static double PassOf(double[] saltPass, int i)
        {
            if (saltPass == null || i < 0 || i >= saltPass.Length) return 0.0;
            return Clamp(saltPass[i], 0.0, 1.0);
        }

        /// <summary>The recovery the membrane flux would produce at trial recovery r.</summary>
        private static double FluxRecovery(double r, Spec s, double[] f, int wi, double[] saltPass,
                                           double sigma, double tK, double piFeedBar, double feedWaterMol)
        {
            if (feedWaterMol <= 0) return 0.0;
            int n = f.Length;
            var perm = new double[n];
            var conc = new double[n];
            var frac = new double[n];
            for (int i = 0; i < n; i++) frac[i] = (i == wi) ? r : PassOf(saltPass, i) * r;
            ProcessOps.SplitFlows(f, frac, perm, conc);
            double piConc = ProcessOps.OsmoticPressureBar(null, conc, wi, s.VantHoffI, tK, null);
            double ndp = Math.Max(0.0, s.AppliedBar - sigma * 0.5 * (piFeedBar + piConc));
            double jw = s.WaterPermA * ndp;                                  // L/m2/h
            double permWaterMol = jw * s.AreaM2 / 3600.0 / WaterMwKgMol;      // mol/s
            return permWaterMol / feedWaterMol;
        }

        /// <summary>
        /// Natural (uncapped) rating recovery by bisection of g(r) = r − h(r), which
        /// is strictly increasing (h decreases as pi_avg rises with r) → unique root
        /// on [0, 1). ~60 halvings reach machine precision, deterministically.
        /// </summary>
        private static double SolveRatingRecovery(Spec s, double[] f, int wi, double[] saltPass, double sigma,
                                                  double tK, double piFeedBar, double feedWaterMol, out int iters)
        {
            iters = 0;
            double lo = 0.0, hi = 0.999999;
            double gLo = lo - FluxRecovery(lo, s, f, wi, saltPass, sigma, tK, piFeedBar, feedWaterMol);
            if (gLo >= 0.0) return 0.0;                      // applied ≤ sigma·feed osmotic → no permeation
            double gHi = hi - FluxRecovery(hi, s, f, wi, saltPass, sigma, tK, piFeedBar, feedWaterMol);
            if (gHi <= 0.0) return hi;                       // membrane could pass essentially all water
            for (iters = 0; iters < 100; iters++)
            {
                double mid = 0.5 * (lo + hi);
                double gMid = mid - FluxRecovery(mid, s, f, wi, saltPass, sigma, tK, piFeedBar, feedWaterMol);
                if (gMid < 0.0) lo = mid; else hi = mid;
                if (hi - lo < 1e-15) break;
            }
            return 0.5 * (lo + hi);
        }

        /// <summary>
        /// Feed-pump energy. Volumetric flows [m3/s] come from the caller (the block
        /// reads them from the property package: package mass ÷ package density,
        /// which cancels any host mass-unit convention). NF is low-pressure with no
        /// energy-recovery device.
        /// </summary>
        public static Energy CalcEnergy(Spec s, double appliedBarUsed, double feedM3s, double permM3s)
        {
            double dpPa = Math.Max(0.0, appliedBarUsed * 1e5 - AtmPa);
            double eta = s.PumpEffPct > 0 ? s.PumpEffPct / 100.0 : 0.0;
            double pumpKW = eta > 0 ? dpPa * feedM3s / eta / 1000.0 : 0.0;
            double permM3h = permM3s * 3600.0;
            return new Energy { PumpKW = pumpKW, SEC = permM3h > 1e-12 ? pumpKW / permM3h : 0.0 };
        }

        /// <summary>Calculation mode from the integer-coded parameter (0 = Rating, 1 = Design).</summary>
        public static Mode ModeFromCode(double code) { return code >= 0.5 ? Mode.Design : Mode.Rating; }

        /// <summary>
        /// True when a component id looks like a multivalent ion / salt (Mg2+, Ca2+,
        /// SO4 2-, CO3 2-, and their common salts), which NF rejects far more strongly
        /// than monovalent Na+/Cl-/K+. Used by the block to pick each solute's
        /// rejection; kept here so the classification travels with the physics.
        /// </summary>
        public static bool IsMultivalent(string compId)
        {
            if (string.IsNullOrEmpty(compId)) return false;
            string id = compId.ToUpperInvariant();
            string[] keys = { "MG", "CA", "SO4", "SO42", "CO3", "CACO3", "CASO4", "MGSO4", "MGCL2",
                              "CACL2", "AL", "FE", "SULFATE", "SULPHATE", "CARBONATE", "PO4", "PHOSPHATE" };
            foreach (string k in keys) if (id.IndexOf(k, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        private static double Clamp(double x, double lo, double hi) { return x < lo ? lo : (x > hi ? hi : x); }
    }
}
