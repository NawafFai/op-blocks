using System;

namespace OPBlocks.Core
{
    // =====================================================================
    //  Family E engines — energy, gas & advanced oxidation. Pure physics.
    // =====================================================================

    /// <summary>
    /// Shared water-electrolyzer engine (OP-PEM and OP-AEL — identical Faradaic
    /// physics, different operating ranges).
    ///
    ///     I      = j * A_cell * 1e4              [A per cell]
    ///     N_H2   = eta_F * I * N_cells / (2 F)   (2 e- per H2)
    ///     N_O2   = N_H2 / 2;   water consumed = N_H2
    /// Cell-voltage energy metrics (exact closed forms, pinned by tests):
    ///     SEC    = 26.59 * V / eta_F             [kWh/kg H2]
    ///              (26.59 = 2F / (3600 s/h * 0.00201588 kg/mol) / 1000)
    ///     eff_HHV = V_tn / V_cell with V_tn = 1.481 V (thermoneutral)
    ///     eff_LHV = 1.253 / V_cell
    /// Production capped by the water actually fed (water-limited warning).
    ///
    /// Validity: PEM j 1-4 A/cm2, V 1.7-2.1; alkaline j 0.2-0.6 A/cm2,
    /// V 1.8-2.2. At 1.9 V / 99%: SEC ~ 51 kWh/kg — the published band.
    ///
    /// References:
    ///  • M. Carmo, D. L. Fritz, J. Mergel, D. Stolten, "A comprehensive review
    ///    on PEM water electrolysis," Int. J. Hydrogen Energy 38 (2013) 4901-4934.
    ///  • A. Ursua, L. M. Gandia, P. Sanchis, "Hydrogen production from water
    ///    electrolysis: current status and future trends," Proc. IEEE 100 (2012)
    ///    410-426.
    ///  • F. Barbir, "PEM electrolysis for production of hydrogen from renewable
    ///    energy sources," Solar Energy 78 (2005) 661-669.
    /// </summary>
    public static class ElectrolyzerModel
    {
        public const double MwH2 = 0.00201588, MwO2 = 0.0319988, MwH2O = 0.0180153;
        public const double ThermoneutralV = 1.481;   // HHV basis
        public const double LhvVoltEquiv = 1.253;     // LHV basis
        public const double SecPerVolt = 2.0 * 96485.33212 / 3600.0 / MwH2 / 1000.0; // kWh/kg per V

        public struct Spec
        {
            public double CellAreaM2;
            public double CurrentDensityAcm2;
            public double CellCount;        // REAL integer code
            public double CellVoltageV;
            public double FaradaicEffPct;
        }

        public struct Perf
        {
            public double CurrentPerCellA;
            public double H2FaradaicMolS;
            public double H2MolS, O2MolS, WaterConsMolS;
            public bool WaterLimited;
            public double StackPowerKW;
            public double SecKWhKg;
            public double EffHhvPct, EffLhvPct;
        }

        public static int Cells(double code) { return Math.Max(1, (int)Math.Round(code)); }

        public static Perf Solve(Spec s, double waterFeedMolS)
        {
            var p = new Perf();
            p.CurrentPerCellA = s.CurrentDensityAcm2 * s.CellAreaM2 * 1e4;
            double eff = ProcessOps.Clamp(s.FaradaicEffPct, 0, 100) / 100.0;
            int n = Cells(s.CellCount);
            p.H2FaradaicMolS = ProcessOps.FaradayMoles(p.CurrentPerCellA, 2, eff) * n;
            p.H2MolS = Math.Min(p.H2FaradaicMolS, Math.Max(0, waterFeedMolS) * 0.99);
            p.WaterLimited = p.H2MolS < p.H2FaradaicMolS * 0.999;
            p.O2MolS = p.H2MolS / 2.0;
            p.WaterConsMolS = p.H2MolS;
            p.StackPowerKW = s.CellVoltageV * p.CurrentPerCellA * n / 1000.0;
            p.SecKWhKg = eff > 0 ? SecPerVolt * s.CellVoltageV / eff : 0.0;
            p.EffHhvPct = s.CellVoltageV > 0 ? ThermoneutralV / s.CellVoltageV * 100.0 * eff : 0.0;
            p.EffLhvPct = s.CellVoltageV > 0 ? LhvVoltEquiv / s.CellVoltageV * 100.0 * eff : 0.0;
            return p;
        }
    }

    /// <summary>
    /// OP-FC engine — PEM fuel cell (H2 + air → power + water).
    ///
    ///     H2 consumed = utilization * H2 fed, capped by the O2 available
    ///                   (2 H2 per O2 — air-limited warning)
    ///     I = 2 F * N_H2;   P = V_cell * I
    ///     eff_LHV = V_cell / 1.253      (exact closed form, pinned by test;
    ///                                    0.68 V → 54.3 %)
    /// Product water = H2 consumed; the exhaust carries depleted air, product
    /// water and slip H2.
    ///
    /// References:
    ///  • R. O'Hayre, S.-W. Cha, W. Colella, F. B. Prinz, "Fuel Cell
    ///    Fundamentals," 3rd ed. (2016), ch. 2 (voltage-efficiency relations).
    ///  • F. Barbir, "PEM Fuel Cells: Theory and Practice," 2nd ed. (2013).
    ///  • J. Larminie, A. Dicks, "Fuel Cell Systems Explained," 2nd ed. (2003).
    /// </summary>
    public static class FcModel
    {
        public const double MwH2 = 0.00201588, MwH2O = 0.0180153;
        public const double LhvVoltEquiv = 1.253;
        public const double H2LhvKJKg = 120000.0;

