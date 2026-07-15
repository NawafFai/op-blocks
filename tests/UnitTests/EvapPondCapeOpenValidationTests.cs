using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CapeOpen;
using OPBlocks.Core;
using OPBlocks.Desalination;
using Xunit;

namespace OPBlocks.UnitTests
{
    /// <summary>
    /// OP-EVAPPOND validation gate, structured like the OP-RO gate. Two layers:
    ///
    ///  1. STRUCTURAL — canonical cases replayed through the REAL CAPE-OPEN block
    ///     (ports, ICapeParameter, Calculate() behind the boundary guard,
    ///     ThermoProxy against Thermo 1.0 AND 1.1 mocks, outlet writing, result
    ///     read-back) must match the shared <see cref="EvapPondModel"/> within 0.1%.
    ///
    ///  2. PHYSICAL — independent checks. The exact anchor is the ANTOINE saturation
    ///     vapour pressure of water (Psat(100 C) ≈ 1 atm, Psat(25 C) ≈ 3.17 kPa);
    ///     arid evaporation lands in the realistic 4-12 mm/day pond band; a lower
    ///     brine water activity reduces evaporation; wind increases it; salts are
    ///     conserved (only water evaporates).
    ///
    /// Plus determinism (20 runs, 1e-8), results==streams, and the RealParameter-only
    /// / ports / Model&amp;References structural guarantees.
    /// </summary>
    public class EvapPondCapeOpenValidationTests
    {
        private const double RelTol = 1e-3;
        private const double AbsFloor = 1e-12;
        private const double MwWater = 18.0153, MwNaCl = 58.442467;

        public sealed class PondCase
        {
            public string Name;
            public double Area = 10000, Depth = 0.5, Irradiance = 600, AirTemp = 30, RH = 40,
                          Wind = 3, WaterAct = 0.98, CoeffA = 1.2e-8, CoeffB = 2.5e-9, SolarHeat = 0.012;
            public double[] FeedMol, Mw;
            public string[] Ids;
            public int Wi;
            public double Tk = 298.15;
            public override string ToString() { return Name; }

            public EvapPondModel.Spec Spec()
            {
                return new EvapPondModel.Spec
                {
                    AreaM2 = Area, DepthM = Depth, IrradianceWm2 = Irradiance, AirTempC = AirTemp,
                    RHpct = RH, WindSpeedMs = Wind, WaterActivity = WaterAct,
                    CoeffA = CoeffA, CoeffB = CoeffB, SolarHeating = SolarHeat,
                };
            }
        }

        private static PondCase Arid() => new PondCase
        {
            Name = "arid-default", FeedMol = new[] { 200.0, 2.0 },
            Ids = new[] { "WATER", "NACL" }, Mw = new[] { MwWater, MwNaCl }, Wi = 0
        };

        public static IReadOnlyList<PondCase> Cases()
        {
            return new List<PondCase>
            {
                Arid(),
                new PondCase { Name = "humid-no-evap", FeedMol = new[]{200.0, 2.0},
                               Ids = new[]{"WATER","NACL"}, Mw = new[]{MwWater,MwNaCl}, Wi = 0,
                               RH = 100, SolarHeat = 0.0, WaterAct = 0.9 },
                new PondCase { Name = "small-feed-high-cf", FeedMol = new[]{20.0, 0.5},
                               Ids = new[]{"WATER","NACL"}, Mw = new[]{MwWater,MwNaCl}, Wi = 0 },
            };
        }

        public static IEnumerable<object[]> CaseNames()
        {
            foreach (PondCase c in Cases())
            {
                yield return new object[] { c.Name, false };
                yield return new object[] { c.Name, true };
            }
        }

        private static double MassKgS(double[] mol, double[] mw)
        {
            double kg = 0;
            for (int i = 0; i < mol.Length; i++) kg += mol[i] * mw[i] / 1000.0;
            return kg;
        }

        private static EvapPondModel.Result Reference(PondCase c)
        {
            double feedM3s = MassKgS(c.FeedMol, c.Mw) / 1000.0;
            return EvapPondModel.Solve(c.Spec(), c.FeedMol, c.Wi, c.Mw, feedM3s);
        }

        // ------------------------------------------------------------------
        //  block rig
        // ------------------------------------------------------------------
        private sealed class Rig { public EvapPond Block; public IMockMaterial Feed, Conc, Vap; }

        private static object NewMock(bool t11, string[] ids, double[] mw, double[] flows = null)
        {
            if (t11) return new Mock11MaterialObject(ids, mw, 1000.0, flows, 298.15, 101325.0);
            return new MockMaterialObject(ids, mw, 1000.0, flows, 298.15, 101325.0);
        }

