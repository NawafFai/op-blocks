using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using OPBlocks.Core;

namespace OproValidation
{
    /// <summary>
    /// Generates the 9-case reference dataset for validating the OP-RO CAPE-OPEN
    /// block against OPBlocks.Core, the correctness reference (HANDOFF.md §5).
    /// The consumer is tests/UnitTests/RoCapeOpenValidationTests.cs, which replays
    /// every case through the REAL ReverseOsmosis block (ports + parameters +
    /// ThermoProxy + a mock Thermo 1.0 material object) and demands agreement
    /// within 0.1%.
    ///
    /// Osmotic pressure comes from the REAL Core code path
    /// (ProcessOps.OsmoticPressureBar, van 't Hoff fallback branch — the mock
    /// supplies no activity coefficient, matching packages that cannot). The
    /// downstream arithmetic mirrors ReverseOsmosis.Compute line by line
    /// (FamilyB_Membranes.cs) — keep the two in lock-step.
    ///
    /// Unit convention: cases are stated in kmol/s; the replay feeds the block
    /// mol/s (CAPE-OPEN SI) by scaling x1000, and split flows are written back
    /// /1000. QPERM/PUMPKW/SEC/TDS are computed in the mol/s world. The mock
    /// (and this generator) use rho = 1000 kg/m3 for feed and permeate; a live
    /// host supplies real package densities instead.
    ///
    /// Output format v2 (tests/OproValidation/cases.txt), free-format reals:
    ///   line 1: NCASES
    ///   per case:
    ///     name
    ///     NC KH2O(1-based, 0=absent) TK
    ///     MW(1..NC)           g/mol
    ///     AREA PERMA SREJ APPRES VANTI PEFF
    ///     F(1..NC)            feed, kmol/s
    ///     PIBAR RECOV JW QPERM PUMPKW SEC TDSPERM TDSCONC SREJOBS
    ///     FPERM(1..NC)        kmol/s
    ///     FCONC(1..NC)        kmol/s
    /// </summary>
    internal static class Program
    {
        private sealed class Case
        {
            public string Name;
            public double[] FeedKmol;   // kmol/s
            public double[] MwGmol;     // g/mol per component
            public int WaterIdx;        // 0-based, -1 = absent
            public double TK = 298.15;
            public double Area = 40, PermA = 1.0, SaltRej = 99.0,
                          AppPres = 55, VantHoff = 2.0, PumpEff = 80;
        }

        private const double MwWater = 18.0153, MwNaCl = 58.443, MwKCl = 74.551, MwMgCl2 = 95.211;

