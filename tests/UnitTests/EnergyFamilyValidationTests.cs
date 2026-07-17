using System;
using System.Linq;
using OPBlocks.Core;
using OPBlocks.Energy;
using Xunit;

namespace OPBlocks.UnitTests
{
    // =====================================================================
    //  Family E validation gates — OP-PEM, OP-AEL, OP-FC, OP-RPB, OP-UVAOP.
    // =====================================================================

    public class PemCapeOpenValidationTests
    {
        private static readonly string[] Ids = { "WATER", "H2", "O2" };
        private static readonly double[] Mw = { 18.0153, 2.01588, 31.9988 };
        private static readonly double[] Feed = { 50.0, 0, 0 };

        private static ElectrolyzerModel.Spec DefaultSpec()
        {
            return new ElectrolyzerModel.Spec
            {
                CellAreaM2 = 1.0, CurrentDensityAcm2 = 2.0, CellCount = 100,
                CellVoltageV = 1.9, FaradaicEffPct = 99,
            };
        }

        private sealed class Rig { public PemElectrolyzer Block; public object F, H, O; }

        private static Rig BuildRig(bool t11, double[] feed = null)
        {
            var rig = new Rig
            {
                Block = new PemElectrolyzer(),
                F = TestKit.NewMock(t11, Ids, Mw, (double[])(feed ?? Feed).Clone()),
                H = TestKit.NewMock(t11, Ids, Mw),
                O = TestKit.NewMock(t11, Ids, Mw),
            };
            TestKit.Connect(rig.Block, "WaterFeed", rig.F);
            TestKit.Connect(rig.Block, "Hydrogen", rig.H);
            TestKit.Connect(rig.Block, "Oxygen", rig.O);
            return rig;
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Block_MatchesEngine_Within01Percent(bool thermo11)
        {
            ElectrolyzerModel.Perf x = ElectrolyzerModel.Solve(DefaultSpec(), Feed[0]);
            Rig rig = BuildRig(thermo11);
            rig.Block.Calculate();

            TestKit.Close(x.H2MolS * ElectrolyzerModel.MwH2, TestKit.ResultOf(rig.Block, "H2 production"), "H2");
            TestKit.Close(x.StackPowerKW, TestKit.ResultOf(rig.Block, "Stack power"), "power");
            TestKit.Close(x.SecKWhKg, TestKit.ResultOf(rig.Block, "Specific energy"), "SEC");
            TestKit.Close(x.EffHhvPct, TestKit.ResultOf(rig.Block, "Stack efficiency (HHV)"), "effHHV");

            double[] h = TestKit.FlowsOf(rig.H), o = TestKit.FlowsOf(rig.O);
            TestKit.Close(x.H2MolS, h[1], "H2 stream");
            TestKit.Close(x.O2MolS, o[2], "O2 stream");
            // hydrogen atoms: 2*water_in = 2*water_out + 2*H2  (per mol bookkeeping)
            TestKit.Close(Feed[0], o[0] + x.H2MolS, "H2O consumed = H2 produced");
        }

        [Fact]
        public void Faraday_ExactAnchor_PerCell()
        {
            // per-cell current 2F at 100% -> exactly 1 mol/s H2 per cell
            var s = new ElectrolyzerModel.Spec
            {
                CellAreaM2 = 2.0 * ProcessOps.Faraday / 1e4, CurrentDensityAcm2 = 1.0,
                CellCount = 1, CellVoltageV = 1.9, FaradaicEffPct = 100,
            };
            ElectrolyzerModel.Perf x = ElectrolyzerModel.Solve(s, 1e9);
            TestKit.AssertExact(1.0, x.H2MolS, "1 mol/s H2");
            TestKit.AssertExact(0.5, x.O2MolS, "0.5 mol/s O2");
        }

        [Fact]
        public void Sec_ClosedForm_And_PublishedBand()
        {
            ElectrolyzerModel.Perf x = ElectrolyzerModel.Solve(DefaultSpec(), Feed[0]);
            TestKit.AssertExact(ElectrolyzerModel.SecPerVolt * 1.9 / 0.99, x.SecKWhKg, "SEC closed form");
            Assert.InRange(x.SecKWhKg, 45.0, 60.0);   // published PEM band at 1.9 V
        }

        [Fact]
        public void WaterLimited_Warns()
        {
            Rig rig = BuildRig(true, new[] { 0.01, 0.0, 0.0 });
            rig.Block.Calculate();
            TestKit.AssertWarningContains(rig.Block, "Water-limited");
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TwentyConsecutiveRuns_AreIdentical(bool thermo11)
        {
            Rig rig = BuildRig(thermo11);
            TestKit.AssertTwentyRunsIdentical(rig.Block, () => TestKit.FlowsOf(rig.H));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void OutputParameters_MatchResultRows(bool thermo11)
        {
            Rig rig = BuildRig(thermo11);
            rig.Block.Calculate();
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "H2 production"), TestKit.OutParam(rig.Block, "H2Production"), "H2Production");
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "Stack power"), TestKit.OutParam(rig.Block, "StackPower"), "StackPower");
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "Specific energy"), TestKit.OutParam(rig.Block, "SEC"), "SEC");
        }

        [Fact]
        public void PortsAndDefaults_AreFactoryGrade()
        {
            var block = new PemElectrolyzer();
            TestKit.AssertPortNames(block, "WaterFeed", "Hydrogen", "Oxygen");
            TestKit.AssertRealParametersOnly(block);   // CellCount was an IntegerParameter — golden-rule fix
            var outputs = block.Parameters.Cast<CapeOpen.CapeParameter>()
                .Where(p => !UnitBase.IsInputParameter(p)).Select(p => p.ComponentName).ToList();
            foreach (string req in new[] { "H2Production", "O2Production", "StackPower", "SEC", "EffHHV", "EffLHV" })
                Assert.Contains(req, outputs);
        }

        [Fact]
        public void ModelAndReferences_InReport()
        {
            Rig rig = BuildRig(true);
            rig.Block.Calculate();
            TestKit.AssertReportContains(rig.Block, "Model & References", "Carmo", "26.59");
        }
    }

    public class AelCapeOpenValidationTests
    {
        private static readonly string[] Ids = { "WATER", "H2", "O2", "KOH" };
        private static readonly double[] Mw = { 18.0153, 2.01588, 31.9988, 56.1056 };
        private static readonly double[] Feed = { 50.0, 0, 0, 5.0 };

        private static ElectrolyzerModel.Spec DefaultSpec()
        {
            return new ElectrolyzerModel.Spec
            {
                CellAreaM2 = 2.0, CurrentDensityAcm2 = 0.4, CellCount = 100,
                CellVoltageV = 1.9, FaradaicEffPct = 99,
            };
        }

        private sealed class Rig { public AlkalineElectrolyzer Block; public object F, H, O; }

        private static Rig BuildRig(bool t11)
        {
            var rig = new Rig
            {
                Block = new AlkalineElectrolyzer(),
                F = TestKit.NewMock(t11, Ids, Mw, (double[])Feed.Clone()),
                H = TestKit.NewMock(t11, Ids, Mw),
                O = TestKit.NewMock(t11, Ids, Mw),
            };
            TestKit.Connect(rig.Block, "WaterFeed", rig.F);
            TestKit.Connect(rig.Block, "Hydrogen", rig.H);
            TestKit.Connect(rig.Block, "Oxygen", rig.O);
            return rig;
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Block_MatchesEngine_Within01Percent(bool thermo11)
        {
            ElectrolyzerModel.Perf x = ElectrolyzerModel.Solve(DefaultSpec(), Feed[0]);
            Rig rig = BuildRig(thermo11);
            rig.Block.Calculate();

            TestKit.Close(x.H2MolS * ElectrolyzerModel.MwH2, TestKit.ResultOf(rig.Block, "H2 production"), "H2");
            TestKit.Close(x.StackPowerKW, TestKit.ResultOf(rig.Block, "Stack power"), "power");
            TestKit.Close(x.SecKWhKg, TestKit.ResultOf(rig.Block, "Specific energy"), "SEC");

            double[] h = TestKit.FlowsOf(rig.H), o = TestKit.FlowsOf(rig.O);
            TestKit.AssertExact(Feed[3], o[3], "KOH stays in the anolyte stream");
            TestKit.AssertExact(0.0, h[3], "no KOH in the H2 product");
        }

        [Fact]
        public void SharedEngine_SameSpec_SameAnswer()
        {
            // AEL and PEM share ElectrolyzerModel — same spec must give the same physics
            ElectrolyzerModel.Perf a = ElectrolyzerModel.Solve(DefaultSpec(), Feed[0]);
            ElectrolyzerModel.Perf b = ElectrolyzerModel.Solve(DefaultSpec(), Feed[0]);
            TestKit.AssertExact(a.H2MolS, b.H2MolS, "deterministic shared engine");
            Assert.InRange(a.SecKWhKg, 45.0, 60.0);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TwentyConsecutiveRuns_AreIdentical(bool thermo11)
        {
            Rig rig = BuildRig(thermo11);
            TestKit.AssertTwentyRunsIdentical(rig.Block, () => TestKit.FlowsOf(rig.H));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void OutputParameters_MatchResultRows(bool thermo11)
        {
            Rig rig = BuildRig(thermo11);
            rig.Block.Calculate();
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "H2 production"), TestKit.OutParam(rig.Block, "H2Production"), "H2Production");
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "Specific energy"), TestKit.OutParam(rig.Block, "SEC"), "SEC");
        }

        [Fact]
        public void PortsAndDefaults_AreFactoryGrade()
        {
            var block = new AlkalineElectrolyzer();
            TestKit.AssertPortNames(block, "WaterFeed", "Hydrogen", "Oxygen");
            TestKit.AssertRealParametersOnly(block);   // CellCount golden-rule fix
            var outputs = block.Parameters.Cast<CapeOpen.CapeParameter>()
                .Where(p => !UnitBase.IsInputParameter(p)).Select(p => p.ComponentName).ToList();
            foreach (string req in new[] { "H2Production", "O2Production", "StackPower", "SEC", "EffHHV" })
                Assert.Contains(req, outputs);
        }

        [Fact]
        public void ModelAndReferences_InReport()
        {
            Rig rig = BuildRig(true);
            rig.Block.Calculate();
            TestKit.AssertReportContains(rig.Block, "Model & References", "Ursua", "Faradaic");
        }
    }

    public class FcCapeOpenValidationTests
    {
        private static readonly string[] Ids = { "WATER", "H2", "O2", "N2" };
        private static readonly double[] Mw = { 18.0153, 2.01588, 31.9988, 28.0134 };
        private static readonly double[] Fuel = { 0, 1.0, 0, 0 };
        private static readonly double[] Air = { 0, 0, 1.0, 3.76 };

        private sealed class Rig { public FuelCell Block; public object H, A, E; }

        private static Rig BuildRig(bool t11, double[] air = null)
        {
            var rig = new Rig
            {
                Block = new FuelCell(),
                H = TestKit.NewMock(t11, Ids, Mw, (double[])Fuel.Clone()),
                A = TestKit.NewMock(t11, Ids, Mw, (double[])(air ?? Air).Clone()),
                E = TestKit.NewMock(t11, Ids, Mw),
            };
            TestKit.Connect(rig.Block, "HydrogenIn", rig.H);
            TestKit.Connect(rig.Block, "AirIn", rig.A);
            TestKit.Connect(rig.Block, "Exhaust", rig.E);
            return rig;
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Block_MatchesEngine_Within01Percent(bool thermo11)
        {
            var spec = new FcModel.Spec { UtilizationPct = 85, CellVoltageV = 0.68 };
            FcModel.Perf x = FcModel.Solve(spec, Fuel[1], Air[2]);
            Rig rig = BuildRig(thermo11);
            rig.Block.Calculate();

            TestKit.Close(x.PowerKW, TestKit.ResultOf(rig.Block, "Power output"), "power");
            TestKit.Close(x.H2ConsMolS * FcModel.MwH2, TestKit.ResultOf(rig.Block, "H2 consumed"), "H2");
            TestKit.Close(x.EffLhvPct, TestKit.ResultOf(rig.Block, "Electrical efficiency (LHV)"), "eff");

            double[] e = TestKit.FlowsOf(rig.E);
            TestKit.Close(Fuel[1] - x.H2ConsMolS, e[1], "slip H2");
            TestKit.Close(Air[2] - x.O2ConsMolS, e[2], "depleted O2");
            TestKit.Close(x.WaterProdMolS, e[0], "product water");
            TestKit.AssertExact(Air[3], e[3], "inert N2 passes");
        }

        [Fact]
        public void Faraday_And_Power_ExactAnchors()
        {
            var spec = new FcModel.Spec { UtilizationPct = 100, CellVoltageV = 0.68 };
            FcModel.Perf x = FcModel.Solve(spec, 1.0, 10.0);
            TestKit.AssertExact(2.0 * ProcessOps.Faraday, x.CurrentA, "I = 2F per mol/s H2");
            TestKit.AssertExact(0.68 * 2.0 * ProcessOps.Faraday / 1000.0, x.PowerKW, "P = V*I");
        }

        [Fact]
        public void VoltageEfficiency_LHV_ClosedForm()
        {
            // O'Hayre: eff_LHV = V/1.253 -> 0.68 V = 54.27%
            var spec = new FcModel.Spec { UtilizationPct = 85, CellVoltageV = 0.68 };
            FcModel.Perf x = FcModel.Solve(spec, Fuel[1], Air[2]);
            TestKit.AssertExact(0.68 / 1.253 * 100.0, x.EffLhvPct, "eff closed form");
            Assert.InRange(x.EffLhvPct, 50.0, 60.0);
        }

        [Fact]
        public void AirLimited_Capped_WithWarning()
        {
            Rig rig = BuildRig(true, new[] { 0.0, 0.0, 0.1, 0.376 });   // starved O2
            rig.Block.Calculate();
            TestKit.AssertWarningContains(rig.Block, "Air-limited");
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
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "Power output"), TestKit.OutParam(rig.Block, "Power"), "Power");
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "H2 consumed"), TestKit.OutParam(rig.Block, "H2Consumed"), "H2Consumed");
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "Water produced"), TestKit.OutParam(rig.Block, "WaterProduced"), "WaterProduced");
        }

        [Fact]
        public void PortsAndDefaults_AreFactoryGrade()
        {
            var block = new FuelCell();
            TestKit.AssertPortNames(block, "HydrogenIn", "AirIn", "Exhaust");
            TestKit.AssertRealParametersOnly(block);
            var outputs = block.Parameters.Cast<CapeOpen.CapeParameter>()
                .Where(p => !UnitBase.IsInputParameter(p)).Select(p => p.ComponentName).ToList();
            foreach (string req in new[] { "Power", "H2Consumed", "EffLHV", "WaterProduced" })
                Assert.Contains(req, outputs);
        }

        [Fact]
        public void ModelAndReferences_InReport()
        {
            Rig rig = BuildRig(true);
            rig.Block.Calculate();
            TestKit.AssertReportContains(rig.Block, "Model & References", "O'Hayre", "1.253");
        }
    }

    public class RpbCapeOpenValidationTests
    {
        private static readonly string[] Ids = { "N2", "CO2", "WATER" };
        private static readonly double[] Mw = { 28.0134, 44.0095, 18.0153 };
        private static readonly double[] Gas = { 9.0, 1.0, 0.0 };
        private static readonly double[] Liq = { 0.0, 0.0, 50.0 };

        private sealed class Rig { public RotatingPackedBed Block; public object GIn, GOut, LIn, LOut; }

        private static Rig BuildRig(bool t11, double rpm = 1000)
        {
            var rig = new Rig
            {
                Block = new RotatingPackedBed(),
                GIn = TestKit.NewMock(t11, Ids, Mw, (double[])Gas.Clone()),
                GOut = TestKit.NewMock(t11, Ids, Mw),
                LIn = TestKit.NewMock(t11, Ids, Mw, (double[])Liq.Clone()),
                LOut = TestKit.NewMock(t11, Ids, Mw),
            };
            TestKit.Set(rig.Block, "RotorSpeed", rpm);
            TestKit.Connect(rig.Block, "GasIn", rig.GIn); TestKit.Connect(rig.Block, "GasOut", rig.GOut);
            TestKit.Connect(rig.Block, "LiquidIn", rig.LIn); TestKit.Connect(rig.Block, "LiquidOut", rig.LOut);
            return rig;
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Block_MatchesEngine_Within01Percent(bool thermo11)
        {
            var spec = new RpbModel.Spec { RotorRpm = 1000, KlaCal = 0.02, RotorPowerCoeff = 5e-7 };
            RpbModel.Perf x = RpbModel.Solve(spec);
            Rig rig = BuildRig(thermo11);
            rig.Block.Calculate();

            TestKit.Close(x.RemovalFrac * 100, TestKit.ResultOf(rig.Block, "Solute removal"), "removal");
            TestKit.Close(x.Ntu, TestKit.ResultOf(rig.Block, "NTU"), "NTU");
            TestKit.Close(Gas[1] * x.RemovalFrac, TestKit.ResultOf(rig.Block, "Solute absorbed"), "absorbed");

            double[] g = TestKit.FlowsOf(rig.GOut), l = TestKit.FlowsOf(rig.LOut);
            TestKit.Close(Gas[1] * (1 - x.RemovalFrac), g[1], "CO2 in gas out");
            TestKit.Close(Gas[1] * x.RemovalFrac, l[1], "CO2 in liquid out");
            TestKit.AssertExact(Gas[0], g[0], "inert N2 passes");
            TestKit.AssertMassBalance(new[] { Gas, Liq }, new[] { g, l }, 3);
        }

        [Fact]
        public void Ntu_And_Removal_ClosedForms()
        {
            var spec = new RpbModel.Spec { RotorRpm = 1000, KlaCal = 0.02, RotorPowerCoeff = 5e-7 };
            RpbModel.Perf x = RpbModel.Solve(spec);
            TestKit.AssertExact(0.02 * Math.Sqrt(1000.0), x.Ntu, "NTU = k*sqrt(rpm)");
            TestKit.AssertExact(1.0 - Math.Exp(-x.Ntu), x.RemovalFrac, "removal = 1-exp(-NTU)");
            TestKit.AssertExact(5e-7 * 1000.0 * 1000.0, x.RotorKW, "rotor power ~ rpm^2");
        }

        [Fact]
        public void HigherSpeed_MoreRemoval()
        {
            var lo = RpbModel.Solve(new RpbModel.Spec { RotorRpm = 500, KlaCal = 0.02, RotorPowerCoeff = 5e-7 });
            var hi = RpbModel.Solve(new RpbModel.Spec { RotorRpm = 2000, KlaCal = 0.02, RotorPowerCoeff = 5e-7 });
            Assert.True(hi.RemovalFrac > lo.RemovalFrac, "HiGee intensification with speed");
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TwentyConsecutiveRuns_AreIdentical(bool thermo11)
        {
            Rig rig = BuildRig(thermo11);
            TestKit.AssertTwentyRunsIdentical(rig.Block, () => TestKit.FlowsOf(rig.LOut));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void OutputParameters_MatchResultRows(bool thermo11)
        {
            Rig rig = BuildRig(thermo11);
            rig.Block.Calculate();
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "Solute removal"), TestKit.OutParam(rig.Block, "Removal"), "Removal");
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "NTU"), TestKit.OutParam(rig.Block, "NTU"), "NTU");
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "Solute absorbed"), TestKit.OutParam(rig.Block, "Absorbed"), "Absorbed");
        }

        [Fact]
        public void PortsAndDefaults_AreFactoryGrade()
        {
            var block = new RotatingPackedBed();
            TestKit.AssertPortNames(block, "GasIn", "GasOut", "LiquidIn", "LiquidOut");
            TestKit.AssertRealParametersOnly(block);
            var outputs = block.Parameters.Cast<CapeOpen.CapeParameter>()
                .Where(p => !UnitBase.IsInputParameter(p)).Select(p => p.ComponentName).ToList();
            foreach (string req in new[] { "Removal", "NTU", "Absorbed", "RotorPower" })
                Assert.Contains(req, outputs);
        }

        [Fact]
        public void ModelAndReferences_InReport()
        {
            Rig rig = BuildRig(true);
            rig.Block.Calculate();
            TestKit.AssertReportContains(rig.Block, "Model & References", "Ramshaw", "NTU");
        }
    }

    public class UvAopCapeOpenValidationTests
    {
        private static readonly string[] Ids = { "WATER", "ATRAZINE" };
        private static readonly double[] Mw = { 18.0153, 215.68 };
        private static readonly double[] Feed = { 100.0, 1e-6 };

        private static UvAopModel.Spec DefaultSpec()
        {
            return new UvAopModel.Spec { UvDoseMJcm2 = 800, RateKcm2mJ = 0.003, UvtPct = 90, H2o2MgL = 5, LampPowerKW = 10 };
        }

        private static double FlowM3h() { return (Feed[0] * Mw[0] + Feed[1] * Mw[1]) / 1000.0 / 1000.0 * 3600.0; }

        private sealed class Rig { public UvAopReactor Block; public object F, T; }

        private static Rig BuildRig(bool t11)
        {
            var rig = new Rig
            {
                Block = new UvAopReactor(),
                F = TestKit.NewMock(t11, Ids, Mw, (double[])Feed.Clone()),
                T = TestKit.NewMock(t11, Ids, Mw),
            };
            TestKit.Connect(rig.Block, "LiquidIn", rig.F); TestKit.Connect(rig.Block, "LiquidOut", rig.T);
            return rig;
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Block_MatchesEngine_Within01Percent(bool thermo11)
        {
            UvAopModel.Perf x = UvAopModel.Solve(DefaultSpec(), FlowM3h());
            Rig rig = BuildRig(thermo11);
            rig.Block.Calculate();

            TestKit.Close(x.DestroyFrac * 100, TestKit.ResultOf(rig.Block, "Contaminant destruction"), "destruction");
            TestKit.Close(x.LogRemoval, TestKit.ResultOf(rig.Block, "Log removal"), "log");
            TestKit.Close(x.EeoKWhM3Order, TestKit.ResultOf(rig.Block, "EEO"), "EEO");
            TestKit.Close(x.EffDoseMJcm2, TestKit.ResultOf(rig.Block, "Effective UV dose"), "effDose");

            double[] t = TestKit.FlowsOf(rig.T);
            TestKit.AssertExact(Feed[0], t[0], "water passes");
            TestKit.Close(Feed[1] * (1 - x.DestroyFrac), t[1], "residual contaminant");
        }

        [Fact]
        public void LogRemoval_And_FirstOrder_AreConsistent()
        {
            // log removal = k*D/ln10 EXACTLY equals -log10(1-destroyed)
            UvAopModel.Perf x = UvAopModel.Solve(DefaultSpec(), FlowM3h());
            TestKit.AssertExact(x.LogRemoval, -Math.Log10(1.0 - x.DestroyFrac), "first-order identity");
        }

        [Fact]
        public void EffectiveDose_ClosedForm_H2o2Boost()
        {
            UvAopModel.Perf x = UvAopModel.Solve(DefaultSpec(), FlowM3h());
            TestKit.AssertExact(800.0 * 0.9 * (1.0 + 5.0 / 20.0), x.EffDoseMJcm2, "D_eff closed form");
            var spec20 = DefaultSpec(); spec20.H2o2MgL = 20;
            TestKit.AssertExact(800.0 * 0.9 * 2.0, UvAopModel.Solve(spec20, FlowM3h()).EffDoseMJcm2,
                "20 mg/L doubles the effective dose");
        }

        [Fact]
        public void Eeo_IsBoltonDefinition_Exactly()
        {
            UvAopModel.Perf x = UvAopModel.Solve(DefaultSpec(), FlowM3h());
            TestKit.AssertExact(10.0 / (FlowM3h() * x.LogRemoval), x.EeoKWhM3Order, "EEO = P/(Q*log)");
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
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "Contaminant destruction"), TestKit.OutParam(rig.Block, "Destruction"), "Destruction");
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "Log removal"), TestKit.OutParam(rig.Block, "LogRemoval"), "LogRemoval");
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "EEO"), TestKit.OutParam(rig.Block, "EEO"), "EEO");
        }

        [Fact]
        public void PortsAndDefaults_AreFactoryGrade()
        {
            var block = new UvAopReactor();
            TestKit.AssertPortNames(block, "LiquidIn", "LiquidOut");
            TestKit.AssertRealParametersOnly(block);
            var outputs = block.Parameters.Cast<CapeOpen.CapeParameter>()
                .Where(p => !UnitBase.IsInputParameter(p)).Select(p => p.ComponentName).ToList();
            foreach (string req in new[] { "Destruction", "LogRemoval", "EEO", "EffDose" })
                Assert.Contains(req, outputs);
        }

        [Fact]
        public void ModelAndReferences_InReport()
        {
            Rig rig = BuildRig(true);
            rig.Block.Calculate();
            TestKit.AssertReportContains(rig.Block, "Model & References", "Bolton", "EEO");
        }
    }
}
