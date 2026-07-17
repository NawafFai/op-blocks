using System;
using System.Runtime.InteropServices;
using CapeOpen;
using OPBlocks.Core;

namespace OPBlocks.Energy
{
    // ===================================================================
    //  Family E — Energy, Gas & Advanced Oxidation (factory grade)
    // ===================================================================

    /// <summary>OP-PEM — PEM water electrolyzer.</summary>
    [ComVisible(true), Guid("5cf6e601-c4b1-48cd-be66-54e7500f0626"), ProgId("OPBlocks.PEM")]
    [CapeName("OP-PEM"), CapeVersion("1.0"), CapeVendorURL("https://oneprocess.sim")]
    [CapeDescription("PEM electrolyzer: splits water into green H2 and O2 (Faraday + voltage efficiency).")]
    [CapeAbout("ONE PROCESS Blocks — OP-PEM. (c) ONE PROCESS Simulation.")]
    [CapeConsumesThermo(true), CapeSupportsThermodynamics11(true)]
    public class PemElectrolyzer : UnitBase
    {
        public PemElectrolyzer() : base()
        {
            ComponentName = "OP-PEM"; ComponentDescription = "PEM Electrolyzer";
            AddMaterialPort("WaterFeed", "Water feed", CapePortDirection.CAPE_INLET);
            AddMaterialPort("Hydrogen", "H2 product", CapePortDirection.CAPE_OUTLET);
            AddMaterialPort("Oxygen", "O2 product (+ excess water)", CapePortDirection.CAPE_OUTLET);

            AddRealParameter("CellArea", "Active cell area", 1.0, 0.001, 50, "m2");
            AddRealParameter("CurrentDensity", "Current density (PEM 1-4)", 2.0, 0.1, 6, "A/cm2");
            // golden rule: cell count as a REAL integer code
            AddRealParameter("CellCount", "Cells in stack (integer code)", 100, 1, 2000, "-");
            AddRealParameter("CellVoltage", "Cell voltage (PEM 1.7-2.1)", 1.9, 1.23, 2.6, "V");
            AddRealParameter("FaradaicEff", "Faradaic efficiency", 99, 80, 100, "%");

            AddOutputParameter("H2Production", "H2 production", "kg/s");
            AddOutputParameter("O2Production", "O2 production", "kg/s");
            AddOutputParameter("StackPower", "Stack power", "kW");
            AddOutputParameter("SEC", "Specific energy", "kWh/kg H2");
            AddOutputParameter("EffHHV", "Stack efficiency (HHV)", "%");
            AddOutputParameter("EffLHV", "Stack efficiency (LHV)", "%");
        }
        public override string BlockCode => "OP-PEM";

        protected override bool ValidateModel(out string message)
        {
            message = null;
            var f = GetConnectedMaterial("WaterFeed");
            if (f == null) { message = "Connect a water feed."; return false; }
            string[] ids = f.ComponentIds;
            if (ProcessOps.IndexOf(ids, "WATER", "H2O") < 0 || ProcessOps.IndexOf(ids, "H2", "HYDROGEN") < 0 || ProcessOps.IndexOf(ids, "O2", "OXYGEN") < 0)
            { message = "Component list must include WATER, H2 and O2. Add the missing species."; return false; }
            return true;
        }

