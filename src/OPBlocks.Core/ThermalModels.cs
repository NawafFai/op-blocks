using System;

namespace OPBlocks.Core
{
    // =====================================================================
    //  Family A engines — thermal desalination. Pure physics, no CAPE-OPEN.
    //  Each engine is the single source of truth shared by its block + tests.
    // =====================================================================

    /// <summary>
    /// OP-MD engine — direct-contact membrane distillation (DCMD).
    ///
    /// Water VAPOUR crosses a hydrophobic microporous membrane driven by the
    /// vapour-pressure difference between the hot brine and the cold permeate:
    ///     J  = Bm * (a_w * Psat(T_hot) - Psat(T_cold))     [kg/m2/s]
    ///     Bm = K * eps * d_pore / (tau * delta)            [kg/m2/s/Pa]
    /// the standard Knudsen/molecular-diffusion scaling of the membrane
    /// coefficient (Schofield form): porosity eps and pore size help, tortuosity
    /// tau and thickness delta hurt; K is the calibration constant that lumps the
    /// gas-phase diffusivity and temperature-polarization derating. Non-volatile
    /// solutes cannot evaporate — MD rejection of salts is complete by mechanism,
    /// and the brine's reduced water activity a_w lowers the hot-side driving
    /// vapour pressure (Lawson &amp; Lloyd).
    ///
    /// Validity: Th 40-85 C, Tc 15-35 C, pore 0.1-1 um, DCMD flux ~ 5-60 LMH.
    /// References:
    ///  • R. W. Schofield, A. G. Fane, C. J. D. Fell, "Heat and mass transfer in
    ///    membrane distillation," J. Membr. Sci. 33 (1987) 299-313.
    ///  • K. W. Lawson, D. R. Lloyd, "Membrane distillation," J. Membr. Sci. 124
    ///    (1997) 1-25.
    ///  • M. Khayet, "Membranes and theoretical modeling of membrane distillation:
    ///    a review," Adv. Colloid Interface Sci. 164 (2011) 56-88.
    /// </summary>
    public static class MdModel
    {
        public const double WaterMwKgMol = 0.0180153;
        public const double LatentKJKg = 2333.0;      // ~60 C

        public struct Spec
        {
            public double AreaM2;
            public double Kcal;          // calibration constant (lumps D_ab, polarization)
            public double PorosityFrac;  // 0..1
            public double PoreDiaUm;
            public double TortuosityFac;
            public double ThicknessUm;
            public double HotActivity;   // brine water activity a_w
            public double MaxTransferPct;
        }

        public struct Flux
        {
            public double BmKgM2sPa;
            public double PHotPa, PColdPa, DrivingPa;
            public double JKgM2s, FluxLMH;
            public double WaterMolS;
            public double LatentKW;      // heat that must be supplied ~ J*A*lambda
            public bool TransferCapped;
        }

        public static Flux Solve(Spec s, double hotWaterMol, double tHotK, double tColdK)
        {
            var r = new Flux();
            r.BmKgM2sPa = s.Kcal * ProcessOps.Clamp(s.PorosityFrac, 0, 1) * s.PoreDiaUm
                          / (Math.Max(s.TortuosityFac, 1e-9) * Math.Max(s.ThicknessUm, 1e-9));
            r.PHotPa = ProcessOps.Clamp(s.HotActivity, 0, 1) * ProcessOps.PsatWaterPa(tHotK - 273.15);
            r.PColdPa = ProcessOps.PsatWaterPa(tColdK - 273.15);
            r.DrivingPa = Math.Max(0.0, r.PHotPa - r.PColdPa);
            r.JKgM2s = r.BmKgM2sPa * r.DrivingPa;
            double maxFrac = ProcessOps.Clamp(s.MaxTransferPct, 0, 100) / 100.0;
            double molCap = hotWaterMol * maxFrac;
            r.WaterMolS = Math.Min(r.JKgM2s * s.AreaM2 / WaterMwKgMol, molCap);
            r.TransferCapped = hotWaterMol > 0 && r.WaterMolS >= molCap - 1e-15;
            if (r.TransferCapped && s.AreaM2 > 0)
                r.JKgM2s = r.WaterMolS * WaterMwKgMol / s.AreaM2;
            r.FluxLMH = r.JKgM2s * 3600.0;                 // rho_perm ~ 1000 -> kg/m2/h == L/m2/h
            r.LatentKW = r.JKgM2s * s.AreaM2 * LatentKJKg;
            return r;
        }
    }

