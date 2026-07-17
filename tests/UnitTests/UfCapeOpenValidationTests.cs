using System;
using System.Collections.Generic;
using System.Linq;
using OPBlocks.Core;
using OPBlocks.Desalination;
using Xunit;

namespace OPBlocks.UnitTests
{
    /// <summary>
    /// OP-UF validation gate (factory pattern): structural block==UfModel within
    /// 0.1% on both thermo mocks; physical anchors (Darcy flux exact, salts pass —
    /// no osmotic barrier, macro rejection); determinism; results==streams;
    /// RealParameter-only; Model&amp;References.
    /// </summary>
    public class UfCapeOpenValidationTests
    {
        private const double MwWater = 18.0153, MwNaCl = 58.442467, MwHumic = 1000.0;

        public sealed class UfCase
        {
            public string Name;
            public double Area = 40, Lp = 100, Tmp = 1.0, Rej = 95, FF = 0.7, MaxRec = 95, PumpEff = 80;
            public double[] FeedMol;
            public double[] Mw;
            public string[] Ids;
            public int Wi;
            public override string ToString() { return Name; }

            public UfModel.Spec Spec()
            {
                return new UfModel.Spec
                {
                    AreaM2 = Area, PermLp = Lp, TmpBar = Tmp, FoulingFactor = FF,
                    MaxRecoveryPct = MaxRec, PumpEffPct = PumpEff,
                };
            }

            public double[] Passage()
            {
                var pass = new double[FeedMol.Length];
                for (int i = 0; i < pass.Length; i++)
                    pass[i] = (i == Wi) ? 0 : (UfModel.IsDissolvedSalt(Ids[i]) ? 1.0 : 1.0 - Rej / 100.0);
                return pass;
            }

            public bool[] Macro()
            {
                var m = new bool[FeedMol.Length];
                for (int i = 0; i < m.Length; i++) m[i] = i != Wi && !UfModel.IsDissolvedSalt(Ids[i]);
                return m;
            }
        }

        public static IReadOnlyList<UfCase> Cases()
        {
            return new List<UfCase>
            {
                new UfCase { Name = "salty-water", FeedMol = new[]{200.0, 0.2},
                             Ids = new[]{"WATER","NACL"}, Mw = new[]{MwWater,MwNaCl}, Wi = 0 },
                new UfCase { Name = "protein-and-salt", FeedMol = new[]{200.0, 0.2, 0.01},
                             Ids = new[]{"WATER","NACL","HUMIC-MACRO"}, Mw = new[]{MwWater,MwNaCl,MwHumic}, Wi = 0 },
                new UfCase { Name = "oversized-capped", FeedMol = new[]{20.0, 0.02},
                             Ids = new[]{"WATER","NACL"}, Mw = new[]{MwWater,MwNaCl}, Wi = 0, Area = 5000 },
            };
        }

        public static IEnumerable<object[]> CaseNames()
        {
            foreach (UfCase c in Cases()) { yield return new object[] { c.Name, false }; yield return new object[] { c.Name, true }; }
        }

        private static (UfModel.Split, UfModel.Energy) Reference(UfCase c)
        {
            UfModel.Split split = UfModel.Solve(c.Spec(), c.FeedMol, c.Wi, c.Passage(), c.Mw, c.Macro());
            double feedKg = 0, permKg = 0;
            for (int i = 0; i < c.FeedMol.Length; i++)
            {
                feedKg += c.FeedMol[i] * c.Mw[i] / 1000.0;
                permKg += split.PermMol[i] * c.Mw[i] / 1000.0;
            }
            UfModel.Energy e = UfModel.CalcEnergy(c.Spec(), feedKg / 1000.0, permKg / 1000.0);
            return (split, e);
        }

        private sealed class Rig { public Ultrafiltration Block; public object Feed, Perm, Ret; }

