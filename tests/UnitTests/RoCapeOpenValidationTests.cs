using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using CapeOpen;
using OPBlocks.Core;
using OPBlocks.Desalination;
using Xunit;

namespace OPBlocks.UnitTests
{
    /// <summary>
    /// The OP-RO accuracy gate (owner requirement, 2026-07-14): every one of the
    /// 9 Core-generated reference cases (tests/OproValidation/cases.txt) must be
    /// reproduced by the REAL CAPE-OPEN block — constructor ports, parameters set
    /// through ICapeParameter, Calculate() through the boundary guard, thermo
    /// marshalled by ThermoProxy against a Thermo 1.0 material object — within
    /// 0.1%. Plus the owner's determinism bar (20 repeat runs, 1e-8) and the
    /// results==streams exactness bar.
    /// </summary>
    public class RoCapeOpenValidationTests
    {
        private const double RelTol = 1e-3;    // 0.1%
        private const double AbsFloor = 1e-12; // for reference values that are exactly 0

        // ------------------------------------------------------------------
        //  cases.txt parsing
        // ------------------------------------------------------------------
        public sealed class RefCase
        {
            public string Name;
            public int NC, KH2O;            // KH2O 1-based, 0 = no water
            public double TK;
            public double[] MwGmol;
            public double Area, PermA, SaltRej, AppPres, VantHoff, PumpEff;
            public double[] FeedKmol;
            public double PiBar, Recov, Jw, QPerm, PumpKW, Sec, TdsPerm, TdsConc, RejObs;
            public double[] PermKmol, ConcKmol;
            public override string ToString() { return Name; }
        }

        public static IReadOnlyList<RefCase> LoadCases()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cases.txt");
            var ci = CultureInfo.InvariantCulture;
            string[] lines = File.ReadAllLines(path);
            int k = 0;
            int n = int.Parse(lines[k++], ci);
            var cases = new List<RefCase>();
            for (int c = 0; c < n; c++)
            {
                var rc = new RefCase { Name = lines[k++] };
                double[] head = Split(lines[k++]);
                rc.NC = (int)head[0]; rc.KH2O = (int)head[1]; rc.TK = head[2];
                rc.MwGmol = Split(lines[k++]);
                double[] p = Split(lines[k++]);
                rc.Area = p[0]; rc.PermA = p[1]; rc.SaltRej = p[2];
                rc.AppPres = p[3]; rc.VantHoff = p[4]; rc.PumpEff = p[5];
                rc.FeedKmol = Split(lines[k++]);
                double[] r = Split(lines[k++]);
                rc.PiBar = r[0]; rc.Recov = r[1]; rc.Jw = r[2]; rc.QPerm = r[3];
                rc.PumpKW = r[4]; rc.Sec = r[5]; rc.TdsPerm = r[6]; rc.TdsConc = r[7]; rc.RejObs = r[8];
                rc.PermKmol = Split(lines[k++]);
                rc.ConcKmol = Split(lines[k++]);
                cases.Add(rc);
            }
            return cases;
        }

        private static double[] Split(string line)
        {
            return line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                       .Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();
        }

        public static IEnumerable<object[]> CaseNames()
        {
            foreach (RefCase c in LoadCases())
            {
                yield return new object[] { c.Name, false };  // Thermo 1.0 (DWSIM / legacy hosts)
                yield return new object[] { c.Name, true };   // Thermo 1.1 (Aspen Plus V14)
            }
        }

        // ------------------------------------------------------------------
        //  replay through the real block
        // ------------------------------------------------------------------
        private sealed class Rig
        {
            public ReverseOsmosis Block;
            public IMockMaterial Feed, Perm, Conc;
        }

        private static object NewMock(bool thermo11, string[] ids, double[] mw,
                                      double[] flows = null, double tK = 298.15, double pPa = 101325.0)
        {
            if (thermo11)
                return new Mock11MaterialObject(ids, mw, 1000.0, flows, tK, pPa);
            return new MockMaterialObject(ids, mw, 1000.0, flows, tK, pPa);
        }

