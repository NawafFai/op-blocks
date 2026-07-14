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
    /// The OP-RO approval gate (owner, 2026-07-14). Two independent layers:
    ///
    ///  1. STRUCTURAL — 9 canonical cases replayed through the REAL CAPE-OPEN
    ///     block (ports, ICapeParameter, Calculate() behind the boundary guard,
    ///     ThermoProxy against Thermo 1.0 AND 1.1 mocks, outlet writing, result
    ///     read-back) must match the shared <see cref="RoModel"/> reference within
    ///     0.1%. Block and reference share RoModel, so agreement is exact — this
    ///     proves the CAPE-OPEN wiring, not the physics.
    ///
    ///  2. PHYSICAL — independent hand-reasoned range/invariant checks pin the
    ///     physics itself: realistic seawater recovery, van 't Hoff osmotic
    ///     pressure, net SEC in the industrial 2–3 kWh/m3 band with a pressure
    ///     exchanger, ERD saving &gt; 0, gross &gt; net, exact mass balance.
    ///
    /// Plus determinism (20 runs, 1e-8) and results==streams exactness.
    /// </summary>
    public class RoCapeOpenValidationTests
    {
        private const double RelTol = 1e-3;    // 0.1% structural gate
        private const double AbsFloor = 1e-12;

        // seawater ≈ 35,000 ppm NaCl, per-second basis (intensive results are
        // scale-free; feed magnitude only sets absolute area in Design mode).
        private const double MwWater = 18.0153, MwNaCl = 58.442467, MwMgCl2 = 95.211, MwKCl = 74.551;

        public sealed class RoCase
        {
            public string Name;
            public RoModel.Mode Mode = RoModel.Mode.Rating;
            public RoModel.Erd Erd = RoModel.Erd.None;
            public double Area = 40, PermA = 1.0, Rej = 99.5, Applied = 60, VantHoff = 2.0,
                          PumpEff = 80, MaxRec = 50, TargetRec = 45, DesignFlux = 15, ErdEff = 96;
            public double[] FeedMol;
            public double[] Mw;
            public int Wi;
            public double Tk = 298.15;
            public override string ToString() { return Name; }

            public RoModel.Spec Spec()
            {
                return new RoModel.Spec
                {
                    CalcMode = Mode, ErdType = Erd,
                    AreaM2 = Area, WaterPermA = PermA, SaltRejPct = Rej, AppliedBar = Applied,
                    VantHoffI = VantHoff, PumpEffPct = PumpEff, MaxRecoveryPct = MaxRec,
                    TargetRecoveryPct = TargetRec, DesignFluxLMH = DesignFlux, ErdEffPct = ErdEff,
                };
            }
        }

        // seawater feed (35,000 ppm NaCl) sized so a default seawater rating
        // (A=1, 60 bar, 40 m²) lands a realistic ~45% recovery, well under the cap.
        private static double[] Seawater()
        {
            return new[] { 23.5, 0.263 }; // water, NaCl (mol/s)
        }

        public static IReadOnlyList<RoCase> Cases()
        {
            return new List<RoCase>
            {
                new RoCase { Name = "seawater-rating", FeedMol = Seawater(), Mw = new[]{MwWater,MwNaCl}, Wi = 0 },
                new RoCase { Name = "seawater-rating-PX", FeedMol = Seawater(), Mw = new[]{MwWater,MwNaCl}, Wi = 0,
                             Erd = RoModel.Erd.PressureExchanger, ErdEff = 96 },
                new RoCase { Name = "seawater-rating-turbine", FeedMol = Seawater(), Mw = new[]{MwWater,MwNaCl}, Wi = 0,
                             Erd = RoModel.Erd.Turbine, ErdEff = 80 },
                new RoCase { Name = "design-45pct", FeedMol = Seawater(), Mw = new[]{MwWater,MwNaCl}, Wi = 0,
                             Mode = RoModel.Mode.Design, TargetRec = 45, DesignFlux = 15 },
                new RoCase { Name = "design-45pct-PX", FeedMol = Seawater(), Mw = new[]{MwWater,MwNaCl}, Wi = 0,
                             Mode = RoModel.Mode.Design, TargetRec = 45, DesignFlux = 15,
                             Erd = RoModel.Erd.PressureExchanger, ErdEff = 96 },
                new RoCase { Name = "brackish-high-recovery", FeedMol = new[]{55.0, 0.11}, Mw = new[]{MwWater,MwNaCl}, Wi = 0,
                             PermA = 6, Applied = 20, MaxRec = 85, TargetRec = 80 },
                new RoCase { Name = "oversized-area-capped", FeedMol = Seawater(), Mw = new[]{MwWater,MwNaCl}, Wi = 0,
                             Area = 400, PermA = 5, Applied = 70 },
                new RoCase { Name = "no-permeation", FeedMol = Seawater(), Mw = new[]{MwWater,MwNaCl}, Wi = 0,
                             Applied = 20 /* below osmotic */ },
                new RoCase { Name = "multisalt", FeedMol = new[]{23.5, 0.20, 0.06}, Mw = new[]{MwWater,MwNaCl,MwMgCl2}, Wi = 0 },
            };
        }

        public static IEnumerable<object[]> CaseNames()
        {
            foreach (RoCase c in Cases())
            {
                yield return new object[] { c.Name, false }; // Thermo 1.0
                yield return new object[] { c.Name, true };  // Thermo 1.1
            }
        }

        // ------------------------------------------------------------------
        //  reference (shared RoModel, ρ = 1000 volumes as the mocks report)
        // ------------------------------------------------------------------
        private static double PiFeed(RoCase c)
        {
            return ProcessOps.OsmoticPressureBar(null, c.FeedMol, c.Wi, c.VantHoff, c.Tk, null);
        }

        private static double MassKgS(double[] mol, double[] mw)
        {
            double kg = 0;
            for (int i = 0; i < mol.Length; i++) kg += mol[i] * mw[i] / 1000.0;
            return kg;
        }

        private static (RoModel.Split, RoModel.Energy) Reference(RoCase c)
        {
            RoModel.Spec s = c.Spec();
            RoModel.Split split = RoModel.Solve(s, c.FeedMol, c.Wi, c.Mw, c.Tk, PiFeed(c));
            double feedM3s = MassKgS(c.FeedMol, c.Mw) / 1000.0;
            double permM3s = ProcessOps.Sum(split.PermMol) > 1e-30 ? MassKgS(split.PermMol, c.Mw) / 1000.0 : 0.0;
            double concM3s = ProcessOps.Sum(split.ConcMol) > 1e-30 ? MassKgS(split.ConcMol, c.Mw) / 1000.0 : 0.0;
            RoModel.Energy e = RoModel.CalcEnergy(s, split.AppliedBarUsed, feedM3s, permM3s, concM3s);
            return (split, e);
        }

        // ------------------------------------------------------------------
        //  block rig
        // ------------------------------------------------------------------
        private sealed class Rig { public ReverseOsmosis Block; public IMockMaterial Feed, Perm, Conc; }

        private static object NewMock(bool t11, string[] ids, double[] mw, double[] flows = null)
        {
            if (t11) return new Mock11MaterialObject(ids, mw, 1000.0, flows, 298.15, 101325.0);
            return new MockMaterialObject(ids, mw, 1000.0, flows, 298.15, 101325.0);
        }

        private static Rig BuildRig(RoCase c, bool t11)
        {
            var ids = new string[c.FeedMol.Length];
            for (int i = 0; i < ids.Length; i++) ids[i] = (i == c.Wi) ? "WATER" : "SALT" + i;
            object feed = NewMock(t11, ids, c.Mw, (double[])c.FeedMol.Clone());
            object perm = NewMock(t11, ids, c.Mw);
            object conc = NewMock(t11, ids, c.Mw);
            var block = new ReverseOsmosis();
            Set(block, "CalcMode", c.Mode == RoModel.Mode.Design ? 1 : 0);
            Set(block, "ERDType", c.Erd == RoModel.Erd.PressureExchanger ? 1
                                : c.Erd == RoModel.Erd.Turbine ? 2 : 0);
            Set(block, "Area", c.Area); Set(block, "WaterPermA", c.PermA); Set(block, "SaltRejection", c.Rej);
            Set(block, "AppliedPressure", c.Applied); Set(block, "VantHoffI", c.VantHoff); Set(block, "PumpEff", c.PumpEff);
            Set(block, "MaxRecovery", c.MaxRec); Set(block, "TargetRecovery", c.TargetRec);
            Set(block, "DesignFlux", c.DesignFlux); Set(block, "ERDEff", c.ErdEff);
            Connect(block, "Feed", feed); Connect(block, "Permeate", perm); Connect(block, "Concentrate", conc);
            return new Rig { Block = block, Feed = (IMockMaterial)feed, Perm = (IMockMaterial)perm, Conc = (IMockMaterial)conc };
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

        private static bool HasResult(UnitBase block, string label)
        {
            return block.GetResults().Any(r => r.Label == label);
        }

        private static double OutParam(ReverseOsmosis block, string name)
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

        // ------------------------------------------------------------------
        //  1. STRUCTURAL — block == RoModel within 0.1%, both thermo backends
        // ------------------------------------------------------------------
        [Theory]
        [MemberData(nameof(CaseNames))]
        public void Case_BlockMatchesReference_Within01Percent(string name, bool thermo11)
        {
            RoCase c = Cases().First(x => x.Name == name);
            (RoModel.Split split, RoModel.Energy e) = Reference(c);
            Rig rig = BuildRig(c, thermo11);
            rig.Block.Calculate();

            Close(split.Recovery * 100.0, ResultOf(rig.Block, "Water recovery"), "recovery");
            Close(split.FluxLMH, ResultOf(rig.Block, "Permeate flux"), "flux");
            Close(split.PiFeedBar, ResultOf(rig.Block, "Feed osmotic pressure"), "piFeed");
            Close(split.PiAvgBar, ResultOf(rig.Block, "Average osmotic pressure"), "piAvg");
            Close(split.NdpBar, ResultOf(rig.Block, "Net driving pressure"), "ndp");
            Close(split.TdsPermPpm, ResultOf(rig.Block, "Permeate TDS"), "permTDS");
            Close(split.TdsConcPpm, ResultOf(rig.Block, "Concentrate TDS"), "concTDS");
            Close(split.SaltRejObsPct, ResultOf(rig.Block, "Salt rejection (observed)"), "rejObs");
            Close(e.GrossPumpKW, ResultOf(rig.Block, "Gross pump power"), "grossPump");
            Close(e.SecGross, ResultOf(rig.Block, "Gross SEC"), "grossSEC");
            if (c.Erd != RoModel.Erd.None)
            {
                Close(e.ErdRecoveredKW, ResultOf(rig.Block, "ERD recovered power"), "erdRecovered");
                Close(e.NetPumpKW, ResultOf(rig.Block, "Net pump power"), "netPump");
                Close(e.SecNet, ResultOf(rig.Block, "Net SEC"), "netSEC");
                Close(e.EnergySavingPct, ResultOf(rig.Block, "Energy saving"), "saving");
            }
            if (c.Mode == RoModel.Mode.Design)
            {
                Close(split.RequiredAreaM2, ResultOf(rig.Block, "Required membrane area"), "reqArea");
                Close(split.RequiredPressureBar, ResultOf(rig.Block, "Required applied pressure"), "reqPressure");
            }

            // outlet streams per component + exact mass balance
            for (int i = 0; i < c.FeedMol.Length; i++)
            {
                double pm = rig.Perm.Flows == null ? 0.0 : rig.Perm.Flows[i];
                double cm = rig.Conc.Flows == null ? 0.0 : rig.Conc.Flows[i];
                Close(split.PermMol[i], pm, "permMol[" + i + "]");
                Close(split.ConcMol[i], cm, "concMol[" + i + "]");
                Assert.True(Math.Abs(c.FeedMol[i] - pm - cm) <= 1e-9 * Math.Max(1.0, c.FeedMol[i]),
                    "mass balance component " + i);
            }
        }

        // ------------------------------------------------------------------
        //  2. PHYSICAL — independent hand-reasoned checks on the physics
        // ------------------------------------------------------------------
        [Fact]
        public void Seawater_OsmoticPressure_IsTextbook()
        {
            // 35,000 ppm NaCl → published osmotic pressure ≈ 28–33 bar.
            var c = Cases().First(x => x.Name == "seawater-rating");
            double pi = PiFeed(c);
            Assert.InRange(pi, 28.0, 33.0);
        }

        [Fact]
        public void Seawater_Rating_RecoveryIsRealistic_NotNinetyFive()
        {
            var c = Cases().First(x => x.Name == "seawater-rating");
            (RoModel.Split split, _) = Reference(c);
            // single-stage SWRO realistic band; emphatically not the old 95%.
            Assert.InRange(split.Recovery, 0.30, 0.52);
        }

        [Fact]
        public void Seawater_WithPX_NetSEC_InIndustrialBand()
        {
            var c = Cases().First(x => x.Name == "design-45pct-PX");
            (RoModel.Split split, RoModel.Energy e) = Reference(c);
            Assert.InRange(split.Recovery, 0.449, 0.451);           // design target met
            Assert.InRange(split.RequiredPressureBar, 50.0, 70.0);  // realistic SWRO pressure
            Assert.InRange(e.SecGross, 3.2, 5.5);                   // gross before ERD
            Assert.InRange(e.SecNet, 2.0, 3.0);                     // owner's target band
            Assert.True(e.SecNet < e.SecGross, "ERD must lower SEC");
            Assert.InRange(e.EnergySavingPct, 30.0, 55.0);
        }

        [Fact]
        public void Turbine_SavesLessThanPX()
        {
            (_, RoModel.Energy px) = Reference(Cases().First(x => x.Name == "seawater-rating-PX"));
            (_, RoModel.Energy tb) = Reference(Cases().First(x => x.Name == "seawater-rating-turbine"));
            Assert.True(tb.EnergySavingPct > 0, "turbine still recovers energy");
            Assert.True(px.EnergySavingPct > tb.EnergySavingPct, "PX (96%) beats turbine (80%)");
        }

        [Fact]
        public void NoErd_MeansZeroRecovery_NetEqualsGross()
        {
            (_, RoModel.Energy e) = Reference(Cases().First(x => x.Name == "seawater-rating"));
            Assert.Equal(0.0, e.ErdRecoveredKW, 12);
            Assert.Equal(e.GrossPumpKW, e.NetPumpKW, 12);
            Assert.Equal(0.0, e.EnergySavingPct, 9);
        }

        [Fact]
        public void OversizedArea_CapsAtMaxRecovery_WithWarning()
        {
            var c = Cases().First(x => x.Name == "oversized-area-capped");
            (RoModel.Split split, _) = Reference(c);
            Assert.True(split.RecoveryCapped, "should hit the MaxRecovery cap");
            Assert.Equal(c.MaxRec / 100.0, split.Recovery, 6);

            Rig rig = BuildRig(c, true);
            rig.Block.Calculate();
            Assert.Contains(rig.Block.GetReportWarnings(), w => w.IndexOf("MaxRecovery", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        [Fact]
        public void NoPermeation_WhenPressureBelowOsmotic()
        {
            var c = Cases().First(x => x.Name == "no-permeation");
            (RoModel.Split split, _) = Reference(c);
            Assert.Equal(0.0, split.Recovery, 9);
            Rig rig = BuildRig(c, true);
            rig.Block.Calculate();
            Assert.Contains(rig.Block.GetReportWarnings(), w => w.IndexOf("net driving pressure", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        [Fact]
        public void Design_ComputesAreaAndPressure()
        {
            var c = Cases().First(x => x.Name == "design-45pct");
            Rig rig = BuildRig(c, true);
            rig.Block.Calculate();
            Assert.True(ResultOf(rig.Block, "Required membrane area") > 0);
            Assert.InRange(ResultOf(rig.Block, "Required applied pressure"), 45.0, 75.0);
            Assert.InRange(ResultOf(rig.Block, "Water recovery"), 44.9, 45.1);
        }

        // ------------------------------------------------------------------
        //  3. determinism — 20 consecutive runs identical to < 1e-8
        // ------------------------------------------------------------------
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TwentyConsecutiveRuns_AreIdentical(bool thermo11)
        {
            RoCase c = Cases().First(x => x.Name == "seawater-rating-PX");
            Rig rig = BuildRig(c, thermo11);
            double[][] runs = new double[20][];
            double[][] perm = new double[20][];
            for (int r = 0; r < 20; r++)
            {
                rig.Block.Calculate();
                runs[r] = rig.Block.GetResults().Select(x => x.Value).ToArray();
                perm[r] = (double[])rig.Perm.Flows.Clone();
            }
            for (int r = 1; r < 20; r++)
            {
                for (int i = 0; i < runs[0].Length; i++)
                    Assert.True(Math.Abs(runs[r][i] - runs[0][i]) < 1e-8, "result " + i + " drifted at run " + r);
                for (int i = 0; i < perm[0].Length; i++)
                    Assert.True(Math.Abs(perm[r][i] - perm[0][i]) < 1e-8, "permeate " + i + " drifted at run " + r);
            }
        }

        // ------------------------------------------------------------------
        //  4. results table (output parameters) == report rows == streams
        // ------------------------------------------------------------------
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void OutputParameters_MatchResultRows(bool thermo11)
        {
            RoCase c = Cases().First(x => x.Name == "seawater-rating-PX");
            Rig rig = BuildRig(c, thermo11);
            rig.Block.Calculate();

            AssertExact(ResultOf(rig.Block, "Water recovery"), OutParam(rig.Block, "Recovery"), "Recovery");
            AssertExact(ResultOf(rig.Block, "Permeate flow"), OutParam(rig.Block, "PermeateFlow"), "PermeateFlow");
            AssertExact(ResultOf(rig.Block, "Permeate TDS"), OutParam(rig.Block, "PermeateTDS"), "PermeateTDS");
            AssertExact(ResultOf(rig.Block, "Gross pump power"), OutParam(rig.Block, "PumpPower"), "PumpPower");
            AssertExact(ResultOf(rig.Block, "Net SEC"), OutParam(rig.Block, "NetSEC"), "NetSEC");
            AssertExact(ResultOf(rig.Block, "Energy saving"), OutParam(rig.Block, "EnergySaving"), "EnergySaving");
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ReportedResults_MatchOutletStreams_Exactly(bool thermo11)
        {
            RoCase c = Cases().First(x => x.Name == "seawater-rating");
            Rig rig = BuildRig(c, thermo11);
            rig.Block.Calculate();

            double[] pm = rig.Perm.Flows, cm = rig.Conc.Flows;
            int wi = c.Wi;
            double permKg = 0, permSalt = 0, concKg = 0, concSalt = 0;
            for (int i = 0; i < c.FeedMol.Length; i++)
            {
                permKg += pm[i] * c.Mw[i] / 1000.0; concKg += cm[i] * c.Mw[i] / 1000.0;
                if (i != wi) { permSalt += pm[i] * c.Mw[i] / 1000.0; concSalt += cm[i] * c.Mw[i] / 1000.0; }
            }
            double recovery = pm[wi] / c.FeedMol[wi] * 100.0;
            double permM3h = permKg / 1000.0 * 3600.0;
            double tdsPerm = permSalt / permKg * 1e6;
            double tdsConc = concSalt / concKg * 1e6;

            AssertExact(recovery, ResultOf(rig.Block, "Water recovery"), "recovery vs streams");
            AssertExact(permM3h, ResultOf(rig.Block, "Permeate flow"), "permeate flow vs streams");
            AssertExact(tdsPerm, ResultOf(rig.Block, "Permeate TDS"), "permeate TDS vs streams");
            AssertExact(tdsConc, ResultOf(rig.Block, "Concentrate TDS"), "concentrate TDS vs streams");
            Assert.True(rig.Perm.FlashCount > 0 && rig.Conc.FlashCount > 0, "both outlets flashed");
        }

        private static void AssertExact(double expected, double actual, string what)
        {
            Assert.True(Math.Abs(actual - expected) <= 1e-10 * Math.Max(1.0, Math.Abs(expected)),
                what + ": " + expected.ToString("R") + " vs " + actual.ToString("R"));
        }

        // ------------------------------------------------------------------
        //  5. ports, defaults, and the results grid the Aspen wizard renders
        // ------------------------------------------------------------------
        [Fact]
        public void PortsAndDefaults_MatchOwnerSpec()
        {
            var block = new ReverseOsmosis();

            var portNames = block.Ports.Cast<UnitPort>().Select(p => p.ComponentName).ToList();
            Assert.Equal(new[] { "Feed", "Concentrate", "Permeate" }, portNames);

            var inputs = new Dictionary<string, object>();
            var outputs = new List<string>();
            foreach (CapeParameter p in block.Parameters)
            {
                if (UnitBase.IsInputParameter(p)) inputs[p.ComponentName] = ((ICapeParameter)p).value;
                else outputs.Add(p.ComponentName);
            }

            // realistic seawater defaults (owner spec §4 — no more 95%). CalcMode &
            // ERDType are integer-coded REAL params (0 = Rating / None) so Aspen's
            // grid renders them — an OptionParameter would blank the whole grid.
            Assert.Equal(0.0, Convert.ToDouble(inputs["CalcMode"], CultureInfo.InvariantCulture));
            Assert.Equal(0.0, Convert.ToDouble(inputs["ERDType"], CultureInfo.InvariantCulture));
            Assert.Equal(50.0, Convert.ToDouble(inputs["MaxRecovery"], CultureInfo.InvariantCulture));
            Assert.Equal(60.0, Convert.ToDouble(inputs["AppliedPressure"], CultureInfo.InvariantCulture));
            foreach (string req in new[] { "Area", "WaterPermA", "SaltRejection", "VantHoffI", "PumpEff",
                                           "TargetRecovery", "DesignFlux", "ERDEff" })
                Assert.True(inputs.ContainsKey(req), "missing input: " + req);
            // every parameter must be a RealParameter (the only type Aspen's grid renders)
            foreach (CapeParameter p in block.Parameters)
                Assert.True(p is RealParameter, "parameter '" + p.ComponentName +
                    "' must be a RealParameter for Aspen's grid to render it (was " + p.GetType().Name + ")");

            // owner's results table + the new ERD/design outputs
            foreach (string req in new[] { "Recovery", "PermeateFlow", "PermeateTDS", "SaltRejObs",
                                           "PumpPower", "SEC", "ERDRecoveredPower", "NetPumpPower",
                                           "NetSEC", "EnergySaving", "RequiredArea", "RequiredPressure" })
                Assert.Contains(req, outputs);
        }

        [Fact]
        public void ModelAndReferences_InReport()
        {
            var c = Cases().First(x => x.Name == "seawater-rating");
            Rig rig = BuildRig(c, true);
            rig.Block.Calculate();
            string report = "";
            rig.Block.ProduceReport(ref report);
            Assert.Contains("Model & References", report);
            Assert.Contains("solution-diffusion", report);
            Assert.Contains("Baker", report);
        }
    }
}
