using System;
using System.Runtime.InteropServices;
using CapeOpen;
using OPBlocks.Core;

namespace OPBlocks.Demo
{
    /// <summary>
    /// OP-MIXER-DEMO — the M0 skeleton block (spec §11). A trivial two-stream
    /// adiabatic mixer whose only job is to exercise the whole plumbing end to
    /// end: palette registration, drag-and-drop, port connection, parameters,
    /// the Edit GUI, delegated thermodynamics, the calculation report, and
    /// save/reopen persistence — on Aspen Plus V14 and DWSIM.
    ///
    /// All thermodynamics (stream enthalpies, the outlet PH flash) go through the
    /// host material objects via <see cref="ThermoProxy"/>; no property is
    /// hardcoded (Requirement R4).
    /// </summary>
    [ComVisible(true)]
    [Guid("f0785d44-eac0-4d51-a895-68ab52849cb8")]
    [ProgId("OPBlocks.MixerDemo")]
    [CapeName("OP-MIXER-DEMO")]
    [CapeDescription("ONE PROCESS demo mixer — combines two material streams adiabatically with fully delegated thermodynamics (M0 skeleton block).")]
    [CapeVersion("1.0")]
    [CapeVendorURL("https://oneprocess.sim")]
    [CapeAbout("ONE PROCESS Blocks — OP-MIXER-DEMO. (c) ONE PROCESS Simulation.")]
    [CapeConsumesThermo(true)]
    [CapeSupportsThermodynamics11(true)]
    public class MixerDemo : UnitBase
    {
        private const string SpecMinInlet = "Minimum inlet";
        private const string SpecSpecified = "Specified";

        // Cached results for the report (recomputed on each run).
        private bool _hasResult;
        private MixResult _result;
        private double _outletTemperature;
        private string _pressureSpecUsed = SpecMinInlet;

        public MixerDemo() : base()
        {
            ComponentName = "OP-MIXER-DEMO";
            ComponentDescription = "ONE PROCESS demo mixer (M0)";

            AddMaterialPort("Inlet1", "First inlet material stream", CapePortDirection.CAPE_INLET);
            AddMaterialPort("Inlet2", "Second inlet material stream", CapePortDirection.CAPE_INLET);
            AddMaterialPort("Outlet", "Mixed outlet material stream", CapePortDirection.CAPE_OUTLET);

            var pressureSpec = new OptionParameter(
                "PressureSpec",
                "Outlet pressure basis",
                SpecMinInlet, SpecMinInlet,
                new[] { SpecMinInlet, SpecSpecified },
                true,
                CapeParamMode.CAPE_INPUT);
            Parameters.Add(pressureSpec);

            AddRealParameter("SpecifiedPressure",
                "Outlet pressure used when PressureSpec = Specified",
                101325.0, 1.0, 1.0e9, "Pa");
        }

        public override string BlockCode { get { return "OP-MIXER-DEMO"; } }

        protected override bool ValidateModel(out string message)
        {
            message = null;
            ThermoProxy in1 = GetConnectedMaterial("Inlet1");
            ThermoProxy in2 = GetConnectedMaterial("Inlet2");
            if (in1 == null || in2 == null)
            {
                message = "Connect both inlet streams (Inlet1 and Inlet2) before running.";
                return false;
            }
            if (in1.ComponentCount != in2.ComponentCount)
            {
                message = "The two inlet streams use different component lists; a mixer requires matching components.";
                return false;
            }
            return true;
        }

        protected override void Compute()
        {
            ThermoProxy in1 = GetConnectedMaterial("Inlet1");
            ThermoProxy in2 = GetConnectedMaterial("Inlet2");
            ThermoProxy outlet = GetConnectedMaterial("Outlet");
            if (in1 == null || in2 == null || outlet == null)
                throw new CapeSolvingErrorException(
                    "Connect Inlet1, Inlet2 and Outlet before running OP-MIXER-DEMO.");

            double[] flows1 = in1.GetOverallMoleFlows();
            double[] flows2 = in2.GetOverallMoleFlows();
            double h1 = in1.MolarEnthalpy;
            double h2 = in2.MolarEnthalpy;
            double p1 = in1.Pressure;
            double p2 = in2.Pressure;

            string spec = OptionValue("PressureSpec");
            bool specified = string.Equals(spec, SpecSpecified, StringComparison.OrdinalIgnoreCase);
            double specifiedPressure = RealSIValue("SpecifiedPressure");

            MixResult r = MixerMath.Mix(flows1, h1, p1, flows2, h2, p2, specified, specifiedPressure);
            if (r.TotalMoleFlow <= 1.0e-30)
                throw new CapeSolvingErrorException(
                    "Both inlet streams have zero total flow; there is nothing to mix.");

            // Strict Aspen setting order + PH flash handled inside ThermoProxy (§5 rule 2).
            outlet.SetOutletPH(r.MoleFlows, r.Pressure, r.MolarEnthalpy);

            _result = r;
            _pressureSpecUsed = specified ? SpecSpecified : SpecMinInlet;
            try { _outletTemperature = outlet.Temperature; }
            catch { _outletTemperature = double.NaN; }
            _hasResult = true;
        }

        protected override string BuildReport()
        {
            var rep = new ReportBuilder("Demo Mixer", "OP-MIXER-DEMO");
            if (!_hasResult)
            {
                rep.Section("Status").Line("  Block has not been calculated yet. Run the flowsheet to populate results.");
                return rep.Build();
            }

            rep.Section("Configuration")
               .Value("Outlet pressure basis", _pressureSpecUsed);

            rep.Section("Results")
               .Value("Total outlet mole flow", _result.TotalMoleFlow, "mol/s")
               .Value("Outlet pressure", _result.Pressure, "Pa", "0.##")
               .Value("Outlet molar enthalpy", _result.MolarEnthalpy, "J/mol", "0.##");

            if (!double.IsNaN(_outletTemperature))
                rep.Value("Outlet temperature", _outletTemperature, "K", "0.###");

            rep.Section("Energy balance")
               .Line("  Adiabatic mixer: Q = 0.")
               .Line("  Enthalpy balance closed with host-supplied stream enthalpies")
               .Line("  (spec §5 rule 1); no Cp or reference state hardcoded.");

            return rep.Build();
        }

        private string OptionValue(string name)
        {
            var p = FindParameter(name);
            object v = p == null ? null : ((ICapeParameter)p).value;
            return v == null ? string.Empty : v.ToString();
        }

        private double RealSIValue(string name)
        {
            var p = FindParameter(name) as RealParameter;
            return p == null ? 0.0 : p.SIValue;
        }
    }
}
