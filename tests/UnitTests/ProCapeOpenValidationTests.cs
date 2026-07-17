using System;
using System.Collections.Generic;
using System.Linq;
using OPBlocks.Core;
using OPBlocks.Desalination;
using Xunit;

namespace OPBlocks.UnitTests
{
    /// <summary>
    /// OP-PRO validation gate (factory pattern): structural block==ProModel within
    /// 0.1% on both thermo mocks; physical anchors — the CLASSICAL dP* = sigma·dPi/2
    /// power optimum (Loeb; Achilli &amp; Childress), the W = A(sigma·dPi−dP)·dP
    /// parabola, zero power when over-pressured, pressurised draw outlet;
    /// determinism; results==streams; RealParameter-only; report.
    /// </summary>
    public class ProCapeOpenValidationTests
    {
        private const double MwWater = 18.0153, MwNaCl = 58.442467;

        public sealed class ProCase
        {
            public string Name;
            public double Area = 10, A = 1.0, Sigma = 0.95, Dp = 12, VantHoff = 2.0, TurbEff = 90, MaxTransfer = 90;
            public double[] FeedMol, DrawMol;
            public double[] Mw = { MwWater, MwNaCl };
            public string[] Ids = { "WATER", "NACL" };
            public int Wi = 0;
            public double Tk = 298.15;
            public override string ToString() { return Name; }

            public ProModel.Spec Spec()
            {
                return new ProModel.Spec
                {
                    AreaM2 = Area, WaterPermA = A, ReflectionSigma = Sigma, AppliedBar = Dp,
                    VantHoffI = VantHoff, TurbineEffPct = TurbEff, MaxTransferPct = MaxTransfer,
                };
            }
        }

        public static IReadOnlyList<ProCase> Cases()
        {
            return new List<ProCase>
            {
                // river water vs seawater draw — the classical PRO pair (dPi ~ 29 bar)
                new ProCase { Name = "river-sea", FeedMol = new[]{55.0, 0.005}, DrawMol = new[]{55.0, 0.6} },
                // over-pressured: dP > sigma*dPi → no permeation, no power
                new ProCase { Name = "over-pressured", FeedMol = new[]{55.0, 0.005}, DrawMol = new[]{55.0, 0.6}, Dp = 40 },
                // near-optimal pressure
                new ProCase { Name = "near-optimal", FeedMol = new[]{55.0, 0.005}, DrawMol = new[]{55.0, 0.6}, Dp = 13 },
            };
        }

        public static IEnumerable<object[]> CaseNames()
        {
            foreach (ProCase c in Cases()) { yield return new object[] { c.Name, false }; yield return new object[] { c.Name, true }; }
        }

        private static ProModel.Power Reference(ProCase c)
        {
            double piF = ProcessOps.OsmoticPressureBar(null, c.FeedMol, c.Wi, c.VantHoff, c.Tk, null);
            double piD = ProcessOps.OsmoticPressureBar(null, c.DrawMol, c.Wi, c.VantHoff, c.Tk, null);
            return ProModel.Solve(c.Spec(), c.FeedMol, c.DrawMol, c.Wi, c.Tk, c.Tk, piF, piD);
        }

        private sealed class Rig { public PressureRetardedOsmosis Block; public object FIn, FOut, DIn, DOut; }

