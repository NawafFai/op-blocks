using System;

namespace OPBlocks.Core
{
    /// <summary>
    /// The pure solar-evaporation-pond engine — no CAPE-OPEN, no host objects, just
    /// arrays and doubles. Single source of truth for OP-EVAPPOND physics, shared by
    /// the block (<c>OPBlocks.Desalination.EvapPond</c>) and the validation tests so
    /// the two can never drift.
    ///
    /// A third, distinct physics family (mass transfer to the atmosphere — no
    /// pressure, no electric field), to prove the factory survives it. Water leaves
    /// the brine by evaporation set by a **Dalton / aerodynamic mass-transfer law**:
    ///
    ///     E = (a + b*u) * (e_s - e_a)          [kg/m2/s]
    ///
    /// e_s = a_w * Psat(T_surf) is the brine surface vapour pressure (the water
    /// activity a_w &lt; 1 of a concentrated brine LOWERS it — the salinity effect),
    /// e_a = RH * Psat(T_air) is the ambient vapour pressure, u = wind speed, and
    /// a, b are empirical Dalton coefficients. The surface temperature is raised
    /// above ambient by absorbed solar radiation via a lumped linear closure
    /// T_surf = T_air + k_solar*Irradiance. Salts stay in the pond, so the brine
    /// concentrates by CF = feed water / (feed water - evaporated water).
    ///
    /// The saturation vapour pressure is the Antoine correlation in
    /// <see cref="ProcessOps.PsatWaterPa"/> (block-specific ambient closure; stream
    /// phase equilibria still go through the property package).
    ///
    /// References:
    ///  • H. L. Penman, "Natural evaporation from open water, bare soil and grass,"
    ///    Proc. R. Soc. Lond. A 193 (1948) 120-145 (aerodynamic method).
    ///  • E. Sartori, "A critical review on equations employed for the calculation of
    ///    the evaporation rate from free water surfaces," Solar Energy 68 (2000)
    ///    77-89 (Dalton-type coefficients a, b).
    ///  • Y. A. Salhotra, E. E. Adams, D. R. F. Harleman, "Effect of salinity and
    ///    ionic composition on evaporation," Water Resour. Res. 21 (1985) 1336-1344.
    ///  • S. Al-Shammiri, "Evaporation rate as a function of water salinity,"
    ///    Desalination 150 (2002) 189-203 (water-activity reduction).
    /// </summary>
    public static class EvapPondModel
    {
        public const double WaterMwKgMol = 0.0180153;   // kg/mol
        public const double HighConcentrationFactor = 5.0; // CF above → salt saturation / solids onset

        public struct Spec
        {
            public double AreaM2;
            public double DepthM;
            public double IrradianceWm2;
            public double AirTempC;
            public double RHpct;
            public double WindSpeedMs;
            public double WaterActivity;   // surface a_w of the brine
            public double CoeffA;          // kg/m2/s/Pa (Dalton a)
            public double CoeffB;          // kg/m2/s/Pa/(m/s) (Dalton b, wind)
            public double SolarHeating;    // C per W/m2 (surface warming closure)
        }

        public struct Result
        {
            public double SurfaceTempC;
            public double ESurfPa, EAirPa, DrivingForcePa;
            public double FluxKgM2S;
            public double EvapKgS, EvapMol;
            public double EvapMmDay, EvapM3Day;
            public double ConcentrationFactor;
            public double ResidenceDays;
            public bool FeedLimited;        // evaporation would exceed the feed water
            public double[] VaporMol, ConcMol; // mol/s per component
            public double TdsFeedPpm, TdsConcPpm;
        }

        /// <summary>
        /// Evaporative water loss and the concentrated-brine / vapour split.
        /// <paramref name="feedM3s"/> (from the package) is used only for the pond
        /// residence time. Salts do not evaporate — only water leaves to the vapour
        /// outlet; the material balance closes exactly.
        /// </summary>
        public static Result Solve(Spec s, double[] f, int wi, double[] mwGmol, double feedM3s)
        {
            int n = f.Length;
            double feedWaterMol = wi >= 0 ? f[wi] : 0.0;

            double surfC = ProcessOps.Clamp(s.AirTempC + s.SolarHeating * s.IrradianceWm2, 0.0, 95.0);
            double eSurf = ProcessOps.Clamp(s.WaterActivity, 0.0, 1.0) * ProcessOps.PsatWaterPa(surfC);
            double eAir = ProcessOps.Clamp(s.RHpct, 0.0, 100.0) / 100.0 * ProcessOps.PsatWaterPa(s.AirTempC);
            double driving = Math.Max(0.0, eSurf - eAir);
            double flux = Math.Max(0.0, s.CoeffA + s.CoeffB * Math.Max(0.0, s.WindSpeedMs)) * driving; // kg/m2/s
            double evapKgS = flux * Math.Max(0.0, s.AreaM2);
            double evapMolUncapped = evapKgS / WaterMwKgMol;
            double evapMol = Math.Min(evapMolUncapped, feedWaterMol * 0.999);

            var vapor = new double[n];
            var conc = (double[])f.Clone();
            if (wi >= 0) { vapor[wi] = evapMol; conc[wi] -= evapMol; }

            double cf = (feedWaterMol > evapMol && feedWaterMol > 0)
                ? feedWaterMol / (feedWaterMol - evapMol) : double.PositiveInfinity;
            double pondVol = Math.Max(0.0, s.AreaM2) * Math.Max(0.0, s.DepthM);
            double residenceDays = feedM3s > 1e-30 ? pondVol / feedM3s / 86400.0 : 0.0;

            var r = new Result
            {
                SurfaceTempC = surfC,
                ESurfPa = eSurf,
                EAirPa = eAir,
                DrivingForcePa = driving,
                FluxKgM2S = flux,
                EvapKgS = evapMol * WaterMwKgMol,       // actual (post-cap) evaporation
                EvapMol = evapMol,
                EvapMmDay = flux * 86400.0,             // 1 kg/m2 water = 1 mm depth
                EvapM3Day = evapMol * WaterMwKgMol / 1000.0 * 86400.0,
                ConcentrationFactor = cf,
                ResidenceDays = residenceDays,
                FeedLimited = evapMolUncapped > feedWaterMol * 0.999 + 1e-30,
                VaporMol = vapor,
                ConcMol = conc,
            };

            if (mwGmol != null)
            {
                r.TdsFeedPpm = Tds(f, wi, mwGmol);
                r.TdsConcPpm = Tds(conc, wi, mwGmol);
            }
            return r;
        }

        private static double Tds(double[] flows, int wi, double[] mwGmol)
        {
            double kg = 0, salt = 0;
            for (int i = 0; i < flows.Length; i++)
            {
                double m = flows[i] * mwGmol[i];
                kg += m;
                if (i != wi) salt += m;
            }
            return kg > 1e-30 ? salt / kg * 1e6 : 0.0;
        }
    }
}