        private static Rig BuildRig(RefCase c, bool thermo11)
        {
            // Component ids: give the water slot a recognisable name.
            var ids = new string[c.NC];
            for (int i = 0; i < c.NC; i++)
                ids[i] = (i == c.KH2O - 1) ? "WATER" : "SALT" + i;

            var feedFlows = c.FeedKmol.Select(x => x * 1000.0).ToArray(); // kmol/s -> mol/s
            object feed = NewMock(thermo11, ids, c.MwGmol, feedFlows, c.TK, 101325.0);
            object perm = NewMock(thermo11, ids, c.MwGmol);
            object conc = NewMock(thermo11, ids, c.MwGmol);
            var rig = new Rig
            {
                Block = new ReverseOsmosis(),
                Feed = (IMockMaterial)feed,
                Perm = (IMockMaterial)perm,
                Conc = (IMockMaterial)conc,
            };

            Set(rig.Block, "Area", c.Area);
            Set(rig.Block, "WaterPermA", c.PermA);
            Set(rig.Block, "SaltRejection", c.SaltRej);
            Set(rig.Block, "AppliedPressure", c.AppPres);
            Set(rig.Block, "VantHoffI", c.VantHoff);
            Set(rig.Block, "PumpEff", c.PumpEff);

            Connect(rig.Block, "Feed", feed);
            Connect(rig.Block, "Permeate", perm);
            Connect(rig.Block, "Concentrate", conc);
            return rig;
        }

        private static void Set(CapeUnitBase block, string param, double value)
        {
            foreach (CapeParameter p in block.Parameters)
                if (string.Equals(p.ComponentName, param, StringComparison.OrdinalIgnoreCase))
                {
                    ((ICapeParameter)p).value = value;
                    return;
                }
            throw new InvalidOperationException("parameter not found: " + param);
        }

        private static void Connect(CapeUnitBase block, string portName, object material)
        {
            foreach (UnitPort p in block.Ports)
                if (string.Equals(p.ComponentName, portName, StringComparison.OrdinalIgnoreCase))
                {
                    p.Connect(material);
                    return;
                }
            throw new InvalidOperationException("port not found: " + portName);
        }

        private static double ResultOf(UnitBase block, string label)
        {
            UnitBase.ResultEntry row = block.GetResults()
                .FirstOrDefault(r => r.Label == label);
            Assert.True(row != null, "missing result row: " + label);
            return row.Value;
        }

        private static void Close(double expected, double actual, string what)
        {
            double tol = Math.Max(AbsFloor, Math.Abs(expected) * RelTol);
            Assert.True(Math.Abs(actual - expected) <= tol,
                what + ": expected " + expected.ToString("R") + ", got " + actual.ToString("R"));
        }

