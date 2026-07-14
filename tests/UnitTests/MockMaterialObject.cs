using System;
using System.Collections.Generic;
using CapeOpen;

namespace OPBlocks.UnitTests
{
    /// <summary>
    /// Minimal in-process CAPE-OPEN Thermo 1.0 material object, good enough for
    /// <see cref="OPBlocks.Core.ThermoProxy"/> (via the CO-LaN
    /// MaterialObjectWrapper) to drive a real block's Calculate() end-to-end:
    /// component list, overall mole flows, T, P, mass density, and molecular
    /// weights. It deliberately supplies NO activityCoefficient so blocks
    /// exercise the same van 't Hoff fallback branch the package-independent
    /// reference dataset (tests/OproValidation/cases.txt) is generated with.
    /// </summary>
    internal sealed class MockMaterialObject : ICapeThermoMaterialObject
    {
        private readonly string[] _ids;
        private readonly double[] _mwGmol;
        private readonly double _densityKgM3;

        public double[] Flows;          // mol/s, null until set on an outlet
        public double TemperatureK;
        public double PressurePa;
        public int FlashCount;          // CalcEquilibrium calls observed

        public MockMaterialObject(string[] ids, double[] mwGmol, double densityKgM3 = 1000.0,
                                  double[] flows = null, double temperatureK = 298.15,
                                  double pressurePa = 101325.0)
        {
            _ids = ids;
            _mwGmol = mwGmol;
            _densityKgM3 = densityKgM3;
            Flows = flows;
            TemperatureK = temperatureK;
            PressurePa = pressurePa;
        }

        public object ComponentIds { get { return (string[])_ids.Clone(); } }
        public object PhaseIds { get { return new[] { "liquid" }; } }
        public int GetNumComponents() { return _ids.Length; }

        public object GetProp(string property, string phase, object compIds, string calcType, string basis)
        {
            switch ((property ?? "").ToLowerInvariant())
            {
                case "flow":
                    if (Flows == null) throw new InvalidOperationException("mock: no flows set on this stream yet");
                    return (double[])Flows.Clone();
                case "temperature":
                    return new[] { TemperatureK };
                case "pressure":
                    return new[] { PressurePa };
                case "density":
                    if (string.Equals(basis, "Mass", StringComparison.OrdinalIgnoreCase))
                        return new[] { _densityKgM3 };
                    throw new NotSupportedException("mock: only mass density is supplied");
                default:
                    // activityCoefficient lands here on purpose: the block must fall
                    // back to the van 't Hoff branch, like a package that cannot help.
                    throw new NotSupportedException("mock: property '" + property + "' not supplied");
            }
        }

        public void SetProp(string property, string phase, object compIds, string calcType, string basis, object values)
        {
            var v = ToDoubles(values);
            switch ((property ?? "").ToLowerInvariant())
            {
                case "flow": Flows = v; break;
                case "temperature": TemperatureK = v[0]; break;
                case "pressure": PressurePa = v[0]; break;
                default: throw new NotSupportedException("mock: cannot set property '" + property + "'");
            }
        }

        public void CalcEquilibrium(string flashType, object props) { FlashCount++; }

        public void CalcProp(object props, object phases, string calcType)
        {
            // no-op: GetProp answers directly for everything this mock supplies
        }

        public object GetComponentConstant(object props, object compIds)
        {
            foreach (object p in (System.Collections.IEnumerable)props)
            {
                if (string.Equals(Convert.ToString(p), "molecularWeight", StringComparison.OrdinalIgnoreCase))
                {
                    var result = new object[_ids.Length];
                    for (int i = 0; i < _ids.Length; i++) result[i] = _mwGmol[i];
                    return result;
                }
            }
            throw new NotSupportedException("mock: only molecularWeight is supplied");
        }

        private static double[] ToDoubles(object o)
        {
            if (o is double[] d) return (double[])d.Clone();
            var list = new List<double>();
            foreach (object item in (System.Collections.IEnumerable)o)
                list.Add(Convert.ToDouble(item, System.Globalization.CultureInfo.InvariantCulture));
            return list.ToArray();
        }

        // --- unused ICapeThermoMaterialObject surface ---
        public object GetUniversalConstant(object props) { throw new NotSupportedException("mock"); }
        public void SetIndependentVar(object indVars, object values) { throw new NotSupportedException("mock"); }
        public object GetIndependentVar(object indVars) { throw new NotSupportedException("mock"); }
        public object PropCheck(object props) { throw new NotSupportedException("mock"); }
        public object AvailableProps() { throw new NotSupportedException("mock"); }
        public void RemoveResults(object props) { }
        public object CreateMaterialObject() { throw new NotSupportedException("mock"); }
        public object Duplicate() { throw new NotSupportedException("mock"); }
        public object ValidityCheck(object props) { throw new NotSupportedException("mock"); }
        public object GetPropList() { throw new NotSupportedException("mock"); }
    }
}
