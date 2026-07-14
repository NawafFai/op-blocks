using System;

namespace OPBlocks.Core
{
    /// <summary>
    /// The pure reverse-osmosis engine — no CAPE-OPEN, no host objects, just
    /// arrays and doubles. It is the SINGLE source of truth for OP-RO physics:
    /// the CAPE-OPEN block (<c>OPBlocks.Desalination.ReverseOsmosis</c>) and the
    /// validation reference (<c>tests/OproValidation</c>) both call it, so the
    /// two can never drift. Correctness is pinned independently by physical-range
    /// unit tests and by the live Aspen cross-check (results == streams).
    ///
    /// Model (documented for the block's "Model &amp; References" report):
    ///  • Solution–diffusion water flux  Jw = A·(ΔP − Δπ)  [L·m⁻²·h⁻¹],
    ///    A = water permeability, ΔP = applied − permeate (≈ atmospheric) pressure.
    ///  • Osmotic driving force uses the AVERAGE of the feed and concentrate
    ///    osmotic pressures, Δπ = ½(π_feed + π_conc). π rises as the module
    ///    concentrates, so π_avg &gt; π_feed and recovery self-limits — the
    ///    behaviour a single-point feed-π model misses. π from van 't Hoff (or the
    ///    package water activity when it supplies one), via <see cref="ProcessOps"/>.
    ///  • Rating mode solves the implicit recovery ↔ π_avg coupling by a damped
    ///    fixed-point iteration (deterministic → bit-identical every run).
    ///  • Design mode inverts the model: given a target recovery and a design
    ///    flux it returns the required membrane area and applied pressure.
    ///  • Energy: the HP pump raises the WHOLE feed from atmospheric intake to the
    ///    applied pressure; an optional energy-recovery device (pressure exchanger
    ///    or turbine) returns the high-pressure brine's hydraulic power.
    ///
    /// References: Baker, Membrane Technology and Applications, 3rd ed. (2012),
    /// ch. 5; Fritzmann et al., Desalination 216 (2007) 1–76 (SWRO energy, ERD);
    /// Voutchkov, Desalination Engineering (2013), ch. 8 (ERD, SEC ranges).
    /// </summary>
    public static class RoModel
    {
        public enum Mode { Rating, Design }
        public enum Erd { None, PressureExchanger, Turbine }

        public const double WaterMwKgMol = 0.0180153;   // kg/mol
        public const double WaterRhoKgM3 = 1000.0;      // design-basis water density
        private const double AtmPa = 101325.0;

        // engineering-advisory thresholds (documented; warnings only, never block)
        public const double MembraneMaxBar = 82.0;      // ~1200 psi SWRO element limit
        public const double LowBrineRecovery = 0.85;    // recovery above → brine flow low (scaling risk)

        public struct Spec
        {
            public Mode CalcMode;
            public double AreaM2;
            public double WaterPermA;      // L/m2/h/bar
            public double SaltRejPct;      // %
            public double AppliedBar;      // bar (Rating input)
            public double VantHoffI;       // -
            public double PumpEffPct;      // %
            public double MaxRecoveryPct;  // %
            public double TargetRecoveryPct; // % (Design)
            public double DesignFluxLMH;   // L/m2/h (Design)
            public Erd ErdType;
            public double ErdEffPct;       // %
        }

        public struct Split
        {
            public double Recovery;        // fraction of feed water to permeate
            public double NaturalRecovery; // before the MaxRecovery cap
            public bool RecoveryCapped;
            public double FluxLMH;         // achieved permeate water flux
            public double NdpBar;          // net driving pressure (applied − π_avg)
            public double PiFeedBar, PiConcBar, PiAvgBar;
            public double AppliedBarUsed;      // Rating: input; Design: required
            public double RequiredAreaM2;      // Design output (= AreaM2 in Rating)
            public double RequiredPressureBar; // Design output (= AppliedBar in Rating)
            public double[] PermMol, ConcMol;  // mol/s (host scale), per component
            public double TdsPermPpm, TdsConcPpm, TdsFeedPpm, SaltRejObsPct;
            public int Iterations;
        }