        protected override void Compute()
        {
            var feed = RequireMaterial("WaterFeed"); var h2 = RequireMaterial("Hydrogen"); var o2 = RequireMaterial("Oxygen");
            string[] ids = feed.ComponentIds;
            int wi = ProcessOps.IndexOf(ids, "WATER", "H2O");
            int h2i = ProcessOps.IndexOf(ids, "H2", "HYDROGEN");
            int o2i = ProcessOps.IndexOf(ids, "O2", "OXYGEN");
            double[] f = feed.GetOverallMoleFlows(); double Tk = feed.Temperature, p = feed.Pressure;

            var spec = new ElectrolyzerModel.Spec
            {
                CellAreaM2 = R("CellArea"),
                CurrentDensityAcm2 = R("CurrentDensity"),
                CellCount = R("CellCount"),
                CellVoltageV = R("CellVoltage"),
                FaradaicEffPct = R("FaradaicEff"),
            };
            ElectrolyzerModel.Perf x = ElectrolyzerModel.Solve(spec, wi >= 0 ? f[wi] : 0.0);

            if (x.WaterLimited)
                ReportWarning(string.Format(
                    "Water-limited operation: the feed supplies water for only {0:0.####} mol/s H2 " +
                    "(the stack current could produce {1:0.####} mol/s). Increase the water feed.",
                    x.H2MolS, x.H2FaradaicMolS));

            var h2F = new double[f.Length]; h2F[h2i] = x.H2MolS;
            // everything the feed carried besides the consumed water leaves with the
            // anolyte/O2 side — dropping it would leak mass for feeds carrying ions
            var o2F = (double[])f.Clone();
            if (wi >= 0) o2F[wi] = Math.Max(0, f[wi] - x.WaterConsMolS);
            o2F[o2i] += x.O2MolS;
            h2.SetOutletTP(h2F, Tk, p);
            o2.SetOutletTP(o2F, Tk, p);

            double h2KgS = x.H2MolS * ElectrolyzerModel.MwH2;
            Result("H2 production", h2KgS, "kg/s", "0.######");
            Result("O2 production", x.O2MolS * ElectrolyzerModel.MwO2, "kg/s", "0.######");
            Result("Stack power", x.StackPowerKW, "kW", "0.##");
            Result("Specific energy", x.SecKWhKg, "kWh/kg H2", "0.##");
            Result("Stack efficiency (HHV)", x.EffHhvPct, "%", "0.#");
            Result("Stack efficiency (LHV)", x.EffLhvPct, "%", "0.#");

            SetOutputParameter("H2Production", h2KgS);
            SetOutputParameter("O2Production", x.O2MolS * ElectrolyzerModel.MwO2);
            SetOutputParameter("StackPower", x.StackPowerKW);
            SetOutputParameter("SEC", x.SecKWhKg);
            SetOutputParameter("EffHHV", x.EffHhvPct);
            SetOutputParameter("EffLHV", x.EffLhvPct);
        }

        protected override string BuildReport()
        {
            var sb = new System.Text.StringBuilder(base.BuildReport());
            sb.AppendLine();
            sb.AppendLine("Model & References");
            sb.AppendLine("  Faradaic production:  N_H2 = eta_F * I * N_cells / (2F);  I = j*A;");
            sb.AppendLine("    N_O2 = N_H2/2; one water consumed per H2 (capped by the feed).");
            sb.AppendLine("  Energy (exact closed forms):");
            sb.AppendLine("    SEC = 26.59 * V / eta_F  [kWh/kg H2]");
            sb.AppendLine("    eff_HHV = 1.481/V * eta_F;  eff_LHV = 1.253/V * eta_F.");
            sb.AppendLine("  Thermo from the selected Property Package.");
            sb.AppendLine("  Validity: PEM j 1-4 A/cm2, V 1.7-2.1; 1.9 V/99% -> SEC ~ 51 kWh/kg.");
            sb.AppendLine("  Refs: Carmo et al., Int. J. Hydrogen Energy 38 (2013) 4901-4934;");
            sb.AppendLine("        Ursua, Gandia & Sanchis, Proc. IEEE 100 (2012) 410-426;");
            sb.AppendLine("        Barbir, Solar Energy 78 (2005) 661-669.");
            return sb.ToString();
        }
    }