        private static Rig BuildRig(UfCase c, bool t11)
        {
            var rig = new Rig
            {
                Block = new Ultrafiltration(),
                Feed = TestKit.NewMock(t11, c.Ids, c.Mw, (double[])c.FeedMol.Clone()),
                Perm = TestKit.NewMock(t11, c.Ids, c.Mw),
                Ret = TestKit.NewMock(t11, c.Ids, c.Mw),
            };
            TestKit.Set(rig.Block, "Area", c.Area); TestKit.Set(rig.Block, "Permeability", c.Lp);
            TestKit.Set(rig.Block, "TMP", c.Tmp); TestKit.Set(rig.Block, "Rejection", c.Rej);
            TestKit.Set(rig.Block, "FoulingFactor", c.FF); TestKit.Set(rig.Block, "MaxRecovery", c.MaxRec);
            TestKit.Set(rig.Block, "PumpEff", c.PumpEff);
            TestKit.Connect(rig.Block, "Feed", rig.Feed);
            TestKit.Connect(rig.Block, "Permeate", rig.Perm);
            TestKit.Connect(rig.Block, "Retentate", rig.Ret);
            return rig;
        }

        // 1. STRUCTURAL — block == UfModel within 0.1%, both thermo backends
        [Theory]
        [MemberData(nameof(CaseNames))]
        public void Case_BlockMatchesReference_Within01Percent(string name, bool thermo11)
        {
            UfCase c = Cases().First(x => x.Name == name);
            (UfModel.Split split, UfModel.Energy e) = Reference(c);
            Rig rig = BuildRig(c, thermo11);
            rig.Block.Calculate();

            TestKit.Close(split.Recovery * 100.0, TestKit.ResultOf(rig.Block, "Water recovery"), "recovery");
            TestKit.Close(split.FluxLMH, TestKit.ResultOf(rig.Block, "Permeate flux"), "flux");
            TestKit.Close(split.TdsPermPpm, TestKit.ResultOf(rig.Block, "Permeate TDS"), "permTDS");
            TestKit.Close(split.TdsFeedPpm, TestKit.ResultOf(rig.Block, "Feed TDS"), "feedTDS");
            TestKit.Close(e.PumpKW, TestKit.ResultOf(rig.Block, "Pump power"), "pump");
            TestKit.Close(e.SEC, TestKit.ResultOf(rig.Block, "Specific energy (SEC)"), "SEC");

            double[] pm = TestKit.FlowsOf(rig.Perm), rm = TestKit.FlowsOf(rig.Ret);
            for (int i = 0; i < c.FeedMol.Length; i++)
            {
                TestKit.Close(split.PermMol[i], pm[i], "permMol[" + i + "]");
                TestKit.Close(split.ConcMol[i], rm[i], "retMol[" + i + "]");
            }
            TestKit.AssertMassBalance(new[] { c.FeedMol }, new[] { pm, rm }, c.FeedMol.Length);
        }

        // 2. PHYSICAL — hand-reasoned anchors
        [Fact]
        public void DarcyFlux_IsExactlyLpTmpFF()
        {
            // Cheryan (1998): pure Darcy law — Lp=100, TMP=1, FF=0.7 → exactly 70 LMH.
            var c = Cases().First(x => x.Name == "salty-water");
            (UfModel.Split split, _) = Reference(c);
            TestKit.AssertExact(70.0, split.FluxLMH, "Darcy flux");
        }

        [Fact]
        public void DissolvedSalts_PassFreely_PermeateTdsEqualsFeedTds()
        {
            // UF has no osmotic barrier: NaCl-only feed → permeate TDS == feed TDS.
            var c = Cases().First(x => x.Name == "salty-water");
            (UfModel.Split split, _) = Reference(c);
            Assert.True(split.TdsFeedPpm > 100, "feed actually salty");
            TestKit.Close(split.TdsFeedPpm, split.TdsPermPpm, "TDS passes through");

            Rig rig = BuildRig(c, true);
            rig.Block.Calculate();
            TestKit.AssertWarningContains(rig.Block, "no osmotic barrier");
        }

