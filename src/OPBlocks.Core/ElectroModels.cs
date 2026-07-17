using System;

namespace OPBlocks.Core
{
    // =====================================================================
    //  Family C engines — electrochemical separations. Pure physics.
    // =====================================================================

    /// <summary>
    /// OP-EDI engine — electrodeionization (electrodialysis + mixed-bed resin).
    ///
    /// The resin bed keeps conductivity in the dilute channel even at ultrapure
    /// levels, so EDI polishes to ppb — but the ion TRANSPORT is still Faradaic:
    ///     N_max = eta_i * I * N_cp / (z * F)        (mol/s of transferable ions)
    ///     I     = V / R_stack                       (Ohmic stack)
    /// The achieved removal is min(target removal, Faradaic capability). Excess
    /// current beyond the ion load splits water for resin regeneration — that is
    /// EDI's continuous-regeneration mechanism (Ganzi), reported as the
    /// water-splitting fraction. SEC on the dilute product volume.
    ///
    /// Validity: polishing duty (feed &lt; ~50 ppm after RO), removal 90-99.99 %.
    /// References:
    ///  • G. C. Ganzi, Y. Egozy, A. J. Giuffrida, A. D. Jha, "High purity water
    ///    by electrodeionization," Ultrapure Water 4 (1987) 43-50.
    ///  • J. Wood, J. Gifford, J. Arba, M. Shaw, "Production of ultrapure water
    ///    by continuous electrodeionization," Desalination 250 (2010) 973-976.
    ///  • H. Strathmann, "Ion-Exchange Membrane Separation Processes" (2004), ch. 6.
    /// </summary>
    public static class EdiModel
    {
        public const double WaterMwKgMol = 0.0180153;

        public struct Spec
        {
            public double CellPairs;        // REAL integer code
            public double VoltageV;
            public double ResistanceOhm;
            public double TargetRemovalPct;
            public double CurrentEffPct;
            public double IonValence;       // REAL integer code (z)
        }

        public struct Perf
        {
            public double CurrentA;
            public double FaradaicCapMolS;  // max transferable ion flow
            public double RemovedMolS;
            public double RemovalPct;       // achieved
            public bool CurrentLimited;
            public double WaterSplitFrac;   // share of current splitting water
            public double PowerKW;
            public double SecKWhM3;         // per m3 dilute product
            public double[] DiluteOutMol, ConcOutMol;
        }

        public static int Pairs(double code) { return Math.Max(1, (int)Math.Round(code)); }
        public static int Valence(double code) { return Math.Max(1, (int)Math.Round(code)); }

        public static Perf Solve(Spec s, double[] dilute, double[] concentrate, int wi, double diluteM3s)
        {
            var p = new Perf();
            int n = dilute.Length;
            p.CurrentA = s.ResistanceOhm > 1e-12 ? s.VoltageV / s.ResistanceOhm : 0.0;
            double eff = ProcessOps.Clamp(s.CurrentEffPct, 0, 100) / 100.0;
            int z = Valence(s.IonValence);
            p.FaradaicCapMolS = ProcessOps.FaradayMoles(p.CurrentA, z, eff) * Pairs(s.CellPairs);

            double ionLoad = 0.0;
            for (int i = 0; i < n; i++) if (i != wi) ionLoad += dilute[i];
            double want = ionLoad * ProcessOps.Clamp(s.TargetRemovalPct, 0, 100) / 100.0;
            p.RemovedMolS = Math.Min(want, p.FaradaicCapMolS);
            p.CurrentLimited = want > p.FaradaicCapMolS + 1e-15;
            p.RemovalPct = ionLoad > 1e-30 ? p.RemovedMolS / ionLoad * 100.0 : 0.0;
            p.WaterSplitFrac = p.FaradaicCapMolS > 1e-30
                ? Math.Max(0.0, 1.0 - p.RemovedMolS / p.FaradaicCapMolS) : 0.0;

            p.DiluteOutMol = (double[])dilute.Clone();
            p.ConcOutMol = (double[])concentrate.Clone();
            if (ionLoad > 1e-30)
            {
                double frac = p.RemovedMolS / ionLoad;
                for (int i = 0; i < n; i++)
                {
                    if (i == wi) continue;
                    double m = dilute[i] * frac;
                    p.DiluteOutMol[i] -= m;
                    p.ConcOutMol[i] += m;
                }
            }

            p.PowerKW = s.VoltageV * p.CurrentA / 1000.0;
            double m3h = diluteM3s * 3600.0;
            p.SecKWhM3 = m3h > 1e-12 ? p.PowerKW / m3h : 0.0;
            return p;
        }
    }

