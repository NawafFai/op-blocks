using System;
using CapeOpen;
using OPBlocks.Core;
using OPBlocks.Desalination;
using OPBlocks.Electro;
using Xunit;

namespace OPBlocks.UnitTests
{
    /// <summary>
    /// P1 stabilization regressions (v1.1.3, 2026-07-20 live finding):
    /// 1. A zero-permeate outcome (applied pressure below the osmotic pressure) must
    ///    COMPLETE with every outlet SET — leaving a zero-flow outlet unset made
    ///    Aspen's post-Calculate stream handling fail with the blank
    ///    "CAPE-OPEN UNIT CALCULATE CALL FAILED. SEE HISTORY FOR DETAIL."
    /// 2. A physically impossible feed (beyond saturation) must fail EARLY with an
    ///    actionable ECapeUser message, never a cryptic host error.
    /// 3. A merely high (sub-saturation) feed warns and still completes.
    /// </summary>
    public class P1StabilizationTests
    {
        static readonly string[] Ids = { "WATER", "NACL" };
        static readonly double[] Mw = { 18.015, 58.44 };

        static double[] FlowsOf(object mock)
        {
            return ((IMockMaterial)mock).Flows;
        }

        static ReverseOsmosis WireRo(bool t11, double[] feedFlows, double feedPa, out object conc, out object perm)
        {
            var block = new ReverseOsmosis();
            var feed = TestKit.NewMock(t11, Ids, Mw, feedFlows, 298.15, feedPa, 1023.0);
            conc = TestKit.NewMock(t11, Ids, Mw, null, 298.15, feedPa, 1023.0);
            perm = TestKit.NewMock(t11, Ids, Mw, null, 298.15, 101325.0, 997.0);
            TestKit.Connect(block, "Feed", feed);
            TestKit.Connect(block, "Concentrate", conc);
            TestKit.Connect(block, "Permeate", perm);
            return block;
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ZeroPermeate_Completes_And_Sets_The_ZeroFlow_Outlet(bool t11)
        {
            // textbook seawater (~35,000 ppm, pi ~ 30 bar) at only 20 bar applied
            var feedFlows = new[] { 23.503, 0.2628 };
            object conc, perm;
            var block = WireRo(t11, feedFlows, 20e5, out conc, out perm);
            TestKit.Set(block, "AppliedPressure", 20.0);

            block.Calculate(); // must not throw — zero permeate is honest physics

            double[] pf = FlowsOf(perm);
            Assert.NotNull(pf); // the outlet WAS set (the P1 bug left it unset)
            for (int i = 0; i < pf.Length; i++)
                TestKit.AssertExact(0.0, pf[i], "permeate flow [" + i + "]");
            double[] cf = FlowsOf(conc);
            for (int i = 0; i < feedFlows.Length; i++)
                TestKit.AssertExact(feedFlows[i], cf[i], "concentrate flow [" + i + "]");
            TestKit.AssertExact(0.0, TestKit.OutParam(block, "Recovery"), "Recovery");
            TestKit.AssertWarningContains(block, "No net driving pressure");
            TestKit.AssertWarningContains(block, "no permeate is produced");
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void SupersaturatedFeed_Fails_With_Actionable_Message(bool t11)
        {
            // the owner's live case: 0.8/0.2 mole water/NaCl = 44.8 wt% (~2x saturation)
            object conc, perm;
            var block = WireRo(t11, new[] { 80.0, 20.0 }, 60e5, out conc, out perm);
            TestKit.Set(block, "AppliedPressure", 60.0);

            var ex = Assert.Throws<CapeComputationException>(() => block.Calculate());
            Assert.Contains("beyond the physical envelope", ex.Message);
            Assert.Contains("OP-MVC", ex.Message); // actionable: names the alternative
        }

        [Fact]
        public void HighButSubSaturationFeed_Warns_And_Completes()
        {
            // 12 wt% NaCl — beyond typical SWRO but physically computable
            object conc, perm;
            var block = WireRo(true, new[] { 48.85, 2.053 }, 60e5, out conc, out perm);

            block.Calculate();

            TestKit.AssertWarningContains(block, "beyond the typical envelope");
            Assert.NotNull(FlowsOf(perm));
        }

        [Fact]
        public void Nf_SupersaturatedFeed_Fails_With_Actionable_Message()
        {
            var block = new Nanofiltration();
            var feed = TestKit.NewMock(true, Ids, Mw, new[] { 80.0, 20.0 }, 298.15, 10e5, 1200.0);
            TestKit.Connect(block, "Feed", feed);
            TestKit.Connect(block, "Concentrate", TestKit.NewMock(true, Ids, Mw, null, 298.15, 10e5, 1200.0));
            TestKit.Connect(block, "Permeate", TestKit.NewMock(true, Ids, Mw, null, 298.15, 101325.0, 997.0));

            var ex = Assert.Throws<CapeComputationException>(() => block.Calculate());
            Assert.Contains("nanofiltration", ex.Message);
        }

        [Fact]
        public void Ed_SupersaturatedDiluate_Fails_With_Actionable_Message()
        {
            var block = new Electrodialysis();
            TestKit.Connect(block, "DiluateIn", TestKit.NewMock(true, Ids, Mw, new[] { 80.0, 20.0 }, 298.15, 2e5, 1200.0));
            TestKit.Connect(block, "ConcentrateIn", TestKit.NewMock(true, Ids, Mw, new[] { 55.0, 0.05 }, 298.15, 2e5, 1010.0));
            TestKit.Connect(block, "DiluateOut", TestKit.NewMock(true, Ids, Mw, null, 298.15, 2e5, 1010.0));
            TestKit.Connect(block, "ConcentrateOut", TestKit.NewMock(true, Ids, Mw, null, 298.15, 2e5, 1010.0));

            var ex = Assert.Throws<CapeComputationException>(() => block.Calculate());
            Assert.Contains("electrodialysis", ex.Message);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void EvapPond_ZeroEvaporation_Sets_The_Vapor_Outlet(bool t11)
        {
            // humid, dark, low-activity brine => evaporation clamps to zero; the
            // Vapor outlet must still be SET (it was skipped before the P1 fix).
            var block = new EvapPond();
            var feed = TestKit.NewMock(t11, Ids, Mw, new[] { 50.0, 1.0 }, 303.15, 101325.0, 1080.0);
            var conc = TestKit.NewMock(t11, Ids, Mw, null, 303.15, 101325.0, 1080.0);
            var vap = TestKit.NewMock(t11, Ids, Mw, null, 303.15, 101325.0, 1.2);
            TestKit.Connect(block, "BrineFeed", feed);
            TestKit.Connect(block, "Concentrate", conc);
            TestKit.Connect(block, "Vapor", vap);
            TestKit.Set(block, "RH", 100.0);
            TestKit.Set(block, "Irradiance", 0.0);
            TestKit.Set(block, "WaterActivity", 0.5);

            block.Calculate();

            double[] vf = FlowsOf(vap);
            Assert.NotNull(vf);
            for (int i = 0; i < vf.Length; i++)
                TestKit.AssertExact(0.0, vf[i], "vapor flow [" + i + "]");
        }
    }
}
