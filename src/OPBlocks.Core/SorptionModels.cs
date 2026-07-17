using System;

namespace OPBlocks.Core
{
    // =====================================================================
    //  Family D engines — lithium, sorption & precipitation. Pure physics.
    // =====================================================================

    /// <summary>
    /// OP-DLE engine — direct lithium extraction sorption column, cycle-averaged.
    ///
    /// Equilibrium loading from the LANGMUIR isotherm:
    ///     q* = q_max * K * C / (1 + K * C)          [mg Li / g sorbent]
    /// (half-loading exactly at C = 1/K — pinned by test), approached over the
    /// loading cycle with the GLUECKAUF linear-driving-force closure:
    ///     q(t_cycle) = q* * (1 - exp(-k_LDF * t_cycle))
    /// Captured Li per cycle = q * m_bed / MW_Li, cycle-averaged to mol/s and
    /// capped by the Li actually fed. Co-sorbed Mg follows the Mg/Li
    /// selectivity: Mg_captured = Mg_feed * (Li_recovery / S_MgLi).
    ///
    /// References:
    ///  • I. Langmuir, "The adsorption of gases on plane surfaces of glass, mica
    ///    and platinum," J. Am. Chem. Soc. 40 (1918) 1361-1403.
    ///  • E. Glueckauf, "Theory of chromatography. Part 10 — formulae for
    ///    diffusion into spheres," Trans. Faraday Soc. 51 (1955) 1540-1551.
    ///  • A. Battistel et al., "Electrochemical methods for lithium recovery,"
    ///    Adv. Mater. 32 (2020) 1905440 (Li sorbent performance ranges).
    /// </summary>
    public static class DleModel
    {
        public const double MwLiGmol = 6.941;

        public struct Spec
        {
            public double QmaxMgG;
            public double KlangLmg;
            public double KldfPerS;
            public double BedVolumeL;
            public double SorbentGL;
            public double CycleTimeS;
            public double MgLiSelectivity;
        }

        public struct Perf
        {
            public double CLiMgL;          // feed Li concentration
            public double QstarMgG;        // Langmuir equilibrium loading
            public double QcycleMgG;       // after LDF approach
            public double LdfApproach;     // 1 - exp(-k t)
            public double CapturedLiMolS;
            public double RecoveryFrac;
            public double MgCapturedMolS;
            public bool CapacityLimited;   // sorbent (not feed) is the bottleneck
        }

        public static double Langmuir(double qmax, double k, double c)
        {
            return qmax * k * c / (1.0 + k * c);
        }

        public static Perf Solve(Spec s, double liFeedMolS, double mgFeedMolS, double waterFeedMolS)
        {
            var p = new Perf();
            p.CLiMgL = ProcessOps.MolarityMolL(liFeedMolS, waterFeedMolS) * MwLiGmol * 1000.0;
            p.QstarMgG = Langmuir(s.QmaxMgG, s.KlangLmg, p.CLiMgL);
            p.LdfApproach = 1.0 - Math.Exp(-Math.Max(0, s.KldfPerS) * Math.Max(0, s.CycleTimeS));
            p.QcycleMgG = p.QstarMgG * p.LdfApproach;

            double sorbentG = s.BedVolumeL * s.SorbentGL;
            double capMolPerCycle = p.QcycleMgG * sorbentG / MwLiGmol / 1000.0;
            double feedPerCycle = liFeedMolS * Math.Max(s.CycleTimeS, 1e-9);
            double captured = Math.Min(capMolPerCycle, feedPerCycle * 0.99);
            p.CapacityLimited = capMolPerCycle < feedPerCycle * 0.99;
            p.RecoveryFrac = feedPerCycle > 1e-30 ? captured / feedPerCycle : 0.0;
            p.CapturedLiMolS = p.RecoveryFrac * liFeedMolS;
            p.MgCapturedMolS = s.MgLiSelectivity > 1e-9 ? mgFeedMolS * (p.RecoveryFrac / s.MgLiSelectivity) : 0.0;
            return p;
        }
    }

