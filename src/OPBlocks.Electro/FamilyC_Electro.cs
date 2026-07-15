using System;
using System.Runtime.InteropServices;
using CapeOpen;
using OPBlocks.Core;

namespace OPBlocks.Electro
{
    // ===================================================================
    //  Family C — Electrochemical & Ion Separation
    // ===================================================================

    /// <summary>
    /// OP-ED — Electrodialysis stack. Ion transport diluate→concentrate under a DC
    /// field, set by Faraday's law across N cell pairs; electro-osmotic water drag;
    /// Ohmic stack current. Rating (voltage given → removal) and Design (target
    /// removal → required current/voltage) modes. Physics lives in
    /// <see cref="EdModel"/> (shared with the validation tests); see docs/OP-ED_MODEL.md.
    /// </summary>
    [ComVisible(true), Guid("dba4d883-276c-4937-82d4-444c5f4d499a"), ProgId("OPBlocks.ED")]
    [CapeName("OP-ED"), CapeVersion("1.0"), CapeVendorURL("https://oneprocess.sim")]
    [CapeDescription("Electrodialysis: Faradaic ion transport diluate->concentrate across a cell-pair stack; Rating/Design modes.")]
    [CapeAbout("ONE PROCESS Blocks — OP-ED. (c) ONE PROCESS Simulation. See the block report's 'Model & References' section for equations and literature.")]
    [CapeConsumesThermo(true), CapeSupportsThermodynamics11(true)]
    public class Electrodialysis : UnitBase
    {
        public Electrodialysis() : base()
        {
            ComponentName = "OP-ED"; ComponentDescription = "Electrodialysis Stack";
            AddMaterialPort("DiluateIn", "Diluate in", CapePortDirection.CAPE_INLET);
            AddMaterialPort("DiluateOut", "Diluate out (product)", CapePortDirection.CAPE_OUTLET);
            AddMaterialPort("ConcentrateIn", "Concentrate in", CapePortDirection.CAPE_INLET);
            AddMaterialPort("ConcentrateOut", "Concentrate out", CapePortDirection.CAPE_OUTLET);

            // NOTE: mode selector and cell-pair COUNT are REAL parameters with
            // integer semantics, NOT CAPE-OPEN option/integer parameters — Aspen's
            // grid renders only RealParameter (IATCapeXRealParameterSpec); any other
            // type blanks the whole grid (diagnosed live on OP-RO 2026-07-14). The
            // old AddIntParameter("CellPairs") would have blanked OP-ED's form.
            AddRealParameter("CalcMode",
                "Calculation mode: 0 = Rating (voltage given -> removal); 1 = Design (target removal -> required current/voltage)",
                0, 0, 1, "-");
            AddRealParameter("CellPairs", "Number of cell pairs (integer)", 100, 1, 1000, "-");
            AddRealParameter("AppliedVoltage", "Stack voltage (Rating input; computed in Design)", 24, 1, 500, "V");
            AddRealParameter("StackResistance", "Stack resistance", 5, 0.1, 500, "ohm");
            AddRealParameter("CurrentEfficiency", "Current (Faradaic) efficiency", 90, 20, 100, "%");
            AddRealParameter("IonValence", "Ion valence z (integer)", 1, 1, 3, "-");
            AddRealParameter("WaterTransport", "Electro-osmotic water transport", 8, 0, 30, "mol H2O/mol");
            AddRealParameter("TargetRemoval", "Target salt removal (Design mode)", 90, 5, 98, "%");

            AddOutputParameter("SaltRemoved", "Salt transferred to concentrate", "mol/s");
            AddOutputParameter("SaltRemoval", "Salt removal from diluate", "%");
            AddOutputParameter("DiluateTDSout", "Product (diluate out) TDS", "ppm");
            AddOutputParameter("ConcentrateTDSout", "Concentrate out TDS", "ppm");
            AddOutputParameter("FeedTDS", "Diluate feed TDS", "ppm");
            AddOutputParameter("StackCurrent", "Stack current", "A");
            AddOutputParameter("StackVoltageOut", "Stack voltage (used)", "V");
            AddOutputParameter("StackPower", "Stack electrical power", "kW");
            AddOutputParameter("SEC", "Specific energy per m3 product", "kWh/m3");
            AddOutputParameter("WaterTransfer", "Electro-osmotic water to concentrate", "m3/h");
            AddOutputParameter("RequiredCurrent", "Required current (Design mode)", "A");
            AddOutputParameter("RequiredVoltage", "Required voltage (Design mode)", "V");
        }
        public override string BlockCode => "OP-ED";