        private static Rig BuildRig(PondCase c, bool t11)
        {
            object feed = NewMock(t11, c.Ids, c.Mw, (double[])c.FeedMol.Clone());
            object conc = NewMock(t11, c.Ids, c.Mw);
            object vap = NewMock(t11, c.Ids, c.Mw);
            var block = new EvapPond();
            Set(block, "Area", c.Area); Set(block, "Depth", c.Depth); Set(block, "Irradiance", c.Irradiance);
            Set(block, "AirTemp", c.AirTemp); Set(block, "RH", c.RH); Set(block, "WindSpeed", c.Wind);
            Set(block, "WaterActivity", c.WaterAct); Set(block, "CoeffA", c.CoeffA); Set(block, "CoeffB", c.CoeffB);
            Set(block, "SolarHeating", c.SolarHeat);
            Connect(block, "BrineFeed", feed); Connect(block, "Concentrate", conc); Connect(block, "Vapor", vap);
            return new Rig { Block = block, Feed = (IMockMaterial)feed, Conc = (IMockMaterial)conc, Vap = (IMockMaterial)vap };
        }

        private static void Set(CapeUnitBase block, string param, double value)
        {
            foreach (CapeParameter p in block.Parameters)
                if (string.Equals(p.ComponentName, param, StringComparison.OrdinalIgnoreCase))
                { ((ICapeParameter)p).value = value; return; }
            throw new InvalidOperationException("parameter not found: " + param);
        }

        private static void Connect(CapeUnitBase block, string portName, object material)
        {
            foreach (UnitPort p in block.Ports)
                if (string.Equals(p.ComponentName, portName, StringComparison.OrdinalIgnoreCase))
                { p.Connect(material); return; }
            throw new InvalidOperationException("port not found: " + portName);
        }

        private static double ResultOf(UnitBase block, string label)
        {
            UnitBase.ResultEntry row = block.GetResults().FirstOrDefault(r => r.Label == label);
            Assert.True(row != null, "missing result row: " + label);
            return row.Value;
        }

        private static double OutParam(EvapPond block, string name)
        {
            foreach (CapeParameter p in block.Parameters)
                if (!UnitBase.IsInputParameter(p) && string.Equals(p.ComponentName, name, StringComparison.OrdinalIgnoreCase))
                    return Convert.ToDouble(((ICapeParameter)p).value, CultureInfo.InvariantCulture);
            throw new InvalidOperationException("output parameter not found: " + name);
        }

        private static void Close(double expected, double actual, string what)
        {
            double tol = Math.Max(AbsFloor, Math.Abs(expected) * RelTol);
            Assert.True(Math.Abs(actual - expected) <= tol,
                what + ": expected " + expected.ToString("R") + ", got " + actual.ToString("R"));
        }

        private static void AssertExact(double expected, double actual, string what)
        {
            Assert.True(Math.Abs(actual - expected) <= 1e-10 * Math.Max(1.0, Math.Abs(expected)),
                what + ": " + expected.ToString("R") + " vs " + actual.ToString("R"));
        }

        // ------------------------------------------------------------------
        //  1. STRUCTURAL — block == EvapPondModel within 0.1%, both backends
        // ------------------------------------------------------------------
        [Theory]
        [MemberData(nameof(CaseNames))]
        public void Case_BlockMatchesReference_Within01Percent(string name, bool thermo11)
        {
            PondCase c = Cases().First(x => x.Name == name);
            EvapPondModel.Result res = Reference(c);
            Rig rig = BuildRig(c, thermo11);
            rig.Block.Calculate();

            Close(res.EvapMmDay, ResultOf(rig.Block, "Evaporation flux"), "mmDay");
            Close(res.EvapM3Day, ResultOf(rig.Block, "Evaporation rate"), "m3Day");
            Close(res.DrivingForcePa, ResultOf(rig.Block, "Vapour-pressure driving force"), "driving");
            Close(res.SurfaceTempC, ResultOf(rig.Block, "Surface temperature"), "surfT");
            Close(res.EvapKgS, ResultOf(rig.Block, "Water evaporated"), "evapKg");
            Close(res.TdsConcPpm, ResultOf(rig.Block, "Concentrate TDS"), "concTDS");
            if (!double.IsInfinity(res.ConcentrationFactor))
                Close(res.ConcentrationFactor, ResultOf(rig.Block, "Concentration factor"), "CF");

            for (int i = 0; i < c.FeedMol.Length; i++)
            {
                double vm = rig.Vap.Flows == null ? 0.0 : rig.Vap.Flows[i];
                double cm = rig.Conc.Flows == null ? 0.0 : rig.Conc.Flows[i];
                Close(res.VaporMol[i], vm, "vaporMol[" + i + "]");
                Close(res.ConcMol[i], cm, "concMol[" + i + "]");
                Assert.True(Math.Abs(c.FeedMol[i] - vm - cm) <= 1e-9 * Math.Max(1.0, c.FeedMol[i]),
                    "mass balance component " + i);
            }
        }

