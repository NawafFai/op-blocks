using System.Collections.Generic;
using OPBlocks.Core;
using Xunit;

namespace OPBlocks.UnitTests
{
    /// <summary>
    /// QA §4 (user-freedom guarantee): osmotic pressure must stay physical for
    /// ANY feed the user enters — seawater, brines, even nonsense compositions —
    /// never the absurd magnitudes the v1.0 molarity route produced (272,000+ bar
    /// for a 50/50 water/salt feed).
    /// </summary>
    public class OsmoticPressureTests
    {
        private static double Pi(double xw, double i = 2.0, double tK = 298.15, List<string> notes = null)
        {
            // stream = null exercises the package-independent fallback route.
            var flows = new[] { xw, 1.0 - xw }; // water, salt (per 1 mol/s total)
            return ProcessOps.OsmoticPressureBar(null, flows, 0, i, tK, notes);
        }

        [Fact]
        public void PureWater_HasZeroOsmoticPressure()
        {
            Assert.Equal(0.0, Pi(1.0), 6);
        }

        [Fact]
        public void SeawaterLikeFeed_GivesRealisticPressure()
        {
            // x_salt = 1% (NaCl-apparent seawater): published π ≈ 25–30 bar.
            double pi = Pi(0.99);
            Assert.InRange(pi, 20.0, 35.0);
        }

        [Fact]
        public void DiluteLimit_MatchesVantHoff()
        {
            // 0.1% salt: -i·ln(xw)·RT/Vw ≈ i·x_salt·RT/Vw within ~0.1%.
            double pi = Pi(0.999);
            Assert.InRange(pi, 2.0, 3.5); // ≈2.75 bar
        }

        [Fact]
        public void ExtremeBrine_IsClampedWithWarning()
        {
            var notes = new List<string>();
            double pi = Pi(0.01, notes: notes); // the user's 50/50-style extreme case
            Assert.Equal(ProcessOps.OsmoticClampBar, pi, 6);
            Assert.Contains(notes, n => n.Contains("clamped"));
        }

        [Fact]
        public void NoWaterAtAll_ReportsCeilingAndWarning()
        {
            var notes = new List<string>();
            double pi = Pi(0.0, notes: notes);
            Assert.Equal(ProcessOps.OsmoticClampBar, pi, 6);
            Assert.NotEmpty(notes);
        }

        [Fact]
        public void FallbackRoute_StatesItsAssumption()
        {
            var notes = new List<string>();
            Pi(0.97, notes: notes);
            Assert.Contains(notes, n => n.Contains("ideal solution"));
        }

        // ---- package water-activity route (γw) ---------------------------------
        //
        // A dissolved salt ALWAYS lowers water activity (a_w = γw·xw < xw ⟹ γw < 1).
        // Only γw < 1 carries real electrolyte information; γw ≥ 1 (an ideal or a
        // molecular-salt package such as NRTL/UNIQUAC) does NOT model the salt's
        // osmotic effect and must fall back to van 't Hoff — never report ~0 bar for
        // a clearly saline feed (the DWSIM + NRTL defect: feed osmotic pressure = 0).

        private static double PiWithGamma(double xw, double gammaWater, List<string> notes = null)
        {
            var ids = new[] { "Water", "NaCl" };
            var mw = new[] { 18.015, 58.44 };
            var flows = new[] { xw, 1.0 - xw };
            var mock = new MockMaterialObject(ids, mw, 1000.0, flows,
                activityCoefficients: new[] { gammaWater, 1.0 });
            var proxy = new OPBlocks.Core.ThermoProxy(mock, "Feed");
            return ProcessOps.OsmoticPressureBar(proxy, flows, 0, 2.0, 298.15, notes);
        }

        [Fact]
        public void GammaAboveOne_FallsBackToVantHoff_NotZero()
        {
            // NRTL-with-molecular-NaCl returns γw slightly > 1 → the old code clamped
            // a_w to 1 and reported π = 0 for a 1%-salt feed. It must now match the
            // van 't Hoff estimate (~28 bar) and say it fell back.
            var notes = new List<string>();
            double pi = PiWithGamma(0.99, 1.05, notes);
            Assert.InRange(pi, 20.0, 35.0);
            Assert.Equal(Pi(0.99), pi, 6); // identical to the van 't Hoff route
            Assert.Contains(notes, n => n.Contains("ideal solution"));
        }

        [Fact]
        public void GammaBelowOne_UsesPackageActivity()
        {
            // A real electrolyte package (γw < 1, e.g. ELECNRTL ≈ 0.99) is trusted:
            // π = −(RT/V̄w)·ln(γw·xw), which differs from the plain van 't Hoff value,
            // and no fallback warning is emitted.
            var notes = new List<string>();
            double pi = PiWithGamma(0.99, 0.98, notes);
            Assert.True(pi > 0.0);
            Assert.NotEqual(Pi(0.99), pi, 3);
            Assert.DoesNotContain(notes, n => n.Contains("ideal solution"));
        }
    }
}
