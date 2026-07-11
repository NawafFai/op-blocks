using System;
using System.Runtime.InteropServices;
using CapeOpen;
using OPBlocks.Core;

namespace OPBlocks.Lithium
{
    // ===================================================================
    //  Family D — Lithium, Sorption & Precipitation
    // ===================================================================

    /// <summary>OP-DLE — Direct Lithium Extraction adsorption column (flagship).</summary>
    [ComVisible(true), Guid("56288974-1bd8-4fa2-b05d-d588a4035f14"), ProgId("OPBlocks.DLE")]
    [CapeName("OP-DLE"), CapeVersion("1.0"), CapeVendorURL("https://oneprocess.sim")]
    [CapeDescription("Direct lithium extraction: selective Li+ sorption column (Langmuir + LDF, cycle-averaged).")]
    [CapeAbout("ONE PROCESS Blocks — OP-DLE. (c) ONE PROCESS Simulation.")]
    [CapeConsumesThermo(true), CapeSupportsThermodynamics11(true)]
    public class DirectLithiumExtraction : UnitBase
    {
        public DirectLithiumExtraction() : base()
        {
            ComponentName = "OP-DLE"; ComponentDescription = "Direct Lithium Extraction Column";
            AddMaterialPort("BrineFeed", "Brine feed", CapePortDirection.CAPE_INLET);
            AddMaterialPort("TreatedBrine", "Li-depleted brine", CapePortDirection.CAPE_OUTLET);
            AddMaterialPort("Eluate", "Li-rich eluate (cycle-averaged)", CapePortDirection.CAPE_OUTLET);
            AddMaterialPort("WashWater", "Wash / strip water in", CapePortDirection.CAPE_INLET);
            AddRealParameter("QmaxLi", "Sorbent max Li loading q_max", 8.0, 0.5, 40, "mg/g");
            AddRealParameter("Klangmuir", "Langmuir affinity K", 0.05, 0.001, 5, "L/mg");
            AddRealParameter("kLDF", "LDF mass-transfer coefficient", 0.002, 1e-5, 1, "1/s");
            AddRealParameter("BedVolume", "Sorbent bed volume", 1000, 1, 1e6, "L");
            AddRealParameter("SorbentDensity", "Sorbent bulk density", 800, 100, 2000, "g/L");
            AddRealParameter("CycleTime", "Loading cycle time", 3600, 60, 86400, "s");
            AddRealParameter("MgLiSelectivity", "Mg/Li selectivity", 20, 1, 500, "-");
        }
        public override string BlockCode => "OP-DLE";

        protected override bool ValidateModel(out string message)
        {
            message = null;
            var f = GetConnectedMaterial("BrineFeed"); var w = GetConnectedMaterial("WashWater");
            if (f == null || w == null) { message = "Connect BrineFeed and WashWater."; return false; }
            if (ProcessOps.IndexOf(f.ComponentIds, "LICL", "LI+", "LITHIUM", "LI") < 0)
            { message = "Feed must contain a lithium species. Add LiCl / Li+ to the component list (or map it)."; return false; }
            return true;
        }

