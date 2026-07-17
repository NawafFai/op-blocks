using System;
using System.Linq;
using OPBlocks.Core;
using OPBlocks.Electro;
using Xunit;

namespace OPBlocks.UnitTests
{
    // =====================================================================
    //  Family C validation gates — OP-EDI, OP-CDI, OP-CHLORALK, OP-IX.
    // =====================================================================

    public class EdiCapeOpenValidationTests
    {
        private const double MwWater = 18.0153, MwNaCl = 58.442467;
        private static readonly string[] Ids = { "WATER", "NACL" };
        private static readonly double[] Mw = { MwWater, MwNaCl };
        private static readonly double[] Dilute = { 10.0, 1e-4 };   // polishing feed (~ 32 ppm)
        private static readonly double[] Conc = { 5.0, 0.001 };

        private static EdiModel.Spec DefaultSpec()
        {
            return new EdiModel.Spec
            {
                CellPairs = 50, VoltageV = 100, ResistanceOhm = 40,
                TargetRemovalPct = 99, CurrentEffPct = 90, IonValence = 1,
            };
        }

        private static double DiluteM3s() { return Dilute[0] * MwWater / 1000.0 / 1000.0; }

        private sealed class Rig { public Electrodeionization Block; public object DIn, DOut, CIn, COut; }

