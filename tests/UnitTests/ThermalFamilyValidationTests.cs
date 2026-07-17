using System;
using System.Collections.Generic;
using System.Linq;
using OPBlocks.Core;
using OPBlocks.Desalination;
using Xunit;

namespace OPBlocks.UnitTests
{
    // =====================================================================
    //  Family A validation gates — OP-MD, OP-MED, OP-MSF, OP-MVC.
    //  Same factory pattern as OP-RO/NF: structural block==engine (both
    //  thermo mocks), physical anchors vs published data, determinism,
    //  results==streams, RealParameter-only, Model&References.
    // =====================================================================

    public class MdCapeOpenValidationTests
    {
        private const double MwWater = 18.0153, MwNaCl = 58.442467;
        private static readonly string[] Ids = { "WATER", "NACL" };
        private static readonly double[] Mw = { MwWater, MwNaCl };

        public sealed class MdCase
        {
            public string Name;
            public double Area = 1.0, K = 3.5e-4, Poro = 0.75, Pore = 0.2, Tort = 1.5, Thick = 100,
                          Aw = 0.96, MaxTr = 95, ThC = 60, TcC = 20;
            public double[] Hot = { 10.0, 0.01 };
            public double[] Cold = { 10.0, 0.0 };
            public override string ToString() { return Name; }

            public MdModel.Spec Spec()
            {
                return new MdModel.Spec
                {
                    AreaM2 = Area, Kcal = K, PorosityFrac = Poro, PoreDiaUm = Pore,
                    TortuosityFac = Tort, ThicknessUm = Thick, HotActivity = Aw, MaxTransferPct = MaxTr,
                };
            }
        }

        public static IReadOnlyList<MdCase> Cases()
        {
            return new List<MdCase>
            {
                new MdCase { Name = "standard-60-20" },
                new MdCase { Name = "no-gradient", ThC = 25, TcC = 25, Aw = 1.0 },
                new MdCase { Name = "oversized-capped", Area = 5000, Hot = new[]{1.0, 0.001} },
            };
        }

        public static IEnumerable<object[]> CaseNames()
        {
            foreach (MdCase c in Cases()) { yield return new object[] { c.Name, false }; yield return new object[] { c.Name, true }; }
        }

        private static MdModel.Flux Reference(MdCase c)
        {
            return MdModel.Solve(c.Spec(), c.Hot[0], c.ThC + 273.15, c.TcC + 273.15);
        }

        private sealed class Rig { public MembraneDistillation Block; public object HIn, HOut, CIn, COut; }

        private static Rig BuildRig(MdCase c, bool t11)
        {
            var rig = new Rig
            {
                Block = new MembraneDistillation(),
                HIn = TestKit.NewMock(t11, Ids, Mw, (double[])c.Hot.Clone(), c.ThC + 273.15),
                HOut = TestKit.NewMock(t11, Ids, Mw),
                CIn = TestKit.NewMock(t11, Ids, Mw, (double[])c.Cold.Clone(), c.TcC + 273.15),
                COut = TestKit.NewMock(t11, Ids, Mw),
            };
            TestKit.Set(rig.Block, "Area", c.Area); TestKit.Set(rig.Block, "Kmembrane", c.K);
            TestKit.Set(rig.Block, "Porosity", c.Poro); TestKit.Set(rig.Block, "PoreDia", c.Pore);
            TestKit.Set(rig.Block, "Tortuosity", c.Tort); TestKit.Set(rig.Block, "Thickness", c.Thick);
            TestKit.Set(rig.Block, "HotActivity", c.Aw); TestKit.Set(rig.Block, "MaxTransfer", c.MaxTr);
            TestKit.Connect(rig.Block, "HotIn", rig.HIn); TestKit.Connect(rig.Block, "HotOut", rig.HOut);
            TestKit.Connect(rig.Block, "ColdIn", rig.CIn); TestKit.Connect(rig.Block, "ColdOut", rig.COut);
            return rig;
        }