    /// <summary>
    /// OP-MED engine — multi-effect distillation (industrial shortcut model).
    ///
    /// N evaporator effects reuse the vapour of each effect as the heat source of
    /// the next, so one unit of motive steam yields close to N units of product:
    ///     GOR = k_GOR * N            (k_GOR ~ 0.8-0.9; El-Dessouky &amp; Ettouney)
    ///     Q_steam = D * lambda / GOR
    /// Distillate D = recovery * feed water (salts are non-volatile). The brine
    /// concentration factor CF = feed TDS/(1 - recovery_mass) is checked against a
    /// maximum brine salinity advisory. Top brine temperature above ~70 C risks
    /// CaSO4/CaCO3 scaling in MED (advisory warning).
    ///
    /// Validity: N 2-16, TBT 55-70 C, recovery 20-50 %; GOR ~ 0.85 N gives the
    /// published MED band (N=8 -> GOR ~ 6.8; specific thermal energy ~ 95 kWh/m3).
    /// References:
    ///  • H. T. El-Dessouky, H. M. Ettouney, "Fundamentals of Salt Water
    ///    Desalination," Elsevier (2002), ch. 8 (MED) — GOR ~ 0.85 N shortcut.
    ///  • M. Al-Shammiri, M. Safar, "Multi-effect distillation plants: state of
    ///    the art," Desalination 126 (1999) 45-59.
    /// </summary>
    public static class MedModel
    {
        public const double WaterMwKgMol = 0.0180153;
        public const double ScalingTbtC = 70.0;

        public struct Spec
        {
            public double NEffects;      // REAL integer code (golden rule)
            public double RecoveryPct;
            public double TopBrineTempC;
            public double GorPerEffect;  // k_GOR
            public double LatentKJKg;
        }

        public struct Perf
        {
            public double DistWaterMolS;
            public double Gor;
            public double SteamKW;
            public double SteKWhM3;      // specific thermal energy per m3 distillate
            public double DistKgS, DistM3h;
        }

        public static int Effects(double code) { return Math.Max(1, (int)Math.Round(code)); }

        public static Perf Solve(Spec s, double feedWaterMol)
        {
            var p = new Perf();
            int n = Effects(s.NEffects);
            p.DistWaterMolS = feedWaterMol * ProcessOps.Clamp(s.RecoveryPct, 0, 100) / 100.0;
            p.Gor = Math.Max(0.1, s.GorPerEffect * n);
            p.DistKgS = p.DistWaterMolS * WaterMwKgMol;
            p.SteamKW = p.DistKgS * s.LatentKJKg / p.Gor;
            p.DistM3h = p.DistKgS / 1000.0 * 3600.0;
            p.SteKWhM3 = p.DistM3h > 1e-12 ? p.SteamKW / p.DistM3h : 0.0;
            return p;
        }
    }

    /// <summary>
    /// OP-MSF engine — multi-stage flash, once-through shortcut model
    /// (El-Dessouky &amp; Ettouney ch. 6).
    ///
    /// Brine heated to the top brine temperature flashes through N stages of
    /// decreasing pressure over the flash range dT_range = TBT - T_last:
    ///     y      = cp * dT_stage / lambda      (flash fraction per stage)
    ///     D      = Mf * [1 - (1 - y)^N]        (total distillate)
    /// so the once-through recovery is ~ cp*dT_range/lambda ~ 8-10 % — MSF is a
    /// LOW-recovery, high-flow process by nature. The brine heater must supply
    /// only the terminal temperature difference plus the per-stage thermodynamic
    /// losses (BPE + non-equilibrium allowance):
    ///     Q_heater = Mf * cp * (dT_stage + dT_loss)
    ///     PR       = D * lambda / Q_heater     (performance ratio, ~ GOR)
    /// Typical 24-stage plants: PR ~ 8-12.
    ///
    /// Validity: N 10-40, TBT 90-112 C, T_last 30-45 C, dT_loss 1.5-3 K.
    /// References:
    ///  • H. T. El-Dessouky, H. M. Ettouney, "Fundamentals of Salt Water
    ///    Desalination," Elsevier (2002), ch. 6 — once-through MSF model.
    ///  • A. D. Khawaji, I. K. Kutubkhanah, J.-M. Wie, "Advances in seawater
    ///    desalination technologies," Desalination 221 (2008) 47-69.
    /// </summary>
    public static class MsfModel
    {
        public const double WaterMwKgMol = 0.0180153;

        public struct Spec
        {
            public double NStages;       // REAL integer code
            public double TopBrineTempC;
            public double LastStageTempC;
            public double ThermoLossK;   // BPE + NEA per stage
            public double CpKJKgK;
            public double LatentKJKg;
        }

