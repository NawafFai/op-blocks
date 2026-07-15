using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CapeOpen;
using OPBlocks.Core;
using OPBlocks.Electro;
using Xunit;

namespace OPBlocks.UnitTests
{
    /// <summary>
    /// OP-ED validation gate, structured like the OP-RO gate. Two layers:
    ///
    ///  1. STRUCTURAL — canonical cases replayed through the REAL CAPE-OPEN block
    ///     (ports, ICapeParameter, Calculate() behind the boundary guard,
    ///     ThermoProxy against Thermo 1.0 AND 1.1 mocks, outlet writing, result
    ///     read-back) must match the shared <see cref="EdModel"/> within 0.1%.
    ///
    ///  2. PHYSICAL — independent checks against the governing law. The exact anchor
    ///     is FARADAY'S LAW: at I = F ≈ 96485 A with one cell pair, 100% efficiency
    ///     and z = 1, exactly 1 mol/s of monovalent ions crosses; doubling z halves
    ///     the molar transfer; removal scales with current efficiency; the depletion
    ///     cap fires when the current outruns the available salt.
    ///
    /// Plus determinism (20 runs, 1e-8), results==streams, and the RealParameter-only
    /// / ports / Model&amp;References structural guarantees.
    /// </summary>
    public class EdCapeOpenValidationTests
    {
        private const double RelTol = 1e-3;
        private const double AbsFloor = 1e-12;
        private const double MwWater = 18.0153, MwNaCl = 58.442467, MwCaCl2 = 110.984;
        private static double F => ProcessOps.Faraday;

        public sealed class EdCase
        {
            public string Name;
            public EdModel.Mode Mode = EdModel.Mode.Rating;
            public double CellPairs = 100, Voltage = 24, Resistance = 5, Eff = 90, Valence = 1,
                          WaterT = 8, TargetRemoval = 90;
            public double[] DilMol, ConcMol, Mw;
            public string[] Ids;
            public int Wi;
            public double Td = 298.15, Tc = 298.15;
            public override string ToString() { return Name; }

            public EdModel.Spec Spec()
            {
                return new EdModel.Spec
                {
                    CalcMode = Mode, CellPairs = CellPairs, AppliedVoltageV = Voltage,
                    StackResistanceOhm = Resistance, CurrentEfficiencyPct = Eff, Valence = Valence,
                    WaterTransport = WaterT, TargetRemovalPct = TargetRemoval,
                };
            }
        }

        private static EdCase Brackish() => new EdCase
        {
            Name = "brackish-ed",
            DilMol = new[] { 55.0, 0.05 }, ConcMol = new[] { 55.0, 0.05 },
            Ids = new[] { "WATER", "NACL" }, Mw = new[] { MwWater, MwNaCl }, Wi = 0
        };

        public static IReadOnlyList<EdCase> Cases()
        {
            return new List<EdCase>
            {
                Brackish(),
                new EdCase { Name = "high-current-depletion",
                             DilMol = new[]{55.0, 0.05}, ConcMol = new[]{55.0, 0.05},
                             Ids = new[]{"WATER","NACL"}, Mw = new[]{MwWater,MwNaCl}, Wi = 0,
                             Voltage = 500, Resistance = 5 },
                new EdCase { Name = "design-90pct",
                             DilMol = new[]{55.0, 0.05}, ConcMol = new[]{55.0, 0.05},
                             Ids = new[]{"WATER","NACL"}, Mw = new[]{MwWater,MwNaCl}, Wi = 0,
                             Mode = EdModel.Mode.Design, TargetRemoval = 90 },
                new EdCase { Name = "divalent",
                             DilMol = new[]{55.0, 0.05}, ConcMol = new[]{55.0, 0.05},
                             Ids = new[]{"WATER","CACL2"}, Mw = new[]{MwWater,MwCaCl2}, Wi = 0, Valence = 2 },
            };
        }