        [Theory]
        [MemberData(nameof(CaseNames))]
        public void Case_BlockMatchesReference_Within01Percent(string name, bool thermo11)
        {
            MdCase c = Cases().First(x => x.Name == name);
            MdModel.Flux x = Reference(c);
            Rig rig = BuildRig(c, thermo11);
            rig.Block.Calculate();

            TestKit.Close(x.FluxLMH, TestKit.ResultOf(rig.Block, "Permeate flux"), "flux");
            TestKit.Close(x.WaterMolS * 0.0180153, TestKit.ResultOf(rig.Block, "Permeate rate"), "rate");
            TestKit.Close(x.BmKgM2sPa, TestKit.ResultOf(rig.Block, "Membrane coefficient Bm"), "Bm");
            TestKit.Close(x.DrivingPa, TestKit.ResultOf(rig.Block, "Vapour-pressure driving force"), "driving");

            double[] ho = TestKit.FlowsOf(rig.HOut), co = TestKit.FlowsOf(rig.COut);
            TestKit.Close(c.Hot[0] - x.WaterMolS, ho[0], "hot water out");
            TestKit.Close(c.Cold[0] + x.WaterMolS, co[0], "cold water out");
            TestKit.AssertExact(c.Hot[1], ho[1], "salt stays hot side");
            TestKit.AssertMassBalance(new[] { c.Hot, c.Cold }, new[] { ho, co }, 2);
        }

        [Fact]
        public void Antoine_Psat_MatchesSteamTables()
        {
            // steam tables: Psat(60 C) = 19.95 kPa, Psat(20 C) = 2.339 kPa (±1%)
            Assert.InRange(ProcessOps.PsatWaterPa(60.0), 19750, 20150);
            Assert.InRange(ProcessOps.PsatWaterPa(20.0), 2315, 2365);
        }

        [Fact]
        public void NoGradient_ZeroFlux_WithWarning()
        {
            MdCase c = Cases().First(x => x.Name == "no-gradient");
            MdModel.Flux x = Reference(c);
            Assert.Equal(0.0, x.JKgM2s, 15);
            Rig rig = BuildRig(c, true);
            rig.Block.Calculate();
            TestKit.AssertWarningContains(rig.Block, "driving force");
        }

        [Fact]
        public void LowerWaterActivity_ReducesFlux()
        {
            var pure = new MdCase { Name = "aw1", Aw = 1.0 };
            var brine = new MdCase { Name = "aw09", Aw = 0.90 };
            Assert.True(Reference(brine).JKgM2s < Reference(pure).JKgM2s,
                "brine (lower a_w) must evaporate slower");
        }

