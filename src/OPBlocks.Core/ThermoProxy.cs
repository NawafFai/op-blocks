using System;
using CapeOpen;

namespace OPBlocks.Core
{
    /// <summary>
    /// The single funnel for every thermodynamic property call (spec §5,
    /// Requirement R4). Blocks NEVER hardcode enthalpy, density, fugacity or VLE;
    /// they ask a <see cref="ThermoProxy"/>, which delegates to the host Material
    /// Object so results always match the user's chosen Property Package and
    /// component list.
    ///
    /// Aspen Plus V14 hands CAPE-OPEN units a Thermo 1.0 material object
    /// (<see cref="ICapeThermoMaterialObject"/>); we wrap it with the CO-LaN
    /// <see cref="MaterialObjectWrapper"/> for typed access. Every delegated call
    /// is guarded and, on failure, converted to an actionable
    /// <see cref="CapeComputationException"/> (never a raw .NET exception, R3).
    /// </summary>
    public sealed class ThermoProxy
    {
        // CAPE-OPEN Thermo 1.0 vocabulary.
        private const string Overall = "Overall";
        private const string Mixture = "Mixture";
        private const string MoleBasis = "Mole";

        private readonly MaterialObjectWrapper _mo;
        private readonly object _raw;
        private readonly string _portName;

        /// <param name="connectedObject">
        /// The object returned by a connected port's <c>connectedObject</c>.
        /// </param>
        /// <param name="portName">Port name, used only for error messages.</param>
        public ThermoProxy(object connectedObject, string portName)
        {
            if (connectedObject == null)
                throw new CapeInvalidArgumentException(
                    "Port '" + portName + "' is not connected to a material stream.", 1);
            _portName = portName;
            _raw = connectedObject;
            try
            {
                _mo = new MaterialObjectWrapper(connectedObject);
            }
            catch (Exception ex)
            {
                throw new CapeComputationException(
                    "Stream on port '" + portName + "' does not expose a CAPE-OPEN material object.", ex);
            }
        }

        /// <summary>Underlying wrapper, for advanced block-specific calls.</summary>
        public MaterialObjectWrapper Material { get { return _mo; } }

        /// <summary>Component IDs in the flowsheet's order (matches flow arrays).</summary>
        public string[] ComponentIds
        {
            get { return Guard("component list", () => _mo.ComponentIds); }
        }

        public int ComponentCount
        {
            get { return Guard("component count", () => _mo.GetNumComponents()); }
        }

        /// <summary>Overall temperature [K].</summary>
        public double Temperature
        {
            get { return Scalar("temperature", null); }
        }

        /// <summary>Overall pressure [Pa].</summary>
        public double Pressure
        {
            get { return Scalar("pressure", null); }
        }

        /// <summary>Overall molar enthalpy [J/mol] from the host property package.</summary>
        public double MolarEnthalpy
        {
            get { return Scalar("enthalpy", MoleBasis); }
        }

        /// <summary>Per-component overall mole flows [mol/s], in <see cref="ComponentIds"/> order.</summary>
        public double[] GetOverallMoleFlows()
        {
            return Guard("component mole flows", () =>
                _mo.GetProp("flow", Overall, _mo.ComponentIds, Mixture, MoleBasis));
        }

        public double GetTotalMoleFlow()
        {
            double[] f = GetOverallMoleFlows();
            double s = 0.0;
            for (int i = 0; i < f.Length; i++) s += f[i];
            return s;
        }

        /// <summary>
        /// Sets an outlet stream in the strict order Aspen requires (spec §5 rule 2):
        /// composition/flow first, then pressure, then enthalpy, then a single PH
        /// flash. Violating this order silently produces zero-flow outlets.
        /// </summary>
        public void SetOutletPH(double[] componentMoleFlows, double pressurePa, double molarEnthalpy)
        {
            Guard("set outlet stream", () =>
            {
                _mo.SetProp("flow", Overall, _mo.ComponentIds, Mixture, MoleBasis, componentMoleFlows);
                _mo.SetProp("pressure", Overall, null, Mixture, null, new[] { pressurePa });
                _mo.SetProp("enthalpy", Overall, null, Mixture, MoleBasis, new[] { molarEnthalpy });
                return 0;
            });
            Guard("PH flash", () => { _mo.CalcEquilibrium("PH", null); return 0; });
        }

        /// <summary>Sets an outlet at specified T,P and flashes (TP flash).</summary>
        public void SetOutletTP(double[] componentMoleFlows, double temperatureK, double pressurePa)
        {
            Guard("set outlet stream", () =>
            {
                _mo.SetProp("flow", Overall, _mo.ComponentIds, Mixture, MoleBasis, componentMoleFlows);
                _mo.SetProp("temperature", Overall, null, Mixture, null, new[] { temperatureK });
                _mo.SetProp("pressure", Overall, null, Mixture, null, new[] { pressurePa });
                return 0;
            });
            Guard("TP flash", () => { _mo.CalcEquilibrium("TP", null); return 0; });
        }

