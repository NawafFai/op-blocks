using System;
using System.Runtime.InteropServices;
using CapeOpen;
using OPBlocks.Core;

namespace OPBlocks.Energy
{
    // ===================================================================
    //  Family E — Energy, Gas & Advanced Oxidation
    // ===================================================================

    /// <summary>OP-PEM — PEM water electrolyzer.</summary>
    [ComVisible(true), Guid("5cf6e601-c4b1-48cd-be66-54e7500f0626"), ProgId("OPBlocks.PEM")]
    [CapeName("OP-PEM"), CapeVersion("1.0"), CapeVendorURL("https://oneprocess.sim")]
    [CapeDescription("PEM electrolyzer: splits water into green H2 and O2 (polarization + Faraday).")]
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
            AddRealParameter("CurrentDensity", "Current density", 2.0, 0.1, 6, "A/cm2");
            AddIntParameter("CellCount", "Cells in stack", 100, 1, 2000);
            AddRealParameter("CellVoltage", "Cell voltage", 1.9, 1.23, 2.6, "V");
            AddRealParameter("FaradaicEff", "Faradaic efficiency", 99, 80, 100, "%");
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

            double current = R("CurrentDensity") * R("CellArea") * 1e4;          // A (per cell)
            double h2Faradaic = ProcessOps.FaradayMoles(current, 2, R("FaradaicEff") / 100.0) * I("CellCount");
            double h2Mol = Math.Min(h2Faradaic, f[wi] * 0.99);
            if (h2Mol < h2Faradaic * 0.999)
                ReportWarning(string.Format(
                    "Water-limited operation: the feed supplies water for only {0:0.####} mol/s H2 " +
                    "(the stack current could produce {1:0.####} mol/s). Increase the water feed.",
                    h2Mol, h2Faradaic));
            double o2Mol = h2Mol / 2.0;
            double excessWater = f[wi] - h2Mol;

            var h2F = new double[f.Length]; h2F[h2i] = h2Mol;
            // Everything the feed carried besides the consumed water leaves with
            // the anolyte/O2 side — dropping it would leak mass for feeds that
            // carry ions or additives (user-freedom rule).
            var o2F = (double[])f.Clone();
            o2F[wi] = Math.Max(0, excessWater);
            o2F[o2i] += o2Mol;
            h2.SetOutletTP(h2F, Tk, p);
            o2.SetOutletTP(o2F, Tk, p);