        protected override bool ValidateModel(out string message)
        {
            message = null;
            var d = GetConnectedMaterial("DiluateIn"); var c = GetConnectedMaterial("ConcentrateIn");
            if (d == null || c == null) { message = "Connect both DiluateIn and ConcentrateIn."; return false; }
            if (ProcessOps.IndexOf(d.ComponentIds, "WATER", "H2O") < 0) { message = "Streams must contain water."; return false; }
            return true;
        }

        /// <summary>Volumetric flow [m3/s]: package mass ÷ package density (unit-safe), else mole-derived kg/s over 1000 kg/m3 + warning.</summary>
        private double VolumetricM3S(ThermoProxy stream, double moleDerivedKgS, string label, bool haveMw)
        {
            double mPkg, rhoPkg;
            if (stream.TryGetTotalMassFlowKgS(out mPkg) && mPkg > 1e-30 &&
                stream.TryGetMassDensityKgM3(out rhoPkg))
                return mPkg / rhoPkg;
            if (!haveMw || moleDerivedKgS <= 1e-30) return 0.0;
            ReportWarning("The property package did not supply a " + label +
                          " mass flow/density pair — 1000 kg/m3 assumed for its volumetric flow.");
            return moleDerivedKgS / 1000.0;
        }

        private static double MoleDerivedKgS(double[] flowsMol, double[] mwGmol)
        {
            if (mwGmol == null) return 0.0;
            double kg = 0;
            for (int i = 0; i < flowsMol.Length; i++) kg += flowsMol[i] * mwGmol[i] / 1000.0;
            return kg;
        }