        /// <summary>
        /// Liquid-phase activity coefficient of one component from the host
        /// property package, or false when the package cannot supply it (ideal
        /// packages, missing liquid phase, host quirks). Never throws — callers
        /// fall back to an ideal estimate and say so in the report (§5 rule 5).
        /// </summary>
        public bool TryGetLiquidActivityCoefficient(int componentIndex, out double gamma)
        {
            gamma = 1.0;
            string[] ids;
            try { ids = _mo.ComponentIds; } catch { return false; }
            if (componentIndex < 0 || ids == null || componentIndex >= ids.Length) return false;

            foreach (string phase in new[] { "liquid", "Liquid", "Liquid1", "Overall" })
            {
                try
                {
                    try { _mo.CalcProp(new[] { "activityCoefficient" }, new[] { phase }, Mixture); }
                    catch { /* some hosts compute on GetProp directly */ }
                    double[] r = _mo.GetProp("activityCoefficient", phase, ids, Mixture, null);
                    if (r != null && r.Length > componentIndex)
                    {
                        double g = r[componentIndex];
                        if (!double.IsNaN(g) && !double.IsInfinity(g) && g > 0)
                        {
                            gamma = g;
                            return true;
                        }
                    }
                }
                catch { /* try the next phase label */ }
            }
            return false;
        }

        /// <summary>
        /// Per-component molecular weights [g/mol] from the host property package
        /// (CAPE-OPEN component constant "molecularWeight"), or false when the
        /// package cannot supply them. Values reported in kg/mol by a host are
        /// normalised to g/mol (no real compound is lighter than H2 ≈ 2 g/mol).
        /// Never throws — callers fall back and say so in the report (§5 rule 5).
        /// </summary>
        public bool TryGetMolecularWeightsGmol(out double[] mw)
        {
            mw = null;
            try
            {
                string[] ids = _mo.ComponentIds;
                if (ids == null || ids.Length == 0) return false;

                var thermo10 = _raw as ICapeThermoMaterialObject;
                if (thermo10 == null) return false;
                object res = thermo10.GetComponentConstant(new object[] { "molecularWeight" }, ids);
                double[] vals = ToDoubleArray(res);
                if (vals == null || vals.Length < ids.Length) return false;

                var outv = new double[ids.Length];
                double max = 0;
                for (int i = 0; i < ids.Length; i++)
                {
                    double v = vals[i];
                    if (double.IsNaN(v) || double.IsInfinity(v) || v <= 0) return false;
                    outv[i] = v;
                    if (v > max) max = v;
                }
                if (max < 1.5) // host answered in kg/mol
                    for (int i = 0; i < outv.Length; i++) outv[i] *= 1000.0;
                mw = outv;
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// Mass density [kg/m3] of the stream at its current state from the host
        /// property package, or false when the package cannot supply it. Tries the
        /// liquid phase first (our outlets are liquid brines), then Overall.
        /// Never throws — callers fall back to 1000 kg/m3 and say so in the report.
        /// </summary>
        public bool TryGetMassDensityKgM3(out double rho)
        {
            rho = 0.0;
            foreach (string phase in new[] { "liquid", "Liquid", "Liquid1", "Overall" })
            {
                try
                {
                    try { _mo.CalcProp(new[] { "density" }, new[] { phase }, Mixture); }
                    catch { /* some hosts compute on GetProp directly */ }
                    double[] r = _mo.GetProp("density", phase, null, Mixture, "Mass");
                    if (r != null && r.Length > 0)
                    {
                        double d = r[0];
                        if (!double.IsNaN(d) && !double.IsInfinity(d) && d > 0.1)
                        {
                            rho = d;
                            return true;
                        }
                    }
                }
                catch { /* try the next phase label */ }
            }
            return false;
        }

        private static double[] ToDoubleArray(object o)
        {
            if (o == null) return null;
            if (o is double[] d) return d;
            if (o is object[] arr)
            {
                var r = new double[arr.Length];
                for (int i = 0; i < arr.Length; i++)
                {
                    try { r[i] = Convert.ToDouble(arr[i], System.Globalization.CultureInfo.InvariantCulture); }
                    catch { return null; }
                }
                return r;
            }
            if (o is Array a)
            {
                var r = new double[a.Length];
                int k = 0;
                foreach (object item in a)
                {
                    try { r[k++] = Convert.ToDouble(item, System.Globalization.CultureInfo.InvariantCulture); }
                    catch { return null; }
                }
                return r;
            }
            return null;
        }

        private double Scalar(string property, string basis)
        {
            double[] r = Guard(property, () => _mo.GetProp(property, Overall, null, Mixture, basis));
            if (r == null || r.Length == 0)
                throw new CapeComputationException(
                    "Property package returned no '" + property + "' for stream on port '" + _portName + "'.");
            return r[0];
        }

        private T Guard<T>(string what, Func<T> action)
        {
            try
            {
                return action();
            }
            catch (CapeUserException)
            {
                throw; // already an actionable CAPE-OPEN error
            }
            catch (Exception ex)
            {
                throw new CapeComputationException(
                    "Property package failed to supply " + what + " for stream on port '" +
                    _portName + "'. Check the selected Property Package and component list.", ex);
            }
        }
    }
}