    /// <summary>
    /// OP-CDI engine — capacitive deionization, cycle-averaged.
    ///
    /// Ions electro-sorb into the double layers of porous carbon electrodes.
    /// Two independent limits govern a cycle:
    ///  • CAPACITY: salt adsorption capacity SAC [mg salt / g electrode]
    ///        N_cap = SAC * m_elec / (MW_salt * t_cycle)      [mol/s averaged]
    ///  • CHARGE: the charge efficiency Lambda ties salt to charge
    ///        N_salt = Lambda * Q / F   →   Q = N_salt * F / Lambda
    ///    and the electrical energy is E = Q * V_cell (charging energy; no
    ///    recovery assumed — conservative).
    /// SEC on product volume; published brackish CDI: 0.1-1 kWh/m3.
    ///
    /// References:
    ///  • S. Porada, R. Zhao, A. van der Wal, V. Presser, P. M. Biesheuvel,
    ///    "Review on the science and technology of water desalination by
    ///    capacitive deionization," Prog. Mater. Sci. 58 (2013) 1388-1442.
    ///  • M. E. Suss et al., "Water desalination via capacitive deionization,"
    ///    Energy Environ. Sci. 8 (2015) 2296-2319.
    /// </summary>
    public static class CdiModel
    {
        public const double WaterMwKgMol = 0.0180153;

        public struct Spec
        {
            public double SacMgG;
            public double ElectrodeKg;
            public double CycleTimeS;
            public double ChargeEffPct;    // Lambda
            public double CellVoltageV;
            public double WaterRecoveryPct;
            public double SaltMwGmol;      // MW used for the SAC capacity (NaCl default)
        }

        public struct Perf
        {
            public double CapacityMolS;    // SAC-limited average removal
            public double RemovedMolS;     // achieved (min of capacity, feed salt)
            public double RemovalPct;
            public bool FeedLimited;
            public double ChargeA;         // average current equivalent
            public double PowerKW;
            public double SecKWhM3;
            public double[] ProductMol, WasteMol;
        }

