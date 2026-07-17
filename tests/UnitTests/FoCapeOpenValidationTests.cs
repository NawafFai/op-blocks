using System;
using System.Collections.Generic;
using System.Linq;
using OPBlocks.Core;
using OPBlocks.Desalination;
using Xunit;

namespace OPBlocks.UnitTests
{
    /// <summary>
    /// OP-FO validation gate (factory pattern): structural block==FoModel within
    /// 0.1% on both thermo mocks; physical anchors (osmotic direction, seawater
    /// draw magnitude, reverse-salt-flux direction, zero transfer without a
    /// gradient); determinism; results==streams; RealParameter-only; report.
    /// </summary>
    public class FoCapeOpenValidationTests
    {
        private const double MwWater = 18.0153, MwNaCl = 58.442467;

        public sealed class FoCase
        {
            public string Name;
            public double Area = 10, A = 1.0, B = 0.3, Sigma = 0.95, VantHoff = 2.0, MaxTransfer = 90;
            public double[] FeedMol, DrawMol;
            public double[] Mw = { MwWater, MwNaCl };
            public string[] Ids = { "WATER", "NACL" };
            public int Wi = 0;
            public double Tk = 298.15;
            public override string ToString() { return Name; }

            public FoModel.Spec Spec()
            {
                return new FoModel.Spec
                {
                    AreaM2 = Area, WaterPermA = A, SaltPermB = B, ReflectionSigma = Sigma,
                    VantHoffI = VantHoff, MaxTransferPct = MaxTransfer,
                };
            }
        }

        public static IReadOnlyList<FoCase> Cases()
        {
            return new List<FoCase>
            {
                // brackish feed (~0.05 M) against a seawater-strength draw (~0.6 M)
                new FoCase { Name = "brackish-vs-seawater", FeedMol = new[]{55.0, 0.05}, DrawMol = new[]{55.0, 0.6} },
                // no gradient: draw weaker than feed → zero transfer
                new FoCase { Name = "no-gradient", FeedMol = new[]{55.0, 0.5}, DrawMol = new[]{55.0, 0.05} },
                // oversized area → transfer capped at MaxTransfer
                new FoCase { Name = "oversized-capped", FeedMol = new[]{5.0, 0.005}, DrawMol = new[]{55.0, 0.6}, Area = 2000 },
            };
        }

        public static IEnumerable<object[]> CaseNames()
        {
            foreach (FoCase c in Cases()) { yield return new object[] { c.Name, false }; yield return new object[] { c.Name, true }; }
        }

        private static FoModel.Transfer Reference(FoCase c)
        {
            double piF = ProcessOps.OsmoticPressureBar(null, c.FeedMol, c.Wi, c.VantHoff, c.Tk, null);
            double piD = ProcessOps.OsmoticPressureBar(null, c.DrawMol, c.Wi, c.VantHoff, c.Tk, null);
            return FoModel.Solve(c.Spec(), c.FeedMol, c.DrawMol, c.Wi, c.Tk, c.Tk, piF, piD);
        }

        private sealed class Rig { public ForwardOsmosis Block; public object FIn, FOut, DIn, DOut; }

        private static Rig BuildRig(FoCase c, bool t11)
        {
            var rig = new Rig
            {
                Block = new ForwardOsmosis(),
                FIn = TestKit.NewMock(t11, c.Ids, c.Mw, (double[])c.FeedMol.Clone()),
                FOut = TestKit.NewMock(t11, c.Ids, c.Mw),
                DIn = TestKit.NewMock(t11, c.Ids, c.Mw, (double[])c.DrawMol.Clone()),
                DOut = TestKit.NewMock(t11, c.Ids, c.Mw),
            };
            TestKit.Set(rig.Block, "Area", c.Area); TestKit.Set(rig.Block, "WaterPermA", c.A);
            TestKit.Set(rig.Block, "SaltPermB", c.B); TestKit.Set(rig.Block, "Reflection", c.Sigma);
            TestKit.Set(rig.Block, "VantHoffI", c.VantHoff); TestKit.Set(rig.Block, "MaxTransfer", c.MaxTransfer);
            TestKit.Connect(rig.Block, "FeedIn", rig.FIn); TestKit.Connect(rig.Block, "FeedOut", rig.FOut);
            TestKit.Connect(rig.Block, "DrawIn", rig.DIn); TestKit.Connect(rig.Block, "DrawOut", rig.DOut);
            return rig;
        }

        // 1. STRUCTURAL
        [Theory]
        [MemberData(nameof(CaseNames))]
        public void Case_BlockMatchesReference_Within01Percent(string name, bool thermo11)
        {
            FoCase c = Cases().First(x => x.Name == name);
            FoModel.Transfer t = Reference(c);
            Rig rig = BuildRig(c, thermo11);
            rig.Block.Calculate();

            TestKit.Close(t.FluxLMH, TestKit.ResultOf(rig.Block, "Water flux"), "flux");
            TestKit.Close(t.WaterMolS * 0.0180153, TestKit.ResultOf(rig.Block, "Water transferred"), "transferred");
            TestKit.Close(t.PiFeedBar, TestKit.ResultOf(rig.Block, "Feed osmotic pressure"), "piFeed");
            TestKit.Close(t.PiDrawBar, TestKit.ResultOf(rig.Block, "Draw osmotic pressure"), "piDraw");
            TestKit.Close(t.NetDrivingBar, TestKit.ResultOf(rig.Block, "Net osmotic driving force"), "ndf");
            TestKit.Close(t.RevSaltMolS, TestKit.ResultOf(rig.Block, "Reverse draw-solute flow"), "revSalt");

            double[] fo = TestKit.FlowsOf(rig.FOut), doo = TestKit.FlowsOf(rig.DOut);
            for (int i = 0; i < c.FeedMol.Length; i++)
            {
                TestKit.Close(t.FeedOutMol[i], fo[i], "feedOut[" + i + "]");
                TestKit.Close(t.DrawOutMol[i], doo[i], "drawOut[" + i + "]");
            }
            TestKit.AssertMassBalance(new[] { c.FeedMol, c.DrawMol }, new[] { fo, doo }, c.FeedMol.Length);
        }