        protected override void Compute()
        {
            var feed = RequireMaterial("BrineFeed"); var treated = RequireMaterial("TreatedBrine");
            var eluate = RequireMaterial("Eluate"); var wash = RequireMaterial("WashWater");
            string[] ids = feed.ComponentIds;
            int wi = ProcessOps.IndexOf(ids, "WATER", "H2O");
            int li = ProcessOps.IndexOf(ids, "LICL", "LI+", "LITHIUM", "LI");
            int mg = ProcessOps.IndexOf(ids, "MGCL2", "MG+", "MAGNESIUM", "MG");
            double[] f = feed.GetOverallMoleFlows(); double[] wf = wash.GetOverallMoleFlows();
            double Tk = feed.Temperature, p = feed.Pressure;

            double feedLi = f[li];
            if (feedLi <= 1e-15)
                ReportWarning("The brine feed carries no lithium flow (the Li species is in the component list " +
                              "but its amount in the feed stream is zero). Set the Li fraction in the feed composition.");
            double C_mgL = ProcessOps.MolarityMolL(feedLi, wi >= 0 ? f[wi] : ProcessOps.Sum(f)) * 6941.0;
            double qStar = R("QmaxLi") * R("Klangmuir") * C_mgL / (1.0 + R("Klangmuir") * C_mgL); // mg/g
            double massG = R("BedVolume") * R("SorbentDensity");
            double capMol = qStar * massG / 6941.0;                 // Li mol capacity per cycle
            double feedPerCycle = feedLi * R("CycleTime");
            double captured = Math.Min(capMol, feedPerCycle * 0.99);
            double recovery = feedPerCycle > 0 ? captured / feedPerCycle : 0;
            double liCapturedRate = recovery * feedLi;              // mol/s

            double mgCapturedRate = 0;
            if (mg >= 0) { mgCapturedRate = f[mg] * (recovery / R("MgLiSelectivity")); }

            var tf = (double[])f.Clone(); tf[li] -= liCapturedRate; if (mg >= 0) tf[mg] -= mgCapturedRate;
            var ef = (double[])wf.Clone(); ef[li] += liCapturedRate; if (mg >= 0) ef[mg] += mgCapturedRate;
            treated.SetOutletTP(tf, Tk, p);
            eluate.SetOutletTP(ef, Tk, p);

            double eluWaterMol = wf[wi >= 0 ? wi : 0];
            double eluConc = ProcessOps.MolarityMolL(liCapturedRate, eluWaterMol) * 6941.0; // mg/L
            double prod = R("BedVolume") > 0 ? liCapturedRate * 0.006941 * 86400.0 / (R("BedVolume") / 1000.0) : 0;
            Result("Li recovery", recovery * 100, "%", "0.##");
            Result("Equilibrium loading q*", qStar, "mg/g", "0.###");
            Result("Eluate Li concentration", eluConc, "mg/L", "0.#");
            Result("Mg/Li selectivity", R("MgLiSelectivity"), "-", "0.#");
            Result("Productivity", prod, "kg Li/m3/day", "0.###");
            ReportWarning("Placeholder sorbent parameters — replace with ACM v5.3 defaults (dle_defaults.json) for calibration.");
        }
    }

    /// <summary>OP-SX — Solvent Extraction (mixer-settler cascade).</summary>
    [ComVisible(true), Guid("d51c7832-8220-4a2a-94d7-c3fcc59c5efa"), ProgId("OPBlocks.SX")]
    [CapeName("OP-SX"), CapeVersion("1.0"), CapeVendorURL("https://oneprocess.sim")]
    [CapeDescription("Solvent extraction: counter-current liquid-liquid extraction (Kremser cascade).")]
    [CapeAbout("ONE PROCESS Blocks — OP-SX. (c) ONE PROCESS Simulation.")]
    [CapeConsumesThermo(true), CapeSupportsThermodynamics11(true)]
    public class SolventExtraction : UnitBase
    {
        public SolventExtraction() : base()
        {
            ComponentName = "OP-SX"; ComponentDescription = "Solvent Extraction (Mixer-Settler)";
            AddMaterialPort("AqueousIn", "Aqueous feed in", CapePortDirection.CAPE_INLET);
            AddMaterialPort("AqueousOut", "Raffinate out", CapePortDirection.CAPE_OUTLET);
            AddMaterialPort("OrganicIn", "Organic (solvent) in", CapePortDirection.CAPE_INLET);
            AddMaterialPort("OrganicOut", "Loaded organic out", CapePortDirection.CAPE_OUTLET);
            AddRealParameter("DistributionCoeff", "Distribution coefficient D", 5, 0.01, 1000, "-");
            AddIntParameter("Stages", "Number of stages", 3, 1, 20);
            AddRealParameter("OAratio", "Organic/Aqueous flow ratio", 1.0, 0.05, 20, "-");
            AddRealParameter("StageEfficiency", "Stage (Murphree) efficiency", 90, 20, 100, "%");
        }
        public override string BlockCode => "OP-SX";

