using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CapeOpen;
using OPBlocks.Core;
using OPBlocks.Desalination;
using Xunit;

namespace OPBlocks.UnitTests
{
    /// <summary>
    /// OP-NF validation gate, structured exactly like the OP-RO gate. Two layers:
    ///
    ///  1. STRUCTURAL — canonical cases replayed through the REAL CAPE-OPEN block
    ///     (ports, ICapeParameter, Calculate() behind the boundary guard,
    ///     ThermoProxy against Thermo 1.0 AND 1.1 mocks, outlet writing, result
    ///     read-back) must match the shared <see cref="NfModel"/> reference within
    ///     0.1%. Block and reference share NfModel, so agreement proves the
    ///     CAPE-OPEN wiring, not the physics.
    ///
    ///  2. PHYSICAL — independent hand-reasoned checks pin the physics itself:
    ///     brackish-water osmotic pressure vs the van 't Hoff textbook value, NF
    ///     selectivity (multivalent ions permeate far less than monovalent), the
    ///     reflection-coefficient effect, exact mass balance.
    ///
    /// Plus determinism (20 runs, 1e-8), results==streams exactness, and the
    /// RealParameter-only / ports / Model&amp;References structural guarantees.
    /// </summary>
    public class NfCapeOpenValidationTests
    {
        private const double RelTol = 1e-3;    // 0.1% structural gate
        private const double AbsFloor = 1e-12;

        private const double MwWater = 18.0153, MwNaCl = 58.442467, MwMgSO4 = 120.366, MwKCl = 74.551;

        public sealed class NfCase
        {
            public string Name;
            public NfModel.Mode Mode = NfModel.Mode.Rating;
            public double Area = 40, PermA = 8.0, MultiRej = 97, MonoRej = 50, Applied = 10,
                          Sigma = 0.95, VantHoff = 2.0, PumpEff = 80, MaxRec = 80, TargetRec = 75, DesignFlux = 40;
            public double[] FeedMol;
            public double[] Mw;
            public string[] Ids;
            public int Wi;
            public double Tk = 298.15;
            public override string ToString() { return Name; }

            public NfModel.Spec Spec()
            {
                return new NfModel.Spec
                {
                    CalcMode = Mode, AreaM2 = Area, WaterPermA = PermA, ReflectionSigma = Sigma,
                    AppliedBar = Applied, VantHoffI = VantHoff, PumpEffPct = PumpEff,
                    MaxRecoveryPct = MaxRec, TargetRecoveryPct = TargetRec, DesignFluxLMH = DesignFlux,
                };
            }

            /// <summary>Per-component passage the block builds from the two rejections.</summary>
            public double[] SaltPass()
            {
                var sp = new double[FeedMol.Length];
                double multiPass = 1.0 - MultiRej / 100.0;
                double monoPass = 1.0 - MonoRej / 100.0;
                for (int i = 0; i < sp.Length; i++)
                    sp[i] = (i == Wi) ? 0.0 : (NfModel.IsMultivalent(Ids[i]) ? multiPass : monoPass);
                return sp;
            }
        }

        // ~3000 ppm NaCl brackish feed (mono-only); ~2.5 bar osmotic (0.8 bar/1000 ppm rule).
        private static NfCase Brackish() => new NfCase
        {
            Name = "brackish-nacl", FeedMol = new[] { 55.0, 0.05 },
            Ids = new[] { "WATER", "NACL" }, Mw = new[] { MwWater, MwNaCl }, Wi = 0
        };

        public static IReadOnlyList<NfCase> Cases()
        {
            return new List<NfCase>
            {
                Brackish(),
                new NfCase { Name = "brackish-multisalt", FeedMol = new[]{55.0, 0.05, 0.02},
                             Ids = new[]{"WATER","NACL","MGSO4"}, Mw = new[]{MwWater,MwNaCl,MwMgSO4}, Wi = 0 },
                new NfCase { Name = "design-75pct", FeedMol = new[]{55.0, 0.05},
                             Ids = new[]{"WATER","NACL"}, Mw = new[]{MwWater,MwNaCl}, Wi = 0,
                             Mode = NfModel.Mode.Design, TargetRec = 75, DesignFlux = 40 },
                new NfCase { Name = "oversized-area-capped", FeedMol = new[]{55.0, 0.05},
                             Ids = new[]{"WATER","NACL"}, Mw = new[]{MwWater,MwNaCl}, Wi = 0,
                             Area = 600, PermA = 15, Applied = 20 },
                new NfCase { Name = "no-permeation", FeedMol = new[]{55.0, 0.5},
                             Ids = new[]{"WATER","NACL"}, Mw = new[]{MwWater,MwNaCl}, Wi = 0, Applied = 8 },
            };
        }