    /// <summary>
    /// OP-SX engine — counter-current solvent extraction (KREMSER cascade).
    ///
    ///     E = D * (O/A)                       (extraction factor)
    ///     f = (E^(N+1) - E) / (E^(N+1) - 1)   (Kremser fraction extracted)
    ///     f = N/(N+1) exactly at E = 1
    /// times the Murphree stage efficiency. Closed-form anchors: E=2, N=3 →
    /// f = 14/15 exactly.
    ///
    /// References:
    ///  • A. Kremser, "Theoretical analysis of absorption process," Natl.
    ///    Petroleum News 22 (1930) 43-49.
    ///  • J. D. Seader, E. J. Henley, D. K. Roper, "Separation Process
    ///    Principles," 3rd ed. (2011), ch. 5.
    ///  • R. E. Treybal, "Mass-Transfer Operations," 3rd ed. (1980), ch. 10.
    /// </summary>
    public static class SxModel
    {
        public struct Spec
        {
            public double DistCoeff;
            public double Stages;          // REAL integer code
            public double OAratio;
            public double StageEffPct;
        }

        public static int StageCount(double code) { return Math.Max(1, (int)Math.Round(code)); }

        public static double KremserFraction(double e, int n)
        {
            if (Math.Abs(e - 1.0) < 1e-9) return n / (double)(n + 1);
            return (Math.Pow(e, n + 1) - e) / (Math.Pow(e, n + 1) - 1.0);
        }

        public static double ExtractedFraction(Spec s)
        {
            double e = s.DistCoeff * s.OAratio;
            return ProcessOps.Clamp01(KremserFraction(e, StageCount(s.Stages))
                                      * ProcessOps.Clamp(s.StageEffPct, 0, 100) / 100.0);
        }
    }

    /// <summary>
    /// OP-GAC engine — granular activated carbon adsorption.
    ///
    /// Equilibrium capacity at the feed concentration from the FREUNDLICH
    /// isotherm:  q0 = K_F * C0^(1/n)   [mg/g]  (q0 = K_F exactly at C0 = 1 mg/L
    /// — pinned by test). Removal over the bed from the first-order
    /// contact-time law R = 1 - exp(-EBCT / tau). Bed economics from the
    /// CARBON USAGE RATE (Crittenden):
    ///     CUR = (C0 - Ce) / q0                      [g carbon per L treated]
    ///     bed life = m_bed / (CUR * Q)
    ///
    /// References:
    ///  • H. Freundlich, "Über die Adsorption in Lösungen," Z. Phys. Chem. 57
    ///    (1906) 385-470.
    ///  • J. C. Crittenden et al. (MWH), "Water Treatment: Principles and
    ///    Design," 3rd ed. (2012), ch. 15 — CUR and GAC design.
    ///  • H. Sontheimer, J. Crittenden, S. Summers, "Activated Carbon for Water
    ///    Treatment," 2nd ed. (1988).
    /// </summary>
    public static class GacModel
    {
        public struct Spec
        {
            public double FreundlichK;     // (mg/g)(L/mg)^(1/n)
            public double FreundlichInvN;  // 1/n (0.2-0.8 typical)
            public double BedMassKg;
            public double EbctMin;
            public double TauMin;          // characteristic contact time
        }

        public struct Perf
        {
            public double RemovalFrac;
            public double LogRemoval;
            public double C0MgL, CeMgL;
            public double Q0MgG;           // Freundlich capacity at C0
            public double CurGL;           // carbon usage rate
            public double BedLifeDays;
        }

        public static double Freundlich(double k, double invN, double cMgL)
        {
            return cMgL > 0 ? k * Math.Pow(cMgL, invN) : 0.0;
        }

        public static Perf Solve(Spec s, double c0MgL, double feedM3s)
        {
            var p = new Perf();
            p.C0MgL = c0MgL;
            p.RemovalFrac = ProcessOps.Clamp01(1.0 - Math.Exp(-s.EbctMin / Math.Max(s.TauMin, 1e-9)));
            p.CeMgL = c0MgL * (1.0 - p.RemovalFrac);
            p.LogRemoval = p.RemovalFrac < 1 ? -Math.Log10(Math.Max(1e-9, 1.0 - p.RemovalFrac)) : 9;
            p.Q0MgG = Freundlich(s.FreundlichK, s.FreundlichInvN, c0MgL);
            p.CurGL = p.Q0MgG > 1e-12 ? (p.C0MgL - p.CeMgL) / p.Q0MgG : 0.0;   // g/L
            double literPerS = feedM3s * 1000.0;
            double gPerS = p.CurGL * literPerS;
            p.BedLifeDays = gPerS > 1e-12 ? s.BedMassKg * 1000.0 / gPerS / 86400.0 : 0.0;
            return p;
        }
    }