    /// <summary>OP-AEL — Alkaline electrolyzer.</summary>
    [ComVisible(true), Guid("a85ddf54-c98c-465f-9c91-dd94f61e51c3"), ProgId("OPBlocks.AEL")]
    [CapeName("OP-AEL"), CapeVersion("1.0"), CapeVendorURL("https://oneprocess.sim")]
    [CapeDescription("Alkaline electrolyzer: KOH-based water electrolysis to H2 and O2 (Faraday).")]
    [CapeAbout("ONE PROCESS Blocks — OP-AEL. (c) ONE PROCESS Simulation.")]
    [CapeConsumesThermo(true), CapeSupportsThermodynamics11(true)]
    public class AlkalineElectrolyzer : UnitBase
    {
        public AlkalineElectrolyzer() : base()
        {
            ComponentName = "OP-AEL"; ComponentDescription = "Alkaline Electrolyzer";
            AddMaterialPort("WaterFeed", "Water/KOH feed", CapePortDirection.CAPE_INLET);
            AddMaterialPort("Hydrogen", "H2 product", CapePortDirection.CAPE_OUTLET);
            AddMaterialPort("Oxygen", "O2 product (+ excess water/KOH)", CapePortDirection.CAPE_OUTLET);

            AddRealParameter("CellArea", "Active cell area", 2.0, 0.001, 50, "m2");
            AddRealParameter("CurrentDensity", "Current density (alkaline 0.2-0.6)", 0.4, 0.05, 1.0, "A/cm2");
            // golden rule: cell count as a REAL integer code
            AddRealParameter("CellCount", "Cells in stack (integer code)", 100, 1, 2000, "-");
            AddRealParameter("CellVoltage", "Cell voltage (alkaline 1.8-2.2)", 1.9, 1.23, 2.4, "V");
            AddRealParameter("KOHconc", "KOH concentration (advisory)", 30, 10, 40, "wt%");
            AddRealParameter("FaradaicEff", "Faradaic efficiency", 99, 80, 100, "%");

            AddOutputParameter("H2Production", "H2 production", "kg/s");
            AddOutputParameter("O2Production", "O2 production", "kg/s");
            AddOutputParameter("StackPower", "Stack power", "kW");
            AddOutputParameter("SEC", "Specific energy", "kWh/kg H2");
            AddOutputParameter("EffHHV", "Stack efficiency (HHV)", "%");
        }
        public override string BlockCode => "OP-AEL";

        protected override bool ValidateModel(out string message)
        {
            message = null;
            var f = GetConnectedMaterial("WaterFeed");
            if (f == null) { message = "Connect a water/KOH feed."; return false; }
            string[] ids = f.ComponentIds;
            if (ProcessOps.IndexOf(ids, "WATER", "H2O") < 0 || ProcessOps.IndexOf(ids, "H2", "HYDROGEN") < 0 || ProcessOps.IndexOf(ids, "O2", "OXYGEN") < 0)
            { message = "Component list must include WATER, H2 and O2."; return false; }
            return true;
        }

        protected override void Compute()
        {
            var feed = RequireMaterial("WaterFeed"); var h2 = RequireMaterial("Hydrogen"); var o2 = RequireMaterial("Oxygen");
            string[] ids = feed.ComponentIds;
            int wi = ProcessOps.IndexOf(ids, "WATER", "H2O");
            int h2i = ProcessOps.IndexOf(ids, "H2", "HYDROGEN");
            int o2i = ProcessOps.IndexOf(ids, "O2", "OXYGEN");
            double[] f = feed.GetOverallMoleFlows(); double Tk = feed.Temperature, p = feed.Pressure;

            var spec = new ElectrolyzerModel.Spec
            {
                CellAreaM2 = R("CellArea"),
                CurrentDensityAcm2 = R("CurrentDensity"),
                CellCount = R("CellCount"),
                CellVoltageV = R("CellVoltage"),
                FaradaicEffPct = R("FaradaicEff"),
            };
            ElectrolyzerModel.Perf x = ElectrolyzerModel.Solve(spec, wi >= 0 ? f[wi] : 0.0);

            if (x.WaterLimited)
                ReportWarning(string.Format(
                    "Water-limited operation: the feed supplies water for only {0:0.####} mol/s H2 " +
                    "(the stack current could produce {1:0.####} mol/s). Increase the water feed.",
                    x.H2MolS, x.H2FaradaicMolS));

            var h2F = new double[f.Length]; h2F[h2i] = x.H2MolS;
            var o2F = (double[])f.Clone();   // KOH and excess water leave with the O2/anolyte side
            if (wi >= 0) o2F[wi] = Math.Max(0, f[wi] - x.WaterConsMolS);
            o2F[o2i] += x.O2MolS;
            h2.SetOutletTP(h2F, Tk, p);
            o2.SetOutletTP(o2F, Tk, p);

            double h2KgS = x.H2MolS * ElectrolyzerModel.MwH2;
            Result("H2 production", h2KgS, "kg/s", "0.######");
            Result("O2 production", x.O2MolS * ElectrolyzerModel.MwO2, "kg/s", "0.######");
            Result("Stack power", x.StackPowerKW, "kW", "0.##");
            Result("Specific energy", x.SecKWhKg, "kWh/kg H2", "0.##");
            Result("Stack efficiency (HHV)", x.EffHhvPct, "%", "0.#");

            SetOutputParameter("H2Production", h2KgS);
            SetOutputParameter("O2Production", x.O2MolS * ElectrolyzerModel.MwO2);
            SetOutputParameter("StackPower", x.StackPowerKW);
            SetOutputParameter("SEC", x.SecKWhKg);
            SetOutputParameter("EffHHV", x.EffHhvPct);
        }