        public static IEnumerable<object[]> CaseNames()
        {
            foreach (NfCase c in Cases())
            {
                yield return new object[] { c.Name, false }; // Thermo 1.0
                yield return new object[] { c.Name, true };  // Thermo 1.1
            }
        }

        // ------------------------------------------------------------------
        //  reference (shared NfModel; ρ = 1000 volumes as the mocks report)
        // ------------------------------------------------------------------
        private static double PiFeed(NfCase c) =>
            ProcessOps.OsmoticPressureBar(null, c.FeedMol, c.Wi, c.VantHoff, c.Tk, null);

        private static double MassKgS(double[] mol, double[] mw)
        {
            double kg = 0;
            for (int i = 0; i < mol.Length; i++) kg += mol[i] * mw[i] / 1000.0;
            return kg;
        }

        private static (NfModel.Split, NfModel.Energy) Reference(NfCase c)
        {
            NfModel.Spec s = c.Spec();
            NfModel.Split split = NfModel.Solve(s, c.FeedMol, c.Wi, c.SaltPass(), c.Mw, c.Tk, PiFeed(c));
            double feedM3s = MassKgS(c.FeedMol, c.Mw) / 1000.0;
            double permM3s = ProcessOps.Sum(split.PermMol) > 1e-30 ? MassKgS(split.PermMol, c.Mw) / 1000.0 : 0.0;
            NfModel.Energy e = NfModel.CalcEnergy(s, split.AppliedBarUsed, feedM3s, permM3s);
            return (split, e);
        }

        // ------------------------------------------------------------------
        //  block rig
        // ------------------------------------------------------------------
        private sealed class Rig { public Nanofiltration Block; public IMockMaterial Feed, Perm, Conc; }

        private static object NewMock(bool t11, string[] ids, double[] mw, double[] flows = null)
        {
            if (t11) return new Mock11MaterialObject(ids, mw, 1000.0, flows, 298.15, 101325.0);
            return new MockMaterialObject(ids, mw, 1000.0, flows, 298.15, 101325.0);
        }

        private static Rig BuildRig(NfCase c, bool t11)
        {
            object feed = NewMock(t11, c.Ids, c.Mw, (double[])c.FeedMol.Clone());
            object perm = NewMock(t11, c.Ids, c.Mw);
            object conc = NewMock(t11, c.Ids, c.Mw);
            var block = new Nanofiltration();
            Set(block, "CalcMode", c.Mode == NfModel.Mode.Design ? 1 : 0);
            Set(block, "Area", c.Area); Set(block, "WaterPermA", c.PermA);
            Set(block, "MultivalRejection", c.MultiRej); Set(block, "MonovalRejection", c.MonoRej);
            Set(block, "AppliedPressure", c.Applied); Set(block, "ReflectionSigma", c.Sigma);
            Set(block, "VantHoffI", c.VantHoff); Set(block, "PumpEff", c.PumpEff);
            Set(block, "MaxRecovery", c.MaxRec); Set(block, "TargetRecovery", c.TargetRec);
            Set(block, "DesignFlux", c.DesignFlux);
            Connect(block, "Feed", feed); Connect(block, "Permeate", perm); Connect(block, "Concentrate", conc);
            return new Rig { Block = block, Feed = (IMockMaterial)feed, Perm = (IMockMaterial)perm, Conc = (IMockMaterial)conc };
        }

        private static void Set(CapeUnitBase block, string param, double value)
        {
            foreach (CapeParameter p in block.Parameters)
                if (string.Equals(p.ComponentName, param, StringComparison.OrdinalIgnoreCase))
                { ((ICapeParameter)p).value = value; return; }
            throw new InvalidOperationException("parameter not found: " + param);
        }

        private static void Connect(CapeUnitBase block, string portName, object material)
        {
            foreach (UnitPort p in block.Ports)
                if (string.Equals(p.ComponentName, portName, StringComparison.OrdinalIgnoreCase))
                { p.Connect(material); return; }
            throw new InvalidOperationException("port not found: " + portName);
        }

        private static double ResultOf(UnitBase block, string label)
        {
            UnitBase.ResultEntry row = block.GetResults().FirstOrDefault(r => r.Label == label);
            Assert.True(row != null, "missing result row: " + label);
            return row.Value;
        }

