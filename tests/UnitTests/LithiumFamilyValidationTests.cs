using System;
using System.Linq;
using OPBlocks.Core;
using OPBlocks.Lithium;
using Xunit;

namespace OPBlocks.UnitTests
{
    // =====================================================================
    //  Family D validation gates — OP-DLE, OP-SX, OP-GAC, OP-CRYST, OP-PPT.
    // =====================================================================

    public class DleCapeOpenValidationTests
    {
        private static readonly string[] Ids = { "WATER", "LICL", "MGCL2" };
        private static readonly double[] Mw = { 18.0153, 42.394, 95.211 };
        private static readonly double[] Brine = { 100.0, 0.005, 0.05 };   // Li-bearing brine
        private static readonly double[] Wash = { 10.0, 0.0, 0.0 };

        private static DleModel.Spec DefaultSpec()
        {
            return new DleModel.Spec
            {
                QmaxMgG = 8.0, KlangLmg = 0.05, KldfPerS = 0.002, BedVolumeL = 1000,
                SorbentGL = 800, CycleTimeS = 3600, MgLiSelectivity = 20,
            };
        }

        private sealed class Rig { public DirectLithiumExtraction Block; public object B, T, E, W; }

        private static Rig BuildRig(bool t11, double[] brine = null)
        {
            var rig = new Rig
            {
                Block = new DirectLithiumExtraction(),
                B = TestKit.NewMock(t11, Ids, Mw, (double[])(brine ?? Brine).Clone()),
                T = TestKit.NewMock(t11, Ids, Mw),
                E = TestKit.NewMock(t11, Ids, Mw),
                W = TestKit.NewMock(t11, Ids, Mw, (double[])Wash.Clone()),
            };
            TestKit.Connect(rig.Block, "BrineFeed", rig.B); TestKit.Connect(rig.Block, "TreatedBrine", rig.T);
            TestKit.Connect(rig.Block, "Eluate", rig.E); TestKit.Connect(rig.Block, "WashWater", rig.W);
            return rig;
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Block_MatchesEngine_Within01Percent(bool thermo11)
        {
            DleModel.Perf x = DleModel.Solve(DefaultSpec(), Brine[1], Brine[2], Brine[0]);
            Rig rig = BuildRig(thermo11);
            rig.Block.Calculate();

            TestKit.Close(x.RecoveryFrac * 100, TestKit.ResultOf(rig.Block, "Li recovery"), "recovery");
            TestKit.Close(x.QstarMgG, TestKit.ResultOf(rig.Block, "Equilibrium loading q*"), "qstar");
            TestKit.Close(x.QcycleMgG, TestKit.ResultOf(rig.Block, "Cycle loading"), "qcycle");
            TestKit.Close(x.CapturedLiMolS, TestKit.ResultOf(rig.Block, "Li captured"), "captured");
            TestKit.Close(x.MgCapturedMolS, TestKit.ResultOf(rig.Block, "Mg co-captured"), "mg");

            double[] t = TestKit.FlowsOf(rig.T), e = TestKit.FlowsOf(rig.E);
            TestKit.Close(Brine[1] - x.CapturedLiMolS, t[1], "treated Li");
            TestKit.Close(x.CapturedLiMolS, e[1], "eluate Li");
            TestKit.AssertMassBalance(new[] { Brine, Wash }, new[] { t, e }, 3);
        }

        [Fact]
        public void Langmuir_HalfLoading_AtC_EqualsOneOverK()
        {
            // Langmuir (1918): q(C = 1/K) = qmax/2 EXACTLY
            TestKit.AssertExact(4.0, DleModel.Langmuir(8.0, 0.05, 1.0 / 0.05), "half loading");
            Assert.True(DleModel.Langmuir(8.0, 0.05, 1e9) > 7.99, "saturates to qmax");
        }

        [Fact]
        public void LdfApproach_IsClosedForm()
        {
            DleModel.Perf x = DleModel.Solve(DefaultSpec(), Brine[1], Brine[2], Brine[0]);
            TestKit.AssertExact(1.0 - Math.Exp(-0.002 * 3600.0), x.LdfApproach, "LDF closed form");
        }

        [Fact]
        public void MgCoCapture_FollowsSelectivity()
        {
            DleModel.Perf x = DleModel.Solve(DefaultSpec(), Brine[1], Brine[2], Brine[0]);
            TestKit.AssertExact(Brine[2] * x.RecoveryFrac / 20.0, x.MgCapturedMolS, "Mg via selectivity");
            Assert.True(x.MgCapturedMolS / Brine[2] < x.CapturedLiMolS / Brine[1], "Li preferred over Mg");
        }

        [Fact]
        public void ZeroLithiumFeed_Warns()
        {
            Rig rig = BuildRig(true, new[] { 100.0, 0.0, 0.05 });
            rig.Block.Calculate();
            TestKit.AssertWarningContains(rig.Block, "no lithium flow");
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TwentyConsecutiveRuns_AreIdentical(bool thermo11)
        {
            Rig rig = BuildRig(thermo11);
            TestKit.AssertTwentyRunsIdentical(rig.Block, () => TestKit.FlowsOf(rig.E));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void OutputParameters_MatchResultRows(bool thermo11)
        {
            Rig rig = BuildRig(thermo11);
            rig.Block.Calculate();
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "Li recovery"), TestKit.OutParam(rig.Block, "LiRecovery"), "LiRecovery");
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "Li captured"), TestKit.OutParam(rig.Block, "LiCaptured"), "LiCaptured");
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "Eluate Li concentration"), TestKit.OutParam(rig.Block, "EluateLiConc"), "EluateLiConc");
        }

        [Fact]
        public void PortsAndDefaults_AreFactoryGrade()
        {
            var block = new DirectLithiumExtraction();
            TestKit.AssertPortNames(block, "BrineFeed", "TreatedBrine", "Eluate", "WashWater");
            TestKit.AssertRealParametersOnly(block);
            var outputs = block.Parameters.Cast<CapeOpen.CapeParameter>()
                .Where(p => !UnitBase.IsInputParameter(p)).Select(p => p.ComponentName).ToList();
            foreach (string req in new[] { "LiRecovery", "Qstar", "Qcycle", "LiCaptured", "EluateLiConc", "MgCaptured" })
                Assert.Contains(req, outputs);
        }

        [Fact]
        public void ModelAndReferences_InReport()
        {
            Rig rig = BuildRig(true);
            rig.Block.Calculate();
            TestKit.AssertReportContains(rig.Block, "Model & References", "Langmuir", "Glueckauf");
        }
    }

    public class SxCapeOpenValidationTests
    {
        private static readonly string[] Ids = { "WATER", "LICL" };
        private static readonly double[] Mw = { 18.0153, 42.394 };
        private static readonly double[] Aq = { 50.0, 0.02 };
        private static readonly double[] Org = { 10.0, 0.0 };   // organic phase (index 0 as carrier)

        private sealed class Rig { public SolventExtraction Block; public object AIn, AOut, OIn, OOut; }

        private static Rig BuildRig(bool t11, double d = 5, double stages = 3, double oa = 1.0, double eff = 90)
        {
            var rig = new Rig
            {
                Block = new SolventExtraction(),
                AIn = TestKit.NewMock(t11, Ids, Mw, (double[])Aq.Clone()),
                AOut = TestKit.NewMock(t11, Ids, Mw),
                OIn = TestKit.NewMock(t11, Ids, Mw, (double[])Org.Clone()),
                OOut = TestKit.NewMock(t11, Ids, Mw),
            };
            TestKit.Set(rig.Block, "DistributionCoeff", d); TestKit.Set(rig.Block, "Stages", stages);
            TestKit.Set(rig.Block, "OAratio", oa); TestKit.Set(rig.Block, "StageEfficiency", eff);
            TestKit.Connect(rig.Block, "AqueousIn", rig.AIn); TestKit.Connect(rig.Block, "AqueousOut", rig.AOut);
            TestKit.Connect(rig.Block, "OrganicIn", rig.OIn); TestKit.Connect(rig.Block, "OrganicOut", rig.OOut);
            return rig;
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Block_MatchesEngine_Within01Percent(bool thermo11)
        {
            var spec = new SxModel.Spec { DistCoeff = 5, Stages = 3, OAratio = 1.0, StageEffPct = 90 };
            double frac = SxModel.ExtractedFraction(spec);
            Rig rig = BuildRig(thermo11);
            rig.Block.Calculate();

            TestKit.Close(frac * 100, TestKit.ResultOf(rig.Block, "Extraction efficiency"), "extraction");
            TestKit.Close(Aq[1] * frac, TestKit.ResultOf(rig.Block, "Solute extracted"), "extracted");

            double[] a = TestKit.FlowsOf(rig.AOut), o = TestKit.FlowsOf(rig.OOut);
            TestKit.Close(Aq[1] * (1 - frac), a[1], "raffinate solute");
            TestKit.AssertMassBalance(new[] { Aq, Org }, new[] { a, o }, 2);
        }

        [Fact]
        public void Kremser_ExactClosedForm_E2N3()
        {
            // (2^4 - 2)/(2^4 - 1) = 14/15 EXACTLY (Kremser 1930)
            TestKit.AssertExact(14.0 / 15.0, SxModel.KremserFraction(2.0, 3), "Kremser E=2 N=3");
        }

        [Fact]
        public void Kremser_UnityLimit_IsNOverNPlus1()
        {
            TestKit.AssertExact(3.0 / 4.0, SxModel.KremserFraction(1.0, 3), "E=1 limit");
            TestKit.AssertExact(5.0 / 6.0, SxModel.KremserFraction(1.0, 5), "E=1 limit N=5");
        }

        [Fact]
        public void SubUnityExtractionFactor_Warns()
        {
            Rig rig = BuildRig(true, 0.5, 3, 1.0, 90);   // E = 0.5
            rig.Block.Calculate();
            TestKit.AssertWarningContains(rig.Block, "below 1");
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TwentyConsecutiveRuns_AreIdentical(bool thermo11)
        {
            Rig rig = BuildRig(thermo11);
            TestKit.AssertTwentyRunsIdentical(rig.Block, () => TestKit.FlowsOf(rig.OOut));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void OutputParameters_MatchResultRows(bool thermo11)
        {
            Rig rig = BuildRig(thermo11);
            rig.Block.Calculate();
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "Extraction efficiency"), TestKit.OutParam(rig.Block, "Extraction"), "Extraction");
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "Extraction factor E"), TestKit.OutParam(rig.Block, "ExtFactor"), "ExtFactor");
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "Solute extracted"), TestKit.OutParam(rig.Block, "SoluteExtracted"), "SoluteExtracted");
        }

        [Fact]
        public void PortsAndDefaults_AreFactoryGrade()
        {
            var block = new SolventExtraction();
            TestKit.AssertPortNames(block, "AqueousIn", "AqueousOut", "OrganicIn", "OrganicOut");
            TestKit.AssertRealParametersOnly(block);   // Stages was an IntegerParameter — golden-rule fix
            var outputs = block.Parameters.Cast<CapeOpen.CapeParameter>()
                .Where(p => !UnitBase.IsInputParameter(p)).Select(p => p.ComponentName).ToList();
            foreach (string req in new[] { "Extraction", "ExtFactor", "SoluteExtracted" })
                Assert.Contains(req, outputs);
        }

        [Fact]
        public void ModelAndReferences_InReport()
        {
            Rig rig = BuildRig(true);
            rig.Block.Calculate();
            TestKit.AssertReportContains(rig.Block, "Model & References", "Kremser", "14/15");
        }
    }

    public class GacCapeOpenValidationTests
    {
        private static readonly string[] Ids = { "WATER", "PHENOL" };
        private static readonly double[] Mw = { 18.0153, 94.113 };
        private static readonly double[] Feed = { 100.0, 1e-5 };   // trace organic

        private static GacModel.Spec DefaultSpec()
        {
            return new GacModel.Spec { FreundlichK = 20, FreundlichInvN = 0.5, BedMassKg = 1000, EbctMin = 15, TauMin = 6 };
        }

        private static double FeedM3s() { return (Feed[0] * Mw[0] + Feed[1] * Mw[1]) / 1000.0 / 1000.0; }
        private static double C0MgL() { return Feed[1] * Mw[1] * 1000.0 / (FeedM3s() * 1000.0); }

        private sealed class Rig { public ActivatedCarbon Block; public object F, T; }

        private static Rig BuildRig(bool t11)
        {
            var rig = new Rig
            {
                Block = new ActivatedCarbon(),
                F = TestKit.NewMock(t11, Ids, Mw, (double[])Feed.Clone()),
                T = TestKit.NewMock(t11, Ids, Mw),
            };
            TestKit.Connect(rig.Block, "Feed", rig.F); TestKit.Connect(rig.Block, "Treated", rig.T);
            return rig;
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Block_MatchesEngine_Within01Percent(bool thermo11)
        {
            GacModel.Perf x = GacModel.Solve(DefaultSpec(), C0MgL(), FeedM3s());
            Rig rig = BuildRig(thermo11);
            rig.Block.Calculate();

            TestKit.Close(x.RemovalFrac * 100, TestKit.ResultOf(rig.Block, "Contaminant removal"), "removal");
            TestKit.Close(x.LogRemoval, TestKit.ResultOf(rig.Block, "Log removal"), "log");
            TestKit.Close(x.CurGL, TestKit.ResultOf(rig.Block, "Carbon usage rate"), "CUR");
            TestKit.Close(x.BedLifeDays, TestKit.ResultOf(rig.Block, "Bed life"), "bedLife");

            double[] t = TestKit.FlowsOf(rig.T);
            TestKit.AssertExact(Feed[0], t[0], "water passes");
            TestKit.Close(Feed[1] * (1 - x.RemovalFrac), t[1], "residual contaminant");
        }

        [Fact]
        public void Freundlich_AtUnitConcentration_EqualsK()
        {
            TestKit.AssertExact(20.0, GacModel.Freundlich(20, 0.5, 1.0), "q(1) = K");
        }

        [Fact]
        public void Removal_IsFirstOrderInContactTime()
        {
            GacModel.Perf x = GacModel.Solve(DefaultSpec(), C0MgL(), FeedM3s());
            TestKit.AssertExact(1.0 - Math.Exp(-15.0 / 6.0), x.RemovalFrac, "R closed form");
        }

        [Fact]
        public void CarbonUsageRate_IsCrittendenForm()
        {
            GacModel.Perf x = GacModel.Solve(DefaultSpec(), C0MgL(), FeedM3s());
            double q0 = 20.0 * Math.Pow(x.C0MgL, 0.5);
            TestKit.AssertExact((x.C0MgL - x.CeMgL) / q0, x.CurGL, "CUR = (C0-Ce)/q0");
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
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "Contaminant removal"), TestKit.OutParam(rig.Block, "Removal"), "Removal");
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "Carbon usage rate"), TestKit.OutParam(rig.Block, "CUR"), "CUR");
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "Bed life"), TestKit.OutParam(rig.Block, "BedLife"), "BedLife");
        }

        [Fact]
        public void PortsAndDefaults_AreFactoryGrade()
        {
            var block = new ActivatedCarbon();
            TestKit.AssertPortNames(block, "Feed", "Treated");
            TestKit.AssertRealParametersOnly(block);
            var outputs = block.Parameters.Cast<CapeOpen.CapeParameter>()
                .Where(p => !UnitBase.IsInputParameter(p)).Select(p => p.ComponentName).ToList();
            foreach (string req in new[] { "Removal", "LogRemoval", "AdsorbedLoad", "CUR", "BedLife" })
                Assert.Contains(req, outputs);
        }

        [Fact]
        public void ModelAndReferences_InReport()
        {
            Rig rig = BuildRig(true);
            rig.Block.Calculate();
            TestKit.AssertReportContains(rig.Block, "Model & References", "Freundlich", "usage rate");
        }
    }

    public class CrystCapeOpenValidationTests
    {
        private static readonly string[] Ids = { "WATER", "NACL" };
        private static readonly double[] Mw = { 18.0153, 58.442467 };
        // 100 g/s water + 50 g/s NaCl: 5.5509 mol/s water, 0.85554 mol/s NaCl
        private static readonly double[] Feed = { 100.0 / 18.0153, 50.0 / 58.442467 };

        private sealed class Rig { public Crystallizer Block; public object F, L, C; }

        private static Rig BuildRig(bool t11, double solubility = 36.0, double evap = 0, double[] feed = null)
        {
            var rig = new Rig
            {
                Block = new Crystallizer(),
                F = TestKit.NewMock(t11, Ids, Mw, (double[])(feed ?? Feed).Clone()),
                L = TestKit.NewMock(t11, Ids, Mw),
                C = TestKit.NewMock(t11, Ids, Mw),
            };
            TestKit.Set(rig.Block, "Solubility", solubility);
            TestKit.Set(rig.Block, "EvapFrac", evap);
            TestKit.Connect(rig.Block, "Feed", rig.F); TestKit.Connect(rig.Block, "MotherLiquor", rig.L);
            TestKit.Connect(rig.Block, "Crystals", rig.C);
            return rig;
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Block_MatchesEngine_Within01Percent(bool thermo11)
        {
            var spec = new CrystModel.Spec { SolubilityG100g = 36.0, EvapFrac = 0, TempC = 25 };
            CrystModel.Perf x = CrystModel.Solve(spec, Feed[1], Mw[1], Feed[0], Mw[0]);
            Rig rig = BuildRig(thermo11);
            rig.Block.Calculate();

            TestKit.Close(x.CrystalMolS, TestKit.ResultOf(rig.Block, "Crystal production"), "crystals");
            TestKit.Close(x.YieldFrac * 100, TestKit.ResultOf(rig.Block, "Crystallization yield"), "yield");

            double[] l = TestKit.FlowsOf(rig.L), c = TestKit.FlowsOf(rig.C);
            TestKit.Close(Feed[1] - x.CrystalMolS, l[1], "ML salt");
            TestKit.AssertExact(0.0, c[0], "crystals carry no water");
            TestKit.AssertMassBalance(new[] { Feed }, new[] { l, c }, 2);
        }

        [Fact]
        public void MullinYield_HandCase_100gWater_50gNaCl()
        {
            // saturation holds 36 g/100 g water -> 36 g/s stays dissolved, 14 g/s crystallizes
            var spec = new CrystModel.Spec { SolubilityG100g = 36.0, EvapFrac = 0, TempC = 25 };
            CrystModel.Perf x = CrystModel.Solve(spec, Feed[1], Mw[1], Feed[0], Mw[0]);
            TestKit.Close(14.0 / 58.442467, x.CrystalMolS, "14 g/s crystals");
            TestKit.Close(14.0 / 50.0, x.YieldFrac, "28% yield");
        }

        [Fact]
        public void Evaporation_RaisesYield()
        {
            var noEvap = new CrystModel.Spec { SolubilityG100g = 36.0, EvapFrac = 0, TempC = 25 };
            var evap = new CrystModel.Spec { SolubilityG100g = 36.0, EvapFrac = 0.5, TempC = 25 };
            double y0 = CrystModel.Solve(noEvap, Feed[1], Mw[1], Feed[0], Mw[0]).YieldFrac;
            double y1 = CrystModel.Solve(evap, Feed[1], Mw[1], Feed[0], Mw[0]).YieldFrac;
            Assert.True(y1 > y0, "evaporating solvent must raise the crystal yield");
            // closed form: crystals = salt - 0.36*water*(1-0.5)
            TestKit.Close((50.0 - 36.0 * 0.5) / 58.442467,
                CrystModel.Solve(evap, Feed[1], Mw[1], Feed[0], Mw[0]).CrystalMolS, "evap closed form");
        }

        [Fact]
        public void Undersaturated_NoCrystals_WithWarning()
        {
            var dilute = new[] { 100.0 / 18.0153, 10.0 / 58.442467 };   // 10 g salt per 100 g water < 36
            Rig rig = BuildRig(true, 36.0, 0, dilute);
            rig.Block.Calculate();
            TestKit.AssertWarningContains(rig.Block, "UNDERSATURATED");
            TestKit.AssertExact(0.0, TestKit.FlowsOf(rig.C)[1], "no crystals");
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TwentyConsecutiveRuns_AreIdentical(bool thermo11)
        {
            Rig rig = BuildRig(thermo11);
            TestKit.AssertTwentyRunsIdentical(rig.Block, () => TestKit.FlowsOf(rig.C));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void OutputParameters_MatchResultRows(bool thermo11)
        {
            Rig rig = BuildRig(thermo11);
            rig.Block.Calculate();
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "Crystal production"), TestKit.OutParam(rig.Block, "CrystalProd"), "CrystalProd");
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "Crystallization yield"), TestKit.OutParam(rig.Block, "Yield"), "Yield");
        }

        [Fact]
        public void PortsAndDefaults_AreFactoryGrade()
        {
            var block = new Crystallizer();
            TestKit.AssertPortNames(block, "Feed", "MotherLiquor", "Crystals");
            TestKit.AssertRealParametersOnly(block);
            var outputs = block.Parameters.Cast<CapeOpen.CapeParameter>()
                .Where(p => !UnitBase.IsInputParameter(p)).Select(p => p.ComponentName).ToList();
            foreach (string req in new[] { "CrystalProd", "Yield", "MLSaltConc", "VaporWater" })
                Assert.Contains(req, outputs);
        }

        [Fact]
        public void ModelAndReferences_InReport()
        {
            Rig rig = BuildRig(true);
            rig.Block.Calculate();
            TestKit.AssertReportContains(rig.Block, "Model & References", "Mullin", "SATURATED");
        }
    }

    public class PptCapeOpenValidationTests
    {
        private static readonly string[] Ids = { "WATER", "CACL2", "NAOH" };
        private static readonly double[] Mw = { 18.0153, 110.98, 39.997 };
        private static readonly double[] Feed = { 100.0, 0.01, 0.0 };
        private static readonly double[] ReagentAmple = { 10.0, 0.0, 0.05 };
        private static readonly double[] ReagentLean = { 10.0, 0.0, 0.002 };

        private sealed class Rig { public ChemicalPrecipitation Block; public object F, R, T, S; }

        private static Rig BuildRig(bool t11, double[] reagent)
        {
            var rig = new Rig
            {
                Block = new ChemicalPrecipitation(),
                F = TestKit.NewMock(t11, Ids, Mw, (double[])Feed.Clone()),
                R = TestKit.NewMock(t11, Ids, Mw, (double[])reagent.Clone()),
                T = TestKit.NewMock(t11, Ids, Mw),
                S = TestKit.NewMock(t11, Ids, Mw),
            };
            TestKit.Connect(rig.Block, "Feed", rig.F); TestKit.Connect(rig.Block, "Reagent", rig.R);
            TestKit.Connect(rig.Block, "Treated", rig.T); TestKit.Connect(rig.Block, "Sludge", rig.S);
            return rig;
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Block_MatchesEngine_Within01Percent(bool thermo11)
        {
            var spec = new PptModel.Spec { RemovalPct = 90, DoseMolMol = 1.1, pH = 10.5 };
            PptModel.Perf x = PptModel.Solve(spec, Feed[1], ReagentAmple[2]);
            Rig rig = BuildRig(thermo11, ReagentAmple);
            rig.Block.Calculate();

            TestKit.Close(x.AchievedRemovalPct, TestKit.ResultOf(rig.Block, "Achieved removal"), "removal");
            TestKit.Close(x.RemovedMolS, TestKit.ResultOf(rig.Block, "Precipitate formed"), "precipitate");
            TestKit.Close(x.ReagentConsumedMolS, TestKit.ResultOf(rig.Block, "Reagent consumed"), "reagentUsed");

            double[] t = TestKit.FlowsOf(rig.T), s = TestKit.FlowsOf(rig.S);
            TestKit.AssertMassBalance(new[] { Feed, ReagentAmple }, new[] { t, s }, 3);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Sludge_IsWet_AtTheConfiguredSolidsContent(bool thermo11)
        {
            // Regression net for the 2026-07-19 Aspen engine CRASH: a bone-dry
            // sludge (pure NaOH has no vapour-pressure data) killed the V14 engine
            // on the TP flash (RPC failure, isolated live: dry-NaOH crystals crash,
            // dry-MgCl2 pass, wet NaOH catholyte pass). The sludge must now carry
            // water at the SludgeSolids setting (default 20 wt% solids).
            Rig rig = BuildRig(thermo11, ReagentAmple);
            rig.Block.Calculate();

            double[] s = TestKit.FlowsOf(rig.S);
            Assert.True(s[0] > 0, "sludge must carry water (wet sludge)");
            double solidsKg = s[1] * Mw[1] / 1000.0 + s[2] * Mw[2] / 1000.0;
            double waterKg = s[0] * Mw[0] / 1000.0;
            double solidsFrac = solidsKg / (solidsKg + waterKg);
            Assert.InRange(solidsFrac, 0.199, 0.201);   // default SludgeSolids = 20%

            // and the wet split must still close the total mass balance
            double[] t = TestKit.FlowsOf(rig.T);
            TestKit.AssertMassBalance(new[] { Feed, ReagentAmple }, new[] { t, s }, 3);
        }

        [Fact]
        public void AmpleReagent_AchievesTarget_StoichConsumption()
        {
            var spec = new PptModel.Spec { RemovalPct = 90, DoseMolMol = 1.1, pH = 10.5 };
            PptModel.Perf x = PptModel.Solve(spec, Feed[1], ReagentAmple[2]);
            TestKit.AssertExact(Feed[1] * 0.9, x.RemovedMolS, "removed = eff*target");
            TestKit.AssertExact(Feed[1] * 0.9 * 1.1, x.ReagentConsumedMolS, "consumed = removed*dose");
            Assert.False(x.ReagentLimited);
        }

        [Fact]
        public void LeanReagent_IsLimited_Exactly_WithWarning()
        {
            var spec = new PptModel.Spec { RemovalPct = 90, DoseMolMol = 1.1, pH = 10.5 };
            PptModel.Perf x = PptModel.Solve(spec, Feed[1], ReagentLean[2]);
            Assert.True(x.ReagentLimited);
            TestKit.AssertExact(ReagentLean[2] / 1.1, x.RemovedMolS, "removed = reagent/dose");

            Rig rig = BuildRig(true, ReagentLean);
            rig.Block.Calculate();
            TestKit.AssertWarningContains(rig.Block, "Reagent-limited");
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TwentyConsecutiveRuns_AreIdentical(bool thermo11)
        {
            Rig rig = BuildRig(thermo11, ReagentAmple);
            TestKit.AssertTwentyRunsIdentical(rig.Block, () => TestKit.FlowsOf(rig.S));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void OutputParameters_MatchResultRows(bool thermo11)
        {
            Rig rig = BuildRig(thermo11, ReagentAmple);
            rig.Block.Calculate();
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "Achieved removal"), TestKit.OutParam(rig.Block, "Removal"), "Removal");
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "Precipitate formed"), TestKit.OutParam(rig.Block, "Precipitate"), "Precipitate");
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "Reagent consumed"), TestKit.OutParam(rig.Block, "ReagentUsed"), "ReagentUsed");
        }

        [Fact]
        public void PortsAndDefaults_AreFactoryGrade()
        {
            var block = new ChemicalPrecipitation();
            TestKit.AssertPortNames(block, "Feed", "Reagent", "Treated", "Sludge");
            TestKit.AssertRealParametersOnly(block);
            var outputs = block.Parameters.Cast<CapeOpen.CapeParameter>()
                .Where(p => !UnitBase.IsInputParameter(p)).Select(p => p.ComponentName).ToList();
            foreach (string req in new[] { "Removal", "Precipitate", "ReagentUsed", "SludgeFlow" })
                Assert.Contains(req, outputs);
        }

        [Fact]
        public void ModelAndReferences_InReport()
        {
            Rig rig = BuildRig(true, ReagentAmple);
            rig.Block.Calculate();
            TestKit.AssertReportContains(rig.Block, "Model & References", "Metcalf", "reagent-limited");
        }
    }
}