            double stackPowerKW = R("CellVoltage") * current * I("CellCount") / 1000.0;
            double h2KgS = h2Mol * 0.002016;
            double lhvKW = h2KgS * 120000.0;                                     // 120 MJ/kg LHV
            Result("H2 production", h2KgS, "kg/s", "0.######");
            Result("Cell voltage", R("CellVoltage"), "V", "0.###");
            Result("Stack power", stackPowerKW, "kW", "0.##");
            Result("Stack efficiency (LHV)", stackPowerKW > 0 ? lhvKW / stackPowerKW * 100 : 0, "%", "0.#");
            Result("Specific energy", h2KgS > 0 ? stackPowerKW / (h2KgS * 3600.0) : 0, "kWh/kg H2", "0.##");
        }
    }

    /// <summary>OP-AEL — Alkaline electrolyzer.</summary>
    [ComVisible(true), Guid("a85ddf54-c98c-465f-9c91-dd94f61e51c3"), ProgId("OPBlocks.AEL")]
    [CapeName("OP-AEL"), CapeVersion("1.0"), CapeVendorURL("https://oneprocess.sim")]
    [CapeDescription("Alkaline electrolyzer: KOH-based water electrolysis to H2 and O2.")]
    [CapeAbout("ONE PROCESS Blocks — OP-AEL. (c) ONE PROCESS Simulation.")]
    [CapeConsumesThermo(true), CapeSupportsThermodynamics11(true)]
    public class AlkalineElectrolyzer : UnitBase
    {
        public AlkalineElectrolyzer() : base()
        {
            ComponentName = "OP-AEL"; ComponentDescription = "Alkaline Electrolyzer";
            AddMaterialPort("WaterFeed", "Water/KOH feed", CapePortDirection.CAPE_INLET);
            AddMaterialPort("Hydrogen", "H2 product", CapePortDirection.CAPE_OUTLET);
            AddMaterialPort("Oxygen", "O2 product (+ excess water)", CapePortDirection.CAPE_OUTLET);
            AddRealParameter("CellArea", "Active cell area", 2.0, 0.001, 50, "m2");
            AddRealParameter("CurrentDensity", "Current density", 0.4, 0.05, 1.0, "A/cm2");
            AddIntParameter("CellCount", "Cells in stack", 100, 1, 2000);
            AddRealParameter("CellVoltage", "Cell voltage", 1.9, 1.23, 2.4, "V");
            AddRealParameter("KOHconc", "KOH concentration", 30, 10, 40, "wt%");
            AddRealParameter("FaradaicEff", "Faradaic efficiency", 99, 80, 100, "%");
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

            double current = R("CurrentDensity") * R("CellArea") * 1e4;
            double h2Faradaic = ProcessOps.FaradayMoles(current, 2, R("FaradaicEff") / 100.0) * I("CellCount");
            double h2Mol = Math.Min(h2Faradaic, f[wi] * 0.99);
            if (h2Mol < h2Faradaic * 0.999)
                ReportWarning(string.Format(
                    "Water-limited operation: the feed supplies water for only {0:0.####} mol/s H2 " +
                    "(the stack current could produce {1:0.####} mol/s). Increase the water feed.",
                    h2Mol, h2Faradaic));
            double o2Mol = h2Mol / 2.0;
            double excessWater = f[wi] - h2Mol;

            var h2F = new double[f.Length]; h2F[h2i] = h2Mol;
            var o2F = (double[])f.Clone();  // KOH and excess water leave with the O2/anolyte side
            o2F[wi] = Math.Max(0, excessWater); o2F[o2i] += o2Mol;
            h2.SetOutletTP(h2F, Tk, p);
            o2.SetOutletTP(o2F, Tk, p);

            double stackPowerKW = R("CellVoltage") * current * I("CellCount") / 1000.0;
            double h2KgS = h2Mol * 0.002016;
            Result("H2 production", h2KgS, "kg/s", "0.######");
            Result("Stack power", stackPowerKW, "kW", "0.##");
            Result("Specific energy", h2KgS > 0 ? stackPowerKW / (h2KgS * 3600.0) : 0, "kWh/kg H2", "0.##");
        }
    }

    /// <summary>OP-FC — PEM Fuel Cell.</summary>
    [ComVisible(true), Guid("47412507-fbf7-4694-9311-3320f0c7d07d"), ProgId("OPBlocks.FuelCell")]
    [CapeName("OP-FC"), CapeVersion("1.0"), CapeVendorURL("https://oneprocess.sim")]
    [CapeDescription("PEM fuel cell: H2 + air to electric power and water.")]
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
            AddRealParameter("CellVoltage", "Cell voltage", 0.68, 0.4, 1.0, "V");
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

            double util = R("Utilization") / 100.0;
            double h2Want = hf[h2i] * util;
            double o2Cons = Math.Min(h2Want / 2.0, af[o2i]);
            double h2Cons = o2Cons * 2.0;              // limited by available O2
            if (h2Cons < h2Want * 0.999)
                ReportWarning(string.Format(
                    "Air-limited operation: the air feed supplies oxygen for only {0:0.####} mol/s H2 " +
                    "(utilization asked for {1:0.####} mol/s). Increase the air flow.",
                    h2Cons, h2Want));
            if (hf[h2i] <= 1e-15)
                ReportWarning("The fuel feed carries no hydrogen flow — set the H2 amount in the HydrogenIn stream.");
            double waterProd = h2Cons;

            var exF = new double[hf.Length];
            for (int i = 0; i < exF.Length; i++) exF[i] = af[i] + (i < hf.Length ? hf[i] : 0);
            exF[h2i] -= h2Cons;
            exF[o2i] -= o2Cons;
            exF[wi] += waterProd;
            ex.SetOutletTP(exF, aIn.Temperature, aIn.Pressure);

            double current = h2Cons * 2.0 * ProcessOps.Faraday;   // A equivalent
            double powerKW = R("CellVoltage") * current / 1000.0;
            double lhvKW = h2Cons * 0.002016 * 120000.0;
            Result("Power output", powerKW, "kW", "0.##");
            Result("H2 consumed", h2Cons * 0.002016, "kg/s", "0.######");
            Result("Electrical efficiency (LHV)", lhvKW > 0 ? powerKW / lhvKW * 100 : 0, "%", "0.#");
            Result("Water produced", waterProd * 0.0180153, "kg/s", "0.#####");
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
            AddRealParameter("RotorSpeed", "Rotor speed", 1000, 100, 3000, "rpm");
            AddRealParameter("KlaCoeff", "k_La correlation constant", 0.02, 0.001, 0.2, "-");
            AddRealParameter("SoluteMW", "Absorbed solute molar mass", 44, 2, 200, "g/mol");
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
            int co2 = ProcessOps.IndexOf(ids, "CO2", "CARBON DIOXIDE");
            if (co2 < 0)   // fall back to most abundant non-water gas component
            {
                int wi = ProcessOps.IndexOf(ids, "WATER", "H2O"); double best = -1;
                for (int i = 0; i < gf.Length; i++) if (i != wi && gf[i] > best) { best = gf[i]; co2 = i; }
                ReportWarning("No CO2 in component list — absorbing the most abundant gas solute instead.");
            }

            double ntu = R("KlaCoeff") * Math.Sqrt(R("RotorSpeed"));
            double removalFrac = ProcessOps.Clamp01(1.0 - Math.Exp(-ntu));
            double absorbed = gf[co2] * removalFrac;

            var gof = (double[])gf.Clone(); gof[co2] -= absorbed;
            var lof = (double[])lf.Clone(); if (co2 < lof.Length) lof[co2] += absorbed;
            gOut.SetOutletTP(gof, gIn.Temperature, gIn.Pressure);
            lOut.SetOutletTP(lof, lIn.Temperature, lIn.Pressure);

            double rotorKW = 1e-6 * Math.Pow(R("RotorSpeed"), 2) * 0.5; // indicative rotor power
            Result("Removal", removalFrac * 100, "%", "0.##");
            Result("NTU", ntu, "-", "0.###");
            Result("Solute absorbed", absorbed * R("SoluteMW") / 1000.0, "kg/s", "0.#####");
            Result("Rotor power (indicative)", rotorKW, "kW", "0.###");
        }
    }

    /// <summary>OP-UVAOP — UV / advanced oxidation reactor.</summary>
    [ComVisible(true), Guid("71ab6cc5-95d4-4c8d-b45f-8623b9e47538"), ProgId("OPBlocks.UVAOP")]
    [CapeName("OP-UVAOP"), CapeVersion("1.0"), CapeVendorURL("https://oneprocess.sim")]
    [CapeDescription("UV / advanced oxidation reactor: UV(+H2O2) destruction of trace contaminants.")]
    [CapeAbout("ONE PROCESS Blocks — OP-UVAOP. (c) ONE PROCESS Simulation.")]
    [CapeConsumesThermo(true), CapeSupportsThermodynamics11(true)]
    public class UvAopReactor : UnitBase
    {
        public UvAopReactor() : base()
        {
            ComponentName = "OP-UVAOP"; ComponentDescription = "UV / Advanced Oxidation Reactor";
            AddMaterialPort("LiquidIn", "Liquid in", CapePortDirection.CAPE_INLET);
            AddMaterialPort("LiquidOut", "Treated liquid out", CapePortDirection.CAPE_OUTLET);
            AddRealParameter("UVDose", "UV dose", 800, 10, 5000, "mJ/cm2");
            AddRealParameter("RateConstant", "Dose-response k", 0.003, 1e-5, 0.1, "cm2/mJ");
            AddRealParameter("UVT", "UV transmittance (254 nm)", 90, 30, 99, "%");
            AddRealParameter("H2O2Dose", "H2O2 dose", 5, 0, 50, "mg/L");
            AddRealParameter("LampPower", "Total lamp power", 10, 0.1, 1000, "kW");
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
            // Effective dose scaled by UVT and boosted by H2O2 (radical) dosing.
            double h2o2Boost = 1.0 + R("H2O2Dose") / 20.0;
            double effDose = R("UVDose") * (R("UVT") / 100.0) * h2o2Boost;
            double destroyFrac = ProcessOps.Clamp01(1.0 - Math.Exp(-R("RateConstant") * effDose));

            var of = (double[])f.Clone(); double destroyed = 0;
            for (int i = 0; i < f.Length; i++)
                if (i != wi) { double m = f[i] * destroyFrac; of[i] -= m; destroyed += m; }
            lOut.SetOutletTP(of, lIn.Temperature, lIn.Pressure);

            double logRemoval = destroyFrac < 1 ? -Math.Log10(Math.Max(1e-6, 1 - destroyFrac)) : 6;
            double flowM3h = f[wi] * 0.0180153 / 1000.0 * 3600.0;
            double eeo = (flowM3h > 0 && logRemoval > 0) ? R("LampPower") / (flowM3h * logRemoval) : 0;
            Result("Contaminant destruction", destroyFrac * 100, "%", "0.##");
            Result("Log removal", logRemoval, "log", "0.##");
            Result("EEO", eeo, "kWh/m3/order", "0.###");
            Result("Lamp power", R("LampPower"), "kW", "0.##");
            ReportWarning("Destroyed contaminant is oxidised (mineralised); its stream flow is removed at the outlet.");
        }
    }
}
