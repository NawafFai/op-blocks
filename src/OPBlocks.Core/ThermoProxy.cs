using System;
using CapeOpen;

namespace OPBlocks.Core
{
    /// <summary>
    /// Which single phase an outlet stream should be flashed into. Our blocks
    /// produce single-phase outlets, so we tell the host up front instead of
    /// letting it attempt a multi-phase (vapour / free-water) split over a brine.
    /// </summary>
    public enum PhaseHint
    {
        /// <summary>A subcooled liquid brine / product water (the common case).</summary>
        Liquid,
        /// <summary>A pure-water vapour (e.g. an evaporation block's vapour port).</summary>
        Vapor
    }

    /// <summary>
    /// The single funnel for every thermodynamic property call (spec §5,
    /// Requirement R4). Blocks NEVER hardcode enthalpy, density, fugacity or VLE;
    /// they ask a <see cref="ThermoProxy"/>, which delegates to the host Material
    /// Object so results always match the user's chosen Property Package and
    /// component list.
    ///
    /// Two material-object generations are supported, detected per stream:
    ///  • CAPE-OPEN Thermo 1.1 (<see cref="ICapeThermoMaterial"/> + Compounds/
    ///    EquilibriumRoutine/PropertyRoutine on the same object) — what Aspen
    ///    Plus V14 hands a unit registered with the Thermo-1.1 category
    ///    (proven live 2026-07-14: the 1.0-only path failed Validate with
    ///    "does not expose a CAPE-OPEN material object").
    ///  • CAPE-OPEN Thermo 1.0 (<see cref="ICapeThermoMaterialObject"/> via the
    ///    CO-LaN <see cref="MaterialObjectWrapper"/>) — DWSIM and older hosts.
    ///
    /// Every delegated call is guarded and, on failure, converted to an
    /// actionable <see cref="CapeComputationException"/> (never a raw .NET
    /// exception, R3).
    /// </summary>
    public sealed class ThermoProxy
    {
        // CAPE-OPEN Thermo 1.0 vocabulary.
        private const string Overall = "Overall";
        private const string Mixture = "Mixture";
        private const string MoleBasis = "Mole";

        // Thermo 1.0 backend (null when the stream is Thermo 1.1)
        private readonly MaterialObjectWrapper _mo;

        // Thermo 1.1 backend (null when the stream is Thermo 1.0)
        private readonly ICapeThermoMaterial _mat11;
        private readonly ICapeThermoCompounds _compounds11;
        private readonly ICapeThermoEquilibriumRoutine _equil11;
        private readonly ICapeThermoPropertyRoutine _props11;
        private readonly ICapeThermoPhases _phases11;

        private readonly object _raw;
        private readonly string _portName;
        private string[] _idCache;

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

            // Thermo 1.0 FIRST. DWSIM material streams implement BOTH generations,
            // and the native DWSIM adapter's outlet bridge is built on the 1.0
            // SetProp protocol — preferring 1.1 there silently produced ZERO-flow
            // outlets (found 2026-07-19 by the DwsimHostTest e2e: the block computed
            // correctly but the host streams stayed empty). Aspen Plus V14 materials
            // are 1.1-ONLY (the 1.0 wrapper refuses them), so Aspen still lands on
            // the 1.1 branch below. Order = proven path per host, no guessing.
            try
            {
                _mo = new MaterialObjectWrapper(connectedObject);
                return;
            }
            catch { /* not a Thermo 1.0 material — try 1.1 */ }

            // Thermo 1.1 — what Aspen Plus V14 supplies.
            _mat11 = connectedObject as ICapeThermoMaterial;
            if (_mat11 != null)
            {
                _compounds11 = connectedObject as ICapeThermoCompounds;
                _equil11 = connectedObject as ICapeThermoEquilibriumRoutine;
                _props11 = connectedObject as ICapeThermoPropertyRoutine;
                _phases11 = connectedObject as ICapeThermoPhases;
                if (_compounds11 == null)
                    throw new CapeComputationException(
                        "Stream on port '" + portName + "' is a Thermo 1.1 material without " +
                        "ICapeThermoCompounds — cannot read its component list.");
                return;
            }