        protected override void Compute()
        {
            var dIn = RequireMaterial("DiluateIn"); var dOut = RequireMaterial("DiluateOut");
            var cIn = RequireMaterial("ConcentrateIn"); var cOut = RequireMaterial("ConcentrateOut");
            int wi = ProcessOps.IndexOf(dIn.ComponentIds, "WATER", "H2O");
            double[] df = dIn.GetOverallMoleFlows(); double[] cf = cIn.GetOverallMoleFlows();
            double Td = dIn.Temperature, Tc = cIn.Temperature, Pd = dIn.Pressure, Pc = cIn.Pressure;

            double[] mw;
            bool haveMw = dIn.TryGetMolecularWeightsGmol(out mw);
            if (!haveMw)
                ReportWarning("The property package did not supply molecular weights — TDS results are unavailable this run.");

            var spec = new EdModel.Spec
            {
                CalcMode = EdModel.ModeFromCode(R("CalcMode")),
                CellPairs = R("CellPairs"),
                AppliedVoltageV = R("AppliedVoltage"),
                StackResistanceOhm = R("StackResistance"),
                CurrentEfficiencyPct = R("CurrentEfficiency"),
                Valence = R("IonValence"),
                WaterTransport = R("WaterTransport"),
                TargetRemovalPct = R("TargetRemoval"),
            };

            EdModel.Result res = EdModel.Solve(spec, df, cf, wi, haveMw ? mw : null);
            dOut.SetOutletTP(res.DiluateOut, Td, Pd);
            cOut.SetOutletTP(res.ConcentrateOut, Tc, Pc);

            // product volumetric flow (diluate out) straight from the package
            double prodM3s = ProcessOps.Sum(res.DiluateOut) > 1e-30
                ? VolumetricM3S(dOut, MoleDerivedKgS(res.DiluateOut, mw), "product", haveMw) : 0.0;
            double prodM3h = prodM3s * 3600.0;
            EdModel.Energy e = EdModel.CalcEnergy(res.StackVoltageV, res.StackCurrentA, prodM3h);
            double waterM3h = res.WaterMovedMol * EdModel.WaterMwKgMol / 1000.0 * 3600.0;

            EmitWarnings(spec, res);

            Result("Salt removed", res.SaltMovedMol, "mol/s", "0.######");
            Result("Salt removal", res.RemovalPct, "%", "0.##");
            Result("Stack current", res.StackCurrentA, "A", "0.###");
            Result("Stack voltage", res.StackVoltageV, "V", "0.##");
            Result("Stack power", e.StackPowerKW, "kW", "0.###");
            Result("Specific energy (SEC)", e.SEC, "kWh/m3", "0.###");
            Result("Electro-osmotic water transfer", waterM3h, "m3/h", "0.#####");
            if (haveMw)
            {
                Result("Diluate feed TDS", res.TdsDiluateInPpm, "ppm", "0.#");
                Result("Product (diluate) TDS", res.TdsDiluateOutPpm, "ppm", "0.#");
                Result("Concentrate TDS", res.TdsConcentrateOutPpm, "ppm", "0.#");
            }
            if (spec.CalcMode == EdModel.Mode.Design)
            {
                Result("Required current", res.StackCurrentA, "A", "0.###");
                Result("Required voltage", res.StackVoltageV, "V", "0.##");
            }

            SetOutputParameter("SaltRemoved", res.SaltMovedMol);
            SetOutputParameter("SaltRemoval", res.RemovalPct);
            SetOutputParameter("DiluateTDSout", res.TdsDiluateOutPpm);
            SetOutputParameter("ConcentrateTDSout", res.TdsConcentrateOutPpm);
            SetOutputParameter("FeedTDS", res.TdsDiluateInPpm);
            SetOutputParameter("StackCurrent", res.StackCurrentA);
            SetOutputParameter("StackVoltageOut", res.StackVoltageV);
            SetOutputParameter("StackPower", e.StackPowerKW);
            SetOutputParameter("SEC", e.SEC);
            SetOutputParameter("WaterTransfer", waterM3h);
            SetOutputParameter("RequiredCurrent", spec.CalcMode == EdModel.Mode.Design ? res.StackCurrentA : 0.0);
            SetOutputParameter("RequiredVoltage", spec.CalcMode == EdModel.Mode.Design ? res.StackVoltageV : 0.0);
        }

        private void EmitWarnings(EdModel.Spec spec, EdModel.Result res)
        {
            if (res.SaltInDiluateMol <= 1e-12)
                ReportWarning("The diluate feed contains no dissolved species besides water — nothing to transfer. " +
                              "Add the salt/ions to the diluate stream composition.");
            if (res.DepletionLimited)
                ReportWarning(string.Format(
                    "Limiting-current / depletion regime: the applied current could transfer {0:0.####E+0} mol/s of ions " +
                    "but the diluate only supplies enough for {1:0.####E+0} mol/s ({2:0.#}% removal cap). " +
                    "Above the limiting current density, extra voltage splits water instead of moving ions — " +
                    "stage the stack or lower the current.",
                    res.FaradaicSaltMol, res.SaltMovedMol, EdModel.MaxDepletion * 100));
            if (spec.CalcMode == EdModel.Mode.Design && res.StackVoltageV > 500)
                ReportWarning(string.Format(
                    "Design requires {0:0.#} V across the stack, beyond a typical single-stack limit (~500 V). " +
                    "Split into multiple electrical stages or add cell pairs.", res.StackVoltageV));
        }

