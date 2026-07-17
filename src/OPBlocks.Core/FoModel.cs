using System;

namespace OPBlocks.Core
{
    /// <summary>
    /// Pure forward-osmosis engine — the single source of truth for OP-FO physics,
    /// shared by the CAPE-OPEN block and the tests.
    ///
    /// FO physics (osmotically driven — the mirror image of RO):
    ///  • Water crosses from the LOW-osmotic feed into the HIGH-osmotic draw
    ///    solution with no applied pressure:
    ///        Jw = A * sigma * (pi_draw − pi_feed)      [L/m2/h]
    ///    A = water permeability, sigma = reflection coefficient. (Concentration
    ///    polarization is lumped into an effective sigma — the rigorous internal-CP
    ///    model of McCutcheon &amp; Elimelech reduces the effective driving force
    ///    exactly this way for a fixed membrane orientation.)
    ///  • REVERSE SALT FLUX: draw solute leaks backwards into the feed,
    ///        Js = B * (C_draw − C_feed)                [mol/m2/h]
    ///    with B the solute permeability and C the molar concentrations. The
    ///    specific reverse flux Js/Jw is the standard FO membrane selectivity
    ///    metric (Phillip et al.).
    ///  • The feed concentrates (CF = feed_in/feed_out water) and the draw dilutes;
    ///    both effects raise/lower the respective osmotic pressures — this engine
    ///    evaluates the driving force at the module average of inlet and outlet
    ///    osmotic pressures on each side, solved by deterministic fixed-point
    ///    iteration (contraction: transfer shrinks the driving force).
    ///
    /// Validity: FO A ~ 0.5-3 L/m2/h/bar, B such that Js/Jw ~ 0.05-0.5 mol/L;
    /// pi from van 't Hoff or the package water activity.
    ///
    /// References:
    ///  • T. Y. Cath, A. E. Childress, M. Elimelech, "Forward osmosis: principles,
    ///    applications, and recent developments," J. Membr. Sci. 281 (2006) 70-87.
    ///  • J. R. McCutcheon, M. Elimelech, "Influence of concentrative and dilutive
    ///    internal concentration polarization on flux behavior in forward osmosis,"
    ///    J. Membr. Sci. 284 (2006) 237-247.
    ///  • W. A. Phillip, J. S. Yong, M. Elimelech, "Reverse draw solute permeation
    ///    in forward osmosis," Environ. Sci. Technol. 44 (2010) 5170-5176.
    /// </summary>
    public static class FoModel
    {
        public const double WaterMwKgMol = 0.0180153;

        public struct Spec
        {
            public double AreaM2;
            public double WaterPermA;     // L/m2/h/bar
            public double SaltPermB;      // L/m2/h (reverse solute permeability)
            public double ReflectionSigma;
            public double VantHoffI;
            public double MaxTransferPct; // cap on feed water transferred, %
        }

        public struct Transfer
        {
            public double WaterMolS;      // feed → draw
            public double RevSaltMolS;    // draw → feed (total draw solute)
            public double FluxLMH;
            public double PiFeedBar, PiDrawBar, NetDrivingBar;
            public double[] FeedOutMol, DrawOutMol;
            public double ConcFactorFeed; // feed water in / feed water out
            public double DilutionDraw;   // draw water out / draw water in
            public bool TransferCapped;
            public int Iterations;
        }