        // ------------------------------------------------------------------
        //  1. the 0.1% accuracy gate, all 9 cases
        // ------------------------------------------------------------------
        [Theory]
        [MemberData(nameof(CaseNames))]
        public void Case_MatchesCoreReference_Within01Percent(string name, bool thermo11)
        {
            RefCase c = LoadCases().First(x => x.Name == name);
            Rig rig = BuildRig(c, thermo11);
            rig.Block.Calculate();

            Close(c.PiBar, ResultOf(rig.Block, "Feed osmotic pressure"), "PIBAR [bar]");
            Close(c.Recov * 100.0, ResultOf(rig.Block, "Water recovery"), "RECOVERY [%]");
            Close(c.Jw, ResultOf(rig.Block, "Permeate flux"), "JW [L/m2/h]");
            Close(c.QPerm, ResultOf(rig.Block, "Permeate flow"), "QPERM [m3/h]");
            Close(c.PumpKW, ResultOf(rig.Block, "Pump power"), "PUMPKW [kW]");
            Close(c.Sec, ResultOf(rig.Block, "Specific energy (SEC)"), "SEC [kWh/m3]");
            Close(c.TdsPerm, ResultOf(rig.Block, "Permeate TDS"), "TDSPERM [ppm]");
            Close(c.TdsConc, ResultOf(rig.Block, "Concentrate TDS"), "TDSCONC [ppm]");
            Close(c.RejObs, ResultOf(rig.Block, "Salt rejection (observed)"), "SREJOBS [%]");

            // outlet streams, per component (unset outlet = all-zero flows)
            for (int i = 0; i < c.NC; i++)
            {
                double permMol = rig.Perm.Flows == null ? 0.0 : rig.Perm.Flows[i];
                double concMol = rig.Conc.Flows == null ? 0.0 : rig.Conc.Flows[i];
                Close(c.PermKmol[i], permMol / 1000.0, "FPERM[" + i + "] [kmol/s]");
                Close(c.ConcKmol[i], concMol / 1000.0, "FCONC[" + i + "] [kmol/s]");
            }

            // mass balance closes exactly: feed = permeate + concentrate
            for (int i = 0; i < c.NC; i++)
            {
                double permMol = rig.Perm.Flows == null ? 0.0 : rig.Perm.Flows[i];
                double concMol = rig.Conc.Flows == null ? 0.0 : rig.Conc.Flows[i];
                double feedMol = c.FeedKmol[i] * 1000.0;
                Assert.True(Math.Abs(feedMol - permMol - concMol) <= 1e-9 * Math.Max(1.0, feedMol),
                    "mass balance component " + i);
            }
        }

        // ------------------------------------------------------------------
        //  2. determinism: 20 consecutive runs identical to < 1e-8
        // ------------------------------------------------------------------
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TwentyConsecutiveRuns_AreIdentical(bool thermo11)
        {
            RefCase c = LoadCases().First(x => x.Name == "dilute-nacl-defaults");
            Rig rig = BuildRig(c, thermo11);

            double[][] runs = new double[20][];
            double[][] permFlows = new double[20][];
            for (int r = 0; r < 20; r++)
            {
                rig.Block.Calculate();
                runs[r] = rig.Block.GetResults().Select(x => x.Value).ToArray();
                permFlows[r] = (double[])rig.Perm.Flows.Clone();
            }

            for (int r = 1; r < 20; r++)
            {
                Assert.Equal(runs[0].Length, runs[r].Length);
                for (int i = 0; i < runs[0].Length; i++)
                    Assert.True(Math.Abs(runs[r][i] - runs[0][i]) < 1e-8,
                        "run " + r + " result " + i + " drifted: " + runs[0][i].ToString("R") +
                        " -> " + runs[r][i].ToString("R"));
                for (int i = 0; i < permFlows[0].Length; i++)
                    Assert.True(Math.Abs(permFlows[r][i] - permFlows[0][i]) < 1e-8,
                        "run " + r + " permeate flow " + i + " drifted");
            }
        }