        /// <summary>Model &amp; References — travels with the simulation (ASCII, for Aspen's report viewer).</summary>
        protected override string BuildReport()
        {
            string body = base.BuildReport();
            var sb = new System.Text.StringBuilder(body);
            sb.AppendLine();
            sb.AppendLine("Model & References");
            sb.AppendLine("  Ion transport (Faraday's law across N cell pairs):");
            sb.AppendLine("    N_salt = eta * I * N_cp / (z * F)   [mol/s]");
            sb.AppendLine("    eta = current efficiency, I = stack current, N_cp = cell pairs,");
            sb.AppendLine("    z = ion valence, F = 96485 C/mol (Faraday constant).");
            sb.AppendLine("  Stack current (Rating):  I = AppliedVoltage / StackResistance (Ohm's law).");
            sb.AppendLine("  Design mode inverts Faraday: I_req = N_salt_target * z * F / (eta * N_cp),");
            sb.AppendLine("    V_req = I_req * StackResistance.");
            sb.AppendLine("  Water transport (electro-osmotic drag):  N_water = t_w * N_salt.");
            sb.AppendLine("  Salt transfer is capped at " + (EdModel.MaxDepletion * 100).ToString("0") +
                          "% of the diluate salt (limiting-current / depletion).");
            sb.AppendLine("  Power:  P = V * I;  SEC = P / Q_product (diluate-out volume).");
            sb.AppendLine("  All stream thermodynamics come from the selected Property Package.");
            sb.AppendLine("  Refs: Strathmann, Ion-Exchange Membrane Separation Processes (2004);");
            sb.AppendLine("        Strathmann, Desalination 264 (2010) 268-288;");
            sb.AppendLine("        Baker, Membrane Technology & Applications 3e (2012) ch.10.");
            return sb.ToString();
        }
    }

    /// <summary>OP-EDI — Electrodeionization.</summary>
    [ComVisible(true), Guid("59ceaff7-5f26-406b-9755-a7d7f3ba07a9"), ProgId("OPBlocks.EDI")]
    [CapeName("OP-EDI"), CapeVersion("1.0"), CapeVendorURL("https://oneprocess.sim")]
    [CapeDescription("Electrodeionization: electrodialysis plus ion-exchange resin for deep deionization.")]
    [CapeAbout("ONE PROCESS Blocks — OP-EDI. (c) ONE PROCESS Simulation.")]
    [CapeConsumesThermo(true), CapeSupportsThermodynamics11(true)]
    public class Electrodeionization : UnitBase
    {
        public Electrodeionization() : base()
        {
            ComponentName = "OP-EDI"; ComponentDescription = "Electrodeionization";
            AddMaterialPort("DiluteIn", "Dilute in", CapePortDirection.CAPE_INLET);
            AddMaterialPort("DiluteOut", "Product (ultrapure)", CapePortDirection.CAPE_OUTLET);
            AddMaterialPort("ConcentrateIn", "Concentrate in", CapePortDirection.CAPE_INLET);
            AddMaterialPort("ConcentrateOut", "Concentrate out", CapePortDirection.CAPE_OUTLET);
            AddIntParameter("CellPairs", "Number of cell pairs", 50, 1, 500);
            AddRealParameter("AppliedVoltage", "Stack voltage", 100, 1, 600, "V");
            AddRealParameter("StackResistance", "Stack resistance", 40, 0.1, 2000, "ohm");
            AddRealParameter("RemovalEfficiency", "Ion removal (resin-enhanced)", 99, 80, 99.99, "%");
        }
        public override string BlockCode => "OP-EDI";

        protected override bool ValidateModel(out string message)
        {
            message = null;
            var d = GetConnectedMaterial("DiluteIn"); var c = GetConnectedMaterial("ConcentrateIn");
            if (d == null || c == null) { message = "Connect both DiluteIn and ConcentrateIn."; return false; }
            if (ProcessOps.IndexOf(d.ComponentIds, "WATER", "H2O") < 0) { message = "Streams must contain water."; return false; }
            return true;
        }

