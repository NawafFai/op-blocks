using CapeOpen;
using OPBlocks.Core.Persistence;
using OPBlocks.Demo;
using Xunit;

namespace OPBlocks.UnitTests
{
    /// <summary>
    /// Spec §8.6: parameters must survive save → close host → reopen. We drive the
    /// block's IPersistStreamInit directly over an in-memory IStream and assert a
    /// lossless round-trip — the same contract Aspen exercises against .apw/.bkp.
    /// </summary>
    public class PersistenceTests
    {
        private static CapeParameter Param(MixerDemo m, string name)
        {
            foreach (CapeParameter p in m.Parameters)
                if (p.ComponentName == name) return p;
            return null;
        }

        [Fact]
        public void Parameters_RoundTrip_Through_PersistStream()
        {
            var source = new MixerDemo();
            ((ICapeParameter)Param(source, "SpecifiedPressure")).value = 275000.0;
            ((ICapeParameter)Param(source, "PressureSpec")).value = "Specified";

            var stream = new ComMemoryStream();
            ((IPersistStreamInit)source).Save(stream, true);

            var restored = new MixerDemo();
            var reload = new ComMemoryStream(stream.ToArray());
            ((IPersistStreamInit)restored).Load(reload);

            Assert.Equal(275000.0, (double)((ICapeParameter)Param(restored, "SpecifiedPressure")).value, 6);
            Assert.Equal("Specified", (string)((ICapeParameter)Param(restored, "PressureSpec")).value);
        }

        [Fact]
        public void FreshBlock_HasExpectedDefaults()
        {
            var m = new MixerDemo();
            Assert.Equal("OP-MIXER-DEMO", m.BlockCode);
            Assert.Equal(3, m.Ports.Count);           // Inlet1, Inlet2, Outlet
            Assert.Equal(2, m.Parameters.Count);      // PressureSpec, SpecifiedPressure
            Assert.Equal(101325.0, (double)((ICapeParameter)Param(m, "SpecifiedPressure")).value, 6);
        }

        [Fact]
        public void Load_FromEmptyStream_KeepsDefaults_NoThrow()
        {
            var m = new MixerDemo();
            var empty = new ComMemoryStream();
            // Should not throw and should not corrupt defaults (spec §8 robustness).
            ((IPersistStreamInit)m).Load(empty);
            Assert.Equal(101325.0, (double)((ICapeParameter)Param(m, "SpecifiedPressure")).value, 6);
        }
    }
}