        protected override string BuildReport()
        {
            var sb = new System.Text.StringBuilder(base.BuildReport());
            sb.AppendLine();
            sb.AppendLine("Model & References");
            sb.AppendLine("  Same Faradaic core as OP-PEM at alkaline operating ranges:");
            sb.AppendLine("    N_H2 = eta_F * I * N_cells / (2F);  SEC = 26.59 * V / eta_F;");
            sb.AppendLine("    eff_HHV = 1.481/V * eta_F. KOH concentration is advisory (electrolyte");
            sb.AppendLine("    conductivity peaks near 30 wt%).");
            sb.AppendLine("  Thermo from the selected Property Package.");
            sb.AppendLine("  Validity: j 0.2-0.6 A/cm2, V 1.8-2.2 -> SEC ~ 48-55 kWh/kg.");
            sb.AppendLine("  Refs: Ursua, Gandia & Sanchis, Proc. IEEE 100 (2012) 410-426;");
            sb.AppendLine("        Carmo et al., Int. J. Hydrogen Energy 38 (2013) 4901-4934.");
            return sb.ToString();
        }
    }

    /// <summary>OP-FC — PEM Fuel Cell.</summary>
    [ComVisible(true), Guid("47412507-fbf7-4694-9311-3320f0c7d07d"), ProgId("OPBlocks.FuelCell")]
    [CapeName("OP-FC"), CapeVersion("1.0"), CapeVendorURL("https://oneprocess.sim")]
    [CapeDescription("PEM fuel cell: H2 + air to electric power and water (Faraday + voltage efficiency).")]
    [CapeAbout("ONE PROCESS Blocks — OP-FC. (c) ONE PROCESS Simulation.")]
    [CapeConsumesThermo(true), CapeSupportsThermodynamics11(true)]
    public class FuelCell : UnitBase
    {
        public FuelCell() : base()
        {
            ComponentName = "OP-FC"; ComponentDescription = "PEM Fuel Cell";
            AddMaterialPort("HydrogenIn", "H2 fuel in", CapePortDirection.CAPE_INLET);
            AddMaterialPort("AirIn", "Air in", CapePortDirection.CAPE_INLET);
            AddMaterialPort("Exhaust", "Exhaust (depleted air + product water + unreacted H2)", CapePortDirection.CAPE_OUTLET);

            AddRealParameter("Utilization", "H2 utilization", 85, 20, 99, "%");
            AddRealParameter("CellVoltage", "Cell voltage (PEM under load 0.6-0.8)", 0.68, 0.4, 1.0, "V");

            AddOutputParameter("Power", "Power output", "kW");
            AddOutputParameter("H2Consumed", "H2 consumed", "kg/s");
            AddOutputParameter("EffLHV", "Electrical efficiency (LHV)", "%");
            AddOutputParameter("WaterProduced", "Water produced", "kg/s");
        }
        public override string BlockCode => "OP-FC";

