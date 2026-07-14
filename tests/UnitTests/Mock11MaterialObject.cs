using System;
using CapeOpen;

namespace OPBlocks.UnitTests
{
    /// <summary>
    /// Minimal in-process CAPE-OPEN Thermo 1.1 material object — the generation
    /// Aspen Plus V14 hands our units (proven live 2026-07-14). Implements the
    /// material + compounds + equilibrium + phases interfaces on one object, as
    /// the 1.1 spec requires of a PME material. Supplies mole fraction/totalFlow
    /// composition, T, P, mass density and molecular weights; deliberately NO
    /// activityCoefficient, so blocks exercise the same van 't Hoff fallback
    /// branch the reference dataset is generated with.
    /// </summary>
    internal sealed class Mock11MaterialObject :
        ICapeThermoMaterial, ICapeThermoCompounds, ICapeThermoEquilibriumRoutine, ICapeThermoPhases, IMockMaterial
    {
        private readonly string[] _ids;
        private readonly double[] _mwGmol;
        private readonly double _densityKgM3;

        private double[] _fractions;     // mole fractions
        private double _totalFlow;       // mol/s
        public double TemperatureK;
        public double PressurePa;
        public int FlashCount { get; set; }

        /// <summary>
        /// Scale of this host's MASS-basis answers relative to kg: 1.0 emulates a
        /// spec-SI host (kg/s, kg/m3); 1000.0 emulates Aspen Plus V14's socket,
        /// which answers mass flow in g/s and mass density in g/m3 (proven live
        /// 2026-07-14). Mole flows are mol/s either way, per the spec.
        /// </summary>
        private readonly double _massUnitScale;

        public Mock11MaterialObject(string[] ids, double[] mwGmol, double densityKgM3 = 1000.0,
                                    double[] flows = null, double temperatureK = 298.15,
                                    double pressurePa = 101325.0, double massUnitScale = 1.0)
        {
            _ids = ids;
            _mwGmol = mwGmol;
            _densityKgM3 = densityKgM3;
            _massUnitScale = massUnitScale;
            TemperatureK = temperatureK;
            PressurePa = pressurePa;
            if (flows != null)
            {
                double total = 0;
                foreach (double f in flows) total += f;
                _totalFlow = total;
                _fractions = new double[flows.Length];
                if (total > 0)
                    for (int i = 0; i < flows.Length; i++) _fractions[i] = flows[i] / total;
            }
        }

        /// <summary>Per-component mole flows [mol/s] as currently stored, or null when never set.</summary>
        public double[] Flows
        {
            get
            {
                if (_fractions == null) return null;
                var f = new double[_fractions.Length];
                for (int i = 0; i < f.Length; i++) f[i] = _fractions[i] * _totalFlow;
                return f;
            }
        }

        // ---- ICapeThermoMaterial ----
        public void ClearAllProps() { _fractions = null; _totalFlow = 0; }
        public void CopyFromMaterial(ref object source) { throw new NotSupportedException("mock"); }
        public object CreateMaterial() { throw new NotSupportedException("mock"); }

        public void GetOverallProp(string property, string basis, ref object results)
        {
            switch ((property ?? "").ToLowerInvariant())
            {
                case "temperature": results = new[] { TemperatureK }; return;
                case "pressure": results = new[] { PressurePa }; return;
                case "fraction":
                    if (_fractions == null) throw new InvalidOperationException("mock: no composition set");
                    results = (double[])_fractions.Clone(); return;
                case "totalflow":
                    if (_fractions == null) throw new InvalidOperationException("mock: no composition set");
                    if (string.Equals(basis, "mass", StringComparison.OrdinalIgnoreCase))
                    {
                        double kgS = 0;
                        for (int i = 0; i < _fractions.Length; i++)
                            kgS += _fractions[i] * _totalFlow * _mwGmol[i] / 1000.0;
                        results = new[] { kgS * _massUnitScale };   // host mass unit
                        return;
                    }
                    results = new[] { _totalFlow }; return;
                default:
                    throw new NotSupportedException("mock: overall '" + property + "' not supplied");
            }
        }

        public void GetOverallTPFraction(ref double temperature, ref double pressure, ref object composition)
        {
            temperature = TemperatureK; pressure = PressurePa;
            composition = _fractions == null ? null : (double[])_fractions.Clone();
        }

        public void GetPresentPhases(ref object phaseLabels, ref object phaseStatus)
        {
            phaseLabels = new[] { "Liquid" };
            phaseStatus = new[] { CapePhaseStatus.CAPE_ATEQUILIBRIUM };
        }

        public void GetSinglePhaseProp(string property, string phaseLabel, string basis, ref object results)
        {
            switch ((property ?? "").ToLowerInvariant())
            {
                case "density":
                    if (string.Equals(basis, "mass", StringComparison.OrdinalIgnoreCase))
                    {
                        results = new[] { _densityKgM3 * _massUnitScale };   // host mass unit
                        return;
                    }
                    throw new NotSupportedException("mock: only mass density");
                default:
                    // activityCoefficient lands here on purpose (fallback branch)
                    throw new NotSupportedException("mock: single-phase '" + property + "' not supplied");
            }
        }

        public void GetTPFraction(string phaseLabel, ref double temperature, ref double pressure, ref object composition)
        {
            GetOverallTPFraction(ref temperature, ref pressure, ref composition);
        }

        public void GetTwoPhaseProp(string property, object phaseLabels, string basis, ref object results)
        {
            throw new NotSupportedException("mock");
        }

        public void SetOverallProp(string property, string basis, object values)
        {
            double[] v = ToDoubles(values);
            switch ((property ?? "").ToLowerInvariant())
            {
                case "fraction": _fractions = v; return;
                case "totalflow": _totalFlow = v[0]; return;
                case "temperature": TemperatureK = v[0]; return;
                case "pressure": PressurePa = v[0]; return;
                default: throw new NotSupportedException("mock: cannot set overall '" + property + "'");
            }
        }

        public void SetPresentPhases(object phaseLabels, object phaseStatus) { }
        public void SetSinglePhaseProp(string property, string phaseLabel, string basis, object values)
        {
            throw new NotSupportedException("mock");
        }
        public void SetTwoPhaseProp(string property, object phaseLabels, string basis, object values)
        {
            throw new NotSupportedException("mock");
        }

        // ---- ICapeThermoCompounds ----
        public object GetCompoundConstant(object props, object compIds)
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
            throw new NotSupportedException("mock: only molecularWeight");
        }