        public struct Energy
        {
            public double GrossPumpKW;
            public double ErdRecoveredKW;
            public double NetPumpKW;
            public double SecGross;        // kWh/m3
            public double SecNet;          // kWh/m3
            public double EnergySavingPct;
        }

        /// <summary>
        /// Solves the membrane balance. <paramref name="piFeedBar"/> is the feed
        /// osmotic pressure the caller obtained (package water activity if it has
        /// one, else van 't Hoff); the concentrate osmotic pressure is evaluated
        /// internally by the same van 't Hoff routine on the concentrate
        /// composition, so any feed (multi-salt, any concentration) is handled.
        /// </summary>
        public static Split Solve(Spec s, double[] f, int wi, double[] mwGmol, double tK, double piFeedBar)
        {
            int n = f.Length;
            double feedWaterMol = wi >= 0 ? f[wi] : 0.0;
            double saltPass = 1.0 - Clamp(s.SaltRejPct, 0, 100) / 100.0;
            double maxRec = Clamp(s.MaxRecoveryPct, 0, 100) / 100.0;

            double recovery;
            double naturalRec;
            int iters = 0;
            if (s.CalcMode == Mode.Design)
            {
                double target = Clamp(s.TargetRecoveryPct, 0, 100) / 100.0;
                naturalRec = target;
                recovery = Math.Min(target, maxRec);
            }
            else
            {
                // Rating: solve r = h(r), where h(r) is the recovery the membrane
                // flux would produce at driving force ΔP − π_avg(r). π_avg rises with
                // r, so h decreases with r → the residual g(r) = r − h(r) is strictly
                // increasing with a unique root. Bisection is exact and always
                // converges (deterministic → bit-identical every run), unlike a
                // fixed-point iteration which oscillates for steep, high-gain feeds.
                naturalRec = SolveRatingRecovery(s, f, wi, saltPass, tK, piFeedBar, feedWaterMol, out iters);
                recovery = Math.Min(naturalRec, maxRec);
            }

            var split = new Split
            {
                PiFeedBar = piFeedBar,
                Iterations = iters,
                PermMol = new double[n],
                ConcMol = new double[n],
            };
            // final composition split at the resolved recovery
            var frac = new double[n];
            for (int i = 0; i < n; i++) frac[i] = (i == wi) ? recovery : saltPass * recovery;
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
                split.RequiredPressureBar = piAvgFinal + split.NdpBar;
                split.RequiredAreaM2 = jw > 0 ? permWaterMolFinal * WaterMwKgMol * 3600.0 / jw : 0.0;
                split.AppliedBarUsed = split.RequiredPressureBar;
            }
            else
            {
                split.FluxLMH = s.AreaM2 > 0 ? permWaterMolFinal * WaterMwKgMol * 3600.0 / s.AreaM2 : 0.0;
                split.NdpBar = Math.Max(0.0, s.AppliedBar - piAvgFinal);
                split.RequiredAreaM2 = s.AreaM2;
                split.RequiredPressureBar = s.AppliedBar;
                split.AppliedBarUsed = s.AppliedBar;
            }

            // TDS (mass ratios of mole flows × MW — host-unit independent)
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

        /// <summary>π of the concentrate at a trial recovery (van 't Hoff, any feed).</summary>
        private static double PiConcAt(double r, Spec s, double[] f, int wi, double saltPass, double tK)
        {
            int n = f.Length;
            var perm = new double[n];
            var conc = new double[n];
            var frac = new double[n];
            for (int i = 0; i < n; i++) frac[i] = (i == wi) ? r : saltPass * r;
            ProcessOps.SplitFlows(f, frac, perm, conc);
            return ProcessOps.OsmoticPressureBar(null, conc, wi, s.VantHoffI, tK, null);
        }

        /// <summary>The recovery the membrane flux would produce at trial recovery r.</summary>
        private static double FluxRecovery(double r, Spec s, double[] f, int wi, double saltPass,
                                           double tK, double piFeedBar, double feedWaterMol)
        {
            if (feedWaterMol <= 0) return 0.0;
            double piConc = PiConcAt(r, s, f, wi, saltPass, tK);
            double ndp = Math.Max(0.0, s.AppliedBar - 0.5 * (piFeedBar + piConc));
            double jw = s.WaterPermA * ndp;                                  // L/m2/h
            double permWaterMol = jw * s.AreaM2 / 3600.0 / WaterMwKgMol;      // mol/s
            return permWaterMol / feedWaterMol;
        }