        private static double OutParam(Nanofiltration block, string name)
        {
            foreach (CapeParameter p in block.Parameters)
                if (!UnitBase.IsInputParameter(p) && string.Equals(p.ComponentName, name, StringComparison.OrdinalIgnoreCase))
                    return Convert.ToDouble(((ICapeParameter)p).value, CultureInfo.InvariantCulture);
            throw new InvalidOperationException("output parameter not found: " + name);
        }

        private static void Close(double expected, double actual, string what)
        {
            double tol = Math.Max(AbsFloor, Math.Abs(expected) * RelTol);
            Assert.True(Math.Abs(actual - expected) <= tol,
                what + ": expected " + expected.ToString("R") + ", got " + actual.ToString("R"));
        }

        private static void AssertExact(double expected, double actual, string what)
        {
            Assert.True(Math.Abs(actual - expected) <= 1e-10 * Math.Max(1.0, Math.Abs(expected)),
                what + ": " + expected.ToString("R") + " vs " + actual.ToString("R"));
        }

        // ------------------------------------------------------------------
        //  1. STRUCTURAL — block == NfModel within 0.1%, both thermo backends
        // ------------------------------------------------------------------
        [Theory]
        [MemberData(nameof(CaseNames))]
        public void Case_BlockMatchesReference_Within01Percent(string name, bool thermo11)
        {
            NfCase c = Cases().First(x => x.Name == name);
            (NfModel.Split split, NfModel.Energy e) = Reference(c);
            Rig rig = BuildRig(c, thermo11);
            rig.Block.Calculate();

            Close(split.Recovery * 100.0, ResultOf(rig.Block, "Water recovery"), "recovery");
            Close(split.FluxLMH, ResultOf(rig.Block, "Permeate flux"), "flux");
            Close(split.PiFeedBar, ResultOf(rig.Block, "Feed osmotic pressure"), "piFeed");
            Close(split.PiAvgBar, ResultOf(rig.Block, "Average osmotic pressure"), "piAvg");
            Close(split.NdpBar, ResultOf(rig.Block, "Net driving pressure"), "ndp");
            Close(split.TdsPermPpm, ResultOf(rig.Block, "Permeate TDS"), "permTDS");
            Close(split.TdsConcPpm, ResultOf(rig.Block, "Concentrate TDS"), "concTDS");
            Close(split.SaltRejObsPct, ResultOf(rig.Block, "Salt rejection (observed)"), "rejObs");
            Close(e.PumpKW, ResultOf(rig.Block, "Pump power"), "pump");
            Close(e.SEC, ResultOf(rig.Block, "Specific energy (SEC)"), "SEC");
            if (c.Mode == NfModel.Mode.Design)
            {
                Close(split.RequiredAreaM2, ResultOf(rig.Block, "Required membrane area"), "reqArea");
                Close(split.RequiredPressureBar, ResultOf(rig.Block, "Required applied pressure"), "reqPressure");
            }

            for (int i = 0; i < c.FeedMol.Length; i++)
            {
                double pm = rig.Perm.Flows == null ? 0.0 : rig.Perm.Flows[i];
                double cm = rig.Conc.Flows == null ? 0.0 : rig.Conc.Flows[i];
                Close(split.PermMol[i], pm, "permMol[" + i + "]");
                Close(split.ConcMol[i], cm, "concMol[" + i + "]");
                Assert.True(Math.Abs(c.FeedMol[i] - pm - cm) <= 1e-9 * Math.Max(1.0, c.FeedMol[i]),
                    "mass balance component " + i);
            }
        }

        // ------------------------------------------------------------------
        //  2. PHYSICAL — independent hand-reasoned checks
        // ------------------------------------------------------------------
        [Fact]
        public void Brackish_OsmoticPressure_IsTextbook()
        {
            // ~3000 ppm NaCl → van 't Hoff osmotic pressure ≈ 2.4–2.6 bar
            // (the ≈0.8 bar per 1000 ppm NaCl engineering rule of thumb).
            double pi = PiFeed(Brackish());
            Assert.InRange(pi, 2.0, 3.0);
        }

        [Fact]
        public void NF_RunsAtMuchLowerPressureThanRO_StillPermeates()
        {
            var c = Brackish();
            (NfModel.Split split, _) = Reference(c);
            Assert.True(c.Applied <= 15.0, "NF is a low-pressure process (default 10 bar)");
            Assert.True(split.NdpBar > 0, "positive net driving pressure at NF pressure");
            Assert.True(split.Recovery > 0, "water permeates");
        }