        protected override bool ValidateModel(out string message)
        {
            message = null;
            var h = GetConnectedMaterial("HydrogenIn"); var a = GetConnectedMaterial("AirIn");
            if (h == null || a == null) { message = "Connect both HydrogenIn and AirIn."; return false; }
            string[] ids = h.ComponentIds;
            if (ProcessOps.IndexOf(ids, "H2", "HYDROGEN") < 0 || ProcessOps.IndexOf(ids, "O2", "OXYGEN") < 0 || ProcessOps.IndexOf(ids, "WATER", "H2O") < 0)
            { message = "Component list must include H2, O2 and WATER."; return false; }
            return true;
        }

        protected override void Compute()
        {
            var hIn = RequireMaterial("HydrogenIn"); var aIn = RequireMaterial("AirIn"); var ex = RequireMaterial("Exhaust");
            string[] ids = hIn.ComponentIds;
            int wi = ProcessOps.IndexOf(ids, "WATER", "H2O");
            int h2i = ProcessOps.IndexOf(ids, "H2", "HYDROGEN");
            int o2i = ProcessOps.IndexOf(ids, "O2", "OXYGEN");
            double[] hf = hIn.GetOverallMoleFlows(); double[] af = aIn.GetOverallMoleFlows();

            if (hf[h2i] <= 1e-15)
                ReportWarning("The fuel feed carries no hydrogen flow — set the H2 amount in the HydrogenIn stream.");

            var spec = new FcModel.Spec { UtilizationPct = R("Utilization"), CellVoltageV = R("CellVoltage") };
            FcModel.Perf x = FcModel.Solve(spec, hf[h2i], af[o2i]);

            if (x.AirLimited)
                ReportWarning(string.Format(
                    "Air-limited operation: the air feed supplies oxygen for only {0:0.####} mol/s H2 " +
                    "(utilization asked for {1:0.####} mol/s). Increase the air flow.",
                    x.H2ConsMolS, x.H2WantMolS));

            var exF = new double[hf.Length];
            for (int i = 0; i < exF.Length; i++) exF[i] = af[i] + (i < hf.Length ? hf[i] : 0);
            exF[h2i] -= x.H2ConsMolS;
            exF[o2i] -= x.O2ConsMolS;
            exF[wi] += x.WaterProdMolS;
            ex.SetOutletTP(exF, aIn.Temperature, aIn.Pressure);

            double h2KgS = x.H2ConsMolS * FcModel.MwH2;
            Result("Power output", x.PowerKW, "kW", "0.##");
            Result("H2 consumed", h2KgS, "kg/s", "0.######");
            Result("Electrical efficiency (LHV)", x.EffLhvPct, "%", "0.#");
            Result("Water produced", x.WaterProdMolS * FcModel.MwH2O, "kg/s", "0.#####");

            SetOutputParameter("Power", x.PowerKW);
            SetOutputParameter("H2Consumed", h2KgS);
            SetOutputParameter("EffLHV", x.EffLhvPct);
            SetOutputParameter("WaterProduced", x.WaterProdMolS * FcModel.MwH2O);
        }

        protected override string BuildReport()
        {
            var sb = new System.Text.StringBuilder(base.BuildReport());
            sb.AppendLine();
            sb.AppendLine("Model & References");
            sb.AppendLine("  Faradaic consumption: H2 = utilization * feed, capped by O2 (2 H2/O2);");
            sb.AppendLine("    I = 2F * N_H2;   P = V_cell * I;   water produced = H2 consumed.");
            sb.AppendLine("  Voltage efficiency (exact): eff_LHV = V_cell / 1.253");
            sb.AppendLine("    (0.68 V -> 54.3%; the standard PEM operating point).");
            sb.AppendLine("  Thermo from the selected Property Package.");
            sb.AppendLine("  Refs: O'Hayre, Cha, Colella & Prinz, Fuel Cell Fundamentals 3e (2016)");
            sb.AppendLine("        ch.2;  Barbir, PEM Fuel Cells 2e (2013);");
            sb.AppendLine("        Larminie & Dicks, Fuel Cell Systems Explained 2e (2003).");
            return sb.ToString();
        }
    }

