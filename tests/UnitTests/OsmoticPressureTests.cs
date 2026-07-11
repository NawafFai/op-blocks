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
    }
}