        protected override void Compute()
        {
            var dIn = RequireMaterial("DiluteIn"); var dOut = RequireMaterial("DiluteOut");
            var cIn = RequireMaterial("ConcentrateIn"); var cOut = RequireMaterial("ConcentrateOut");
            int wi = ProcessOps.IndexOf(dIn.ComponentIds, "WATER", "H2O");
            double[] df = dIn.GetOverallMoleFlows(); double[] cf = cIn.GetOverallMoleFlows();
            double frac = R("RemovalEfficiency") / 100.0;

            if (ProcessOps.Sum(df) - df[wi] <= 1e-12)
                ReportWarning("The dilute feed contains no ions besides water — the product equals the feed. " +
                              "Add the dissolved species to the stream composition.");

            var dof = (double[])df.Clone(); var cof = (double[])cf.Clone();
            for (int i = 0; i < df.Length; i++)
                if (i != wi) { double m = df[i] * frac; dof[i] -= m; cof[i] += m; }
            dOut.SetOutletTP(dof, dIn.Temperature, dIn.Pressure);
            cOut.SetOutletTP(cof, cIn.Temperature, cIn.Pressure);

            double current = R("AppliedVoltage") / R("StackResistance");
            double dilM3h = df[wi] * 0.0180153 / 1000.0 * 3600.0;
            Result("Ion removal", R("RemovalEfficiency"), "%", "0.###");
            Result("Stack current", current, "A", "0.##");
            Result("Specific energy consumption", dilM3h > 0 ? R("AppliedVoltage") * current / 1000.0 / dilM3h : 0, "kWh/m3", "0.###");
        }
    }

    /// <summary>OP-CDI — Capacitive Deionization.</summary>
    [ComVisible(true), Guid("f449ed69-b1c4-4c23-816a-29188920ed71"), ProgId("OPBlocks.CDI")]
    [CapeName("OP-CDI"), CapeVersion("1.0"), CapeVendorURL("https://oneprocess.sim")]
    [CapeDescription("Capacitive deionization: cycle-averaged electrosorption of salt onto porous electrodes.")]
    [CapeAbout("ONE PROCESS Blocks — OP-CDI. (c) ONE PROCESS Simulation.")]
    [CapeConsumesThermo(true), CapeSupportsThermodynamics11(true)]
    public class CapacitiveDeionization : UnitBase
    {
        public CapacitiveDeionization() : base()
        {
            ComponentName = "OP-CDI"; ComponentDescription = "Capacitive Deionization";
            AddMaterialPort("Feed", "Feed", CapePortDirection.CAPE_INLET);
            AddMaterialPort("Product", "Product (desalinated)", CapePortDirection.CAPE_OUTLET);
            AddMaterialPort("Waste", "Waste (regeneration)", CapePortDirection.CAPE_OUTLET);
            AddRealParameter("SAC", "Salt adsorption capacity", 15, 1, 60, "mg/g");
            AddRealParameter("ElectrodeMass", "Total electrode mass", 1.0, 0.01, 1000, "kg");
            AddRealParameter("CycleTime", "Adsorption cycle time", 300, 10, 3600, "s");
            AddRealParameter("ChargeEfficiency", "Charge efficiency", 70, 20, 100, "%");
            AddRealParameter("WaterRecovery", "Water recovery", 80, 20, 95, "%");
            AddRealParameter("CellVoltage", "Cell voltage", 1.2, 0.6, 1.8, "V");
        }
        public override string BlockCode => "OP-CDI";

        protected override bool ValidateModel(out string message)
        {
            message = null;
            var f = GetConnectedMaterial("Feed");
            if (f == null) { message = "Connect a feed stream."; return false; }
            if (ProcessOps.IndexOf(f.ComponentIds, "WATER", "H2O") < 0) { message = "Feed must contain water."; return false; }
            return true;
        }