        public struct Perf
        {
            public double FlashRangeK, StageDtK, FlashFracPerStage;
            public double RecoveryFrac;
            public double DistWaterMolS, DistKgS, DistM3h;
            public double HeaterKW;
            public double PerfRatio;
            public double SteKWhM3;
        }

        public static int Stages(double code) { return Math.Max(1, (int)Math.Round(code)); }

        public static Perf Solve(Spec s, double feedWaterMol, double feedMassKgS)
        {
            var p = new Perf();
            int n = Stages(s.NStages);
            p.FlashRangeK = Math.Max(0.0, s.TopBrineTempC - s.LastStageTempC);
            p.StageDtK = p.FlashRangeK / n;
            p.FlashFracPerStage = s.CpKJKgK * p.StageDtK / Math.Max(s.LatentKJKg, 1e-9);
            p.RecoveryFrac = 1.0 - Math.Pow(1.0 - p.FlashFracPerStage, n);
            p.DistWaterMolS = feedWaterMol * p.RecoveryFrac;
            p.DistKgS = p.DistWaterMolS * WaterMwKgMol;
            p.DistM3h = p.DistKgS / 1000.0 * 3600.0;
            p.HeaterKW = feedMassKgS * s.CpKJKgK * (p.StageDtK + Math.Max(0, s.ThermoLossK));
            p.PerfRatio = p.HeaterKW > 1e-12 ? p.DistKgS * s.LatentKJKg / p.HeaterKW : 0.0;
            p.SteKWhM3 = p.DistM3h > 1e-12 ? p.HeaterKW / p.DistM3h : 0.0;
            return p;
        }
    }

    /// <summary>
    /// OP-MVC engine — mechanical vapour compression evaporator.
    ///
    /// The evaporated vapour is compressed (raising its saturation temperature)
    /// and returned as the heat source, so MVC is all-electric. Specific
    /// compression work for steam treated as ideal gas over a modest ratio:
    ///     w = cp_v * T_sat * [ CR^((gamma-1)/gamma) - 1 ] / eta_isentropic
    /// with cp_v = 1.88 kJ/kg K and gamma = 1.33 for steam ((gamma-1)/gamma =
    /// 0.248). The compression ratio must at least overcome the boiling-point
    /// elevation of the brine, or no temperature driving force remains
    /// (advisory). Published MVC SEC: ~ 8-15 kWh/m3.
    ///
    /// Validity: CR 1.1-2, recovery 30-60 %, T_evap 50-75 C.
    /// References:
    ///  • H. T. El-Dessouky, H. M. Ettouney, "Fundamentals of Salt Water
    ///    Desalination," Elsevier (2002), ch. 7 — single-effect MVC.
    ///  • N. H. Aly, A. K. El-Fiqi, "Mechanical vapor compression desalination
    ///    systems — a case study," Desalination 158 (2003) 143-150.
    /// </summary>
    public static class MvcModel
    {
        public const double WaterMwKgMol = 0.0180153;
        public const double CpVaporKJKgK = 1.88;
        public const double GammaExp = 0.248;        // (gamma-1)/gamma, steam
        public const double MinUsefulCR = 1.1;

        public struct Spec
        {
            public double RecoveryPct;
            public double CompressionRatio;
            public double CompressorEffPct;
            public double EvapTempC;
        }

        public struct Perf
        {
            public double VaporMolS, VaporKgS, DistM3h;
            public double SpecWorkKJKg;
            public double PowerKW;
            public double SecKWhM3;
        }

        public static Perf Solve(Spec s, double feedWaterMol)
        {
            var p = new Perf();
            p.VaporMolS = feedWaterMol * ProcessOps.Clamp(s.RecoveryPct, 0, 100) / 100.0;
            p.VaporKgS = p.VaporMolS * WaterMwKgMol;
            double tSatK = s.EvapTempC + 273.15;
            double eta = s.CompressorEffPct > 0 ? s.CompressorEffPct / 100.0 : 1.0;
            p.SpecWorkKJKg = CpVaporKJKgK * tSatK * (Math.Pow(Math.Max(s.CompressionRatio, 1.0), GammaExp) - 1.0) / eta;
            p.PowerKW = p.VaporKgS * p.SpecWorkKJKg;
            p.DistM3h = p.VaporKgS / 1000.0 * 3600.0;
            p.SecKWhM3 = p.DistM3h > 1e-12 ? p.PowerKW / p.DistM3h : 0.0;
            return p;
        }
    }
}
