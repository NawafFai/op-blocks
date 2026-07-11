using System;
using System.Runtime.InteropServices;
using CapeOpen;
using OPBlocks.Core;

namespace OPBlocks.Electro
{
    // ===================================================================
    //  Family C — Electrochemical & Ion Separation
    // ===================================================================

    /// <summary>OP-ED — Electrodialysis stack.</summary>
    [ComVisible(true), Guid("dba4d883-276c-4937-82d4-444c5f4d499a"), ProgId("OPBlocks.ED")]
    [CapeName("OP-ED"), CapeVersion("1.0"), CapeVendorURL("https://oneprocess.sim")]
    [CapeDescription("Electrodialysis: ion transport diluate→concentrate under a DC field (cell-pair model).")]
    [CapeAbout("ONE PROCESS Blocks — OP-ED. (c) ONE PROCESS Simulation.")]
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
            AddIntParameter("CellPairs", "Number of cell pairs", 100, 1, 1000);
            AddRealParameter("AppliedVoltage", "Stack voltage", 24, 1, 500, "V");
            AddRealParameter("StackResistance", "Stack resistance", 5, 0.1, 500, "ohm");
            AddRealParameter("CurrentEfficiency", "Current efficiency", 90, 20, 100, "%");
            AddRealParameter("IonValence", "Ion valence z", 1, 1, 3, "-");
            AddRealParameter("WaterTransport", "Water transport number", 8, 0, 30, "mol H2O/mol");
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

        protected override void Compute()
        {
            var dIn = RequireMaterial("DiluateIn"); var dOut = RequireMaterial("DiluateOut");
            var cIn = RequireMaterial("ConcentrateIn"); var cOut = RequireMaterial("ConcentrateOut");
            int wi = ProcessOps.IndexOf(dIn.ComponentIds, "WATER", "H2O");
            double[] df = dIn.GetOverallMoleFlows(); double[] cf = cIn.GetOverallMoleFlows();
            double Td = dIn.Temperature, Tc = cIn.Temperature, Pd = dIn.Pressure, Pc = cIn.Pressure;

            double current = R("AppliedVoltage") / R("StackResistance");
            double eff = R("CurrentEfficiency") / 100.0;
            double saltMove = ProcessOps.FaradayMoles(current, (int)Math.Round(R("IonValence")), eff) * I("CellPairs");
            double saltDil = ProcessOps.Sum(df) - df[wi];
            saltMove = Math.Min(saltMove, saltDil * 0.98);
            double waterMove = Math.Min(saltMove * R("WaterTransport"), df[wi] * 0.5);

            var dof = (double[])df.Clone(); var cof = (double[])cf.Clone();
            for (int i = 0; i < df.Length; i++)
            {
                if (i == wi) { dof[i] -= waterMove; cof[i] += waterMove; }
                else if (saltDil > 0) { double m = saltMove * df[i] / saltDil; dof[i] -= m; cof[i] += m; }
            }
            dOut.SetOutletTP(dof, Td, Pd);
            cOut.SetOutletTP(cof, Tc, Pc);

            double powerKW = R("AppliedVoltage") * current / 1000.0;
            double dilM3h = df[wi] * 0.0180153 / 1000.0 * 3600.0;
            Result("Salt removed", saltMove, "mol/s", "0.####");
            Result("Salt removal", saltDil > 0 ? saltMove / saltDil * 100 : 0, "%", "0.#");
            Result("Stack current", current, "A", "0.##");
            Result("Specific energy consumption", dilM3h > 0 ? powerKW / dilM3h : 0, "kWh/m3", "0.###");
            if (saltMove >= saltDil * 0.97) ReportWarning("Near limiting current — diluate nearly depleted; check current density.");
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
            double cl2Mol = ProcessOps.FaradayMoles(R("Current"), 2, eff);   // 2 e- per Cl2
            double h2Mol = cl2Mol;                                            // 1 H2 per Cl2
            double naohMol = 2 * cl2Mol;                                      // 2 NaOH per Cl2
            double naclCons = Math.Min(2 * cl2Mol, f[naclI] * 0.99);          // 2 NaCl per Cl2
            double waterMove = naohMol * R("WaterTransport");

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
