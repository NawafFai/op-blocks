using System;
using OPBlocks.Demo;
using Xunit;

namespace OPBlocks.UnitTests
{
    /// <summary>Spec §9 layer 1: model math vs. hand calculations.</summary>
    public class MixerMathTests
    {
        [Fact]
        public void Mix_SumsComponentFlows()
        {
            var r = MixerMath.Mix(
                new[] { 1.0, 2.0, 0.0 }, -100.0, 200000.0,
                new[] { 0.5, 0.0, 3.0 }, -50.0, 150000.0,
                false, 0.0);

            Assert.Equal(1.5, r.MoleFlows[0], 10);
            Assert.Equal(2.0, r.MoleFlows[1], 10);
            Assert.Equal(3.0, r.MoleFlows[2], 10);
            Assert.Equal(6.5, r.TotalMoleFlow, 10);
        }

        [Fact]
        public void Mix_ClosesEnthalpyBalance()
        {
            // Molar enthalpies weighted by molar flow, per adiabatic Q=0 balance.
            double[] f1 = { 2.0, 1.0 };   // Σ = 3
            double[] f2 = { 1.0, 0.0 };   // Σ = 1
            double h1 = -300.0, h2 = 100.0;
            var r = MixerMath.Mix(f1, h1, 1e5, f2, h2, 1e5, false, 0.0);

            double expected = (h1 * 3.0 + h2 * 1.0) / 4.0; // = -200
            Assert.Equal(expected, r.MolarEnthalpy, 9);
            Assert.Equal(4.0, r.TotalMoleFlow, 10);
        }

        [Fact]
        public void Mix_MinimumInletPressure_IsDefault()
        {
            var r = MixerMath.Mix(new[] { 1.0 }, 0.0, 300000.0, new[] { 1.0 }, 0.0, 180000.0, false, 0.0);
            Assert.Equal(180000.0, r.Pressure, 6);
        }

        [Fact]
        public void Mix_SpecifiedPressure_Overrides()
        {
            var r = MixerMath.Mix(new[] { 1.0 }, 0.0, 300000.0, new[] { 1.0 }, 0.0, 180000.0, true, 250000.0);
            Assert.Equal(250000.0, r.Pressure, 6);
        }

        [Fact]
        public void Mix_ZeroTotalFlow_GivesZeroMolarEnthalpy_NotNaN()
        {
            var r = MixerMath.Mix(new[] { 0.0, 0.0 }, -100.0, 1e5, new[] { 0.0, 0.0 }, -100.0, 1e5, false, 0.0);
            Assert.Equal(0.0, r.TotalMoleFlow, 12);
            Assert.False(double.IsNaN(r.MolarEnthalpy));
        }

        [Fact]
        public void Mix_MismatchedComponentCounts_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                MixerMath.Mix(new[] { 1.0, 2.0 }, 0.0, 1e5, new[] { 1.0 }, 0.0, 1e5, false, 0.0));
        }
    }
}