    /// <summary>OP-RPB — Rotating Packed Bed absorber (HiGee).</summary>
    [ComVisible(true), Guid("20916487-6782-448c-85e7-e93e1db3e632"), ProgId("OPBlocks.RPB")]
    [CapeName("OP-RPB"), CapeVersion("1.0"), CapeVendorURL("https://oneprocess.sim")]
    [CapeDescription("Rotating packed bed (HiGee) absorber: centrifugal-field intensified gas absorption.")]
    [CapeAbout("ONE PROCESS Blocks — OP-RPB. (c) ONE PROCESS Simulation.")]
    [CapeConsumesThermo(true), CapeSupportsThermodynamics11(true)]
    public class RotatingPackedBed : UnitBase
    {
        public RotatingPackedBed() : base()
        {
            ComponentName = "OP-RPB"; ComponentDescription = "Rotating Packed Bed Absorber (HiGee)";
            AddMaterialPort("GasIn", "Gas in", CapePortDirection.CAPE_INLET);
            AddMaterialPort("GasOut", "Treated gas out", CapePortDirection.CAPE_OUTLET);
            AddMaterialPort("LiquidIn", "Absorbent liquid in", CapePortDirection.CAPE_INLET);
            AddMaterialPort("LiquidOut", "Loaded liquid out", CapePortDirection.CAPE_OUTLET);

            AddRealParameter("RotorSpeed", "Rotor speed (HiGee 500-2500)", 1000, 100, 3000, "rpm");
            AddRealParameter("KlaCoeff", "k_La calibration constant (NTU = k*sqrt(rpm))", 0.02, 0.001, 0.2, "-");
            AddRealParameter("RotorPowerCoeff", "Rotor power coefficient (kW per rpm^2)", 5e-7, 1e-8, 1e-5, "kW/rpm2");

            AddOutputParameter("Removal", "Solute removal", "%");
            AddOutputParameter("NTU", "Transfer units", "-");
            AddOutputParameter("Absorbed", "Solute absorbed", "mol/s");
            AddOutputParameter("RotorPower", "Rotor power (indicative)", "kW");
        }
        public override string BlockCode => "OP-RPB";

        protected override bool ValidateModel(out string message)
        {
            message = null;
            var g = GetConnectedMaterial("GasIn"); var l = GetConnectedMaterial("LiquidIn");
            if (g == null || l == null) { message = "Connect both GasIn and LiquidIn."; return false; }
            return true;
        }

        protected override void Compute()
        {
            var gIn = RequireMaterial("GasIn"); var gOut = RequireMaterial("GasOut");
            var lIn = RequireMaterial("LiquidIn"); var lOut = RequireMaterial("LiquidOut");
            string[] ids = gIn.ComponentIds;
            double[] gf = gIn.GetOverallMoleFlows(); double[] lf = lIn.GetOverallMoleFlows();
            int solute = ProcessOps.IndexOf(ids, "CO2", "CARBON DIOXIDE");
            if (solute < 0)   // fall back to most abundant non-water gas component
            {
                int wi = ProcessOps.IndexOf(ids, "WATER", "H2O"); double best = -1;
                for (int i = 0; i < gf.Length; i++) if (i != wi && gf[i] > best) { best = gf[i]; solute = i; }
                ReportWarning("No CO2 in the component list — absorbing the most abundant non-water gas species instead.");
            }
            if (solute < 0 || gf[solute] <= 1e-15)
                ReportWarning("The gas feed carries no absorbable solute flow — nothing to absorb.");

            var spec = new RpbModel.Spec
            {
                RotorRpm = R("RotorSpeed"),
                KlaCal = R("KlaCoeff"),
                RotorPowerCoeff = R("RotorPowerCoeff"),
            };
            RpbModel.Perf x = RpbModel.Solve(spec);
            double absorbed = solute >= 0 ? gf[solute] * x.RemovalFrac : 0.0;

            var gof = (double[])gf.Clone(); if (solute >= 0) gof[solute] -= absorbed;
            var lof = (double[])lf.Clone(); if (solute >= 0 && solute < lof.Length) lof[solute] += absorbed;
            gOut.SetOutletTP(gof, gIn.Temperature, gIn.Pressure);
            lOut.SetOutletTP(lof, lIn.Temperature, lIn.Pressure);

            Result("Solute removal", x.RemovalFrac * 100, "%", "0.##");
            Result("NTU", x.Ntu, "-", "0.###");
            Result("Solute absorbed", absorbed, "mol/s", "0.######");
            Result("Rotor power (indicative)", x.RotorKW, "kW", "0.###");

            SetOutputParameter("Removal", x.RemovalFrac * 100);
            SetOutputParameter("NTU", x.Ntu);
            SetOutputParameter("Absorbed", absorbed);
            SetOutputParameter("RotorPower", x.RotorKW);
        }