        public struct Spec
        {
            public double UtilizationPct;
            public double CellVoltageV;
        }

        public struct Perf
        {
            public double H2WantMolS, H2ConsMolS, O2ConsMolS, WaterProdMolS;
            public bool AirLimited;
            public double CurrentA;
            public double PowerKW;
            public double EffLhvPct;
        }

        public static Perf Solve(Spec s, double h2FeedMolS, double o2FeedMolS)
        {
            var p = new Perf();
            double util = ProcessOps.Clamp(s.UtilizationPct, 0, 100) / 100.0;
            p.H2WantMolS = h2FeedMolS * util;
            p.O2ConsMolS = Math.Min(p.H2WantMolS / 2.0, Math.Max(0, o2FeedMolS));
            p.H2ConsMolS = p.O2ConsMolS * 2.0;
            p.AirLimited = p.H2ConsMolS < p.H2WantMolS * 0.999;
            p.WaterProdMolS = p.H2ConsMolS;
            p.CurrentA = p.H2ConsMolS * 2.0 * ProcessOps.Faraday;
            p.PowerKW = s.CellVoltageV * p.CurrentA / 1000.0;
            p.EffLhvPct = s.CellVoltageV / LhvVoltEquiv * 100.0;
            return p;
        }
    }

    /// <summary>
    /// OP-RPB engine — rotating packed bed (HiGee) absorber.
    ///
    /// The centrifugal field (hundreds of g) shears the liquid into fine films
    /// and droplets, intensifying k_L·a by one to two orders of magnitude over
    /// a static column. Transfer units scale with the centrifugal acceleration;
    /// with a ∝ ω², the practical correlation reduces to
    ///     NTU = k_cal * sqrt(RPM)
    ///     removal = 1 - exp(-NTU)               (plug-flow transfer units)
    /// k_cal calibrates packing geometry, liquid rate and diffusivity into one
    /// constant (Chen's k_La correlations reduce this way at fixed flows).
    /// Rotor power is an indicative windage/acceleration estimate ∝ ω².
    ///
    /// References:
    ///  • C. Ramshaw, R. H. Mallinson, "Mass transfer process," US Patent
    ///    4,283,255 (1981) — the original HiGee disclosure.
    ///  • Y.-S. Chen, C.-C. Lin, H.-S. Liu, "Mass transfer in a rotating packed
    ///    bed with various radii of the bed," Ind. Eng. Chem. Res. 44 (2005)
    ///    7868-7875.
    /// </summary>
    public static class RpbModel
    {
        public struct Spec
        {
            public double RotorRpm;
            public double KlaCal;
            public double RotorPowerCoeff;   // kW per rpm^2 (indicative)
        }

        public struct Perf
        {
            public double Ntu;
            public double RemovalFrac;
            public double RotorKW;
        }

        public static Perf Solve(Spec s)
        {
            var p = new Perf();
            p.Ntu = s.KlaCal * Math.Sqrt(Math.Max(0, s.RotorRpm));
            p.RemovalFrac = ProcessOps.Clamp01(1.0 - Math.Exp(-p.Ntu));
            p.RotorKW = s.RotorPowerCoeff * s.RotorRpm * s.RotorRpm;
            return p;
        }
    }

    /// <summary>
    /// OP-UVAOP engine — UV / H2O2 advanced oxidation.
    ///
    /// First-order UV dose-response (the standard collimated-beam kinetics):
    ///     ln(C/C0) = -k * D_eff
    ///     D_eff    = D * (UVT/100) * (1 + H2O2/20)
    /// (the H2O2 term is the empirical hydroxyl-radical enhancement; 20 mg/L
    /// doubles the effective rate — typical of published UV/H2O2 pilot data).
    ///     log removal = k * D_eff / ln(10)
    /// The ELECTRICAL ENERGY PER ORDER is Bolton's IUPAC figure of merit:
    ///     EEO = P / (Q * log_removal)           [kWh/m3/order]
    /// (exact by definition — pinned by test). Published UV/H2O2 EEO for trace
    /// organics: ~ 0.1-2.5 kWh/m3/order.
    ///
    /// References:
    ///  • J. R. Bolton, K. G. Bircher, W. Tumas, C. A. Tolman, "Figures-of-merit
    ///    for the technical development and application of advanced oxidation
    ///    technologies," Pure Appl. Chem. 73 (2001) 627-637.
    ///  • T. Oppenländer, "Photochemical Purification of Water and Air" (2003).
    /// </summary>
    public static class UvAopModel
    {
        public struct Spec
        {
            public double UvDoseMJcm2;
            public double RateKcm2mJ;
            public double UvtPct;
            public double H2o2MgL;
            public double LampPowerKW;
        }

        public struct Perf
        {
            public double EffDoseMJcm2;
            public double DestroyFrac;
            public double LogRemoval;
            public double EeoKWhM3Order;
        }

        public static Perf Solve(Spec s, double flowM3h)
        {
            var p = new Perf();
            double boost = 1.0 + Math.Max(0, s.H2o2MgL) / 20.0;
            p.EffDoseMJcm2 = s.UvDoseMJcm2 * ProcessOps.Clamp(s.UvtPct, 0, 100) / 100.0 * boost;
            p.DestroyFrac = ProcessOps.Clamp01(1.0 - Math.Exp(-s.RateKcm2mJ * p.EffDoseMJcm2));
            p.LogRemoval = s.RateKcm2mJ * p.EffDoseMJcm2 / Math.Log(10.0);
            p.EeoKWhM3Order = (flowM3h > 1e-12 && p.LogRemoval > 1e-12)
                ? s.LampPowerKW / (flowM3h * p.LogRemoval) : 0.0;
            return p;
        }
    }
}