        protected override bool ValidateModel(out string message)
        {
            message = null;
            var a = GetConnectedMaterial("AqueousIn"); var o = GetConnectedMaterial("OrganicIn");
            if (a == null || o == null) { message = "Connect both AqueousIn and OrganicIn."; return false; }
            return true;
        }

        protected override void Compute()
        {
            var aIn = RequireMaterial("AqueousIn"); var aOut = RequireMaterial("AqueousOut");
            var oIn = RequireMaterial("OrganicIn"); var oOut = RequireMaterial("OrganicOut");
            int wi = ProcessOps.IndexOf(aIn.ComponentIds, "WATER", "H2O");
            double[] af = aIn.GetOverallMoleFlows(); double[] of = oIn.GetOverallMoleFlows();

            double E = R("DistributionCoeff") * R("OAratio");                    // extraction factor
            int N = I("Stages");
            double kremser = Math.Abs(E - 1.0) < 1e-6
                ? N / (double)(N + 1)
                : (Math.Pow(E, N + 1) - E) / (Math.Pow(E, N + 1) - 1.0);
            double extractFrac = ProcessOps.Clamp01(kremser * R("StageEfficiency") / 100.0);

            var aof = (double[])af.Clone(); var oof = (double[])of.Clone();
            double extracted = 0;
            for (int i = 0; i < af.Length; i++)
                if (i != wi) { double m = af[i] * extractFrac; aof[i] -= m; if (i < oof.Length) oof[i] += m; extracted += m; }
            aOut.SetOutletTP(aof, aIn.Temperature, aIn.Pressure);
            oOut.SetOutletTP(oof, oIn.Temperature, oIn.Pressure);

            Result("Extraction efficiency", extractFrac * 100, "%", "0.##");
            Result("Extraction factor E", E, "-", "0.###");
            Result("Solute extracted", extracted, "mol/s", "0.#####");
        }
    }

    /// <summary>OP-GAC — Activated Carbon Adsorption column.</summary>
    [ComVisible(true), Guid("7720a360-e026-4976-8f81-b8db159f8b8a"), ProgId("OPBlocks.GAC")]
    [CapeName("OP-GAC"), CapeVersion("1.0"), CapeVendorURL("https://oneprocess.sim")]
    [CapeDescription("Granular activated carbon adsorption: micropollutant removal (Freundlich isotherm, EBCT).")]
    [CapeAbout("ONE PROCESS Blocks — OP-GAC. (c) ONE PROCESS Simulation.")]
    [CapeConsumesThermo(true), CapeSupportsThermodynamics11(true)]
    public class ActivatedCarbon : UnitBase
    {
        public ActivatedCarbon() : base()
        {
            ComponentName = "OP-GAC"; ComponentDescription = "Activated Carbon Adsorption";
            AddMaterialPort("Feed", "Feed", CapePortDirection.CAPE_INLET);
            AddMaterialPort("Treated", "Treated water", CapePortDirection.CAPE_OUTLET);
            AddRealParameter("FreundlichK", "Freundlich K", 20, 0.1, 1000, "(mg/g)(L/mg)^1/n");
            AddRealParameter("FreundlichN", "Freundlich 1/n exponent inverse n", 2.0, 1, 6, "-");
            AddRealParameter("BedMass", "GAC bed mass", 1000, 1, 1e6, "kg");
            AddRealParameter("EBCT", "Empty-bed contact time", 15, 1, 120, "min");
        }
        public override string BlockCode => "OP-GAC";

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
            var feed = RequireMaterial("Feed"); var treated = RequireMaterial("Treated");
            int wi = ProcessOps.IndexOf(feed.ComponentIds, "WATER", "H2O");
            double[] f = feed.GetOverallMoleFlows();
            double removalFrac = ProcessOps.Clamp01(1.0 - Math.Exp(-(R("FreundlichK") / 40.0) * Math.Pow(R("EBCT"), 1.0 / R("FreundlichN"))));