        [Fact]
        public void Multivalent_IsRejectedMoreThanMonovalent()
        {
            var c = Cases().First(x => x.Name == "brackish-multisalt");
            (NfModel.Split split, _) = Reference(c);
            int naCl = 1, mgSo4 = 2; // per the ids/order above
            double passNaCl = split.PermMol[naCl] / c.FeedMol[naCl];   // fraction of NaCl that permeated
            double passMgSo4 = split.PermMol[mgSo4] / c.FeedMol[mgSo4];
            Assert.True(passMgSo4 < passNaCl,
                "multivalent MgSO4 must permeate less than monovalent NaCl (NF selectivity)");
            // and MgSO4 rejection near the configured 97%
            double rejMgSo4 = (1.0 - passMgSo4 / split.Recovery) * 100.0; // observed on that ion
            Assert.InRange(rejMgSo4, 95.0, 99.0);
        }

        [Fact]
        public void ReflectionCoefficient_BelowOne_RaisesNetDrivingPressure()
        {
            var full = Brackish(); full.Sigma = 1.0;       // RO limit — full osmotic barrier
            var leaky = Brackish(); leaky.Sigma = 0.85;    // leaky NF — reduced barrier
            (NfModel.Split sFull, _) = Reference(full);
            (NfModel.Split sLeaky, _) = Reference(leaky);
            Assert.True(sLeaky.NdpBar > sFull.NdpBar,
                "a lower reflection coefficient reduces the osmotic barrier, raising NDP");
        }