        public static IEnumerable<object[]> CaseNames()
        {
            foreach (EdCase c in Cases())
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

        private static (EdModel.Result, EdModel.Energy) Reference(EdCase c)
        {
            EdModel.Spec s = c.Spec();
            EdModel.Result res = EdModel.Solve(s, c.DilMol, c.ConcMol, c.Wi, c.Mw);
            double prodM3h = ProcessOps.Sum(res.DiluateOut) > 1e-30 ? MassKgS(res.DiluateOut, c.Mw) / 1000.0 * 3600.0 : 0.0;
            EdModel.Energy e = EdModel.CalcEnergy(res.StackVoltageV, res.StackCurrentA, prodM3h);
            return (res, e);
        }

        // ------------------------------------------------------------------
        //  block rig
        // ------------------------------------------------------------------
        private sealed class Rig { public Electrodialysis Block; public IMockMaterial DilIn, DilOut, ConcIn, ConcOut; }

        private static object NewMock(bool t11, string[] ids, double[] mw, double[] flows = null)
        {
            if (t11) return new Mock11MaterialObject(ids, mw, 1000.0, flows, 298.15, 101325.0);
            return new MockMaterialObject(ids, mw, 1000.0, flows, 298.15, 101325.0);
        }

        private static Rig BuildRig(EdCase c, bool t11)
        {
            object dIn = NewMock(t11, c.Ids, c.Mw, (double[])c.DilMol.Clone());
            object dOut = NewMock(t11, c.Ids, c.Mw);
            object cIn = NewMock(t11, c.Ids, c.Mw, (double[])c.ConcMol.Clone());
            object cOut = NewMock(t11, c.Ids, c.Mw);
            var block = new Electrodialysis();
            Set(block, "CalcMode", c.Mode == EdModel.Mode.Design ? 1 : 0);
            Set(block, "CellPairs", c.CellPairs); Set(block, "AppliedVoltage", c.Voltage);
            Set(block, "StackResistance", c.Resistance); Set(block, "CurrentEfficiency", c.Eff);
            Set(block, "IonValence", c.Valence); Set(block, "WaterTransport", c.WaterT);
            Set(block, "TargetRemoval", c.TargetRemoval);
            Connect(block, "DiluateIn", dIn); Connect(block, "DiluateOut", dOut);
            Connect(block, "ConcentrateIn", cIn); Connect(block, "ConcentrateOut", cOut);
            return new Rig
            {
                Block = block,
                DilIn = (IMockMaterial)dIn, DilOut = (IMockMaterial)dOut,
                ConcIn = (IMockMaterial)cIn, ConcOut = (IMockMaterial)cOut
            };
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

        private static double OutParam(Electrodialysis block, string name)
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
        //  1. STRUCTURAL — block == EdModel within 0.1%, both thermo backends
        // ------------------------------------------------------------------
        [Theory]
        [MemberData(nameof(CaseNames))]
        public void Case_BlockMatchesReference_Within01Percent(string name, bool thermo11)
        {
            EdCase c = Cases().First(x => x.Name == name);
            (EdModel.Result res, EdModel.Energy e) = Reference(c);
            Rig rig = BuildRig(c, thermo11);
            rig.Block.Calculate();

            Close(res.SaltMovedMol, ResultOf(rig.Block, "Salt removed"), "saltMoved");
            Close(res.RemovalPct, ResultOf(rig.Block, "Salt removal"), "removal");
            Close(res.StackCurrentA, ResultOf(rig.Block, "Stack current"), "current");
            Close(res.StackVoltageV, ResultOf(rig.Block, "Stack voltage"), "voltage");
            Close(e.StackPowerKW, ResultOf(rig.Block, "Stack power"), "power");
            Close(e.SEC, ResultOf(rig.Block, "Specific energy (SEC)"), "SEC");
            Close(res.TdsDiluateOutPpm, ResultOf(rig.Block, "Product (diluate) TDS"), "prodTDS");
            Close(res.TdsConcentrateOutPpm, ResultOf(rig.Block, "Concentrate TDS"), "concTDS");

            for (int i = 0; i < c.DilMol.Length; i++)
            {
                double dof = rig.DilOut.Flows == null ? 0.0 : rig.DilOut.Flows[i];
                double cof = rig.ConcOut.Flows == null ? 0.0 : rig.ConcOut.Flows[i];
                Close(res.DiluateOut[i], dof, "diluateOut[" + i + "]");
                Close(res.ConcentrateOut[i], cof, "concentrateOut[" + i + "]");
                double inSum = c.DilMol[i] + c.ConcMol[i];
                Assert.True(Math.Abs(inSum - dof - cof) <= 1e-9 * Math.Max(1.0, inSum),
                    "mass balance component " + i);
            }
        }

        // ------------------------------------------------------------------
        //  2. PHYSICAL — Faraday's law (exact), valence, efficiency, depletion
        // ------------------------------------------------------------------
        [Fact]
        public void Faraday_OneEquivalentPerFaraday_Exact()
        {
            // I = F (A), 1 cell pair, 100% efficiency, z = 1  ->  exactly 1 mol/s.
            var s = new EdModel.Spec
            {
                CalcMode = EdModel.Mode.Rating, CellPairs = 1, AppliedVoltageV = F, StackResistanceOhm = 1,
                CurrentEfficiencyPct = 100, Valence = 1, WaterTransport = 0, TargetRemovalPct = 0,
            };
            var dil = new[] { 55.0, 2.0 };   // enough salt so the cap does not bite
            var conc = new[] { 55.0, 0.0 };
            EdModel.Result r = EdModel.Solve(s, dil, conc, 0, null);
            Assert.Equal(F, r.StackCurrentA, 6);
            Assert.Equal(1.0, r.FaradaicSaltMol, 9);
            Assert.Equal(1.0, r.SaltMovedMol, 9);
        }

        [Fact]
        public void Faraday_OneAmp_TransfersOneOverF()
        {
            var s = new EdModel.Spec
            {
                CalcMode = EdModel.Mode.Rating, CellPairs = 1, AppliedVoltageV = 1, StackResistanceOhm = 1,
                CurrentEfficiencyPct = 100, Valence = 1, WaterTransport = 0, TargetRemovalPct = 0,
            };
            var dil = new[] { 55.0, 0.01 };
            var conc = new[] { 55.0, 0.0 };
            EdModel.Result r = EdModel.Solve(s, dil, conc, 0, null);
            Assert.Equal(1.0 / F, r.FaradaicSaltMol, 12);
        }

        [Fact]
        public void Valence_Two_HalvesMolarTransfer()
        {
            var mono = Brackish(); mono.Valence = 1; mono.DilMol = new[] { 55.0, 2.0 };
            var di = Brackish(); di.Valence = 2; di.DilMol = new[] { 55.0, 2.0 };
            (EdModel.Result rm, _) = Reference(mono);
            (EdModel.Result rd, _) = Reference(di);
            // same current (same V/R), z doubled -> half the moles per Coulomb
            Assert.Equal(rm.FaradaicSaltMol / 2.0, rd.FaradaicSaltMol, 9);
        }

        [Fact]
        public void Removal_ScalesWithCurrentEfficiency()
        {
            var a = Brackish(); a.Eff = 45; a.DilMol = new[] { 55.0, 5.0 };
            var b = Brackish(); b.Eff = 90; b.DilMol = new[] { 55.0, 5.0 };
            (EdModel.Result ra, _) = Reference(a);
            (EdModel.Result rb, _) = Reference(b);
            Assert.Equal(2.0, rb.FaradaicSaltMol / ra.FaradaicSaltMol, 6); // 90/45 = 2x
        }

        [Fact]
        public void Design_RecoversTargetRemoval_AndRoundTrips()
        {
            var c = Cases().First(x => x.Name == "design-90pct");
            (EdModel.Result res, _) = Reference(c);
            Assert.InRange(res.RemovalPct, 89.9, 90.1);
            // feed the required current back through Rating -> same removal
            var rating = new EdModel.Spec
            {
                CalcMode = EdModel.Mode.Rating, CellPairs = c.CellPairs,
                AppliedVoltageV = res.StackVoltageV, StackResistanceOhm = c.Resistance,
                CurrentEfficiencyPct = c.Eff, Valence = c.Valence, WaterTransport = c.WaterT,
            };
            EdModel.Result back = EdModel.Solve(rating, c.DilMol, c.ConcMol, c.Wi, c.Mw);
            Assert.Equal(res.SaltMovedMol, back.SaltMovedMol, 9);
        }

        [Fact]
        public void HighCurrent_HitsDepletionCap_WithWarning()
        {
            var c = Cases().First(x => x.Name == "high-current-depletion");
            (EdModel.Result res, _) = Reference(c);
            Assert.True(res.DepletionLimited, "faradaic transfer should exceed the available salt");
            Assert.InRange(res.RemovalPct, EdModel.MaxDepletion * 100 - 0.01, EdModel.MaxDepletion * 100 + 0.01);

            Rig rig = BuildRig(c, true);
            rig.Block.Calculate();
            Assert.Contains(rig.Block.GetReportWarnings(),
                w => w.IndexOf("depletion", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        // ------------------------------------------------------------------
        //  3. determinism
        // ------------------------------------------------------------------
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TwentyConsecutiveRuns_AreIdentical(bool thermo11)
        {
            EdCase c = Brackish();
            Rig rig = BuildRig(c, thermo11);
            double[][] runs = new double[20][];
            double[][] dil = new double[20][];
            for (int r = 0; r < 20; r++)
            {
                rig.Block.Calculate();
                runs[r] = rig.Block.GetResults().Select(x => x.Value).ToArray();
                dil[r] = (double[])rig.DilOut.Flows.Clone();
            }
            for (int r = 1; r < 20; r++)
            {
                for (int i = 0; i < runs[0].Length; i++)
                    Assert.True(Math.Abs(runs[r][i] - runs[0][i]) < 1e-8, "result " + i + " drifted at run " + r);
                for (int i = 0; i < dil[0].Length; i++)
                    Assert.True(Math.Abs(dil[r][i] - dil[0][i]) < 1e-8, "diluate " + i + " drifted at run " + r);
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
            EdCase c = Brackish();
            Rig rig = BuildRig(c, thermo11);
            rig.Block.Calculate();
            AssertExact(ResultOf(rig.Block, "Salt removed"), OutParam(rig.Block, "SaltRemoved"), "SaltRemoved");
            AssertExact(ResultOf(rig.Block, "Salt removal"), OutParam(rig.Block, "SaltRemoval"), "SaltRemoval");
            AssertExact(ResultOf(rig.Block, "Stack current"), OutParam(rig.Block, "StackCurrent"), "StackCurrent");
            AssertExact(ResultOf(rig.Block, "Stack power"), OutParam(rig.Block, "StackPower"), "StackPower");
            AssertExact(ResultOf(rig.Block, "Specific energy (SEC)"), OutParam(rig.Block, "SEC"), "SEC");
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void SaltRemoved_MatchesOutletStreams_Exactly(bool thermo11)
        {
            EdCase c = Brackish();
            Rig rig = BuildRig(c, thermo11);
            rig.Block.Calculate();
            double[] dof = rig.DilOut.Flows, cof = rig.ConcOut.Flows;
            int wi = c.Wi;
            double saltRemovedFromStreams = 0, saltInFeed = 0;
            for (int i = 0; i < c.DilMol.Length; i++)
                if (i != wi) { saltRemovedFromStreams += c.DilMol[i] - dof[i]; saltInFeed += c.DilMol[i]; }
            AssertExact(saltRemovedFromStreams, ResultOf(rig.Block, "Salt removed"), "salt removed vs streams");
            AssertExact(saltRemovedFromStreams / saltInFeed * 100.0, ResultOf(rig.Block, "Salt removal"), "removal vs streams");
            Assert.True(rig.DilOut.FlashCount > 0 && rig.ConcOut.FlashCount > 0, "both outlets flashed");
        }

        // ------------------------------------------------------------------
        //  5. ports, defaults, RealParameter-only, Model&References
        // ------------------------------------------------------------------
        [Fact]
        public void PortsAndDefaults_AreFactoryGrade()
        {
            var block = new Electrodialysis();

            var portNames = block.Ports.Cast<UnitPort>().Select(p => p.ComponentName).ToList();
            Assert.Equal(new[] { "DiluateIn", "DiluateOut", "ConcentrateIn", "ConcentrateOut" }, portNames);

            var inputs = new Dictionary<string, object>();
            var outputs = new List<string>();
            foreach (CapeParameter p in block.Parameters)
            {
                if (UnitBase.IsInputParameter(p)) inputs[p.ComponentName] = ((ICapeParameter)p).value;
                else outputs.Add(p.ComponentName);
            }

            Assert.Equal(0.0, Convert.ToDouble(inputs["CalcMode"], CultureInfo.InvariantCulture));
            Assert.Equal(100.0, Convert.ToDouble(inputs["CellPairs"], CultureInfo.InvariantCulture));
            Assert.Equal(24.0, Convert.ToDouble(inputs["AppliedVoltage"], CultureInfo.InvariantCulture));
            Assert.Equal(90.0, Convert.ToDouble(inputs["CurrentEfficiency"], CultureInfo.InvariantCulture));

            // GOLDEN RULE: every parameter must be a RealParameter — the old
            // AddIntParameter("CellPairs") would have blanked Aspen's whole grid.
            foreach (CapeParameter p in block.Parameters)
                Assert.True(p is RealParameter, "parameter '" + p.ComponentName +
                    "' must be a RealParameter (was " + p.GetType().Name + ")");

            foreach (string req in new[] { "SaltRemoved", "SaltRemoval", "DiluateTDSout", "StackCurrent",
                                           "StackPower", "SEC", "RequiredCurrent", "RequiredVoltage" })
                Assert.Contains(req, outputs);
        }

        [Fact]
        public void ModelAndReferences_InReport()
        {
            Rig rig = BuildRig(Brackish(), true);
            rig.Block.Calculate();
            string report = "";
            rig.Block.ProduceReport(ref report);
            Assert.Contains("Model & References", report);
            Assert.Contains("Faraday", report);
            Assert.Contains("Strathmann", report);
        }
    }
}