        // ------------------------------------------------------------------
        //  3. results == outlet streams, exactly (not just 0.1%)
        // ------------------------------------------------------------------
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ReportedResults_MatchOutletStreams_Exactly(bool thermo11)
        {
            RefCase c = LoadCases().First(x => x.Name == "dilute-nacl-defaults");
            Rig rig = BuildRig(c, thermo11);
            rig.Block.Calculate();

            double[] permMol = rig.Perm.Flows;
            double[] concMol = rig.Conc.Flows;
            double[] feedMol = c.FeedKmol.Select(x => x * 1000.0).ToArray();
            int wi = c.KH2O - 1;

            // recompute every reported figure from the STREAM values alone
            double permKgS = 0, concKgS = 0, permSaltKgS = 0, concSaltKgS = 0;
            for (int i = 0; i < c.NC; i++)
            {
                permKgS += permMol[i] * c.MwGmol[i] / 1000.0;
                concKgS += concMol[i] * c.MwGmol[i] / 1000.0;
                if (i != wi)
                {
                    permSaltKgS += permMol[i] * c.MwGmol[i] / 1000.0;
                    concSaltKgS += concMol[i] * c.MwGmol[i] / 1000.0;
                }
            }

            double recovery = permMol[wi] / feedMol[wi] * 100.0;
            double permM3h = permKgS / 1000.0 * 3600.0;          // mock density = 1000
            double tdsPerm = permSaltKgS / permKgS * 1e6;
            double tdsConc = concSaltKgS / concKgS * 1e6;

            AssertExact(recovery, ResultOf(rig.Block, "Water recovery"), "recovery vs streams");
            AssertExact(permM3h, ResultOf(rig.Block, "Permeate flow"), "permeate flow vs streams");
            AssertExact(tdsPerm, ResultOf(rig.Block, "Permeate TDS"), "permeate TDS vs streams");
            AssertExact(tdsConc, ResultOf(rig.Block, "Concentrate TDS"), "concentrate TDS vs streams");

            // both outlets were actually flashed by the block
            Assert.True(rig.Perm.FlashCount > 0, "permeate was never flashed");
            Assert.True(rig.Conc.FlashCount > 0, "concentrate was never flashed");
        }

        private static void AssertExact(double expected, double actual, string what)
        {
            Assert.True(Math.Abs(actual - expected) <= 1e-12 * Math.Max(1.0, Math.Abs(expected)),
                what + ": " + expected.ToString("R") + " vs " + actual.ToString("R"));
        }

        // ------------------------------------------------------------------
        //  4. ports and parameters — what the Aspen wizard will render
        // ------------------------------------------------------------------
        [Fact]
        public void PortsAndDefaults_MatchOwnerSpec()
        {
            var block = new ReverseOsmosis();

            var portNames = new List<string>();
            foreach (UnitPort p in block.Ports) portNames.Add(p.ComponentName);
            Assert.Equal(new[] { "Feed", "Concentrate", "Permeate" }, portNames);

            var defaults = new Dictionary<string, double>();
            var outputs = new List<string>();
            foreach (CapeParameter p in block.Parameters)
            {
                if (UnitBase.IsInputParameter(p))
                    defaults[p.ComponentName] = Convert.ToDouble(((ICapeParameter)p).value,
                        CultureInfo.InvariantCulture);
                else
                    outputs.Add(p.ComponentName);
            }
            Assert.Equal(6, defaults.Count);
            Assert.Equal(40.0, defaults["Area"]);
            Assert.Equal(1.0, defaults["WaterPermA"]);
            Assert.Equal(99.0, defaults["SaltRejection"]);
            Assert.Equal(55.0, defaults["AppliedPressure"]);
            Assert.Equal(2.0, defaults["VantHoffI"]);
            Assert.Equal(80.0, defaults["PumpEff"]);

            // the owner's results table, rendered by the host from output parameters
            foreach (string required in new[] { "Recovery", "PermeateFlow", "PermeateTDS",
                                                "SaltRejObs", "PumpPower", "SEC" })
                Assert.Contains(required, outputs);
        }