        // 2. PHYSICAL
        [Fact]
        public void SeawaterDraw_HasTextbookOsmoticPressure()
        {
            // ~0.6 M NaCl, i=2 → van 't Hoff π = 2·0.6·R·T ≈ 29 bar (25-33 window)
            FoCase c = Cases().First(x => x.Name == "brackish-vs-seawater");
            FoModel.Transfer t = Reference(c);
            Assert.InRange(t.PiDrawBar, 25.0, 33.0);
        }

        [Fact]
        public void Water_MovesFromFeedToDraw_DrawDilutes()
        {
            FoCase c = Cases().First(x => x.Name == "brackish-vs-seawater");
            FoModel.Transfer t = Reference(c);
            Assert.True(t.WaterMolS > 0, "water must transfer");
            Assert.True(t.DilutionDraw > 1.0, "draw dilutes");
            Assert.True(t.ConcFactorFeed > 1.0, "feed concentrates");
        }

        [Fact]
        public void ReverseSaltFlux_MovesDrawSoluteIntoFeed()
        {
            FoCase c = Cases().First(x => x.Name == "brackish-vs-seawater");
            FoModel.Transfer t = Reference(c);
            Assert.True(t.RevSaltMolS > 0, "reverse flux exists for B>0");
            Assert.True(t.FeedOutMol[1] > c.FeedMol[1], "feed gains draw solute");
            Assert.True(t.DrawOutMol[1] < c.DrawMol[1], "draw loses solute");
        }

        [Fact]
        public void NoGradient_NoTransfer_WithWarning()
        {
            FoCase c = Cases().First(x => x.Name == "no-gradient");
            FoModel.Transfer t = Reference(c);
            Assert.Equal(0.0, t.WaterMolS, 12);
            Rig rig = BuildRig(c, true);
            rig.Block.Calculate();
            TestKit.AssertWarningContains(rig.Block, "No water transfer");
        }

        [Fact]
        public void OversizedArea_CapsAtMaxTransfer_WithWarning()
        {
            FoCase c = Cases().First(x => x.Name == "oversized-capped");
            FoModel.Transfer t = Reference(c);
            Assert.True(t.TransferCapped);
            TestKit.Close(c.FeedMol[0] * c.MaxTransfer / 100.0, t.WaterMolS, "capped transfer");
            Rig rig = BuildRig(c, true);
            rig.Block.Calculate();
            TestKit.AssertWarningContains(rig.Block, "MaxTransfer");
        }

        // 3. determinism
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TwentyConsecutiveRuns_AreIdentical(bool thermo11)
        {
            Rig rig = BuildRig(Cases().First(x => x.Name == "brackish-vs-seawater"), thermo11);
            TestKit.AssertTwentyRunsIdentical(rig.Block, () => TestKit.FlowsOf(rig.DOut));
        }

        // 4. output parameters == result rows; streams flashed
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void OutputParameters_MatchResultRows(bool thermo11)
        {
            Rig rig = BuildRig(Cases().First(x => x.Name == "brackish-vs-seawater"), thermo11);
            rig.Block.Calculate();
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "Water flux"), TestKit.OutParam(rig.Block, "WaterFlux"), "WaterFlux");
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "Water transferred"), TestKit.OutParam(rig.Block, "WaterTransferred"), "WaterTransferred");
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "Net osmotic driving force"), TestKit.OutParam(rig.Block, "NetDriving"), "NetDriving");
            TestKit.AssertExact(TestKit.ResultOf(rig.Block, "Draw dilution ratio"), TestKit.OutParam(rig.Block, "DrawDilution"), "DrawDilution");
            Assert.True(TestKit.FlashCountOf(rig.FOut) > 0 && TestKit.FlashCountOf(rig.DOut) > 0, "both outlets flashed");
        }

        // 5. ports, RealParameter-only, Model&References
        [Fact]
        public void PortsAndDefaults_AreFactoryGrade()
        {
            var block = new ForwardOsmosis();
            TestKit.AssertPortNames(block, "FeedIn", "FeedOut", "DrawIn", "DrawOut");
            TestKit.AssertRealParametersOnly(block);
            var outputs = block.Parameters.Cast<CapeOpen.CapeParameter>()
                .Where(p => !UnitBase.IsInputParameter(p)).Select(p => p.ComponentName).ToList();
            foreach (string req in new[] { "WaterFlux", "WaterTransferred", "PiFeed", "PiDraw", "NetDriving", "RevSaltFlux" })
                Assert.Contains(req, outputs);
        }

        [Fact]
        public void ModelAndReferences_InReport()
        {
            Rig rig = BuildRig(Cases().First(x => x.Name == "brackish-vs-seawater"), true);
            rig.Block.Calculate();
            TestKit.AssertReportContains(rig.Block, "Model & References", "Elimelech", "Reverse draw-solute");
        }
    }
}
