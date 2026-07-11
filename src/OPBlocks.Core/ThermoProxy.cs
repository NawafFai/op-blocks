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