        public static Perf Solve(Spec s, double[] feed, int wi, double productM3s)
        {
            var p = new Perf();
            int n = feed.Length;
            double saltFeed = 0.0;
            for (int i = 0; i < n; i++) if (i != wi) saltFeed += feed[i];

            p.CapacityMolS = s.SacMgG * (s.ElectrodeKg * 1000.0)
                             / Math.Max(s.SaltMwGmol, 1e-9) / 1000.0 / Math.Max(s.CycleTimeS, 1e-9);
            p.RemovedMolS = Math.Min(p.CapacityMolS, saltFeed * 0.999);
            p.FeedLimited = saltFeed * 0.999 < p.CapacityMolS;
            p.RemovalPct = saltFeed > 1e-30 ? p.RemovedMolS / saltFeed * 100.0 : 0.0;

            double lambda = ProcessOps.Clamp(s.ChargeEffPct, 1e-6, 100) / 100.0;
            p.ChargeA = p.RemovedMolS * ProcessOps.Faraday / lambda;      // A (C/s)
            p.PowerKW = p.ChargeA * s.CellVoltageV / 1000.0;

            double rec = ProcessOps.Clamp(s.WaterRecoveryPct, 0, 100) / 100.0;
            double saltFracToProduct = saltFeed > 1e-30 ? (saltFeed - p.RemovedMolS) / saltFeed : 0.0;
            p.ProductMol = new double[n];
            p.WasteMol = new double[n];
            var frac = new double[n];
            for (int i = 0; i < n; i++) frac[i] = (i == wi) ? rec : saltFracToProduct * rec + 0.0;
            // salt not removed splits with the water; the REMOVED salt reports to the waste (regeneration)
            for (int i = 0; i < n; i++)
            {
                if (i == wi) { p.ProductMol[i] = feed[i] * rec; p.WasteMol[i] = feed[i] * (1 - rec); }
                else
                {
                    double kept = feed[i] * (saltFeed > 1e-30 ? (1.0 - p.RemovedMolS / saltFeed) : 1.0);
                    p.ProductMol[i] = kept * rec;
                    p.WasteMol[i] = feed[i] - p.ProductMol[i];
                }
            }

            double m3h = productM3s * 3600.0;
            p.SecKWhM3 = m3h > 1e-12 ? p.PowerKW / m3h : 0.0;
            return p;
        }
    }

    /// <summary>
    /// OP-CHLORALK engine — chlor-alkali membrane cell (brine electrolysis).
    ///
    /// Anode: 2 Cl- → Cl2 + 2 e-;  cathode: 2 H2O + 2 e- → H2 + 2 OH-;
    /// Na+ crosses the cation membrane dragging water. Faradaic production:
    ///     N_Cl2 = eta * I / (2 F);   N_H2 = N_Cl2;   N_NaOH = 2 N_Cl2
    /// consuming 2 NaCl and (at the cathode) 2 H2O per Cl2. Production is capped
    /// by the NaCl actually fed (brine-limited warning). Specific energy at
    /// 3.1 V / 96 %: ~2.44 kWh/kg Cl2 — the published membrane-cell band
    /// (2.3-2.7 kWh/kg Cl2, i.e. ~2100-2400 kWh/t NaOH).
    ///
    /// References:
    ///  • T. F. O'Brien, T. V. Bommaraju, F. Hine, "Handbook of Chlor-Alkali
    ///    Technology" (2005), vol. I ch. 2 (electrochemistry, energy).
    ///  • P. Schmittinger (ed.), "Chlorine: Principles and Industrial Practice"
    ///    (2000).
    /// </summary>
    public static class ChlorAlkaliModel
    {
        public const double MwCl2 = 0.070906, MwNaOH = 0.039997, MwH2 = 0.00201588, MwH2O = 0.0180153;

        public struct Spec
        {
            public double CurrentA;
            public double CurrentEffPct;
            public double CellVoltageV;
            public double WaterTransport;   // mol H2O dragged per mol Na+
        }

        public struct Perf
        {
            public double Cl2MolS, H2MolS, NaOHMolS, NaClConsMolS;
            public double WaterToCatholyteMolS;   // drag + cathode consumption source
            public double FaradaicCl2MolS;
            public bool BrineLimited;
            public double PowerKW;
            public double SecKWhKgCl2;
        }