        protected override string BuildReport()
        {
            var sb = new System.Text.StringBuilder(base.BuildReport());
            sb.AppendLine();
            sb.AppendLine("Model & References");
            sb.AppendLine("  HiGee intensification: the centrifugal field (hundreds of g) shears");
            sb.AppendLine("  the liquid into films/droplets, raising k_La 10-100x over a column.");
            sb.AppendLine("    NTU = k_cal * sqrt(RPM);   removal = 1 - exp(-NTU)");
            sb.AppendLine("  (k_cal lumps packing geometry, flows and diffusivity; Chen's k_La");
            sb.AppendLine("   correlations reduce this way at fixed flows).");
            sb.AppendLine("  Rotor power indicative, proportional to rpm^2.");
            sb.AppendLine("  Thermo from the selected Property Package.");
            sb.AppendLine("  Validity: 500-2500 rpm CO2/VOC absorption duties.");
            sb.AppendLine("  Refs: Ramshaw & Mallinson, US Patent 4,283,255 (1981);");
            sb.AppendLine("        Chen, Lin & Liu, Ind. Eng. Chem. Res. 44 (2005) 7868-7875.");
            return sb.ToString();
        }
    }

    /// <summary>OP-UVAOP — UV / advanced oxidation reactor.</summary>
    [ComVisible(true), Guid("71ab6cc5-95d4-4c8d-b45f-8623b9e47538"), ProgId("OPBlocks.UVAOP")]
    [CapeName("OP-UVAOP"), CapeVersion("1.0"), CapeVendorURL("https://oneprocess.sim")]
    [CapeDescription("UV / advanced oxidation reactor: UV(+H2O2) destruction of trace contaminants (Bolton EEO).")]
    [CapeAbout("ONE PROCESS Blocks — OP-UVAOP. (c) ONE PROCESS Simulation.")]
    [CapeConsumesThermo(true), CapeSupportsThermodynamics11(true)]
    public class UvAopReactor : UnitBase
    {
        public UvAopReactor() : base()
        {
            ComponentName = "OP-UVAOP"; ComponentDescription = "UV / Advanced Oxidation Reactor";
            AddMaterialPort("LiquidIn", "Liquid in", CapePortDirection.CAPE_INLET);
            AddMaterialPort("LiquidOut", "Treated liquid out", CapePortDirection.CAPE_OUTLET);

            AddRealParameter("UVDose", "UV dose (AOP duties 500-2000)", 800, 10, 5000, "mJ/cm2");
            AddRealParameter("RateConstant", "Dose-response rate k", 0.003, 1e-5, 0.1, "cm2/mJ");
            AddRealParameter("UVT", "UV transmittance at 254 nm", 90, 30, 99, "%");
            AddRealParameter("H2O2Dose", "H2O2 dose (radical enhancement)", 5, 0, 50, "mg/L");
            AddRealParameter("LampPower", "Total lamp power", 10, 0.1, 1000, "kW");

            AddOutputParameter("Destruction", "Contaminant destruction", "%");
            AddOutputParameter("LogRemoval", "Log removal", "log");
            AddOutputParameter("EEO", "Electrical energy per order (Bolton)", "kWh/m3/order");
            AddOutputParameter("EffDose", "Effective UV dose", "mJ/cm2");
        }
        public override string BlockCode => "OP-UVAOP";

        protected override bool ValidateModel(out string message)
        {
            message = null;
            var f = GetConnectedMaterial("LiquidIn");
            if (f == null) { message = "Connect a liquid feed."; return false; }
            if (ProcessOps.IndexOf(f.ComponentIds, "WATER", "H2O") < 0) { message = "Feed must contain water."; return false; }
            return true;
        }