        private static int Main(string[] args)
        {
            var cases = new List<Case>
            {
                new Case { Name = "dilute-nacl-defaults",
                    FeedKmol = new[] { 0.99, 0.01 }, MwGmol = new[] { MwWater, MwNaCl }, WaterIdx = 0 },
                new Case { Name = "seawater-no-permeation",
                    FeedKmol = new[] { 0.95, 0.03, 0.02 }, MwGmol = new[] { MwWater, MwNaCl, MwMgCl2 }, WaterIdx = 0 },
                new Case { Name = "brackish",
                    FeedKmol = new[] { 0.998, 0.001, 0.001 }, MwGmol = new[] { MwWater, MwNaCl, MwKCl }, WaterIdx = 0 },
                new Case { Name = "pure-water-cap95",
                    FeedKmol = new[] { 1.0 }, MwGmol = new[] { MwWater }, WaterIdx = 0 },
                new Case { Name = "hot-feed-348K",
                    FeedKmol = new[] { 0.99, 0.01 }, MwGmol = new[] { MwWater, MwNaCl }, WaterIdx = 0, TK = 348.15 },
                new Case { Name = "custom-params",
                    FeedKmol = new[] { 0.005, 0.985, 0.01 }, MwGmol = new[] { MwNaCl, MwWater, MwKCl }, WaterIdx = 1,
                    Area = 500, PermA = 3.0, SaltRej = 99.9, AppPres = 70,
                    VantHoff = 1.8, PumpEff = 65 },
                new Case { Name = "no-water-clamp",
                    FeedKmol = new[] { 0.5, 0.5 }, MwGmol = new[] { MwNaCl, MwMgCl2 }, WaterIdx = -1 },
                new Case { Name = "zero-feed",
                    FeedKmol = new[] { 0.0, 0.0 }, MwGmol = new[] { MwWater, MwNaCl }, WaterIdx = 0 },
                new Case { Name = "trace-water-clamp",
                    FeedKmol = new[] { 1e-14, 1.0 }, MwGmol = new[] { MwWater, MwNaCl }, WaterIdx = 0 },
            };

            var ci = CultureInfo.InvariantCulture;
            string outPath = args.Length > 0 ? args[0] : "cases.txt";
            using (var w = new StreamWriter(outPath))
            {
                w.WriteLine(cases.Count.ToString(ci));
                foreach (var c in cases)
                {
                    int n = c.FeedKmol.Length;
                    // Core reference runs in mol/s
                    var fMol = new double[n];
                    for (int i = 0; i < n; i++) fMol[i] = c.FeedKmol[i] * 1000.0;

                    // --- pi from the real Core routine (fallback branch) ---
                    var notes = new List<string>();
                    double piBar = ProcessOps.OsmoticPressureBar(
                        null, fMol, c.WaterIdx, c.VantHoff, c.TK, notes);

                    // --- mirror of ReverseOsmosis.Compute (keep in lock-step) ---
                    double fw = c.WaterIdx >= 0 ? fMol[c.WaterIdx] : 0.0;
                    double ndp = Math.Max(0, c.AppPres - piBar);
                    double Jw = c.PermA * ndp;
                    double permWaterMol = Math.Min(
                        Jw * c.Area / 3600.0 / 0.0180153, fw * 0.95);
                    double recovery = fw > 0 ? permWaterMol / fw : 0;
                    double saltPass = 1.0 - c.SaltRej / 100.0;
                    var perm = new double[n];
                    var conc = new double[n];
                    for (int i = 0; i < n; i++)
                    {
                        double frac = ProcessOps.Clamp01(
                            i == c.WaterIdx ? recovery : saltPass * recovery);
                        perm[i] = fMol[i] * frac;
                        conc[i] = fMol[i] - perm[i];
                    }

                    // mass-based results (MW in g/mol; rho = 1000 kg/m3 in this
                    // package-independent reference, matching the mock)
                    double feedKgS = 0, permKgS = 0, concKgS = 0,
                           feedSaltKgS = 0, permSaltKgS = 0, concSaltKgS = 0;
                    for (int i = 0; i < n; i++)
                    {
                        double kgMol = c.MwGmol[i] / 1000.0;
                        feedKgS += fMol[i] * kgMol;
                        permKgS += perm[i] * kgMol;
                        concKgS += conc[i] * kgMol;
                        if (i != c.WaterIdx)
                        {
                            feedSaltKgS += fMol[i] * kgMol;
                            permSaltKgS += perm[i] * kgMol;
                            concSaltKgS += conc[i] * kgMol;
                        }
                    }
                    const double rho = 1000.0;
                    double permM3h = permKgS > 1e-30 ? permKgS / rho * 3600.0 : 0.0;
                    double feedM3s = feedKgS > 1e-30 ? feedKgS / rho : 0.0;
                    double pumpPa = Math.Max(0, c.AppPres * 1e5 - 101325.0);
                    double pumpKW = c.PumpEff > 0
                        ? pumpPa * feedM3s / (c.PumpEff / 100.0) / 1000.0 : 0.0;
                    double sec = permM3h > 1e-12 ? pumpKW / permM3h : 0.0;
                    double tdsPerm = permKgS > 1e-30 ? permSaltKgS / permKgS * 1e6 : 0.0;
                    double tdsConc = concKgS > 1e-30 ? concSaltKgS / concKgS * 1e6 : 0.0;
                    double tdsFeed = feedKgS > 1e-30 ? feedSaltKgS / feedKgS * 1e6 : 0.0;
                    double rejObs = tdsFeed > 1e-12 ? (1.0 - tdsPerm / tdsFeed) * 100.0 : 0.0;

                    w.WriteLine(c.Name);
                    w.WriteLine(string.Format(ci, "{0} {1} {2:R}",
                        n, c.WaterIdx + 1, c.TK));
                    w.WriteLine(Join(ci, c.MwGmol, 1.0));
                    w.WriteLine(string.Format(ci,
                        "{0:R} {1:R} {2:R} {3:R} {4:R} {5:R}",
                        c.Area, c.PermA, c.SaltRej, c.AppPres,
                        c.VantHoff, c.PumpEff));
                    w.WriteLine(Join(ci, c.FeedKmol, 1.0));
                    w.WriteLine(string.Format(ci,
                        "{0:R} {1:R} {2:R} {3:R} {4:R} {5:R} {6:R} {7:R} {8:R}",
                        piBar, recovery, Jw, permM3h, pumpKW, sec, tdsPerm, tdsConc, rejObs));
                    w.WriteLine(Join(ci, perm, 1e-3));   // mol/s -> kmol/s
                    w.WriteLine(Join(ci, conc, 1e-3));
                }
            }
            Console.WriteLine("wrote " + outPath + " (" + cases.Count + " cases)");
            return 0;
        }

        private static string Join(CultureInfo ci, double[] v, double scale)
        {
            var parts = new string[v.Length];
            for (int i = 0; i < v.Length; i++)
                parts[i] = (v[i] * scale).ToString("R", ci);
            return string.Join(" ", parts);
        }
    }
}
