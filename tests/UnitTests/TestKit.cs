using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CapeOpen;
using OPBlocks.Core;
using Xunit;

namespace OPBlocks.UnitTests
{
    /// <summary>
    /// Shared rig helpers for the factory-pattern validation suites (Families A-E).
    /// Mirrors the private helpers inside the OP-RO/NF/ED/EVAPPOND suites so every
    /// new block suite stays lean and identical in shape.
    /// </summary>
    internal static class TestKit
    {
        public const double RelTol = 1e-3;    // 0.1% structural gate
        public const double AbsFloor = 1e-12;

        public static object NewMock(bool t11, string[] ids, double[] mw, double[] flows = null,
                                     double tK = 298.15, double pPa = 101325.0, double rho = 1000.0)
        {
            if (t11) return new Mock11MaterialObject(ids, mw, rho, flows, tK, pPa);
            return new MockMaterialObject(ids, mw, rho, flows, tK, pPa);
        }

        public static double PressureOf(object mock)
        {
            var m0 = mock as MockMaterialObject;
            if (m0 != null) return m0.PressurePa;
            var m1 = mock as Mock11MaterialObject;
            if (m1 != null) return m1.PressurePa;
            throw new InvalidOperationException("not a mock material");
        }

        public static double TemperatureOf(object mock)
        {
            var m0 = mock as MockMaterialObject;
            if (m0 != null) return m0.TemperatureK;
            var m1 = mock as Mock11MaterialObject;
            if (m1 != null) return m1.TemperatureK;
            throw new InvalidOperationException("not a mock material");
        }

        public static void Set(CapeUnitBase block, string param, double value)
        {
            foreach (CapeParameter p in block.Parameters)
                if (string.Equals(p.ComponentName, param, StringComparison.OrdinalIgnoreCase))
                { ((ICapeParameter)p).value = value; return; }
            throw new InvalidOperationException("parameter not found: " + param);
        }

        public static void Connect(CapeUnitBase block, string portName, object material)
        {
            foreach (UnitPort p in block.Ports)
                if (string.Equals(p.ComponentName, portName, StringComparison.OrdinalIgnoreCase))
                { p.Connect(material); return; }
            throw new InvalidOperationException("port not found: " + portName);
        }

        public static double ResultOf(UnitBase block, string label)
        {
            UnitBase.ResultEntry row = block.GetResults().FirstOrDefault(r => r.Label == label);
            Assert.True(row != null, "missing result row: " + label);
            return row.Value;
        }

        public static bool HasResult(UnitBase block, string label)
        {
            return block.GetResults().Any(r => r.Label == label);
        }

        public static double OutParam(UnitBase block, string name)
        {
            foreach (CapeParameter p in block.Parameters)
                if (!UnitBase.IsInputParameter(p) && string.Equals(p.ComponentName, name, StringComparison.OrdinalIgnoreCase))
                    return Convert.ToDouble(((ICapeParameter)p).value, CultureInfo.InvariantCulture);
            throw new InvalidOperationException("output parameter not found: " + name);
        }

        public static void Close(double expected, double actual, string what)
        {
            double tol = Math.Max(AbsFloor, Math.Abs(expected) * RelTol);
            Assert.True(Math.Abs(actual - expected) <= tol,
                what + ": expected " + expected.ToString("R") + ", got " + actual.ToString("R"));
        }

        public static void AssertExact(double expected, double actual, string what)
        {
            Assert.True(Math.Abs(actual - expected) <= 1e-10 * Math.Max(1.0, Math.Abs(expected)),
                what + ": " + expected.ToString("R") + " vs " + actual.ToString("R"));
        }

        /// <summary>GOLDEN RULE: every parameter must be a RealParameter.</summary>
        public static void AssertRealParametersOnly(CapeUnitBase block)
        {
            foreach (CapeParameter p in block.Parameters)
                Assert.True(p is RealParameter, "parameter '" + p.ComponentName +
                    "' must be a RealParameter (was " + p.GetType().Name + ")");
        }

        public static void AssertPortNames(CapeUnitBase block, params string[] expected)
        {
            var names = block.Ports.Cast<UnitPort>().Select(p => p.ComponentName).ToList();
            Assert.Equal(expected, names);
        }

        public static void AssertWarningContains(UnitBase block, string fragment)
        {
            Assert.Contains(block.GetReportWarnings(),
                w => w.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public static void AssertReportContains(UnitBase block, params string[] fragments)
        {
            string report = "";
            block.ProduceReport(ref report);
            foreach (string f in fragments) Assert.Contains(f, report);
        }

        public static double[] FlowsOf(object mock)
        {
            return ((IMockMaterial)mock).Flows;
        }

        public static int FlashCountOf(object mock)
        {
            return ((IMockMaterial)mock).FlashCount;
        }

        /// <summary>Exact per-component conservation across any set of in/out streams.</summary>
        public static void AssertMassBalance(double[][] ins, double[][] outs, int n, double relTol = 1e-9)
        {
            for (int i = 0; i < n; i++)
            {
                double fin = 0, fout = 0;
                foreach (double[] s in ins) if (s != null) fin += s[i];
                foreach (double[] s in outs) if (s != null) fout += s[i];
                Assert.True(Math.Abs(fin - fout) <= relTol * Math.Max(1.0, Math.Abs(fin)),
                    "mass balance component " + i + ": in " + fin.ToString("R") + " vs out " + fout.ToString("R"));
            }
        }

        /// <summary>Runs the block 20x and asserts bit-stability of results and a watched stream.</summary>
        public static void AssertTwentyRunsIdentical(UnitBase block, Func<double[]> watchedFlows)
        {
            double[][] runs = new double[20][];
            double[][] outs = new double[20][];
            for (int r = 0; r < 20; r++)
            {
                block.Calculate();
                runs[r] = block.GetResults().Select(x => x.Value).ToArray();
                outs[r] = (double[])watchedFlows().Clone();
            }
            for (int r = 1; r < 20; r++)
            {
                for (int i = 0; i < runs[0].Length; i++)
                    Assert.True(Math.Abs(runs[r][i] - runs[0][i]) < 1e-8, "result " + i + " drifted at run " + r);
                for (int i = 0; i < outs[0].Length; i++)
                    Assert.True(Math.Abs(outs[r][i] - outs[0][i]) < 1e-8, "stream flow " + i + " drifted at run " + r);
            }
        }
    }
}