        // ------------------------------------------------------------------
        //  2. PHYSICAL — Antoine anchor, mm/day band, salinity & wind effects
        // ------------------------------------------------------------------
        [Fact]
        public void Antoine_SaturationPressure_IsTextbook()
        {
            // Psat(100 C) = 1 atm (101.325 kPa); Psat(25 C) ≈ 3.17 kPa.
            Assert.InRange(ProcessOps.PsatWaterPa(100.0), 101325.0 * 0.99, 101325.0 * 1.01);
            Assert.InRange(ProcessOps.PsatWaterPa(25.0), 3169.0 * 0.985, 3169.0 * 1.015);
        }

        [Fact]
        public void Arid_Evaporation_IsRealistic_mmPerDay()
        {
            EvapPondModel.Result res = Reference(Arid());
            // arid open-brine ponds evaporate ~4-12 mm/day.
            Assert.InRange(res.EvapMmDay, 4.0, 12.0);
        }

        [Fact]
        public void LowerWaterActivity_ReducesEvaporation()
        {
            var fresh = Arid(); fresh.WaterAct = 1.00;
            var brine = Arid(); brine.WaterAct = 0.98;
            var dense = Arid(); dense.WaterAct = 0.75;
            double eFresh = Reference(fresh).EvapMmDay;
            double eBrine = Reference(brine).EvapMmDay;
            double eDense = Reference(dense).EvapMmDay;
            Assert.True(eBrine < eFresh, "0.98-activity brine evaporates less than fresh water");
            Assert.True(eDense < eBrine, "a 0.75-activity dense brine evaporates least");
        }

        [Fact]
        public void Wind_IncreasesEvaporation()
        {
            var still = Arid(); still.Wind = 0;
            var windy = Arid(); windy.Wind = 6;
            Assert.True(Reference(windy).EvapMmDay > Reference(still).EvapMmDay,
                "the wind (b) term raises the Dalton evaporation flux");
        }