            throw new CapeComputationException(
                "Stream on port '" + portName + "' does not expose a CAPE-OPEN material object " +
                "(neither Thermo 1.0 ICapeThermoMaterialObject nor Thermo 1.1 ICapeThermoMaterial).");
        }

        /// <summary>True when this stream talks CAPE-OPEN Thermo 1.1.</summary>
        public bool IsThermo11 { get { return _mat11 != null; } }

        /// <summary>Underlying 1.0 wrapper, for advanced block-specific calls (null on 1.1 streams).</summary>
        public MaterialObjectWrapper Material { get { return _mo; } }

        /// <summary>Component IDs in the flowsheet's order (matches flow arrays).</summary>
        public string[] ComponentIds
        {
            get
            {
                if (_idCache != null) return _idCache;
                _idCache = Guard("component list", () =>
                {
                    if (_mat11 != null)
                    {
                        object ids = null, formulae = null, names = null, boilT = null, molwt = null, casno = null;
                        _compounds11.GetCompoundList(ref ids, ref formulae, ref names, ref boilT, ref molwt, ref casno);
                        return ToStringArray(ids);
                    }
                    return _mo.ComponentIds;
                });
                return _idCache;
            }
        }

        public int ComponentCount
        {
            get
            {
                return Guard("component count", () =>
                    _mat11 != null ? _compounds11.GetNumCompounds() : _mo.GetNumComponents());
            }
        }

        /// <summary>Overall temperature [K].</summary>
        public double Temperature
        {
            get
            {
                if (_mat11 != null)
                    return Guard("temperature", () => Overall11("temperature", null)[0]);
                return Scalar("temperature", null);
            }
        }

        /// <summary>Overall pressure [Pa].</summary>
        public double Pressure
        {
            get
            {
                if (_mat11 != null)
                    return Guard("pressure", () => Overall11("pressure", null)[0]);
                return Scalar("pressure", null);
            }
        }

        /// <summary>Overall molar enthalpy [J/mol] from the host property package.</summary>
        public double MolarEnthalpy
        {
            get
            {
                if (_mat11 != null)
                    return Guard("enthalpy", () => Overall11("enthalpy", "mole")[0]);
                return Scalar("enthalpy", MoleBasis);
            }
        }

        /// <summary>Per-component overall mole flows [mol/s], in <see cref="ComponentIds"/> order.</summary>
        public double[] GetOverallMoleFlows()
        {
            return Guard("component mole flows", () =>
            {
                if (_mat11 != null)
                {
                    double total = Overall11("totalFlow", "mole")[0];
                    double[] x = Overall11("fraction", "mole");
                    var f = new double[x.Length];
                    for (int i = 0; i < x.Length; i++) f[i] = x[i] * total;
                    return f;
                }
                return _mo.GetProp("flow", Overall, _mo.ComponentIds, Mixture, MoleBasis);
            });
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
            if (_mat11 != null)
            {
                Guard("set outlet stream", () =>
                {
                    Set11Composition(componentMoleFlows);
                    _mat11.SetOverallProp("pressure", null, new[] { pressurePa });
                    _mat11.SetOverallProp("enthalpy", "mole", new[] { molarEnthalpy });
                    return 0;
                });
                Flash11("enthalpy");
                return;
            }
            Guard("set outlet stream", () =>
            {
                _mo.SetProp("flow", Overall, _mo.ComponentIds, Mixture, MoleBasis, componentMoleFlows);
                _mo.SetProp("pressure", Overall, null, Mixture, null, new[] { pressurePa });
                _mo.SetProp("enthalpy", Overall, null, Mixture, MoleBasis, new[] { molarEnthalpy });
                return 0;
            });
            Guard("PH flash", () => { _mo.CalcEquilibrium("PH", null); return 0; });
        }