            var tf = (double[])f.Clone(); double adsorbed = 0;
            for (int i = 0; i < f.Length; i++)
                if (i != wi) { double m = f[i] * removalFrac; tf[i] -= m; adsorbed += m; }
            treated.SetOutletTP(tf, feed.Temperature, feed.Pressure);

            double logRemoval = removalFrac < 1 ? -Math.Log10(Math.Max(1e-6, 1 - removalFrac)) : 6;
            Result("Contaminant removal", removalFrac * 100, "%", "0.##");
            Result("Log removal", logRemoval, "log", "0.##");
            Result("Adsorbed load", adsorbed, "mol/s", "0.#####");
            ReportWarning("Adsorbed contaminant is retained on the carbon bed (not exported as a stream).");
        }
    }

    /// <summary>OP-CRYST — Crystallizer / salt precipitation.</summary>
    [ComVisible(true), Guid("e66a4bc8-e322-419e-befd-ddabf14310cc"), ProgId("OPBlocks.Cryst")]
    [CapeName("OP-CRYST"), CapeVersion("1.0"), CapeVendorURL("https://oneprocess.sim")]
    [CapeDescription("Crystallizer: cooling/evaporative crystallization of salt from supersaturated brine.")]
    [CapeAbout("ONE PROCESS Blocks — OP-CRYST. (c) ONE PROCESS Simulation.")]
    [CapeConsumesThermo(true), CapeSupportsThermodynamics11(true)]
    public class Crystallizer : UnitBase
    {
        public Crystallizer() : base()
        {
            ComponentName = "OP-CRYST"; ComponentDescription = "Crystallizer / Salt Precipitation";
            AddMaterialPort("Feed", "Feed", CapePortDirection.CAPE_INLET);
            AddMaterialPort("MotherLiquor", "Mother liquor out", CapePortDirection.CAPE_OUTLET);
            AddMaterialPort("Crystals", "Crystal product out", CapePortDirection.CAPE_OUTLET);
            AddRealParameter("Yield", "Crystallization yield of target salt", 60, 1, 99, "%");
            AddRealParameter("Temperature", "Crystallizer temperature", 25, -20, 120, "C");
            AddRealParameter("ResidenceTime", "Residence time", 60, 1, 600, "min");
        }
        public override string BlockCode => "OP-CRYST";

        protected override bool ValidateModel(out string message)
        {
            message = null;
            var f = GetConnectedMaterial("Feed");
            if (f == null) { message = "Connect a feed stream."; return false; }
            int wi = ProcessOps.IndexOf(f.ComponentIds, "WATER", "H2O");
            if (f.ComponentCount - (wi >= 0 ? 1 : 0) < 1) { message = "Feed must contain a dissolved salt to crystallize."; return false; }
            return true;
        }

        protected override void Compute()
        {
            var feed = RequireMaterial("Feed"); var liquor = RequireMaterial("MotherLiquor"); var crystals = RequireMaterial("Crystals");
            string[] ids = feed.ComponentIds;
            int wi = ProcessOps.IndexOf(ids, "WATER", "H2O");
            double[] f = feed.GetOverallMoleFlows();
            // Target salt = most abundant non-water component.
            int salt = -1; double best = -1;
            for (int i = 0; i < f.Length; i++) if (i != wi && f[i] > best) { best = f[i]; salt = i; }

            double cryst = f[salt] * R("Yield") / 100.0;
            var lf = (double[])f.Clone(); lf[salt] -= cryst;
            var cf = new double[f.Length]; cf[salt] = cryst;
            liquor.SetOutletTP(lf, R("Temperature") + 273.15, feed.Pressure);
            crystals.SetOutletTP(cf, R("Temperature") + 273.15, feed.Pressure);

            Result("Crystal production", cryst, "mol/s", "0.#####");
            Result("Yield", R("Yield"), "%", "0.#");
            Result("Crystallizer temperature", R("Temperature"), "C", "0.#");
        }
    }

    /// <summary>OP-PPT — Chemical precipitation reactor.</summary>
    [ComVisible(true), Guid("d820ba95-3e24-4e85-81ef-6e8df1f503c3"), ProgId("OPBlocks.PPT")]
    [CapeName("OP-PPT"), CapeVersion("1.0"), CapeVendorURL("https://oneprocess.sim")]
    [CapeDescription("Chemical precipitation reactor: reagent dosing to precipitate hardness / metals as sludge.")]
    [CapeAbout("ONE PROCESS Blocks — OP-PPT. (c) ONE PROCESS Simulation.")]
    [CapeConsumesThermo(true), CapeSupportsThermodynamics11(true)]
    public class ChemicalPrecipitation : UnitBase
    {
        public ChemicalPrecipitation() : base()
        {
            ComponentName = "OP-PPT"; ComponentDescription = "Chemical Precipitation Reactor";
            AddMaterialPort("Feed", "Feed", CapePortDirection.CAPE_INLET);
            AddMaterialPort("Reagent", "Precipitant reagent in", CapePortDirection.CAPE_INLET);
            AddMaterialPort("Treated", "Treated water out", CapePortDirection.CAPE_OUTLET);
            AddMaterialPort("Sludge", "Precipitated sludge out", CapePortDirection.CAPE_OUTLET);
            AddRealParameter("RemovalEfficiency", "Target species removal", 90, 10, 99.9, "%");
            AddRealParameter("ReagentDose", "Reagent stoichiometric dose", 1.1, 0.5, 5, "mol/mol");
            AddRealParameter("pH", "Operating pH", 10.5, 4, 13, "-");
        }
        public override string BlockCode => "OP-PPT";

        protected override bool ValidateModel(out string message)
        {
            message = null;
            var f = GetConnectedMaterial("Feed"); var r = GetConnectedMaterial("Reagent");
            if (f == null || r == null) { message = "Connect both Feed and Reagent."; return false; }
            return true;
        }

        protected override void Compute()
        {
            var feed = RequireMaterial("Feed"); var reagent = RequireMaterial("Reagent");
            var treated = RequireMaterial("Treated"); var sludge = RequireMaterial("Sludge");
            int wi = ProcessOps.IndexOf(feed.ComponentIds, "WATER", "H2O");
            double[] f = feed.GetOverallMoleFlows(); double[] rf = reagent.GetOverallMoleFlows();
            double eff = R("RemovalEfficiency") / 100.0;

            // Treated = feed (solutes reduced) + reagent carrier water; Sludge = removed solutes.
            var tf = (double[])f.Clone(); var sf = new double[f.Length];
            double removed = 0;
            for (int i = 0; i < f.Length; i++)
                if (i != wi) { double m = f[i] * eff; tf[i] -= m; sf[i] += m; removed += m; }
            if (wi >= 0 && wi < rf.Length) tf[wi] += rf[wi]; // reagent water joins treated
            treated.SetOutletTP(tf, feed.Temperature, feed.Pressure);
            if (ProcessOps.Sum(sf) > 1e-30) sludge.SetOutletTP(sf, feed.Temperature, feed.Pressure);

            Result("Target removal", R("RemovalEfficiency"), "%", "0.#");
            Result("Precipitate formed", removed, "mol/s", "0.#####");
            Result("Reagent consumption", ProcessOps.Sum(rf), "mol/s", "0.#####");
            Result("Operating pH", R("pH"), "-", "0.#");
        }
    }
}