        /// <summary>
        /// Solves the coupled transfer. Osmotic pressures of the INLETS are supplied
        /// by the caller (package activity when available); outlet-side pi values are
        /// recomputed internally with van 't Hoff on the evolving compositions, and
        /// the driving force uses the average of inlet and outlet on each side.
        /// </summary>
        public static Transfer Solve(Spec s, double[] feed, double[] draw, int wi,
                                     double tFeedK, double tDrawK,
                                     double piFeedInBar, double piDrawInBar)
        {
            int n = feed.Length;
            double sigma = ProcessOps.Clamp(s.ReflectionSigma, 0, 1);
            double maxFrac = ProcessOps.Clamp(s.MaxTransferPct, 0, 100) / 100.0;
            double feedWater = wi >= 0 ? feed[wi] : 0.0;

            var t = new Transfer { PiFeedBar = piFeedInBar, PiDrawBar = piDrawInBar };

            // fixed-point on the transferred water (10 rounds is plenty; the map is
            // a contraction because transfer raises pi_feed_out and lowers pi_draw_out)
            double waterMol = 0.0;
            double ndf = 0.0;
            int iter;
            for (iter = 0; iter < 50; iter++)
            {
                double[] fOut = (double[])feed.Clone();
                double[] dOut = (double[])draw.Clone();
                if (wi >= 0) { fOut[wi] -= waterMol; dOut[wi] += waterMol; }
                double piFeedOut = ProcessOps.OsmoticPressureBar(null, fOut, wi, s.VantHoffI, tFeedK, null);
                double piDrawOut = ProcessOps.OsmoticPressureBar(null, dOut, wi, s.VantHoffI, tDrawK, null);
                double piF = 0.5 * (piFeedInBar + piFeedOut);
                double piD = 0.5 * (piDrawInBar + piDrawOut);
                ndf = Math.Max(0.0, sigma * (piD - piF));
                double jw = s.WaterPermA * ndf;                              // L/m2/h
                double next = Math.Min(jw * s.AreaM2 / 3600.0 / WaterMwKgMol,
                                       feedWater * maxFrac);
                if (Math.Abs(next - waterMol) < 1e-14 * Math.Max(1.0, feedWater)) { waterMol = next; break; }
                waterMol = 0.5 * (waterMol + next);                          // damped, deterministic
            }
            t.Iterations = iter;
            t.WaterMolS = waterMol;
            t.TransferCapped = feedWater > 0 && waterMol >= feedWater * maxFrac - 1e-12;
            t.NetDrivingBar = ndf;
            t.FluxLMH = s.AreaM2 > 0 ? waterMol * WaterMwKgMol * 3600.0 / s.AreaM2 : 0.0;

            // reverse draw-solute flux, distributed over the draw's non-water species
            double drawWater = wi >= 0 ? draw[wi] : 0.0;
            double cDraw = ProcessOps.MolarityMolL(ProcessOps.Sum(draw) - drawWater, drawWater);
            double cFeed = ProcessOps.MolarityMolL(ProcessOps.Sum(feed) - feedWater, feedWater);
            double js = Math.Max(0.0, s.SaltPermB * (cDraw - cFeed));        // mol/m2/h
            double revTotal = js * s.AreaM2 / 3600.0;                        // mol/s
            double drawSolute = ProcessOps.Sum(draw) - drawWater;
            revTotal = Math.Min(revTotal, drawSolute * 0.5);                 // never drain the draw

            t.FeedOutMol = (double[])feed.Clone();
            t.DrawOutMol = (double[])draw.Clone();
            if (wi >= 0) { t.FeedOutMol[wi] -= waterMol; t.DrawOutMol[wi] += waterMol; }
            if (drawSolute > 1e-30)
            {
                for (int i = 0; i < n; i++)
                {
                    if (i == wi) continue;
                    double share = draw[i] / drawSolute * revTotal;
                    t.DrawOutMol[i] -= share;
                    t.FeedOutMol[i] += share;
                }
            }
            t.RevSaltMolS = drawSolute > 1e-30 ? revTotal : 0.0;

            double feedWaterOut = wi >= 0 ? t.FeedOutMol[wi] : 0.0;
            double drawWaterOut = wi >= 0 ? t.DrawOutMol[wi] : 0.0;
            t.ConcFactorFeed = feedWaterOut > 1e-30 ? feedWater / feedWaterOut : 0.0;
            t.DilutionDraw = drawWater > 1e-30 ? drawWaterOut / drawWater : 0.0;
            return t;
        }
    }
}