        /// <summary>
        /// Sets an outlet at specified T,P and flashes (TP flash). Our outlet
        /// streams are single-phase (a subcooled liquid brine, or the pure-water
        /// vapour/condensate of an evaporation block), so the flash is restricted to
        /// the known phase — see <see cref="Flash11"/>. This keeps the host from
        /// spinning up a vapour / free-water phase over the brine, which on a
        /// pure-water steam-table method (Aspen STEAMNBS / STEAM-TA) raises the
        /// "steam tables used when components other than water present / water
        /// absent" warnings and flags the block with a physical-property error.
        /// </summary>
        public void SetOutletTP(double[] componentMoleFlows, double temperatureK, double pressurePa,
                                PhaseHint phase = PhaseHint.Liquid)
        {
            if (_mat11 != null)
            {
                Guard("set outlet stream", () =>
                {
                    Set11Composition(componentMoleFlows);
                    _mat11.SetOverallProp("temperature", null, new[] { temperatureK });
                    _mat11.SetOverallProp("pressure", null, new[] { pressurePa });
                    return 0;
                });
                Flash11("TP", phase);
                return;
            }
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
            try { ids = ComponentIds; } catch { return false; }
            if (componentIndex < 0 || ids == null || componentIndex >= ids.Length) return false;

            if (_mat11 != null)
            {
                if (_props11 == null) return false;
                foreach (string phase in LiquidPhaseLabels11())
                {
                    try
                    {
                        try { _props11.CalcSinglePhaseProp(new[] { "activityCoefficient" }, phase); }
                        catch { /* some hosts compute on Get directly */ }
                        double[] r = null;
                        object o = null;
                        _mat11.GetSinglePhaseProp("activityCoefficient", phase, null, ref o);
                        r = ToDoubleArray(o);
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
                string[] ids = ComponentIds;
                if (ids == null || ids.Length == 0) return false;

                double[] vals = null;
                if (_mat11 != null)
                {
                    // GetCompoundList hands molecular weights directly.
                    object cids = null, formulae = null, names = null, boilT = null, molwt = null, casno = null;
                    _compounds11.GetCompoundList(ref cids, ref formulae, ref names, ref boilT, ref molwt, ref casno);
                    vals = ToDoubleArray(molwt);
                    if (vals == null)
                    {
                        object res = _compounds11.GetCompoundConstant(new object[] { "molecularWeight" }, ids);
                        vals = ToDoubleArray(res);
                    }
                }
                else
                {
                    var thermo10 = _raw as ICapeThermoMaterialObject;
                    if (thermo10 == null) return false;
                    object res = thermo10.GetComponentConstant(new object[] { "molecularWeight" }, ids);
                    vals = ToDoubleArray(res);
                }
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
        /// Total mass flow [kg/s] of the stream straight from the host package, or
        /// false when the host cannot supply it. This is the ABSOLUTE-SCALE anchor:
        /// hosts disagree on the unit of "mole" flows (CAPE-OPEN SI says mol/s;
        /// Aspen Plus V14's socket hands kmol/s — proven live 2026-07-14, where
        /// mass-based results came out exactly 1000× small), so blocks derive every
        /// absolute quantity from this mass flow plus composition ratios, never
        /// from the raw mole-flow magnitudes.
        /// </summary>
        public bool TryGetTotalMassFlowKgS(out double massKgS)
        {
            massKgS = 0.0;
            try
            {
                if (_mat11 != null)
                {
                    double[] r = Overall11("totalFlow", "mass");
                    if (r != null && r.Length > 0 && r[0] >= 0 && !double.IsNaN(r[0]) && !double.IsInfinity(r[0]))
                    {
                        massKgS = r[0];
                        return true;
                    }
                    return false;
                }
                double[] m = _mo.GetProp("totalFlow", Overall, null, Mixture, "Mass");
                if (m != null && m.Length > 0 && m[0] >= 0 && !double.IsNaN(m[0]) && !double.IsInfinity(m[0]))
                {
                    massKgS = m[0];
                    return true;
                }
            }
            catch { }
            return false;
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
            if (_mat11 != null)
            {
                foreach (string phase in LiquidPhaseLabels11())
                {
                    try
                    {
                        if (_props11 != null)
                        {
                            try { _props11.CalcSinglePhaseProp(new[] { "density" }, phase); }
                            catch { /* some hosts compute on Get directly */ }
                        }
                        object o = null;
                        _mat11.GetSinglePhaseProp("density", phase, "mass", ref o);
                        double[] r = ToDoubleArray(o);
                        if (r != null && r.Length > 0 && IsPositive(r[0]))
                        {
                            rho = r[0];
                            return true;
                        }
                    }
                    catch { /* try the next phase label */ }
                }
                // last resort: overall mass density (some hosts supply it)
                try
                {
                    double[] r = Overall11("density", "mass");
                    if (r != null && r.Length > 0 && IsPositive(r[0]))
                    {
                        rho = r[0];
                        return true;
                    }
                }
                catch { }
                return false;
            }

            foreach (string phase in new[] { "liquid", "Liquid", "Liquid1", "Overall" })
            {
                try
                {
                    try { _mo.CalcProp(new[] { "density" }, new[] { phase }, Mixture); }
                    catch { /* some hosts compute on GetProp directly */ }
                    double[] r = _mo.GetProp("density", phase, null, Mixture, "Mass");
                    if (r != null && r.Length > 0 && IsPositive(r[0]))
                    {
                        rho = r[0];
                        return true;
                    }
                }
                catch { /* try the next phase label */ }
            }
            return false;
        }

        // ------------------------------------------------------------------
        //  Thermo 1.1 plumbing
        // ------------------------------------------------------------------

        private double[] Overall11(string property, string basis)
        {
            object o = null;
            _mat11.GetOverallProp(property, basis, ref o);
            double[] r = ToDoubleArray(o);
            if (r == null || r.Length == 0)
                throw new CapeComputationException(
                    "Property package returned no '" + property + "' for stream on port '" + _portName + "'.");
            return r;
        }

        private void Set11Composition(double[] componentMoleFlows)
        {
            double total = 0.0;
            for (int i = 0; i < componentMoleFlows.Length; i++) total += componentMoleFlows[i];
            var x = new double[componentMoleFlows.Length];
            if (total > 0)
                for (int i = 0; i < x.Length; i++) x[i] = componentMoleFlows[i] / total;
            else if (x.Length > 0)
                x[0] = 1.0; // degenerate zero-flow composition; flow is zero anyway
            _mat11.SetOverallProp("fraction", "mole", x);
            _mat11.SetOverallProp("totalFlow", "mole", new[] { total });
        }

        private void Flash11(string kind, PhaseHint phase = PhaseHint.Liquid)
        {
            Guard(kind == "TP" ? "TP flash" : "PH flash", () =>
            {
                if (_equil11 == null)
                    throw new CapeComputationException(
                        "Stream on port '" + _portName + "' has no equilibrium routine (Thermo 1.1).");
                object spec1, spec2;
                if (kind == "TP")
                {
                    spec1 = new[] { "temperature", null, "Overall" };
                    spec2 = new[] { "pressure", null, "Overall" };
                }
                else
                {
                    spec1 = new[] { "pressure", null, "Overall" };
                    spec2 = new[] { "enthalpy", null, "Overall" };
                }

                // Preferred path: tell the equilibrium routine the outlet is a single
                // known phase, so it never forms a vapour / free-water phase over the
                // brine. That is what avoids the pure-water steam-table calls on a
                // salt stream (Aspen IAPWS95) and the "water absent" severe error on
                // the would-be vapour phase. Fall back to an all-phases flash if the
                // host rejects the restriction (some packages require the full list).
                if (TryRestrictPhase11(phase))
                {
                    try
                    {
                        _equil11.CalcEquilibrium(spec1, spec2, "Unspecified");
                        return 0;
                    }
                    catch { /* restricted flash refused — retry with all phases below */ }
                }
                AllowAllPhases11();
                _equil11.CalcEquilibrium(spec1, spec2, "Unspecified");
                return 0;
            });
        }

        /// <summary>
        /// Marks only the requested phase (liquid or vapour) present before a flash,
        /// so the equilibrium routine puts the whole outlet in that single phase.
        /// Returns false (and changes nothing) if the host does not expose a matching
        /// phase label, so the caller can fall back to an all-phases flash.
        /// </summary>
        private bool TryRestrictPhase11(PhaseHint hint)
        {
            try
            {
                string[] labels = null;
                if (_phases11 != null)
                {
                    object lab = null, agg = null, key = null;
                    _phases11.GetPhaseList(ref lab, ref agg, ref key);
                    labels = ToStringArray(lab);
                }
                if (labels == null || labels.Length == 0) return false;

                string want = hint == PhaseHint.Vapor ? "vap" : "liq";
                var pick = new System.Collections.Generic.List<string>();
                foreach (string l in labels)
                    if (l != null && l.ToLowerInvariant().Contains(want)) pick.Add(l);
                if (pick.Count == 0) return false;

                var status = new CapePhaseStatus[pick.Count];
                for (int i = 0; i < status.Length; i++) status[i] = CapePhaseStatus.CAPE_UNKNOWNPHASESTATUS;
                new CapeThermoMaterialWrapper(_raw).SetPresentPhases(pick.ToArray(), status);
                return true;
            }
            catch { return false; }
        }

        private void AllowAllPhases11()
        {
            try
            {
                string[] labels = null;
                if (_phases11 != null)
                {
                    object lab = null, agg = null, key = null;
                    _phases11.GetPhaseList(ref lab, ref agg, ref key);
                    labels = ToStringArray(lab);
                }
                if (labels == null || labels.Length == 0) return;
                var status = new CapePhaseStatus[labels.Length];
                for (int i = 0; i < status.Length; i++) status[i] = CapePhaseStatus.CAPE_UNKNOWNPHASESTATUS;
                new CapeThermoMaterialWrapper(_raw).SetPresentPhases(labels, status);
            }
            catch { /* many hosts pre-set the allowed phases; flash decides */ }
        }

        private string[] LiquidPhaseLabels11()
        {
            try
            {
                string[] labels = null;
                if (_phases11 != null)
                {
                    object lab = null, agg = null, key = null;
                    _phases11.GetPhaseList(ref lab, ref agg, ref key);
                    labels = ToStringArray(lab);
                }
                if (labels != null && labels.Length > 0)
                {
                    var liq = new System.Collections.Generic.List<string>();
                    foreach (string l in labels)
                        if (l != null && l.ToLowerInvariant().Contains("liq")) liq.Add(l);
                    foreach (string l in labels)
                        if (!liq.Contains(l)) liq.Add(l); // then the rest
                    return liq.ToArray();
                }
            }
            catch { }
            return new[] { "Liquid", "liquid" };
        }

        // ------------------------------------------------------------------
        //  shared plumbing
        // ------------------------------------------------------------------

        private static bool IsPositive(double d)
        {
            return !double.IsNaN(d) && !double.IsInfinity(d) && d > 0.1;
        }

        private static string[] ToStringArray(object o)
        {
            if (o == null) return null;
            if (o is string[] s) return s;
            if (o is Array a)
            {
                var r = new string[a.Length];
                int k = 0;
                foreach (object item in a) r[k++] = item == null ? null : item.ToString();
                return r;
            }
            return null;
        }

        private static double[] ToDoubleArray(object o)
        {
            if (o == null) return null;
            if (o is double[] d) return d;
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
