using OPBlocks.Core;
using Xunit;

namespace OPBlocks.UnitTests
{
    /// <summary>
    /// A pure-water steam-table property method (Aspen STEAMNBS / STEAM-TA, DWSIM
    /// Steam Tables) cannot represent dissolved salt and makes the host spam
    /// "steam tables used when components other than water present / water absent"
    /// warnings and flag the block with a physical-property error. The blocks now
    /// detect that case from the package density and warn the user to switch to a
    /// salt-capable method. These tests pin the detection so it fires on a
    /// pure-water method and NEVER on a real salt-aware one.
    /// </summary>
    public class PureWaterMethodTests
    {
        [Theory]
        [InlineData(298.15, 997.0, 1.0)]   // 25 C  -> ~997.0 kg/m3
        [InlineData(273.15, 999.84, 0.5)]  // 0 C   -> ~999.84
        [InlineData(373.15, 958.4, 1.5)]   // 100 C -> ~958.4
        public void PureWaterDensity_MatchesKnownValues(double tK, double expected, double tol)
        {
            Assert.InRange(ProcessOps.PureWaterDensityKgM3(tK), expected - tol, expected + tol);
        }

        [Fact]
        public void SteamTableDensity_OnBrine_IsFlagged()
        {
            // Steam table returns pure-water density (~997) for a 3.5% NaCl brine at 25 C.
            Assert.True(ProcessOps.LooksLikePureWaterMethod(997.0, 298.15, 0.035));
        }

        [Fact]
        public void RealBrineDensity_IsNotFlagged()
        {
            // A salt-aware method (IDEAL / ELECNRTL) returns ~1023-1026 for the same
            // brine — several percent above pure water, so no false positive.
            Assert.False(ProcessOps.LooksLikePureWaterMethod(1025.9, 298.15, 0.035));
            Assert.False(ProcessOps.LooksLikePureWaterMethod(1023.0, 298.15, 0.035));
        }

        [Fact]
        public void LowSalinity_IsNeverFlagged()
        {
            // Below 1% salt the two densities are indistinguishable — must not fire.
            Assert.False(ProcessOps.LooksLikePureWaterMethod(997.0, 298.15, 0.005));
        }

        [Fact]
        public void MissingDensity_IsNeverFlagged()
        {
            Assert.False(ProcessOps.LooksLikePureWaterMethod(0.0, 298.15, 0.035));
        }
    }
}