        [Fact]
        public void Bm_ScalesWithMembraneStructure()
        {
            MdCase c = Cases().First(x => x.Name == "standard-60-20");
            MdModel.Flux x = Reference(c);
            TestKit.AssertExact(c.K * c.Poro * c.Pore / (c.Tort * c.Thick), x.BmKgM2sPa, "Bm formula");
            Assert.InRange(x.FluxLMH, 5.0, 60.0);   // published DCMD band at 60/20 C
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TwentyConsecutiveRuns_AreIdentical(bool thermo11)
        {
            Rig rig = BuildRig(Cases().First(x => x.Name == "standard-60-20"), thermo11);
            TestKit.AssertTwentyRunsIdentical(rig.Block, () => TestKit.FlowsOf(rig.COut));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void OutputParameters_MatchResultRows(bool thermo11)
        {
            Rig rig = BuildRig(Cases().First(x => x.Name == "standard-60-20"), thermo11);
            rig.Block.Calculate();
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "Permeate flux"), TestKit.OutParam(rig.Block, "PermFlux"), "PermFlux");
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "Permeate rate"), TestKit.OutParam(rig.Block, "PermRate"), "PermRate");
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "Latent heat duty"), TestKit.OutParam(rig.Block, "LatentDuty"), "LatentDuty");
            Assert.True(TestKit.FlashCountOf(rig.HOut) > 0 && TestKit.FlashCountOf(rig.COut) > 0, "both outlets flashed");
        }

        [Fact]
        public void PortsAndDefaults_AreFactoryGrade()
        {
            var block = new MembraneDistillation();
            TestKit.AssertPortNames(block, "HotIn", "HotOut", "ColdIn", "ColdOut");
            TestKit.AssertRealParametersOnly(block);
            var outputs = block.Parameters.Cast<CapeOpen.CapeParameter>()
                .Where(p => !UnitBase.IsInputParameter(p)).Select(p => p.ComponentName).ToList();
            foreach (string req in new[] { "PermFlux", "PermRate", "Bm", "DrivingForce", "LatentDuty" })
                Assert.Contains(req, outputs);
        }

        [Fact]
        public void ModelAndReferences_InReport()
        {
            Rig rig = BuildRig(Cases().First(x => x.Name == "standard-60-20"), true);
            rig.Block.Calculate();
            TestKit.AssertReportContains(rig.Block, "Model & References", "Schofield", "Psat");
        }
    }

    public class MedCapeOpenValidationTests
    {
        private const double MwWater = 18.0153, MwNaCl = 58.442467;
        private static readonly string[] Ids = { "WATER", "NACL" };
        private static readonly double[] Mw = { MwWater, MwNaCl };
        private static readonly double[] Feed = { 100.0, 1.1 };   // ~35,000 ppm seawater

        private sealed class Rig { public MultiEffectDistillation Block; public object F, D, B; }

        private static Rig BuildRig(bool t11, double nEffects = 8, double rec = 40, double tbt = 65)
        {
            var rig = new Rig
            {
                Block = new MultiEffectDistillation(),
                F = TestKit.NewMock(t11, Ids, Mw, (double[])Feed.Clone()),
                D = TestKit.NewMock(t11, Ids, Mw),
                B = TestKit.NewMock(t11, Ids, Mw),
            };
            TestKit.Set(rig.Block, "NEffects", nEffects);
            TestKit.Set(rig.Block, "WaterRecovery", rec);
            TestKit.Set(rig.Block, "TopBrineTemp", tbt);
            TestKit.Connect(rig.Block, "Feed", rig.F);
            TestKit.Connect(rig.Block, "Distillate", rig.D);
            TestKit.Connect(rig.Block, "Brine", rig.B);
            return rig;
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Block_MatchesEngine_Within01Percent(bool thermo11)
        {
            var spec = new MedModel.Spec { NEffects = 8, RecoveryPct = 40, TopBrineTempC = 65, GorPerEffect = 0.85, LatentKJKg = 2326 };
            MedModel.Perf perf = MedModel.Solve(spec, Feed[0]);
            Rig rig = BuildRig(thermo11);
            rig.Block.Calculate();

            TestKit.Close(perf.DistM3h, TestKit.ResultOf(rig.Block, "Distillate flow"), "distFlow");
            TestKit.Close(perf.Gor, TestKit.ResultOf(rig.Block, "Gained Output Ratio (GOR)"), "GOR");
            TestKit.Close(perf.SteamKW, TestKit.ResultOf(rig.Block, "Motive steam duty"), "steam");
            TestKit.Close(perf.SteKWhM3, TestKit.ResultOf(rig.Block, "Specific thermal energy"), "STE");

            double[] d = TestKit.FlowsOf(rig.D), b = TestKit.FlowsOf(rig.B);
            TestKit.Close(perf.DistWaterMolS, d[0], "distillate water");
            TestKit.AssertExact(0.0, d[1], "distillate is salt-free");
            TestKit.AssertExact(Feed[1], b[1], "brine keeps all salt");
            TestKit.AssertMassBalance(new[] { Feed }, new[] { d, b }, 2);
        }

        [Fact]
        public void Gor_IsPointEightFive_TimesEffects_Exactly()
        {
            // El-Dessouky & Ettouney: GOR ~ 0.85 N -> N=8 gives exactly 6.8 in this model
            var spec = new MedModel.Spec { NEffects = 8, RecoveryPct = 40, TopBrineTempC = 65, GorPerEffect = 0.85, LatentKJKg = 2326 };
            TestKit.AssertExact(6.8, MedModel.Solve(spec, Feed[0]).Gor, "GOR");
        }

        [Fact]
        public void Ste_MatchesPublishedMedBand()
        {
            // GOR 6.8 -> STE = lambda/(3.6*GOR) ~ 95 kWh/m3 (published 60-110 for GOR 6-10)
            var spec = new MedModel.Spec { NEffects = 8, RecoveryPct = 40, TopBrineTempC = 65, GorPerEffect = 0.85, LatentKJKg = 2326 };
            MedModel.Perf p = MedModel.Solve(spec, Feed[0]);
            Assert.InRange(p.SteKWhM3, 60.0, 110.0);
            TestKit.Close(2326.0 / (3.6 * p.Gor), p.SteKWhM3, "STE closed form");
        }

        [Fact]
        public void HighTbt_Warns_ScalingRisk()
        {
            Rig rig = BuildRig(true, 8, 40, 75);
            rig.Block.Calculate();
            TestKit.AssertWarningContains(rig.Block, "scaling");
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TwentyConsecutiveRuns_AreIdentical(bool thermo11)
        {
            Rig rig = BuildRig(thermo11);
            TestKit.AssertTwentyRunsIdentical(rig.Block, () => TestKit.FlowsOf(rig.D));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void OutputParameters_MatchResultRows(bool thermo11)
        {
            Rig rig = BuildRig(thermo11);
            rig.Block.Calculate();
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "Distillate flow"), TestKit.OutParam(rig.Block, "DistFlow"), "DistFlow");
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "Gained Output Ratio (GOR)"), TestKit.OutParam(rig.Block, "GOR"), "GOR");
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "Specific thermal energy"), TestKit.OutParam(rig.Block, "STE"), "STE");
        }

        [Fact]
        public void PortsAndDefaults_AreFactoryGrade()
        {
            var block = new MultiEffectDistillation();
            TestKit.AssertPortNames(block, "Feed", "Distillate", "Brine");
            TestKit.AssertRealParametersOnly(block);   // NEffects is now a REAL integer code
            var outputs = block.Parameters.Cast<CapeOpen.CapeParameter>()
                .Where(p => !UnitBase.IsInputParameter(p)).Select(p => p.ComponentName).ToList();
            foreach (string req in new[] { "DistFlow", "GOR", "SteamDuty", "STE", "BrineCF" })
                Assert.Contains(req, outputs);
        }

        [Fact]
        public void ModelAndReferences_InReport()
        {
            Rig rig = BuildRig(true);
            rig.Block.Calculate();
            TestKit.AssertReportContains(rig.Block, "Model & References", "El-Dessouky", "GOR");
        }
    }

    public class MsfCapeOpenValidationTests
    {
        private const double MwWater = 18.0153, MwNaCl = 58.442467;
        private static readonly string[] Ids = { "WATER", "NACL" };
        private static readonly double[] Mw = { MwWater, MwNaCl };
        private static readonly double[] Feed = { 100.0, 1.1 };

        private static MsfModel.Spec DefaultSpec()
        {
            return new MsfModel.Spec
            {
                NStages = 24, TopBrineTempC = 110, LastStageTempC = 40,
                ThermoLossK = 2.5, CpKJKgK = 4.0, LatentKJKg = 2330,
            };
        }

        private static double FeedKgS()
        {
            return Feed[0] * MwWater / 1000.0 + Feed[1] * MwNaCl / 1000.0;
        }

        private sealed class Rig { public MultiStageFlash Block; public object F, D, B; }

        private static Rig BuildRig(bool t11, double tbt = 110)
        {
            var rig = new Rig
            {
                Block = new MultiStageFlash(),
                F = TestKit.NewMock(t11, Ids, Mw, (double[])Feed.Clone()),
                D = TestKit.NewMock(t11, Ids, Mw),
                B = TestKit.NewMock(t11, Ids, Mw),
            };
            TestKit.Set(rig.Block, "TopBrineTemp", tbt);
            TestKit.Connect(rig.Block, "Feed", rig.F);
            TestKit.Connect(rig.Block, "Distillate", rig.D);
            TestKit.Connect(rig.Block, "Brine", rig.B);
            return rig;
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Block_MatchesEngine_Within01Percent(bool thermo11)
        {
            MsfModel.Perf perf = MsfModel.Solve(DefaultSpec(), Feed[0], FeedKgS());
            Rig rig = BuildRig(thermo11);
            rig.Block.Calculate();

            TestKit.Close(perf.DistM3h, TestKit.ResultOf(rig.Block, "Distillate flow"), "distFlow");
            TestKit.Close(perf.RecoveryFrac * 100, TestKit.ResultOf(rig.Block, "Once-through recovery"), "recovery");
            TestKit.Close(perf.PerfRatio, TestKit.ResultOf(rig.Block, "Performance ratio"), "PR");
            TestKit.Close(perf.HeaterKW, TestKit.ResultOf(rig.Block, "Brine heater duty"), "heater");

            double[] d = TestKit.FlowsOf(rig.D), b = TestKit.FlowsOf(rig.B);
            TestKit.AssertExact(0.0, d[1], "distillate is salt-free");
            TestKit.AssertMassBalance(new[] { Feed }, new[] { d, b }, 2);
        }

        [Fact]
        public void FlashFraction_And_Recovery_MatchHandEvaluation()
        {
            // y = cp*dTstage/lambda = 4*(70/24)/2330 = 0.0050072..., D/Mf = 1-(1-y)^24
            MsfModel.Perf p = MsfModel.Solve(DefaultSpec(), Feed[0], FeedKgS());
            double y = 4.0 * (70.0 / 24.0) / 2330.0;
            TestKit.AssertExact(y, p.FlashFracPerStage, "per-stage flash fraction");
            TestKit.AssertExact(1.0 - Math.Pow(1.0 - y, 24), p.RecoveryFrac, "recovery closed form");
            Assert.InRange(p.RecoveryFrac, 0.08, 0.15);   // MSF once-through band
        }

        [Fact]
        public void PerformanceRatio_InPublishedBand()
        {
            // 24-stage MSF: PR ~ 8-12 (Khawaji 2008); this model gives ~10-11 at defaults
            MsfModel.Perf p = MsfModel.Solve(DefaultSpec(), Feed[0], FeedKgS());
            Assert.InRange(p.PerfRatio, 8.0, 13.0);
        }

        [Fact]
        public void LowRecovery_Advisory_AlwaysPresent()
        {
            Rig rig = BuildRig(true);
            rig.Block.Calculate();
            TestKit.AssertWarningContains(rig.Block, "low-recovery");
        }

        [Fact]
        public void OverTbt_Warns()
        {
            Rig rig = BuildRig(true, 118);
            rig.Block.Calculate();
            TestKit.AssertWarningContains(rig.Block, "scale");
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TwentyConsecutiveRuns_AreIdentical(bool thermo11)
        {
            Rig rig = BuildRig(thermo11);
            TestKit.AssertTwentyRunsIdentical(rig.Block, () => TestKit.FlowsOf(rig.D));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void OutputParameters_MatchResultRows(bool thermo11)
        {
            Rig rig = BuildRig(thermo11);
            rig.Block.Calculate();
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "Distillate flow"), TestKit.OutParam(rig.Block, "DistFlow"), "DistFlow");
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "Performance ratio"), TestKit.OutParam(rig.Block, "PR"), "PR");
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "Brine heater duty"), TestKit.OutParam(rig.Block, "HeaterDuty"), "HeaterDuty");
        }

        [Fact]
        public void PortsAndDefaults_AreFactoryGrade()
        {
            var block = new MultiStageFlash();
            TestKit.AssertPortNames(block, "Feed", "Distillate", "Brine");
            TestKit.AssertRealParametersOnly(block);   // NStages is now a REAL integer code
            var outputs = block.Parameters.Cast<CapeOpen.CapeParameter>()
                .Where(p => !UnitBase.IsInputParameter(p)).Select(p => p.ComponentName).ToList();
            foreach (string req in new[] { "DistFlow", "Recovery", "PR", "HeaterDuty", "STE" })
                Assert.Contains(req, outputs);
        }

        [Fact]
        public void ModelAndReferences_InReport()
        {
            Rig rig = BuildRig(true);
            rig.Block.Calculate();
            TestKit.AssertReportContains(rig.Block, "Model & References", "El-Dessouky", "flash fraction");
        }
    }

    public class MvcCapeOpenValidationTests
    {
        private const double MwWater = 18.0153, MwNaCl = 58.442467;
        private static readonly string[] Ids = { "WATER", "NACL" };
        private static readonly double[] Mw = { MwWater, MwNaCl };
        private static readonly double[] Feed = { 100.0, 1.1 };

        private static MvcModel.Spec DefaultSpec()
        {
            return new MvcModel.Spec { RecoveryPct = 45, CompressionRatio = 1.3, CompressorEffPct = 75, EvapTempC = 60 };
        }

        private sealed class Rig { public MechanicalVaporCompression Block; public object F, D, B; }

        private static Rig BuildRig(bool t11, double cr = 1.3)
        {
            var rig = new Rig
            {
                Block = new MechanicalVaporCompression(),
                F = TestKit.NewMock(t11, Ids, Mw, (double[])Feed.Clone()),
                D = TestKit.NewMock(t11, Ids, Mw),
                B = TestKit.NewMock(t11, Ids, Mw),
            };
            TestKit.Set(rig.Block, "CompressionRatio", cr);
            TestKit.Connect(rig.Block, "Feed", rig.F);
            TestKit.Connect(rig.Block, "Distillate", rig.D);
            TestKit.Connect(rig.Block, "Brine", rig.B);
            return rig;
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Block_MatchesEngine_Within01Percent(bool thermo11)
        {
            MvcModel.Perf perf = MvcModel.Solve(DefaultSpec(), Feed[0]);
            Rig rig = BuildRig(thermo11);
            rig.Block.Calculate();

            TestKit.Close(perf.DistM3h, TestKit.ResultOf(rig.Block, "Distillate flow"), "distFlow");
            TestKit.Close(perf.PowerKW, TestKit.ResultOf(rig.Block, "Compressor power"), "power");
            TestKit.Close(perf.SpecWorkKJKg, TestKit.ResultOf(rig.Block, "Specific compression work"), "specWork");
            TestKit.Close(perf.SecKWhM3, TestKit.ResultOf(rig.Block, "Specific electrical energy"), "SEC");

            double[] d = TestKit.FlowsOf(rig.D), b = TestKit.FlowsOf(rig.B);
            TestKit.AssertExact(0.0, d[1], "distillate is salt-free");
            TestKit.AssertMassBalance(new[] { Feed }, new[] { d, b }, 2);
        }

        [Fact]
        public void SpecificWork_MatchesHandEvaluation()
        {
            // w = 1.88*333.15*(1.3^0.248 - 1)/0.75 ~ 56 kJ/kg
            MvcModel.Perf p = MvcModel.Solve(DefaultSpec(), Feed[0]);
            double expected = 1.88 * 333.15 * (Math.Pow(1.3, 0.248) - 1.0) / 0.75;
            TestKit.AssertExact(expected, p.SpecWorkKJKg, "specific work closed form");
        }

        [Fact]
        public void Sec_InPublishedMvcBand()
        {
            // published MVC SEC ~ 8-16 kWh/m3 (El-Dessouky ch.7; Aly & El-Fiqi 2003)
            MvcModel.Perf p = MvcModel.Solve(DefaultSpec(), Feed[0]);
            Assert.InRange(p.SecKWhM3, 8.0, 18.0);
        }

        [Fact]
        public void LowCompressionRatio_Warns()
        {
            Rig rig = BuildRig(true, 1.06);
            rig.Block.Calculate();
            TestKit.AssertWarningContains(rig.Block, "boiling-point elevation");
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TwentyConsecutiveRuns_AreIdentical(bool thermo11)
        {
            Rig rig = BuildRig(thermo11);
            TestKit.AssertTwentyRunsIdentical(rig.Block, () => TestKit.FlowsOf(rig.D));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void OutputParameters_MatchResultRows(bool thermo11)
        {
            Rig rig = BuildRig(thermo11);
            rig.Block.Calculate();
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "Distillate flow"), TestKit.OutParam(rig.Block, "DistFlow"), "DistFlow");
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "Compressor power"), TestKit.OutParam(rig.Block, "CompPower"), "CompPower");
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "Specific electrical energy"), TestKit.OutParam(rig.Block, "SEC"), "SEC");
        }

        [Fact]
        public void PortsAndDefaults_AreFactoryGrade()
        {
            var block = new MechanicalVaporCompression();
            TestKit.AssertPortNames(block, "Feed", "Distillate", "Brine");
            TestKit.AssertRealParametersOnly(block);
            var outputs = block.Parameters.Cast<CapeOpen.CapeParameter>()
                .Where(p => !UnitBase.IsInputParameter(p)).Select(p => p.ComponentName).ToList();
            foreach (string req in new[] { "DistFlow", "CompPower", "SpecWork", "SEC", "BrineCF" })
                Assert.Contains(req, outputs);
        }

        [Fact]
        public void ModelAndReferences_InReport()
        {
            Rig rig = BuildRig(true);
            rig.Block.Calculate();
            TestKit.AssertReportContains(rig.Block, "Model & References", "El-Dessouky", "isentropic");
        }
    }
}
