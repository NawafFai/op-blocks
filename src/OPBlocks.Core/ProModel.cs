using System;

namespace OPBlocks.Core
{
    /// <summary>
    /// Pure pressure-retarded-osmosis engine — the single source of truth for
    /// OP-PRO physics, shared by the CAPE-OPEN block and the tests.
    ///
    /// PRO physics (osmotic power from a salinity gradient):
    ///  • Water permeates from the low-salinity feed into the PRESSURISED
    ///    high-salinity draw against the applied hydraulic pressure dP:
    ///        Jw = A * (sigma * dPi − dP)               [L/m2/h]
    ///  • Each unit of permeate is expanded through the hydro-turbine at dP,
    ///    so the membrane POWER DENSITY is
    ///        W = Jw * dP                               [W/m2 with SI units]
    ///    Substituting Jw gives W = A (sigma·dPi − dP) dP — a downward parabola
    ///    in dP whose maximum sits EXACTLY at dP* = sigma·dPi / 2 with peak
    ///    W_max = A (sigma·dPi)^2 / 4. This dP* = dPi/2 optimum is the classical
    ///    PRO result (Loeb; Achilli &amp; Childress) and is pinned by a unit test.
    ///  • The gradient collapses as the feed concentrates and the draw dilutes;
    ///    the engine evaluates dPi at the average of inlet and outlet osmotic
    ///    pressures per side (deterministic damped fixed point, like OP-FO).
    ///
    /// Validity: seawater/river pairs dPi ~ 25-30 bar → W_max ~ 4-6 W/m2 at
    /// A ~ 1 L/m2/h/bar (the published PRO viability band); dP below sigma·dPi.
    ///
    /// References:
    ///  • S. Loeb, "Production of energy from concentrated brines by pressure-
    ///    retarded osmosis," J. Membr. Sci. 1 (1976) 49-63.
    ///  • A. Achilli, A. E. Childress, "Pressure retarded osmosis: from the vision
    ///    of Sidney Loeb to the first prototype installation — review,"
    ///    Desalination 261 (2010) 205-211.
    ///  • A. P. Straub, A. Deshmukh, M. Elimelech, "Pressure-retarded osmosis for
    ///    power generation from salinity gradients: is it viable?,"
    ///    Energy Environ. Sci. 9 (2016) 31-48.
    /// </summary>
    public static class ProModel
    {
        public const double WaterMwKgMol = 0.0180153;

        public struct Spec
        {
            public double AreaM2;
            public double WaterPermA;      // L/m2/h/bar
            public double ReflectionSigma; // -
            public double AppliedBar;      // hydraulic dP on the draw side
            public double VantHoffI;
            public double TurbineEffPct;   // hydro-turbine/generator efficiency
            public double MaxTransferPct;  // cap on feed water transferred
        }

        public struct Power
        {
            public double WaterMolS;
            public double FluxLMH;
            public double PiFeedBar, PiDrawBar, DeltaPiBar, NetDrivingBar;
            public double PowerDensityWm2;   // membrane power density (gross)
            public double GrossKW;           // Jw·A·dP
            public double NetKW;             // after turbine efficiency
            public double OptimalDeltaPBar;  // sigma·dPi/2 at module-average dPi
            public double[] FeedOutMol, DrawOutMol;
            public bool TransferCapped;
            public int Iterations;
        }

        public static Power Solve(Spec s, double[] feed, double[] draw, int wi,
                                  double tFeedK, double tDrawK,
                                  double piFeedInBar, double piDrawInBar)
        {
            double sigma = ProcessOps.Clamp(s.ReflectionSigma, 0, 1);
            double maxFrac = ProcessOps.Clamp(s.MaxTransferPct, 0, 100) / 100.0;
            double feedWater = wi >= 0 ? feed[wi] : 0.0;
            double dP = Math.Max(0.0, s.AppliedBar);

            var p = new Power { PiFeedBar = piFeedInBar, PiDrawBar = piDrawInBar };

            double waterMol = 0.0, ndf = 0.0, dPiAvg = 0.0;
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
                dPiAvg = Math.Max(0.0, piD - piF);
                ndf = Math.Max(0.0, sigma * dPiAvg - dP);
                double jw = s.WaterPermA * ndf;                                // L/m2/h
                double next = Math.Min(jw * s.AreaM2 / 3600.0 / WaterMwKgMol,
                                       feedWater * maxFrac);
                if (Math.Abs(next - waterMol) < 1e-14 * Math.Max(1.0, feedWater)) { waterMol = next; break; }
                waterMol = 0.5 * (waterMol + next);
            }
            p.Iterations = iter;
            p.WaterMolS = waterMol;
            p.TransferCapped = feedWater > 0 && waterMol >= feedWater * maxFrac - 1e-12;
            p.DeltaPiBar = dPiAvg;
            p.NetDrivingBar = ndf;
            p.FluxLMH = s.AreaM2 > 0 ? waterMol * WaterMwKgMol * 3600.0 / s.AreaM2 : 0.0;
            p.OptimalDeltaPBar = sigma * dPiAvg / 2.0;

            // W = Jw·dP : LMH × bar → W/m2 is (L/m2/h)(1e-3 m3/L)(1/3600 h/s)(1e5 Pa/bar)
            p.PowerDensityWm2 = p.FluxLMH * dP * (1e-3 / 3600.0 * 1e5);
            double permM3s = waterMol * WaterMwKgMol / 1000.0;                 // ρ≈1000 for the permeating water
            p.GrossKW = dP * 1e5 * permM3s / 1000.0;
            double eta = s.TurbineEffPct > 0 ? s.TurbineEffPct / 100.0 : 0.0;
            p.NetKW = p.GrossKW * eta;

            p.FeedOutMol = (double[])feed.Clone();
            p.DrawOutMol = (double[])draw.Clone();
            if (wi >= 0) { p.FeedOutMol[wi] -= waterMol; p.DrawOutMol[wi] += waterMol; }
            return p;
        }
    }
}