        public void GetCompoundList(ref object compIds, ref object formulae, ref object names,
                                    ref object boilTemps, ref object molwts, ref object casnos)
        {
            compIds = (string[])_ids.Clone();
            formulae = (string[])_ids.Clone();
            names = (string[])_ids.Clone();
            boilTemps = new double[_ids.Length];
            molwts = (double[])_mwGmol.Clone();
            casnos = new string[_ids.Length];
        }

        public object GetConstPropList() { return new[] { "molecularWeight" }; }
        public int GetNumCompounds() { return _ids.Length; }
        public void GetPDependentProperty(object props, double pressure, object compIds, ref object propVals)
        { throw new NotSupportedException("mock"); }
        public object GetPDependentPropList() { return new string[0]; }
        public void GetTDependentProperty(object props, double temperature, object compIds, ref object propVals)
        { throw new NotSupportedException("mock"); }
        public object GetTDependentPropList() { return new string[0]; }

        // ---- ICapeThermoEquilibriumRoutine ----
        public void CalcEquilibrium(object specification1, object specification2, string solutionType)
        {
            FlashCount++;
        }
        public bool CheckEquilibriumSpec(object specification1, object specification2, string solutionType)
        {
            return true;
        }

        // ---- ICapeThermoPhases ----
        public int GetNumPhases() { return 1; }
        public object GetPhaseInfo(string phaseLabel, string phaseAttribute) { return null; }
        public void GetPhaseList(ref object phaseLabels, ref object stateOfAggregation, ref object keyCompoundId)
        {
            phaseLabels = new[] { "Liquid" };
            stateOfAggregation = new[] { "Liquid" };
            keyCompoundId = new string[] { null };
        }

        private static double[] ToDoubles(object o)
        {
            if (o is double[] d) return (double[])d.Clone();
            var list = new System.Collections.Generic.List<double>();
            foreach (object item in (System.Collections.IEnumerable)o)
                list.Add(Convert.ToDouble(item, System.Globalization.CultureInfo.InvariantCulture));
            return list.ToArray();
        }
    }
}