        private static Rig BuildRig(ProCase c, bool t11)
        {
            var rig = new Rig
            {
                Block = new PressureRetardedOsmosis(),
                FIn = TestKit.NewMock(t11, c.Ids, c.Mw, (double[])c.FeedMol.Clone()),
                FOut = TestKit.NewMock(t11, c.Ids, c.Mw),
                DIn = TestKit.NewMock(t11, c.Ids, c.Mw, (double[])c.DrawMol.Clone()),
                DOut = TestKit.NewMock(t11, c.Ids, c.Mw),
            };
            TestKit.Set(rig.Block, "Area", c.Area); TestKit.Set(rig.Block, "WaterPermA", c.A);
            TestKit.Set(rig.Block, "AppliedPressure", c.Dp); TestKit.Set(rig.Block, "Reflection", c.Sigma);
            TestKit.Set(rig.Block, "VantHoffI", c.VantHoff); TestKit.Set(rig.Block, "TurbineEff", c.TurbEff);
            TestKit.Set(rig.Block, "MaxTransfer", c.MaxTransfer);
            TestKit.Connect(rig.Block, "FeedIn", rig.FIn); TestKit.Connect(rig.Block, "FeedOut", rig.FOut);
            TestKit.Connect(rig.Block, "DrawIn", rig.DIn); TestKit.Connect(rig.Block, "DrawOut", rig.DOut);
            return rig;
        }

        // 1. STRUCTURAL
        [Theory]
        [MemberData(nameof(CaseNames))]
        public void Case_BlockMatchesReference_Within01Percent(string name, bool thermo11)
        {
            ProCase c = Cases().First(x => x.Name == name);
            ProModel.Power p = Reference(c);
            Rig rig = BuildRig(c, thermo11);
            rig.Block.Calculate();

            TestKit.Close(p.FluxLMH, TestKit.ResultOf(rig.Block, "Water flux"), "flux");
            TestKit.Close(p.PowerDensityWm2, TestKit.ResultOf(rig.Block, "Power density (gross)"), "powerDensity");
            TestKit.Close(p.GrossKW, TestKit.ResultOf(rig.Block, "Gross power"), "gross");
            TestKit.Close(p.NetKW, TestKit.ResultOf(rig.Block, "Net power"), "net");
            TestKit.Close(p.NetDrivingBar, TestKit.ResultOf(rig.Block, "Net driving pressure"), "ndp");
            TestKit.Close(p.OptimalDeltaPBar, TestKit.ResultOf(rig.Block, "Power-optimal applied pressure"), "optDp");

            double[] fo = TestKit.FlowsOf(rig.FOut), doo = TestKit.FlowsOf(rig.DOut);
            for (int i = 0; i < c.FeedMol.Length; i++)
            {
                TestKit.Close(p.FeedOutMol[i], fo[i], "feedOut[" + i + "]");
                TestKit.Close(p.DrawOutMol[i], doo[i], "drawOut[" + i + "]");
            }
            TestKit.AssertMassBalance(new[] { c.FeedMol, c.DrawMol }, new[] { fo, doo }, c.FeedMol.Length);
        }

        // 2. PHYSICAL
        [Fact]
        public void PowerDensity_PeaksAtHalfOsmoticDifference()
        {
            // Loeb / Achilli-Childress: W = A(sigma·dPi − dP)dP is maximal at dP* = sigma·dPi/2.
            ProCase baseCase = Cases().First(x => x.Name == "river-sea");
            ProModel.Power at = Reference(baseCase);
            double dpStar = at.OptimalDeltaPBar;

            Func<double, double> powerAt = dp =>
            {
                var c2 = new ProCase { Name = "probe", FeedMol = baseCase.FeedMol, DrawMol = baseCase.DrawMol, Dp = dp };
                return Reference(c2).PowerDensityWm2;
            };
            double wStar = powerAt(dpStar);
            foreach (double dp in new[] { dpStar - 6, dpStar - 3, dpStar + 3, dpStar + 6 })
                Assert.True(wStar >= powerAt(dp) - 1e-9,
                    "power at dP*=" + dpStar.ToString("0.##") + " must beat dP=" + dp.ToString("0.##"));
        }