        [Fact]
        public void NoEvaporation_WhenAmbientVapourExceedsSurface()
        {
            var c = Cases().First(x => x.Name == "humid-no-evap");
            EvapPondModel.Result res = Reference(c);
            Assert.Equal(0.0, res.DrivingForcePa, 6);
            Assert.Equal(0.0, res.EvapMol, 9);
            Rig rig = BuildRig(c, true);
            rig.Block.Calculate();
            Assert.Contains(rig.Block.GetReportWarnings(),
                w => w.IndexOf("No evaporation", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        [Fact]
        public void OnlyWaterEvaporates_SaltIsConserved()
        {
            var c = Arid();
            Rig rig = BuildRig(c, true);
            rig.Block.Calculate();
            int wi = c.Wi;
            for (int i = 0; i < c.FeedMol.Length; i++)
            {
                if (i == wi) continue;
                AssertExact(0.0, rig.Vap.Flows[i], "vapour salt[" + i + "] is zero");
                AssertExact(c.FeedMol[i], rig.Conc.Flows[i], "concentrate keeps all salt[" + i + "]");
            }
        }

        [Fact]
        public void SmallFeed_IsFeedLimited_HighCF_WithWarnings()
        {
            var c = Cases().First(x => x.Name == "small-feed-high-cf");
            EvapPondModel.Result res = Reference(c);
            Assert.True(res.FeedLimited, "climate flux x area exceeds the small feed");
            Assert.True(res.ConcentrationFactor > EvapPondModel.HighConcentrationFactor);
            Rig rig = BuildRig(c, true);
            rig.Block.Calculate();
            Assert.Contains(rig.Block.GetReportWarnings(),
                w => w.IndexOf("concentration factor", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        // ------------------------------------------------------------------
        //  3. determinism
        // ------------------------------------------------------------------
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TwentyConsecutiveRuns_AreIdentical(bool thermo11)
        {
            Rig rig = BuildRig(Arid(), thermo11);
            double[][] runs = new double[20][];
            double[][] conc = new double[20][];
            for (int r = 0; r < 20; r++)
            {
                rig.Block.Calculate();
                runs[r] = rig.Block.GetResults().Select(x => x.Value).ToArray();
                conc[r] = (double[])rig.Conc.Flows.Clone();
            }
            for (int r = 1; r < 20; r++)
            {
                for (int i = 0; i < runs[0].Length; i++)
                    Assert.True(Math.Abs(runs[r][i] - runs[0][i]) < 1e-8, "result " + i + " drifted at run " + r);
                for (int i = 0; i < conc[0].Length; i++)
                    Assert.True(Math.Abs(conc[r][i] - conc[0][i]) < 1e-8, "concentrate " + i + " drifted at run " + r);
            }
        }

        // ------------------------------------------------------------------
        //  4. results table == report rows == streams
        // ------------------------------------------------------------------
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void OutputParameters_MatchResultRows(bool thermo11)
        {
            Rig rig = BuildRig(Arid(), thermo11);
            rig.Block.Calculate();
            AssertExact(ResultOf(rig.Block, "Evaporation flux"), OutParam(rig.Block, "EvapFlux"), "EvapFlux");
            AssertExact(ResultOf(rig.Block, "Evaporation rate"), OutParam(rig.Block, "EvapRate"), "EvapRate");
            AssertExact(ResultOf(rig.Block, "Concentration factor"), OutParam(rig.Block, "ConcFactor"), "ConcFactor");
            AssertExact(ResultOf(rig.Block, "Surface temperature"), OutParam(rig.Block, "SurfaceTemp"), "SurfaceTemp");
            AssertExact(ResultOf(rig.Block, "Water evaporated"), OutParam(rig.Block, "WaterEvaporated"), "WaterEvaporated");
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ReportedResults_MatchOutletStreams_Exactly(bool thermo11)
        {
            var c = Arid();
            Rig rig = BuildRig(c, thermo11);
            rig.Block.Calculate();
            int wi = c.Wi;
            double evapMolStream = rig.Vap.Flows[wi];
            double concWater = rig.Conc.Flows[wi];
            double cfStream = c.FeedMol[wi] / concWater;
            AssertExact(evapMolStream * EvapPondModel.WaterMwKgMol, ResultOf(rig.Block, "Water evaporated"), "evap kg/s vs streams");
            AssertExact(cfStream, ResultOf(rig.Block, "Concentration factor"), "CF vs streams");
            Assert.True(rig.Conc.FlashCount > 0 && rig.Vap.FlashCount > 0, "both outlets flashed");
        }

        // ------------------------------------------------------------------
        //  5. ports, defaults, RealParameter-only, Model&References
        // ------------------------------------------------------------------
        [Fact]
        public void PortsAndDefaults_AreFactoryGrade()
        {
            var block = new EvapPond();

            var portNames = block.Ports.Cast<UnitPort>().Select(p => p.ComponentName).ToList();
            Assert.Equal(new[] { "BrineFeed", "Concentrate", "Vapor" }, portNames);

            var inputs = new Dictionary<string, object>();
            var outputs = new List<string>();
            foreach (CapeParameter p in block.Parameters)
            {
                if (UnitBase.IsInputParameter(p)) inputs[p.ComponentName] = ((ICapeParameter)p).value;
                else outputs.Add(p.ComponentName);
            }

            Assert.Equal(10000.0, Convert.ToDouble(inputs["Area"], CultureInfo.InvariantCulture));
            Assert.Equal(0.98, Convert.ToDouble(inputs["WaterActivity"], CultureInfo.InvariantCulture));
            Assert.Equal(40.0, Convert.ToDouble(inputs["RH"], CultureInfo.InvariantCulture));

            foreach (CapeParameter p in block.Parameters)
                Assert.True(p is RealParameter, "parameter '" + p.ComponentName +
                    "' must be a RealParameter (was " + p.GetType().Name + ")");

            foreach (string req in new[] { "EvapRate", "EvapFlux", "ConcFactor", "DrivingForce",
                                           "SurfaceTemp", "ConcentrateTDS", "ResidenceTime", "WaterEvaporated" })
                Assert.Contains(req, outputs);
        }

        [Fact]
        public void ModelAndReferences_InReport()
        {
            Rig rig = BuildRig(Arid(), true);
            rig.Block.Calculate();
            string report = "";
            rig.Block.ProduceReport(ref report);
            Assert.Contains("Model & References", report);
            Assert.Contains("Dalton", report);
            Assert.Contains("Penman", report);
        }
    }
}