        // ------------------------------------------------------------------
        //  4b. Aspen mass-unit convention: the V14 socket answers mass-basis
        //      quantities in g/s and g/m3, not kg (proven live 2026-07-14 —
        //      mixing our kg with its g/m3 made volumetric/power results 1000×
        //      small). The block must produce the SAME absolute results on such
        //      a host, because package mass ÷ package density cancels the unit.
        // ------------------------------------------------------------------
        [Theory]
        [MemberData(nameof(CaseNames))]
        public void AspenGramMassConvention_MatchesCoreReference(string name, bool thermo11)
        {
            if (!thermo11) return; // the gram quirk is an Aspen (Thermo 1.1) behaviour

            RefCase c = LoadCases().First(x => x.Name == name);
            var ids = new string[c.NC];
            for (int i = 0; i < c.NC; i++)
                ids[i] = (i == c.KH2O - 1) ? "WATER" : "SALT" + i;

            var feedFlows = c.FeedKmol.Select(x => x * 1000.0).ToArray(); // mol/s per spec
            var feed = new Mock11MaterialObject(ids, c.MwGmol, 1000.0,
                feedFlows, c.TK, 101325.0, massUnitScale: 1000.0);
            var perm = new Mock11MaterialObject(ids, c.MwGmol, 1000.0, massUnitScale: 1000.0);
            var conc = new Mock11MaterialObject(ids, c.MwGmol, 1000.0, massUnitScale: 1000.0);

            var block = new ReverseOsmosis();
            Set(block, "Area", c.Area);
            Set(block, "WaterPermA", c.PermA);
            Set(block, "SaltRejection", c.SaltRej);
            Set(block, "AppliedPressure", c.AppPres);
            Set(block, "VantHoffI", c.VantHoff);
            Set(block, "PumpEff", c.PumpEff);
            Connect(block, "Feed", feed);
            Connect(block, "Permeate", perm);
            Connect(block, "Concentrate", conc);
            block.Calculate();

            Close(c.Recov * 100.0, ResultOf(block, "Water recovery"), "RECOVERY [%]");
            Close(c.QPerm, ResultOf(block, "Permeate flow"), "QPERM [m3/h]");
            Close(c.PumpKW, ResultOf(block, "Pump power"), "PUMPKW [kW]");
            Close(c.Sec, ResultOf(block, "Specific energy (SEC)"), "SEC [kWh/m3]");
            Close(c.TdsPerm, ResultOf(block, "Permeate TDS"), "TDSPERM [ppm]");
            Close(c.RejObs, ResultOf(block, "Salt rejection (observed)"), "SREJOBS [%]");

            // outlets stay in the host's mol/s numbers
            for (int i = 0; i < c.NC; i++)
            {
                double permF = perm.Flows == null ? 0.0 : perm.Flows[i];
                double concF = conc.Flows == null ? 0.0 : conc.Flows[i];
                Close(c.PermKmol[i], permF / 1000.0, "FPERM[" + i + "] [kmol/s]");
                Close(c.ConcKmol[i], concF / 1000.0, "FCONC[" + i + "] [kmol/s]");
            }
        }

        // ------------------------------------------------------------------
        //  5. host-rendered results table (output parameters) == streams
        // ------------------------------------------------------------------
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void OutputParameters_MatchResults_AfterCalculate(bool thermo11)
        {
            RefCase c = LoadCases().First(x => x.Name == "dilute-nacl-defaults");
            Rig rig = BuildRig(c, thermo11);
            rig.Block.Calculate();

            var outp = new Dictionary<string, double>();
            foreach (CapeParameter p in rig.Block.Parameters)
                if (!UnitBase.IsInputParameter(p))
                    outp[p.ComponentName] = Convert.ToDouble(((ICapeParameter)p).value,
                        CultureInfo.InvariantCulture);

            AssertExact(ResultOf(rig.Block, "Water recovery"), outp["Recovery"], "Recovery");
            AssertExact(ResultOf(rig.Block, "Permeate flow"), outp["PermeateFlow"], "PermeateFlow");
            AssertExact(ResultOf(rig.Block, "Permeate TDS"), outp["PermeateTDS"], "PermeateTDS");
            AssertExact(ResultOf(rig.Block, "Salt rejection (observed)"), outp["SaltRejObs"], "SaltRejObs");
            AssertExact(ResultOf(rig.Block, "Pump power"), outp["PumpPower"], "PumpPower");
            AssertExact(ResultOf(rig.Block, "Specific energy (SEC)"), outp["SEC"], "SEC");
        }
    }
}