        [Fact]
        public void PowerDensity_FollowsParabolaFormula()
        {
            // W[W/m2] = Jw·dP with Jw = A(sigma·dPi − dP): check the closed form.
            ProCase c = Cases().First(x => x.Name == "river-sea");
            ProModel.Power p = Reference(c);
            double expected = p.FluxLMH * c.Dp * (1e-3 / 3600.0 * 1e5);
            TestKit.AssertExact(expected, p.PowerDensityWm2, "W = Jw·dP");
            double jwExpected = c.A * Math.Max(0, c.Sigma * p.DeltaPiBar - c.Dp);
            TestKit.Close(jwExpected, p.FluxLMH, "Jw = A(sigma·dPi − dP)");
        }

        [Fact]
        public void RiverSea_PowerDensity_InPublishedViabilityBand()
        {
            // Straub et al. (2016): river/sea PRO ~ 25-30 bar dPi → W ~ 2-7 W/m2 at A~1.
            ProCase c = Cases().First(x => x.Name == "near-optimal");
            ProModel.Power p = Reference(c);
            Assert.InRange(p.PowerDensityWm2, 2.0, 8.0);
        }

        [Fact]
        public void OverPressured_NoPower_WithWarning()
        {
            ProCase c = Cases().First(x => x.Name == "over-pressured");
            ProModel.Power p = Reference(c);
            Assert.Equal(0.0, p.NetDrivingBar, 9);
            Assert.Equal(0.0, p.GrossKW, 9);
            Rig rig = BuildRig(c, true);
            rig.Block.Calculate();
            TestKit.AssertWarningContains(rig.Block, "No power production");
        }

        [Fact]
        public void DrawOutlet_IsPressurisedByAppliedDp()
        {
            ProCase c = Cases().First(x => x.Name == "river-sea");
            Rig rig = BuildRig(c, true);
            rig.Block.Calculate();
            TestKit.Close(101325.0 + c.Dp * 1e5, TestKit.PressureOf(rig.DOut), "draw outlet pressure");
        }

        // 3. determinism
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TwentyConsecutiveRuns_AreIdentical(bool thermo11)
        {
            Rig rig = BuildRig(Cases().First(x => x.Name == "river-sea"), thermo11);
            TestKit.AssertTwentyRunsIdentical(rig.Block, () => TestKit.FlowsOf(rig.DOut));
        }

        // 4. output parameters == result rows
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void OutputParameters_MatchResultRows(bool thermo11)
        {
            Rig rig = BuildRig(Cases().First(x => x.Name == "river-sea"), thermo11);
            rig.Block.Calculate();
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "Water flux"), TestKit.OutParam(rig.Block, "WaterFlux"), "WaterFlux");
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "Power density (gross)"), TestKit.OutParam(rig.Block, "PowerDensity"), "PowerDensity");
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "Gross power"), TestKit.OutParam(rig.Block, "GrossPower"), "GrossPower");
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "Net power"), TestKit.OutParam(rig.Block, "NetPower"), "NetPower");
            Assert.True(TestKit.FlashCountOf(rig.FOut) > 0 && TestKit.FlashCountOf(rig.DOut) > 0, "both outlets flashed");
        }

        // 5. ports, RealParameter-only, Model&References
        [Fact]
        public void PortsAndDefaults_AreFactoryGrade()
        {
            var block = new PressureRetardedOsmosis();
            TestKit.AssertPortNames(block, "FeedIn", "FeedOut", "DrawIn", "DrawOut");
            TestKit.AssertRealParametersOnly(block);
            var outputs = block.Parameters.Cast<CapeOpen.CapeParameter>()
                .Where(p => !UnitBase.IsInputParameter(p)).Select(p => p.ComponentName).ToList();
            foreach (string req in new[] { "WaterFlux", "PowerDensity", "GrossPower", "NetPower", "NetDriving", "OptimalDP" })
                Assert.Contains(req, outputs);
        }

        [Fact]
        public void ModelAndReferences_InReport()
        {
            Rig rig = BuildRig(Cases().First(x => x.Name == "river-sea"), true);
            rig.Block.Calculate();
            TestKit.AssertReportContains(rig.Block, "Model & References", "Loeb", "dP* = sigma*dPi/2");
        }
    }
}