        private static Rig BuildRig(bool t11, double voltage = 100, double resistance = 40)
        {
            var rig = new Rig
            {
                Block = new Electrodeionization(),
                DIn = TestKit.NewMock(t11, Ids, Mw, (double[])Dilute.Clone()),
                DOut = TestKit.NewMock(t11, Ids, Mw),
                CIn = TestKit.NewMock(t11, Ids, Mw, (double[])Conc.Clone()),
                COut = TestKit.NewMock(t11, Ids, Mw),
            };
            TestKit.Set(rig.Block, "AppliedVoltage", voltage);
            TestKit.Set(rig.Block, "StackResistance", resistance);
            TestKit.Connect(rig.Block, "DiluteIn", rig.DIn); TestKit.Connect(rig.Block, "DiluteOut", rig.DOut);
            TestKit.Connect(rig.Block, "ConcentrateIn", rig.CIn); TestKit.Connect(rig.Block, "ConcentrateOut", rig.COut);
            return rig;
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Block_MatchesEngine_Within01Percent(bool thermo11)
        {
            EdiModel.Perf p = EdiModel.Solve(DefaultSpec(), Dilute, Conc, 0, DiluteM3s());
            Rig rig = BuildRig(thermo11);
            rig.Block.Calculate();

            TestKit.Close(p.RemovalPct, TestKit.ResultOf(rig.Block, "Ion removal (achieved)"), "removal");
            TestKit.Close(p.CurrentA, TestKit.ResultOf(rig.Block, "Stack current"), "current");
            TestKit.Close(p.RemovedMolS, TestKit.ResultOf(rig.Block, "Ions removed"), "removed");
            TestKit.Close(p.PowerKW, TestKit.ResultOf(rig.Block, "Stack power"), "power");

            double[] d = TestKit.FlowsOf(rig.DOut), c = TestKit.FlowsOf(rig.COut);
            TestKit.Close(p.DiluteOutMol[1], d[1], "dilute salt out");
            TestKit.Close(p.ConcOutMol[1], c[1], "conc salt out");
            TestKit.AssertMassBalance(new[] { Dilute, Conc }, new[] { d, c }, 2);
        }

        [Fact]
        public void Faraday_ExactAnchor()
        {
            // I = F amperes, 1 cell pair, 100% eff, z=1 -> capacity exactly 1 mol/s
            var s = new EdiModel.Spec { CellPairs = 1, VoltageV = ProcessOps.Faraday, ResistanceOhm = 1,
                                        TargetRemovalPct = 100, CurrentEffPct = 100, IonValence = 1 };
            EdiModel.Perf p = EdiModel.Solve(s, Dilute, Conc, 0, DiluteM3s());
            TestKit.AssertExact(1.0, p.FaradaicCapMolS, "Faraday capacity");
        }

        [Fact]
        public void AmpleCurrent_AchievesTargetRemoval()
        {
            EdiModel.Perf p = EdiModel.Solve(DefaultSpec(), Dilute, Conc, 0, DiluteM3s());
            Assert.False(p.CurrentLimited);
            TestKit.Close(99.0, p.RemovalPct, "achieved = target");
        }

        [Fact]
        public void StarvedCurrent_IsLimited_WithWarning()
        {
            Rig rig = BuildRig(true, 1, 2000);   // 0.5 mA — cannot move the ion load
            rig.Block.Calculate();
            TestKit.AssertWarningContains(rig.Block, "Current-limited");
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TwentyConsecutiveRuns_AreIdentical(bool thermo11)
        {
            Rig rig = BuildRig(thermo11);
            TestKit.AssertTwentyRunsIdentical(rig.Block, () => TestKit.FlowsOf(rig.DOut));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void OutputParameters_MatchResultRows(bool thermo11)
        {
            Rig rig = BuildRig(thermo11);
            rig.Block.Calculate();
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "Ion removal (achieved)"), TestKit.OutParam(rig.Block, "Removal"), "Removal");
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "Stack current"), TestKit.OutParam(rig.Block, "StackCurrent"), "StackCurrent");
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "Specific energy consumption"), TestKit.OutParam(rig.Block, "SEC"), "SEC");
        }

        [Fact]
        public void PortsAndDefaults_AreFactoryGrade()
        {
            var block = new Electrodeionization();
            TestKit.AssertPortNames(block, "DiluteIn", "DiluteOut", "ConcentrateIn", "ConcentrateOut");
            TestKit.AssertRealParametersOnly(block);   // CellPairs was an IntegerParameter — golden-rule fix
            var outputs = block.Parameters.Cast<CapeOpen.CapeParameter>()
                .Where(p => !UnitBase.IsInputParameter(p)).Select(p => p.ComponentName).ToList();
            foreach (string req in new[] { "Removal", "StackCurrent", "IonsRemoved", "WaterSplit", "Power", "SEC" })
                Assert.Contains(req, outputs);
        }

        [Fact]
        public void ModelAndReferences_InReport()
        {
            Rig rig = BuildRig(true);
            rig.Block.Calculate();
            TestKit.AssertReportContains(rig.Block, "Model & References", "Ganzi", "Faradaic");
        }
    }

    public class CdiCapeOpenValidationTests
    {
        private const double MwWater = 18.0153, MwNaCl = 58.442467;
        private static readonly string[] Ids = { "WATER", "NACL" };
        private static readonly double[] Mw = { MwWater, MwNaCl };
        private static readonly double[] Feed = { 10.0, 5e-4 };   // brackish ~ 160 ppm

        private static CdiModel.Spec DefaultSpec()
        {
            return new CdiModel.Spec
            {
                SacMgG = 15, ElectrodeKg = 1.0, CycleTimeS = 300, ChargeEffPct = 70,
                CellVoltageV = 1.2, WaterRecoveryPct = 80, SaltMwGmol = 58.44,
            };
        }

        private static double ProdM3s() { return Feed[0] * 0.8 * MwWater / 1000.0 / 1000.0; }

        private sealed class Rig { public CapacitiveDeionization Block; public object F, P, W; }

        private static Rig BuildRig(bool t11)
        {
            var rig = new Rig
            {
                Block = new CapacitiveDeionization(),
                F = TestKit.NewMock(t11, Ids, Mw, (double[])Feed.Clone()),
                P = TestKit.NewMock(t11, Ids, Mw),
                W = TestKit.NewMock(t11, Ids, Mw),
            };
            TestKit.Connect(rig.Block, "Feed", rig.F);
            TestKit.Connect(rig.Block, "Product", rig.P);
            TestKit.Connect(rig.Block, "Waste", rig.W);
            return rig;
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Block_MatchesEngine_Within01Percent(bool thermo11)
        {
            CdiModel.Perf x = CdiModel.Solve(DefaultSpec(), Feed, 0, ProdM3s());
            Rig rig = BuildRig(thermo11);
            rig.Block.Calculate();

            TestKit.Close(x.RemovedMolS, TestKit.ResultOf(rig.Block, "Salt removed"), "removed");
            TestKit.Close(x.RemovalPct, TestKit.ResultOf(rig.Block, "Salt removal"), "removalPct");
            TestKit.Close(x.ChargeA, TestKit.ResultOf(rig.Block, "Average charging current"), "current");
            TestKit.Close(x.SecKWhM3, TestKit.ResultOf(rig.Block, "Specific energy consumption"), "SEC");

            double[] pm = TestKit.FlowsOf(rig.P), wm = TestKit.FlowsOf(rig.W);
            TestKit.Close(x.ProductMol[1], pm[1], "product salt");
            TestKit.AssertMassBalance(new[] { Feed }, new[] { pm, wm }, 2);
        }

        [Fact]
        public void ChargeSaltInversion_IsExact()
        {
            // N_salt = Lambda*Q/F  <=>  Q = N*F/Lambda; energy = Q*V
            CdiModel.Perf x = CdiModel.Solve(DefaultSpec(), Feed, 0, ProdM3s());
            TestKit.AssertExact(x.RemovedMolS * ProcessOps.Faraday / 0.70, x.ChargeA, "charge inversion");
            TestKit.AssertExact(x.ChargeA * 1.2 / 1000.0, x.PowerKW, "E = Q*V");
        }

        [Fact]
        public void CapacityLimit_IsSacBased()
        {
            // SAC*m/(MW*t) = 15*1000/(58.44*300) mg->mol = 15*1000/300 mg/s /58440 mg/mol
            CdiModel.Perf x = CdiModel.Solve(DefaultSpec(), Feed, 0, ProdM3s());
            TestKit.AssertExact(15.0 * 1000.0 / 58.44 / 1000.0 / 300.0, x.CapacityMolS, "SAC capacity");
        }

        [Fact]
        public void Sec_InPublishedBrackishBand()
        {
            CdiModel.Perf x = CdiModel.Solve(DefaultSpec(), Feed, 0, ProdM3s());
            Assert.InRange(x.SecKWhM3, 0.05, 1.5);   // Porada/Suss brackish band
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TwentyConsecutiveRuns_AreIdentical(bool thermo11)
        {
            Rig rig = BuildRig(thermo11);
            TestKit.AssertTwentyRunsIdentical(rig.Block, () => TestKit.FlowsOf(rig.P));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void OutputParameters_MatchResultRows(bool thermo11)
        {
            Rig rig = BuildRig(thermo11);
            rig.Block.Calculate();
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "Salt removed"), TestKit.OutParam(rig.Block, "SaltRemoved"), "SaltRemoved");
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "Charging power"), TestKit.OutParam(rig.Block, "Power"), "Power");
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "Specific energy consumption"), TestKit.OutParam(rig.Block, "SEC"), "SEC");
        }

        [Fact]
        public void PortsAndDefaults_AreFactoryGrade()
        {
            var block = new CapacitiveDeionization();
            TestKit.AssertPortNames(block, "Feed", "Product", "Waste");
            TestKit.AssertRealParametersOnly(block);
            var outputs = block.Parameters.Cast<CapeOpen.CapeParameter>()
                .Where(p => !UnitBase.IsInputParameter(p)).Select(p => p.ComponentName).ToList();
            foreach (string req in new[] { "SaltRemoved", "SaltRemoval", "ChargeCurrent", "Power", "SEC" })
                Assert.Contains(req, outputs);
        }

        [Fact]
        public void ModelAndReferences_InReport()
        {
            Rig rig = BuildRig(true);
            rig.Block.Calculate();
            TestKit.AssertReportContains(rig.Block, "Model & References", "Porada", "Lambda");
        }
    }

    public class ChlorAlkaliCapeOpenValidationTests
    {
        private static readonly string[] Ids = { "WATER", "NACL", "NAOH", "CL2", "H2" };
        private static readonly double[] Mw = { 18.0153, 58.442467, 39.997, 70.906, 2.01588 };
        private static readonly double[] Feed = { 300.0, 30.0, 0, 0, 0 };   // strong brine

        private static ChlorAlkaliModel.Spec DefaultSpec()
        {
            return new ChlorAlkaliModel.Spec { CurrentA = 400000, CurrentEffPct = 96, CellVoltageV = 3.1, WaterTransport = 3.5 };
        }

        private sealed class Rig { public ChlorAlkali Block; public object B, Dep, Cat, Cl, H; }

        private static Rig BuildRig(bool t11, double currentA = 400000, double[] feed = null)
        {
            var rig = new Rig
            {
                Block = new ChlorAlkali(),
                B = TestKit.NewMock(t11, Ids, Mw, (double[])(feed ?? Feed).Clone()),
                Dep = TestKit.NewMock(t11, Ids, Mw),
                Cat = TestKit.NewMock(t11, Ids, Mw),
                Cl = TestKit.NewMock(t11, Ids, Mw),
                H = TestKit.NewMock(t11, Ids, Mw),
            };
            TestKit.Set(rig.Block, "Current", currentA);
            TestKit.Connect(rig.Block, "BrineIn", rig.B); TestKit.Connect(rig.Block, "DepletedBrine", rig.Dep);
            TestKit.Connect(rig.Block, "Catholyte", rig.Cat); TestKit.Connect(rig.Block, "Chlorine", rig.Cl);
            TestKit.Connect(rig.Block, "Hydrogen", rig.H);
            return rig;
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Block_MatchesEngine_Within01Percent(bool thermo11)
        {
            ChlorAlkaliModel.Perf x = ChlorAlkaliModel.Solve(DefaultSpec(), Feed[1], Feed[0]);
            Rig rig = BuildRig(thermo11);
            rig.Block.Calculate();

            TestKit.Close(x.Cl2MolS * ChlorAlkaliModel.MwCl2, TestKit.ResultOf(rig.Block, "Chlorine production"), "Cl2");
            TestKit.Close(x.NaOHMolS * ChlorAlkaliModel.MwNaOH, TestKit.ResultOf(rig.Block, "Caustic (NaOH) production"), "NaOH");
            TestKit.Close(x.H2MolS * ChlorAlkaliModel.MwH2, TestKit.ResultOf(rig.Block, "Hydrogen production"), "H2");
            TestKit.Close(x.SecKWhKgCl2, TestKit.ResultOf(rig.Block, "Specific energy (per kg Cl2)"), "SEC");

            // element bookkeeping across the five streams
            double[] dep = TestKit.FlowsOf(rig.Dep), cat = TestKit.FlowsOf(rig.Cat),
                     cl = TestKit.FlowsOf(rig.Cl), h = TestKit.FlowsOf(rig.H);
            // Na: NaCl in = NaCl out + NaOH out
            TestKit.Close(Feed[1], dep[1] + cat[2], "sodium balance");
            // Cl: NaCl in = NaCl out + 2*Cl2 out
            TestKit.Close(Feed[1], dep[1] + 2.0 * cl[3], "chlorine balance");
        }

        [Fact]
        public void Faraday_ExactAnchor()
        {
            // I = 2F at 100% -> exactly 1 mol/s Cl2, 1 H2, 2 NaOH
            var s = new ChlorAlkaliModel.Spec { CurrentA = 2 * ProcessOps.Faraday, CurrentEffPct = 100, CellVoltageV = 3.1, WaterTransport = 3.5 };
            ChlorAlkaliModel.Perf x = ChlorAlkaliModel.Solve(s, 1000.0, 10000.0);
            TestKit.AssertExact(1.0, x.Cl2MolS, "Cl2");
            TestKit.AssertExact(1.0, x.H2MolS, "H2");
            TestKit.AssertExact(2.0, x.NaOHMolS, "NaOH");
        }

        [Fact]
        public void Sec_MatchesPublishedMembraneCellBand()
        {
            // 3.1 V / 96% -> 2.44 kWh/kg Cl2 (published 2.3-2.7)
            ChlorAlkaliModel.Perf x = ChlorAlkaliModel.Solve(DefaultSpec(), Feed[1], Feed[0]);
            Assert.InRange(x.SecKWhKgCl2, 2.3, 2.7);
            double expected = 3.1 * 2.0 * ProcessOps.Faraday / 0.96 / ChlorAlkaliModel.MwCl2 / 3.6e6;
            TestKit.Close(expected, x.SecKWhKgCl2, "SEC closed form");
        }

        [Fact]
        public void BrineLimited_Capped_WithWarning()
        {
            var lean = new double[] { 300.0, 0.01, 0, 0, 0 };
            Rig rig = BuildRig(true, 400000, lean);
            rig.Block.Calculate();
            TestKit.AssertWarningContains(rig.Block, "Brine-limited");
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TwentyConsecutiveRuns_AreIdentical(bool thermo11)
        {
            Rig rig = BuildRig(thermo11);
            TestKit.AssertTwentyRunsIdentical(rig.Block, () => TestKit.FlowsOf(rig.Cl));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void OutputParameters_MatchResultRows(bool thermo11)
        {
            Rig rig = BuildRig(thermo11);
            rig.Block.Calculate();
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "Chlorine production"), TestKit.OutParam(rig.Block, "Cl2Prod"), "Cl2Prod");
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "Cell power"), TestKit.OutParam(rig.Block, "Power"), "Power");
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "Specific energy (per kg Cl2)"), TestKit.OutParam(rig.Block, "SECCl2"), "SECCl2");
        }

        [Fact]
        public void PortsAndDefaults_AreFactoryGrade()
        {
            var block = new ChlorAlkali();
            TestKit.AssertPortNames(block, "BrineIn", "DepletedBrine", "Catholyte", "Chlorine", "Hydrogen");
            TestKit.AssertRealParametersOnly(block);
            var outputs = block.Parameters.Cast<CapeOpen.CapeParameter>()
                .Where(p => !UnitBase.IsInputParameter(p)).Select(p => p.ComponentName).ToList();
            foreach (string req in new[] { "Cl2Prod", "NaOHProd", "H2Prod", "Power", "SECCl2" })
                Assert.Contains(req, outputs);
        }

        [Fact]
        public void ModelAndReferences_InReport()
        {
            Rig rig = BuildRig(true);
            rig.Block.Calculate();
            TestKit.AssertReportContains(rig.Block, "Model & References", "O'Brien", "Faradaic");
        }
    }

    public class IxCapeOpenValidationTests
    {
        private static readonly string[] Ids = { "WATER", "CACL2", "NACL" };
        private static readonly double[] Mw = { 18.0153, 110.98, 58.442467 };
        private static readonly double[] Feed = { 100.0, 0.01, 0.05 };
        private static readonly double[] Regen = { 20.0, 0.0, 0.5 };   // NaCl brine regenerant

        private static IxModel.Spec DefaultSpec()
        {
            return new IxModel.Spec { ResinVolumeL = 1000, CapacityEqL = 1.2, RemovalPct = 95, TargetValence = 2 };
        }

        private static bool[] Targets() { return new[] { false, true, false }; }
        private static double FeedM3s() { return Feed[0] * 18.0153 / 1000.0 / 1000.0; }

        private sealed class Rig { public IonExchange Block; public object F, T, RIn, S; }

        private static Rig BuildRig(bool t11, double[] feed = null)
        {
            var rig = new Rig
            {
                Block = new IonExchange(),
                F = TestKit.NewMock(t11, Ids, Mw, (double[])(feed ?? Feed).Clone()),
                T = TestKit.NewMock(t11, Ids, Mw),
                RIn = TestKit.NewMock(t11, Ids, Mw, (double[])Regen.Clone()),
                S = TestKit.NewMock(t11, Ids, Mw),
            };
            TestKit.Connect(rig.Block, "Feed", rig.F); TestKit.Connect(rig.Block, "Treated", rig.T);
            TestKit.Connect(rig.Block, "RegenerantIn", rig.RIn); TestKit.Connect(rig.Block, "SpentOut", rig.S);
            return rig;
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Block_MatchesEngine_Within01Percent(bool thermo11)
        {
            IxModel.Perf x = IxModel.Solve(DefaultSpec(), Feed, Regen, 0, Targets(), FeedM3s());
            Rig rig = BuildRig(thermo11);
            rig.Block.Calculate();

            TestKit.Close(x.RemovedMolS, TestKit.ResultOf(rig.Block, "Target ions removed"), "removed");
            TestKit.Close(x.BedCapacityEq, TestKit.ResultOf(rig.Block, "Bed capacity"), "capacity");
            TestKit.Close(x.ServiceHours, TestKit.ResultOf(rig.Block, "Service run"), "serviceH");

            double[] t = TestKit.FlowsOf(rig.T), s = TestKit.FlowsOf(rig.S);
            TestKit.Close(x.TreatedMol[1], t[1], "treated Ca");
            TestKit.AssertMassBalance(new[] { Feed, Regen }, new[] { t, s }, 3);
        }

        [Fact]
        public void Softening_RemovesCa_PassesNa()
        {
            IxModel.Perf x = IxModel.Solve(DefaultSpec(), Feed, Regen, 0, Targets(), FeedM3s());
            TestKit.Close(Feed[1] * 0.95, Feed[1] - x.TreatedMol[1], "Ca removed at 95%");
            TestKit.AssertExact(Feed[2], x.TreatedMol[2], "Na passes untouched");
        }

        [Fact]
        public void ServiceTime_IsCapacityOverLoad_Exactly()
        {
            IxModel.Perf x = IxModel.Solve(DefaultSpec(), Feed, Regen, 0, Targets(), FeedM3s());
            double loadEqS = Feed[1] * 0.95 * 2.0;
            TestKit.AssertExact(1000.0 * 1.2 / loadEqS / 3600.0, x.ServiceHours, "service time closed form");
        }

        [Fact]
        public void NoHardness_Warns()
        {
            Rig rig = BuildRig(true, new[] { 100.0, 0.0, 0.05 });
            rig.Block.Calculate();
            TestKit.AssertWarningContains(rig.Block, "multivalent");
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TwentyConsecutiveRuns_AreIdentical(bool thermo11)
        {
            Rig rig = BuildRig(thermo11);
            TestKit.AssertTwentyRunsIdentical(rig.Block, () => TestKit.FlowsOf(rig.T));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void OutputParameters_MatchResultRows(bool thermo11)
        {
            Rig rig = BuildRig(thermo11);
            rig.Block.Calculate();
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "Target ions removed"), TestKit.OutParam(rig.Block, "IonsRemoved"), "IonsRemoved");
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "Bed capacity"), TestKit.OutParam(rig.Block, "BedCapacity"), "BedCapacity");
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "Service run"), TestKit.OutParam(rig.Block, "ServiceTime"), "ServiceTime");
        }

        [Fact]
        public void PortsAndDefaults_AreFactoryGrade()
        {
            var block = new IonExchange();
            TestKit.AssertPortNames(block, "Feed", "Treated", "RegenerantIn", "SpentOut");
            TestKit.AssertRealParametersOnly(block);
            var outputs = block.Parameters.Cast<CapeOpen.CapeParameter>()
                .Where(p => !UnitBase.IsInputParameter(p)).Select(p => p.ComponentName).ToList();
            foreach (string req in new[] { "Removal", "IonsRemoved", "BedCapacity", "ServiceTime", "BedVolumes" })
                Assert.Contains(req, outputs);
        }

        [Fact]
        public void ModelAndReferences_InReport()
        {
            Rig rig = BuildRig(true);
            rig.Block.Calculate();
            TestKit.AssertReportContains(rig.Block, "Model & References", "Helfferich", "service time");
        }
    }
}