    /// <summary>
    /// OP-CRYST engine — solubility-limited crystallization yield.
    ///
    /// The mother liquor leaves SATURATED at the crystallizer temperature; the
    /// crystal make is the feed's excess over saturation (Mullin's standard
    /// yield relation, with optional solvent evaporation):
    ///     crystals [kg/s] = max(0, m_salt - S/100 * m_water * (1 - evap))
    ///     yield = crystals / m_salt
    /// where S is the solubility [g salt / 100 g water] at the operating
    /// temperature (user input — e.g. NaCl: 36.0 at 25 C, CRC Handbook). If the
    /// feed is undersaturated, nothing crystallizes (warning, not an error).
    ///
    /// References:
    ///  • J. W. Mullin, "Crystallization," 4th ed. (2001), ch. 3 — yield
    ///    calculations.
    ///  • A. S. Myerson (ed.), "Handbook of Industrial Crystallization,"
    ///    2nd ed. (2002), ch. 1.
    ///  • CRC Handbook of Chemistry and Physics, aqueous solubility tables.
    /// </summary>
    public static class CrystModel
    {
        public struct Spec
        {
            public double SolubilityG100g;  // g salt / 100 g water at T
            public double EvapFrac;         // fraction of feed water evaporated (0 = cooling cryst.)
            public double TempC;
        }

        public struct Perf
        {
            public double CrystalMolS;
            public double YieldFrac;
            public double SaturationMolS;   // salt held by the remaining water
            public double VaporWaterMolS;
            public bool Undersaturated;
        }

        public static Perf Solve(Spec s, double saltMolS, double saltMwGmol,
                                 double waterMolS, double waterMwGmol)
        {
            var p = new Perf();
            double evap = ProcessOps.Clamp01(s.EvapFrac);
            p.VaporWaterMolS = waterMolS * evap;
            double waterKgS = waterMolS * (1.0 - evap) * waterMwGmol / 1000.0;
            double saltKgS = saltMolS * saltMwGmol / 1000.0;
            double satKgS = s.SolubilityG100g / 100.0 * waterKgS;
            p.SaturationMolS = saltMwGmol > 1e-12 ? satKgS / (saltMwGmol / 1000.0) : 0.0;
            double crystKgS = Math.Max(0.0, saltKgS - satKgS);
            p.CrystalMolS = saltMwGmol > 1e-12 ? crystKgS / (saltMwGmol / 1000.0) : 0.0;
            p.YieldFrac = saltMolS > 1e-30 ? p.CrystalMolS / saltMolS : 0.0;
            p.Undersaturated = crystKgS <= 0.0;
            return p;
        }
    }

    /// <summary>
    /// OP-PPT engine — chemical precipitation, stoichiometric and reagent-limited.
    ///
    /// The reagent dose (mol reagent per mol target) sets the maximum removable:
    ///     removable_by_reagent = reagent_fed / dose
    ///     removed = min(target * removal_eff, removable_by_reagent)
    /// Consumed reagent (removed * dose) reports to the sludge with the
    /// precipitated target species; unreacted reagent stays dissolved in the
    /// treated water. Operating pH is an advisory input (each precipitate has
    /// its optimal window — e.g. hydroxide softening at pH 10.3-11).
    ///
    /// References:
    ///  • Metcalf &amp; Eddy / AECOM, "Wastewater Engineering: Treatment and
    ///    Resource Recovery," 5th ed. (2014), ch. 6 — chemical precipitation.
    ///  • J. C. Crittenden et al. (MWH), "Water Treatment," 3rd ed. (2012),
    ///    ch. 13 — precipitative softening stoichiometry.
    /// </summary>
    public static class PptModel
    {
        public struct Spec
        {
            public double RemovalPct;
            public double DoseMolMol;
            public double pH;
        }

        public struct Perf
        {
            public double RemovedMolS;
            public double ReagentConsumedMolS;
            public double ReagentFedMolS;
            public bool ReagentLimited;
            public double AchievedRemovalPct;
        }

        public static Perf Solve(Spec s, double targetMolS, double reagentMolS)
        {
            var p = new Perf();
            p.ReagentFedMolS = reagentMolS;
            double eff = ProcessOps.Clamp(s.RemovalPct, 0, 100) / 100.0;
            double want = targetMolS * eff;
            double capByReagent = s.DoseMolMol > 1e-12 ? reagentMolS / s.DoseMolMol : want;
            p.RemovedMolS = Math.Min(want, capByReagent);
            p.ReagentLimited = want > capByReagent + 1e-15;
            p.ReagentConsumedMolS = p.RemovedMolS * s.DoseMolMol;
            p.AchievedRemovalPct = targetMolS > 1e-30 ? p.RemovedMolS / targetMolS * 100.0 : 0.0;
            return p;
        }
    }
}