        protected override void Compute()
        {
            var lIn = RequireMaterial("LiquidIn"); var lOut = RequireMaterial("LiquidOut");
            int wi = ProcessOps.IndexOf(lIn.ComponentIds, "WATER", "H2O");
            double[] f = lIn.GetOverallMoleFlows();

            double mF, rhoF, feedM3s;
            if (lIn.TryGetTotalMassFlowKgS(out mF) && mF > 1e-30 && lIn.TryGetMassDensityKgM3(out rhoF))
                feedM3s = mF / rhoF;
            else
                feedM3s = (wi >= 0 ? f[wi] : 0) * 0.0180153 / 1000.0;

            double soluteMolS = ProcessOps.Sum(f) - (wi >= 0 ? f[wi] : 0);
            if (soluteMolS <= 1e-15)
                ReportWarning("The feed carries no contaminant besides water — nothing to destroy. " +
                              "Set the micropollutant flow in the feed composition.");

            var spec = new UvAopModel.Spec
            {
                UvDoseMJcm2 = R("UVDose"),
                RateKcm2mJ = R("RateConstant"),
                UvtPct = R("UVT"),
                H2o2MgL = R("H2O2Dose"),
                LampPowerKW = R("LampPower"),
            };
            UvAopModel.Perf x = UvAopModel.Solve(spec, feedM3s * 3600.0);

            if (x.EeoKWhM3Order > 2.5 && x.LogRemoval > 0)
                ReportWarning(string.Format(
                    "EEO ({0:0.##} kWh/m3/order) is above the published UV/H2O2 band (~0.1-2.5) — " +
                    "the lamp is oversized for this flow, or the UVT is too low.", x.EeoKWhM3Order));
            if (R("UVT") < 60)
                ReportWarning("UV transmittance below 60% — pre-treatment (filtration) is normally required " +
                              "before UV AOP at this water quality.");

            var of = (double[])f.Clone(); double destroyed = 0;
            for (int i = 0; i < f.Length; i++)
                if (i != wi) { double m = f[i] * x.DestroyFrac; of[i] -= m; destroyed += m; }
            lOut.SetOutletTP(of, lIn.Temperature, lIn.Pressure);
            ReportWarning("Destroyed contaminant is oxidised (mineralised); its stream flow is removed at the outlet.");

            Result("Contaminant destruction", x.DestroyFrac * 100, "%", "0.##");
            Result("Log removal", x.LogRemoval, "log", "0.##");
            Result("EEO", x.EeoKWhM3Order, "kWh/m3/order", "0.###");
            Result("Effective UV dose", x.EffDoseMJcm2, "mJ/cm2", "0.#");

            SetOutputParameter("Destruction", x.DestroyFrac * 100);
            SetOutputParameter("LogRemoval", x.LogRemoval);
            SetOutputParameter("EEO", x.EeoKWhM3Order);
            SetOutputParameter("EffDose", x.EffDoseMJcm2);
        }

        protected override string BuildReport()
        {
            var sb = new System.Text.StringBuilder(base.BuildReport());
            sb.AppendLine();
            sb.AppendLine("Model & References");
            sb.AppendLine("  First-order UV dose-response:  ln(C/C0) = -k * D_eff;");
            sb.AppendLine("    D_eff = D * (UVT/100) * (1 + H2O2/20)  (empirical OH-radical boost).");
            sb.AppendLine("  Log removal = k * D_eff / ln(10).");
            sb.AppendLine("  Bolton figure of merit (IUPAC, exact by definition):");
            sb.AppendLine("    EEO = P_lamp / (Q * log_removal)   [kWh/m3/order]");
            sb.AppendLine("  Published UV/H2O2 EEO for trace organics: ~0.1-2.5 kWh/m3/order.");
            sb.AppendLine("  Thermo from the selected Property Package.");
            sb.AppendLine("  Refs: Bolton, Bircher, Tumas & Tolman, Pure Appl. Chem. 73 (2001)");
            sb.AppendLine("        627-637;  Oppenlaender, Photochemical Purification of Water");
            sb.AppendLine("        and Air (2003).");
            return sb.ToString();
        }
    }
}