        protected override void Compute()
        {
            var feed = RequireMaterial("Feed"); var prod = RequireMaterial("Product"); var waste = RequireMaterial("Waste");
            int wi = ProcessOps.IndexOf(feed.ComponentIds, "WATER", "H2O");
            double[] f = feed.GetOverallMoleFlows(); double Tk = feed.Temperature, p = feed.Pressure;
            double saltFeed = ProcessOps.Sum(f) - f[wi];

            if (saltFeed <= 1e-12)
                ReportWarning("The feed contains no dissolved species besides water — nothing to remove. " +
                              "Add the salt/ions to the feed stream composition.");
            double removedMgS = R("SAC") * (R("ElectrodeMass") * 1000.0) / R("CycleTime"); // mg/s
            double removedMolS = Math.Min(removedMgS / 58440.0, saltFeed * 0.95);           // as NaCl
            double rec = R("WaterRecovery") / 100.0;
            double saltFracToProduct = saltFeed > 0 ? (saltFeed - removedMolS) / saltFeed : 0;

            var frac = new double[f.Length];
            for (int i = 0; i < f.Length; i++) frac[i] = (i == wi) ? rec : saltFracToProduct;
            ProcessOps.SplitByRecovery(feed, prod, waste, frac, Tk, p, Tk, p);

            double energyJ = removedMolS * ProcessOps.Faraday * R("CellVoltage") / (R("ChargeEfficiency") / 100.0);
            double prodM3h = f[wi] * rec * 0.0180153 / 1000.0 * 3600.0;
            Result("Salt removed", removedMolS, "mol/s", "0.#####");
            Result("Salt removal", saltFeed > 0 ? removedMolS / saltFeed * 100 : 0, "%", "0.#");
            Result("Water recovery", R("WaterRecovery"), "%", "0.#");
            Result("Specific energy consumption", prodM3h > 0 ? energyJ / 3.6e6 / prodM3h : 0, "kWh/m3", "0.###");
        }
    }

    /// <summary>OP-CHLORALK — Chlor-Alkali Membrane Cell.</summary>
    [ComVisible(true), Guid("db0c2564-a259-4e82-9038-7c1fd5c1754f"), ProgId("OPBlocks.ChlorAlkali")]
    [CapeName("OP-CHLORALK"), CapeVersion("1.0"), CapeVendorURL("https://oneprocess.sim")]
    [CapeDescription("Chlor-alkali membrane cell: brine electrolysis to Cl2, NaOH and H2 (Faradaic).")]
    [CapeAbout("ONE PROCESS Blocks — OP-CHLORALK. (c) ONE PROCESS Simulation.")]
    [CapeConsumesThermo(true), CapeSupportsThermodynamics11(true)]
    public class ChlorAlkali : UnitBase
    {
        public ChlorAlkali() : base()
        {
            ComponentName = "OP-CHLORALK"; ComponentDescription = "Chlor-Alkali Membrane Cell";
            AddMaterialPort("BrineIn", "Brine feed", CapePortDirection.CAPE_INLET);
            AddMaterialPort("DepletedBrine", "Depleted brine", CapePortDirection.CAPE_OUTLET);
            AddMaterialPort("Catholyte", "Caustic (NaOH) out", CapePortDirection.CAPE_OUTLET);
            AddMaterialPort("Chlorine", "Cl2 out", CapePortDirection.CAPE_OUTLET);
            AddMaterialPort("Hydrogen", "H2 out", CapePortDirection.CAPE_OUTLET);
            AddRealParameter("Current", "Cell current", 400000, 1, 1e7, "A");
            AddRealParameter("CurrentEfficiency", "Current efficiency", 96, 70, 100, "%");
            AddRealParameter("CellVoltage", "Cell voltage", 3.1, 2.0, 4.5, "V");
            AddRealParameter("WaterTransport", "Water transport per Na+", 3.5, 0, 8, "mol/mol");
        }
        public override string BlockCode => "OP-CHLORALK";