        public static Perf Solve(Spec s, double naclFeedMolS, double waterFeedMolS)
        {
            var p = new Perf();
            double eff = ProcessOps.Clamp(s.CurrentEffPct, 0, 100) / 100.0;
            p.FaradaicCl2MolS = ProcessOps.FaradayMoles(s.CurrentA, 2, eff);
            p.Cl2MolS = Math.Min(p.FaradaicCl2MolS, Math.Max(0, naclFeedMolS) * 0.99 / 2.0);
            p.BrineLimited = p.Cl2MolS < p.FaradaicCl2MolS * 0.999;
            p.H2MolS = p.Cl2MolS;
            p.NaOHMolS = 2.0 * p.Cl2MolS;
            p.NaClConsMolS = 2.0 * p.Cl2MolS;
            // water leaving the anolyte: electro-osmotic drag with Na+ plus cathode consumption
            double drag = p.NaOHMolS * Math.Max(0, s.WaterTransport);
            double cathode = p.H2MolS;                       // 2 H2O per H2, but 2 OH- return as NaOH solution water — net 1
            p.WaterToCatholyteMolS = Math.Min(drag + cathode, Math.Max(0, waterFeedMolS) * 0.9);
            p.PowerKW = s.CellVoltageV * s.CurrentA / 1000.0;
            double cl2KgS = p.Cl2MolS * MwCl2;
            p.SecKWhKgCl2 = cl2KgS > 1e-15 ? p.PowerKW / (cl2KgS * 3600.0) : 0.0;
            return p;
        }
    }

    /// <summary>
    /// OP-IX engine — fixed-bed ion exchange (softening / selective capture).
    ///
    /// Equivalents-based service model: the bed holds Capacity [eq/L] * Volume,
    /// and the TARGET ions (hardness: multivalent Ca2+/Mg2+ at z=2) load it at
    ///     load [eq/s] = sum(target mol/s) * z * removal
    /// so the service time between regenerations is t = capacity / load, also
    /// expressed as bed volumes treated. Monovalent background ions pass — that
    /// is the selectivity order of strong-acid resins (Ca2+ &gt; Mg2+ &gt;&gt; Na+).
    /// Removed ions leave with the spent regenerant (mass balance across all
    /// four streams).
    ///
    /// References:
    ///  • F. Helfferich, "Ion Exchange," McGraw-Hill (1962) — equilibria and
    ///    selectivity order.
    ///  • J. C. Crittenden et al. (MWH), "Water Treatment: Principles and
    ///    Design," 3rd ed. (2012), ch. 16 — softening service-cycle design.
    /// </summary>
    public static class IxModel
    {
        public struct Spec
        {
            public double ResinVolumeL;
            public double CapacityEqL;
            public double RemovalPct;
            public double TargetValence;    // REAL integer code (2 = hardness)
        }

        public struct Perf
        {
            public double RemovedMolS;      // target ions captured
            public double LoadEqS;
            public double BedCapacityEq;
            public double ServiceHours;     // capacity / load
            public double BedVolumesTreated;// throughput per service run (approx: Q_feed*t/V)
            public double[] TreatedMol, SpentMol;
        }

        public static int Valence(double code) { return Math.Max(1, (int)Math.Round(code)); }

        public static Perf Solve(Spec s, double[] feed, double[] regen, int wi, bool[] isTarget,
                                 double feedM3s)
        {
            var p = new Perf();
            int n = feed.Length;
            double eff = ProcessOps.Clamp(s.RemovalPct, 0, 100) / 100.0;
            int z = Valence(s.TargetValence);

            p.TreatedMol = (double[])feed.Clone();
            p.SpentMol = (double[])regen.Clone();
            for (int i = 0; i < n; i++)
            {
                if (i == wi || isTarget == null || i >= isTarget.Length || !isTarget[i]) continue;
                double m = feed[i] * eff;
                p.TreatedMol[i] -= m;
                if (i < p.SpentMol.Length) p.SpentMol[i] += m;
                p.RemovedMolS += m;
            }
            p.LoadEqS = p.RemovedMolS * z;
            p.BedCapacityEq = s.ResinVolumeL * s.CapacityEqL;
            p.ServiceHours = p.LoadEqS > 1e-30 ? p.BedCapacityEq / p.LoadEqS / 3600.0 : 0.0;
            double bedM3 = s.ResinVolumeL / 1000.0;
            p.BedVolumesTreated = (bedM3 > 1e-12 && p.LoadEqS > 1e-30)
                ? feedM3s * (p.BedCapacityEq / p.LoadEqS) / bedM3 : 0.0;
            return p;
        }
    }
}