        [Fact]
        public void OversizedArea_CapsAtMaxRecovery_WithWarning()
        {
            var c = Cases().First(x => x.Name == "oversized-area-capped");
            (NfModel.Split split, _) = Reference(c);
            Assert.True(split.RecoveryCapped, "should hit the MaxRecovery cap");
            Assert.Equal(c.MaxRec / 100.0, split.Recovery, 6);

            Rig rig = BuildRig(c, true);
            rig.Block.Calculate();
            Assert.Contains(rig.Block.GetReportWarnings(),
                w => w.IndexOf("MaxRecovery", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        [Fact]
        public void NoPermeation_WhenPressureBelowEffectiveOsmotic()
        {
            var c = Cases().First(x => x.Name == "no-permeation");
            (NfModel.Split split, _) = Reference(c);
            Assert.Equal(0.0, split.Recovery, 9);
            Rig rig = BuildRig(c, true);
            rig.Block.Calculate();
            Assert.Contains(rig.Block.GetReportWarnings(),
                w => w.IndexOf("net driving pressure", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        [Fact]
        public void Design_ComputesAreaAndPressure()
        {
            var c = Cases().First(x => x.Name == "design-75pct");
            Rig rig = BuildRig(c, true);
            rig.Block.Calculate();
            Assert.True(ResultOf(rig.Block, "Required membrane area") > 0);
            Assert.InRange(ResultOf(rig.Block, "Water recovery"), 74.9, 75.1);
        }

        // ------------------------------------------------------------------
        //  3. determinism — 20 consecutive runs identical to < 1e-8
        // ------------------------------------------------------------------
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TwentyConsecutiveRuns_AreIdentical(bool thermo11)
        {
            NfCase c = Cases().First(x => x.Name == "brackish-multisalt");
            Rig rig = BuildRig(c, thermo11);
            double[][] runs = new double[20][];
            double[][] perm = new double[20][];
            for (int r = 0; r < 20; r++)
            {
                rig.Block.Calculate();
                runs[r] = rig.Block.GetResults().Select(x => x.Value).ToArray();
                perm[r] = (double[])rig.Perm.Flows.Clone();
            }
            for (int r = 1; r < 20; r++)
            {
                for (int i = 0; i < runs[0].Length; i++)
                    Assert.True(Math.Abs(runs[r][i] - runs[0][i]) < 1e-8, "result " + i + " drifted at run " + r);
                for (int i = 0; i < perm[0].Length; i++)
                    Assert.True(Math.Abs(perm[r][i] - perm[0][i]) < 1e-8, "permeate " + i + " drifted at run " + r);
            }
        }

        // ------------------------------------------------------------------
        //  4. results table (output parameters) == report rows == streams
        // ------------------------------------------------------------------
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void OutputParameters_MatchResultRows(bool thermo11)
        {
            NfCase c = Cases().First(x => x.Name == "brackish-multisalt");
            Rig rig = BuildRig(c, thermo11);
            rig.Block.Calculate();

            AssertExact(ResultOf(rig.Block, "Water recovery"), OutParam(rig.Block, "Recovery"), "Recovery");
            AssertExact(ResultOf(rig.Block, "Permeate flow"), OutParam(rig.Block, "PermeateFlow"), "PermeateFlow");
            AssertExact(ResultOf(rig.Block, "Permeate TDS"), OutParam(rig.Block, "PermeateTDS"), "PermeateTDS");
            AssertExact(ResultOf(rig.Block, "Pump power"), OutParam(rig.Block, "PumpPower"), "PumpPower");
            AssertExact(ResultOf(rig.Block, "Specific energy (SEC)"), OutParam(rig.Block, "SEC"), "SEC");
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ReportedResults_MatchOutletStreams_Exactly(bool thermo11)
        {
            NfCase c = Cases().First(x => x.Name == "brackish-multisalt");
            Rig rig = BuildRig(c, thermo11);
            rig.Block.Calculate();

            double[] pm = rig.Perm.Flows, cm = rig.Conc.Flows;
            int wi = c.Wi;
            double permKg = 0, permSalt = 0, concKg = 0, concSalt = 0;
            for (int i = 0; i < c.FeedMol.Length; i++)
            {
                permKg += pm[i] * c.Mw[i] / 1000.0; concKg += cm[i] * c.Mw[i] / 1000.0;
                if (i != wi) { permSalt += pm[i] * c.Mw[i] / 1000.0; concSalt += cm[i] * c.Mw[i] / 1000.0; }
            }
            double recovery = pm[wi] / c.FeedMol[wi] * 100.0;
            double permM3h = permKg / 1000.0 * 3600.0;
            double tdsPerm = permSalt / permKg * 1e6;
            double tdsConc = concSalt / concKg * 1e6;

            AssertExact(recovery, ResultOf(rig.Block, "Water recovery"), "recovery vs streams");
            AssertExact(permM3h, ResultOf(rig.Block, "Permeate flow"), "permeate flow vs streams");
            AssertExact(tdsPerm, ResultOf(rig.Block, "Permeate TDS"), "permeate TDS vs streams");
            AssertExact(tdsConc, ResultOf(rig.Block, "Concentrate TDS"), "concentrate TDS vs streams");
            Assert.True(rig.Perm.FlashCount > 0 && rig.Conc.FlashCount > 0, "both outlets flashed");
        }

        // ------------------------------------------------------------------
        //  5. ports, defaults, RealParameter-only, Model&References
        // ------------------------------------------------------------------
        [Fact]
        public void PortsAndDefaults_AreFactoryGrade()
        {
            var block = new Nanofiltration();

            var portNames = block.Ports.Cast<UnitPort>().Select(p => p.ComponentName).ToList();
            Assert.Equal(new[] { "Feed", "Concentrate", "Permeate" }, portNames);

            var inputs = new Dictionary<string, object>();
            var outputs = new List<string>();
            foreach (CapeParameter p in block.Parameters)
            {
                if (UnitBase.IsInputParameter(p)) inputs[p.ComponentName] = ((ICapeParameter)p).value;
                else outputs.Add(p.ComponentName);
            }

            Assert.Equal(0.0, Convert.ToDouble(inputs["CalcMode"], CultureInfo.InvariantCulture));
            Assert.Equal(8.0, Convert.ToDouble(inputs["WaterPermA"], CultureInfo.InvariantCulture));
            Assert.Equal(97.0, Convert.ToDouble(inputs["MultivalRejection"], CultureInfo.InvariantCulture));
            Assert.Equal(50.0, Convert.ToDouble(inputs["MonovalRejection"], CultureInfo.InvariantCulture));
            Assert.Equal(0.95, Convert.ToDouble(inputs["ReflectionSigma"], CultureInfo.InvariantCulture));
            Assert.Equal(10.0, Convert.ToDouble(inputs["AppliedPressure"], CultureInfo.InvariantCulture));

            // GOLDEN RULE: every parameter must be a RealParameter (the only type
            // Aspen's grid renders) — an Option/Integer/Boolean param blanks the grid.
            foreach (CapeParameter p in block.Parameters)
                Assert.True(p is RealParameter, "parameter '" + p.ComponentName +
                    "' must be a RealParameter (was " + p.GetType().Name + ")");

            foreach (string req in new[] { "Recovery", "PermeateFlow", "PermeateTDS", "SaltRejObs",
                                           "PermeateFlux", "NDP", "PumpPower", "SEC",
                                           "RequiredArea", "RequiredPressure" })
                Assert.Contains(req, outputs);
        }

        [Fact]
        public void ModelAndReferences_InReport()
        {
            var c = Brackish();
            Rig rig = BuildRig(c, true);
            rig.Block.Calculate();
            string report = "";
            rig.Block.ProduceReport(ref report);
            Assert.Contains("Model & References", report);
            Assert.Contains("Spiegler", report);
            Assert.Contains("reflection coefficient", report);
        }
    }
}