        protected override bool ValidateModel(out string message)
        {
            message = null;
            var b = GetConnectedMaterial("BrineIn");
            if (b == null) { message = "Connect a brine feed."; return false; }
            string[] ids = b.ComponentIds;
            if (ProcessOps.IndexOf(ids, "WATER", "H2O") < 0 ||
                ProcessOps.IndexOf(ids, "NACL", "NA+", "SODIUM", "CL-") < 0 ||
                ProcessOps.IndexOf(ids, "NAOH", "OH-") < 0 ||
                ProcessOps.IndexOf(ids, "CL2", "CHLORINE") < 0 ||
                ProcessOps.IndexOf(ids, "H2", "HYDROGEN") < 0)
            {
                message = "Component list must include WATER, NaCl, NaOH, Cl2 and H2 for the chlor-alkali reaction. Add the missing species (or map them).";
                return false;
            }
            return true;
        }

        protected override void Compute()
        {
            var bIn = RequireMaterial("BrineIn"); var dep = RequireMaterial("DepletedBrine");
            var cat = RequireMaterial("Catholyte"); var cl2 = RequireMaterial("Chlorine"); var h2 = RequireMaterial("Hydrogen");
            string[] ids = bIn.ComponentIds;
            int wi = ProcessOps.IndexOf(ids, "WATER", "H2O");
            int naclI = ProcessOps.IndexOf(ids, "NACL", "SODIUM", "NA+");
            int naohI = ProcessOps.IndexOf(ids, "NAOH", "OH-");
            int cl2I = ProcessOps.IndexOf(ids, "CL2", "CHLORINE");
            int h2I = ProcessOps.IndexOf(ids, "H2", "HYDROGEN");
            double[] f = bIn.GetOverallMoleFlows(); double Tk = bIn.Temperature, p = bIn.Pressure;

            double eff = R("CurrentEfficiency") / 100.0;
            double cl2Faradaic = ProcessOps.FaradayMoles(R("Current"), 2, eff); // 2 e- per Cl2
            // Production can never exceed the NaCl actually fed (2 NaCl per Cl2) —
            // otherwise chlorine appears from nothing and the mass balance breaks.
            double cl2Mol = Math.Min(cl2Faradaic, f[naclI] * 0.99 / 2.0);
            if (cl2Mol < cl2Faradaic * 0.999)
                ReportWarning(string.Format(
                    "Brine-limited operation: the feed supplies only enough NaCl for {0:0.####} mol/s Cl2 " +
                    "(the applied current could produce {1:0.####} mol/s). Increase the brine feed or reduce the current.",
                    cl2Mol, cl2Faradaic));
            double h2Mol = cl2Mol;                                            // 1 H2 per Cl2
            double naohMol = 2 * cl2Mol;                                      // 2 NaOH per Cl2
            double naclCons = 2 * cl2Mol;                                     // 2 NaCl per Cl2
            double waterMove = Math.Min(naohMol * R("WaterTransport"), f[wi] * 0.9);

            var depF = (double[])f.Clone();
            depF[naclI] -= naclCons;
            depF[wi] = Math.Max(0, depF[wi] - waterMove);
            var catF = new double[f.Length]; catF[naohI] = naohMol; catF[wi] = waterMove + naohMol; // caustic solution
            var cl2F = new double[f.Length]; cl2F[cl2I] = cl2Mol;
            var h2F = new double[f.Length]; h2F[h2I] = h2Mol;

            dep.SetOutletTP(depF, Tk, p);
            cat.SetOutletTP(catF, 80 + 273.15, p);
            cl2.SetOutletTP(cl2F, 80 + 273.15, p);
            h2.SetOutletTP(h2F, 80 + 273.15, p);

            double powerKW = R("CellVoltage") * R("Current") / 1000.0;
            Result("Chlorine production", cl2Mol * 0.070906, "kg/s", "0.####");
            Result("Caustic (NaOH) production", naohMol * 0.040, "kg/s", "0.####");
            Result("Hydrogen production", h2Mol * 0.002016, "kg/s", "0.######");
            Result("Cell voltage", R("CellVoltage"), "V", "0.##");
            Result("Specific energy (per t Cl2)", cl2Mol > 0 ? powerKW / (cl2Mol * 0.070906 * 3.6) : 0, "kWh/kg", "0.##");
        }
    }