        /// <summary>
        /// Natural (uncapped) rating recovery by bisection of g(r) = r − h(r), which
        /// is strictly increasing (h decreases as π_avg rises with r) → unique root,
        /// bracketed on [0, 1). ~60 halvings reach machine precision, deterministically.
        /// </summary>
        private static double SolveRatingRecovery(Spec s, double[] f, int wi, double saltPass,
                                                  double tK, double piFeedBar, double feedWaterMol, out int iters)
        {
            iters = 0;
            double lo = 0.0, hi = 0.999999;
            double gLo = lo - FluxRecovery(lo, s, f, wi, saltPass, tK, piFeedBar, feedWaterMol);
            if (gLo >= 0.0) return 0.0;                       // applied ≤ feed osmotic → no permeation
            double gHi = hi - FluxRecovery(hi, s, f, wi, saltPass, tK, piFeedBar, feedWaterMol);
            if (gHi <= 0.0) return hi;                        // membrane could pass essentially all water
            for (iters = 0; iters < 100; iters++)
            {
                double mid = 0.5 * (lo + hi);
                double gMid = mid - FluxRecovery(mid, s, f, wi, saltPass, tK, piFeedBar, feedWaterMol);
                if (gMid < 0.0) lo = mid; else hi = mid;
                if (hi - lo < 1e-15) break;
            }
            return 0.5 * (lo + hi);
        }

        /// <summary>
        /// Pump and energy-recovery balance. Volumetric flows [m3/s] come from the
        /// caller (the block reads them from the property package: package mass ÷
        /// package density, which cancels any host mass-unit convention).
        /// </summary>
        public static Energy CalcEnergy(Spec s, double appliedBarUsed,
                                        double feedM3s, double permM3s, double concM3s)
        {
            double dpPa = Math.Max(0.0, appliedBarUsed * 1e5 - AtmPa);
            double eta = s.PumpEffPct > 0 ? s.PumpEffPct / 100.0 : 0.0;
            double gross = eta > 0 ? dpPa * feedM3s / eta / 1000.0 : 0.0; // kW

            double recovered = 0.0;
            if (s.ErdType != Erd.None)
            {
                // brine leaves at ~applied pressure; its hydraulic power is
                // returned at the device efficiency (PX ~96%, turbine ~80%).
                double erdEta = Clamp(s.ErdEffPct, 0, 100) / 100.0;
                recovered = concM3s * dpPa * erdEta / 1000.0; // kW
            }
            double net = Math.Max(0.0, gross - recovered);

            double permM3h = permM3s * 3600.0;
            return new Energy
            {
                GrossPumpKW = gross,
                ErdRecoveredKW = recovered,
                NetPumpKW = net,
                SecGross = permM3h > 1e-12 ? gross / permM3h : 0.0,
                SecNet = permM3h > 1e-12 ? net / permM3h : 0.0,
                EnergySavingPct = gross > 1e-12 ? (gross - net) / gross * 100.0 : 0.0,
            };
        }

        /// <summary>Default ERD efficiency for a device type (%, used to seed the parameter).</summary>
        public static double DefaultErdEffPct(Erd type)
        {
            switch (type)
            {
                case Erd.PressureExchanger: return 96.0;
                case Erd.Turbine: return 80.0;
                default: return 96.0;
            }
        }

        /// <summary>Calculation mode from the integer-coded parameter (0 = Rating, 1 = Design).</summary>
        public static Mode ModeFromCode(double code)
        {
            return code >= 0.5 ? Mode.Design : Mode.Rating;
        }

        /// <summary>ERD type from the integer-coded parameter (0 = None, 1 = PX, 2 = Turbine).</summary>
        public static Erd ErdFromCode(double code)
        {
            if (code >= 1.5) return Erd.Turbine;
            if (code >= 0.5) return Erd.PressureExchanger;
            return Erd.None;
        }

        private static double Clamp(double x, double lo, double hi) { return x < lo ? lo : (x > hi ? hi : x); }
    }
}