        [Fact]
        public void MacroSolute_IsRejected_AtConfiguredRejection()
        {
            var c = Cases().First(x => x.Name == "protein-and-salt");
            (UfModel.Split split, _) = Reference(c);
            int macro = 2;
            double passObs = split.PermMol[macro] / c.FeedMol[macro];
            double passSalt = split.PermMol[1] / c.FeedMol[1];
            Assert.True(passObs < passSalt, "macro solute must permeate less than salt");
            // per-pass rejection: permeated fraction = (1-Rej)·recovery
            TestKit.Close((1.0 - c.Rej / 100.0) * split.Recovery, passObs, "macro passage");
            Assert.InRange(split.MacroRemovalPct, 90.0, 100.0);
        }

        [Fact]
        public void OversizedArea_CapsAtMaxRecovery_WithWarning()
        {
            var c = Cases().First(x => x.Name == "oversized-capped");
            (UfModel.Split split, _) = Reference(c);
            Assert.True(split.RecoveryCapped);
            Assert.Equal(c.MaxRec / 100.0, split.Recovery, 6);
            Rig rig = BuildRig(c, true);
            rig.Block.Calculate();
            TestKit.AssertWarningContains(rig.Block, "MaxRecovery");
        }

        // 3. determinism
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TwentyConsecutiveRuns_AreIdentical(bool thermo11)
        {
            Rig rig = BuildRig(Cases().First(x => x.Name == "protein-and-salt"), thermo11);
            TestKit.AssertTwentyRunsIdentical(rig.Block, () => TestKit.FlowsOf(rig.Perm));
        }

        // 4. results table == report rows == streams
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void OutputParameters_MatchResultRows(bool thermo11)
        {
            Rig rig = BuildRig(Cases().First(x => x.Name == "protein-and-salt"), thermo11);
            rig.Block.Calculate();
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "Water recovery"), TestKit.OutParam(rig.Block, "Recovery"), "Recovery");
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "Permeate flow"), TestKit.OutParam(rig.Block, "PermeateFlow"), "PermeateFlow");
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "Permeate flux"), TestKit.OutParam(rig.Block, "PermeateFlux"), "PermeateFlux");
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "Pump power"), TestKit.OutParam(rig.Block, "PumpPower"), "PumpPower");
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "Specific energy (SEC)"), TestKit.OutParam(rig.Block, "SEC"), "SEC");
        }

        [Fact]
        public void ReportedRecovery_MatchesOutletStreams_Exactly()
        {
            UfCase c = Cases().First(x => x.Name == "protein-and-salt");
            Rig rig = BuildRig(c, true);
            rig.Block.Calculate();
            double[] pm = TestKit.FlowsOf(rig.Perm);
            TestKit.AssertExact(pm[c.Wi] / c.FeedMol[c.Wi] * 100.0,
                TestKit.ResultOf(rig.Block, "Water recovery"), "recovery vs streams");
            Assert.True(TestKit.FlashCountOf(rig.Perm) > 0 && TestKit.FlashCountOf(rig.Ret) > 0, "both outlets flashed");
        }

        // 5. ports, defaults, RealParameter-only, Model&References
        [Fact]
        public void PortsAndDefaults_AreFactoryGrade()
        {
            var block = new Ultrafiltration();
            TestKit.AssertPortNames(block, "Feed", "Retentate", "Permeate");
            TestKit.AssertRealParametersOnly(block);
            Assert.Equal(100.0, (double)((CapeOpen.ICapeParameter)block.Parameters.Cast<CapeOpen.CapeParameter>()
                .First(p => p.ComponentName == "Permeability")).value);
            var outputs = block.Parameters.Cast<CapeOpen.CapeParameter>()
                .Where(p => !UnitBase.IsInputParameter(p)).Select(p => p.ComponentName).ToList();
            foreach (string req in new[] { "Recovery", "PermeateFlow", "PermeateFlux", "MacroRemoval", "PumpPower", "SEC" })
                Assert.Contains(req, outputs);
        }

        [Fact]
        public void ModelAndReferences_InReport()
        {
            Rig rig = BuildRig(Cases().First(x => x.Name == "salty-water"), true);
            rig.Block.Calculate();
            TestKit.AssertReportContains(rig.Block, "Model & References", "Darcy", "Cheryan");
        }
    }
}