    /// <summary>OP-IX — Ion Exchange Column.</summary>
    [ComVisible(true), Guid("4cd0259f-649f-4db8-90ca-9e78275d3ecf"), ProgId("OPBlocks.IX")]
    [CapeName("OP-IX"), CapeVersion("1.0"), CapeVendorURL("https://oneprocess.sim")]
    [CapeDescription("Ion exchange column: fixed-bed removal of a target ion (softening / selective capture).")]
    [CapeAbout("ONE PROCESS Blocks — OP-IX. (c) ONE PROCESS Simulation.")]
    [CapeConsumesThermo(true), CapeSupportsThermodynamics11(true)]
    public class IonExchange : UnitBase
    {
        public IonExchange() : base()
        {
            ComponentName = "OP-IX"; ComponentDescription = "Ion Exchange Column";
            AddMaterialPort("Feed", "Feed", CapePortDirection.CAPE_INLET);
            AddMaterialPort("Treated", "Treated water", CapePortDirection.CAPE_OUTLET);
            AddMaterialPort("RegenerantIn", "Regenerant in", CapePortDirection.CAPE_INLET);
            AddMaterialPort("SpentOut", "Spent regenerant out", CapePortDirection.CAPE_OUTLET);
            AddRealParameter("ResinVolume", "Resin bed volume", 1000, 1, 1e6, "L");
            AddRealParameter("Capacity", "Working capacity", 1.2, 0.1, 5, "eq/L");
            AddRealParameter("RemovalEfficiency", "Target ion removal", 95, 10, 99.9, "%");
        }
        public override string BlockCode => "OP-IX";

        protected override bool ValidateModel(out string message)
        {
            message = null;
            var f = GetConnectedMaterial("Feed"); var r = GetConnectedMaterial("RegenerantIn");
            if (f == null || r == null) { message = "Connect both Feed and RegenerantIn."; return false; }
            if (ProcessOps.IndexOf(f.ComponentIds, "WATER", "H2O") < 0) { message = "Feed must contain water."; return false; }
            return true;
        }

        protected override void Compute()
        {
            var feed = RequireMaterial("Feed"); var treated = RequireMaterial("Treated");
            var regIn = RequireMaterial("RegenerantIn"); var spent = RequireMaterial("SpentOut");
            int wi = ProcessOps.IndexOf(feed.ComponentIds, "WATER", "H2O");
            double[] f = feed.GetOverallMoleFlows(); double[] rf = regIn.GetOverallMoleFlows();
            double eff = R("RemovalEfficiency") / 100.0;

            var tf = (double[])f.Clone(); var sf = (double[])rf.Clone();
            double removedTotal = 0;
            for (int i = 0; i < f.Length; i++)
                if (i != wi) { double m = f[i] * eff; tf[i] -= m; if (i < sf.Length) sf[i] += m; removedTotal += m; }
            treated.SetOutletTP(tf, feed.Temperature, feed.Pressure);
            spent.SetOutletTP(sf, regIn.Temperature, regIn.Pressure);

            double capEq = R("ResinVolume") * R("Capacity");
            Result("Ion removal", R("RemovalEfficiency"), "%", "0.###");
            Result("Ions removed", removedTotal, "mol/s", "0.#####");
            Result("Bed capacity", capEq, "eq", "0.#");
        }
    }
}
