using System;
using System.Runtime.InteropServices;
using CapeOpen;
using OPBlocks.Core;

namespace OPBlocks.Lithium
{
    // ===================================================================
    //  Family D — Lithium, Sorption & Precipitation (factory grade)
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

            AddRealParameter("QmaxLi", "Sorbent max Li loading q_max (LMO/LLTO sorbents 5-40)", 8.0, 0.5, 40, "mg/g");
            AddRealParameter("Klangmuir", "Langmuir affinity K", 0.05, 0.001, 5, "L/mg");
            AddRealParameter("kLDF", "LDF mass-transfer coefficient", 0.002, 1e-5, 1, "1/s");
            AddRealParameter("BedVolume", "Sorbent bed volume", 1000, 1, 1e6, "L");
            AddRealParameter("SorbentDensity", "Sorbent bulk density", 800, 100, 2000, "g/L");
            AddRealParameter("CycleTime", "Loading cycle time", 3600, 60, 86400, "s");
            AddRealParameter("MgLiSelectivity", "Mg/Li selectivity S (sorbents 20-500)", 20, 1, 500, "-");

            AddOutputParameter("LiRecovery", "Li recovery", "%");
            AddOutputParameter("Qstar", "Langmuir equilibrium loading q*", "mg/g");
            AddOutputParameter("Qcycle", "Cycle loading (after LDF approach)", "mg/g");
            AddOutputParameter("LiCaptured", "Li captured", "mol/s");
            AddOutputParameter("EluateLiConc", "Eluate Li concentration", "mg/L");
            AddOutputParameter("MgCaptured", "Mg co-captured", "mol/s");
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

            if (f[li] <= 1e-15)
                ReportWarning("The brine feed carries no lithium flow (the Li species is in the component list " +
                              "but its amount in the feed stream is zero). Set the Li fraction in the feed composition.");

            var spec = new DleModel.Spec
            {
                QmaxMgG = R("QmaxLi"),
                KlangLmg = R("Klangmuir"),
                KldfPerS = R("kLDF"),
                BedVolumeL = R("BedVolume"),
                SorbentGL = R("SorbentDensity"),
                CycleTimeS = R("CycleTime"),
                MgLiSelectivity = R("MgLiSelectivity"),
            };
            DleModel.Perf x = DleModel.Solve(spec, f[li], mg >= 0 ? f[mg] : 0.0,
                                             wi >= 0 ? f[wi] : ProcessOps.Sum(f));

            if (x.CapacityLimited && f[li] > 1e-15)
                ReportWarning(string.Format(
                    "Sorbent-limited cycle: the bed holds {0:0.###} mg/g this cycle while the feed delivers more Li " +
                    "than that capacity — recovery is {1:0.#}%. Enlarge the bed or shorten the cycle.",
                    x.QcycleMgG, x.RecoveryFrac * 100));
            if (x.LdfApproach < 0.5)
                ReportWarning(string.Format(
                    "The LDF approach to equilibrium is only {0:0.#}% over this cycle time — kinetics dominate. " +
                    "Longer cycles or a faster sorbent (higher k_LDF) would use the capacity better.", x.LdfApproach * 100));

            var tf = (double[])f.Clone(); tf[li] -= x.CapturedLiMolS; if (mg >= 0) tf[mg] -= x.MgCapturedMolS;
            var ef = (double[])wf.Clone(); ef[li] += x.CapturedLiMolS; if (mg >= 0) ef[mg] += x.MgCapturedMolS;
            treated.SetOutletTP(tf, Tk, p);
            eluate.SetOutletTP(ef, Tk, p);

            double eluWater = wf[wi >= 0 ? wi : 0];
            double eluConc = ProcessOps.MolarityMolL(x.CapturedLiMolS, eluWater) * DleModel.MwLiGmol * 1000.0;

            Result("Li recovery", x.RecoveryFrac * 100, "%", "0.##");
            Result("Equilibrium loading q*", x.QstarMgG, "mg/g", "0.###");
            Result("Cycle loading", x.QcycleMgG, "mg/g", "0.###");
            Result("Li captured", x.CapturedLiMolS, "mol/s", "0.######");
            Result("Eluate Li concentration", eluConc, "mg/L", "0.#");
            Result("Mg co-captured", x.MgCapturedMolS, "mol/s", "0.######");

            SetOutputParameter("LiRecovery", x.RecoveryFrac * 100);
            SetOutputParameter("Qstar", x.QstarMgG);
            SetOutputParameter("Qcycle", x.QcycleMgG);
            SetOutputParameter("LiCaptured", x.CapturedLiMolS);
            SetOutputParameter("EluateLiConc", eluConc);
            SetOutputParameter("MgCaptured", x.MgCapturedMolS);
        }

        protected override string BuildReport()
        {
            var sb = new System.Text.StringBuilder(base.BuildReport());
            sb.AppendLine();
            sb.AppendLine("Model & References");
            sb.AppendLine("  Langmuir equilibrium:  q* = q_max * K * C / (1 + K * C)");
            sb.AppendLine("    (half-loading exactly at C = 1/K).");
            sb.AppendLine("  Glueckauf LDF approach:  q = q* * (1 - exp(-k_LDF * t_cycle)).");
            sb.AppendLine("  Captured Li/cycle = q * m_bed / MW_Li, cycle-averaged, capped by feed.");
            sb.AppendLine("  Mg co-sorption: Mg_captured = Mg_feed * (Li_recovery / S_MgLi).");
            sb.AppendLine("  Thermo from the selected Property Package.");
            sb.AppendLine("  Validity: LMO/LLTO-class sorbents q_max 5-40 mg/g, S_MgLi 20-500.");
            sb.AppendLine("  Refs: Langmuir, JACS 40 (1918) 1361-1403;");
            sb.AppendLine("        Glueckauf, Trans. Faraday Soc. 51 (1955) 1540-1551;");
            sb.AppendLine("        Battistel et al., Adv. Mater. 32 (2020) 1905440.");
            return sb.ToString();
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

            AddRealParameter("DistributionCoeff", "Distribution coefficient D = C_org/C_aq", 5, 0.01, 1000, "-");
            // golden rule: stage count as a REAL integer code
            AddRealParameter("Stages", "Number of counter-current stages (integer code)", 3, 1, 20, "-");
            AddRealParameter("OAratio", "Organic/Aqueous flow ratio", 1.0, 0.05, 20, "-");
            AddRealParameter("StageEfficiency", "Stage (Murphree) efficiency", 90, 20, 100, "%");

            AddOutputParameter("Extraction", "Extraction efficiency", "%");
            AddOutputParameter("ExtFactor", "Extraction factor E = D*(O/A)", "-");
            AddOutputParameter("SoluteExtracted", "Solute extracted", "mol/s");
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

            var spec = new SxModel.Spec
            {
                DistCoeff = R("DistributionCoeff"),
                Stages = R("Stages"),
                OAratio = R("OAratio"),
                StageEffPct = R("StageEfficiency"),
            };
            double e = spec.DistCoeff * spec.OAratio;
            double extractFrac = SxModel.ExtractedFraction(spec);

            if (e < 1.0)
                ReportWarning(string.Format(
                    "Extraction factor E = D*(O/A) = {0:0.###} is below 1 — the Kremser cascade cannot approach " +
                    "complete extraction no matter how many stages; raise the O/A ratio or use a stronger extractant.", e));
            if (ProcessOps.Sum(af) - (wi >= 0 ? af[wi] : 0) <= 1e-15)
                ReportWarning("The aqueous feed carries no solute — nothing to extract. Set the solute flow in AqueousIn.");

            var aof = (double[])af.Clone(); var oof = (double[])of.Clone();
            double extracted = 0;
            for (int i = 0; i < af.Length; i++)
                if (i != wi) { double m = af[i] * extractFrac; aof[i] -= m; if (i < oof.Length) oof[i] += m; extracted += m; }
            aOut.SetOutletTP(aof, aIn.Temperature, aIn.Pressure);
            oOut.SetOutletTP(oof, oIn.Temperature, oIn.Pressure);

            Result("Extraction efficiency", extractFrac * 100, "%", "0.##");
            Result("Extraction factor E", e, "-", "0.###");
            Result("Solute extracted", extracted, "mol/s", "0.######");

            SetOutputParameter("Extraction", extractFrac * 100);
            SetOutputParameter("ExtFactor", e);
            SetOutputParameter("SoluteExtracted", extracted);
        }

        protected override string BuildReport()
        {
            var sb = new System.Text.StringBuilder(base.BuildReport());
            sb.AppendLine();
            sb.AppendLine("Model & References");
            sb.AppendLine("  Counter-current Kremser cascade:");
            sb.AppendLine("    E = D * (O/A);   f = (E^(N+1) - E) / (E^(N+1) - 1)");
            sb.AppendLine("    f = N/(N+1) exactly at E = 1;  times the Murphree stage efficiency.");
            sb.AppendLine("  Exact anchor pinned by test: E=2, N=3 -> f = 14/15.");
            sb.AppendLine("  E < 1 flagged: complete extraction unreachable at any stage count.");
            sb.AppendLine("  Thermo from the selected Property Package.");
            sb.AppendLine("  Refs: Kremser, Natl. Petroleum News 22 (1930) 43-49;");
            sb.AppendLine("        Seader, Henley & Roper, Separation Process Principles 3e (2011) ch.5;");
            sb.AppendLine("        Treybal, Mass-Transfer Operations 3e (1980) ch.10.");
            return sb.ToString();
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

            AddRealParameter("FreundlichK", "Freundlich K_F", 20, 0.1, 1000, "(mg/g)(L/mg)^1/n");
            AddRealParameter("FreundlichInvN", "Freundlich exponent 1/n (favourable 0.2-0.8)", 0.5, 0.1, 1.0, "-");
            AddRealParameter("BedMass", "GAC bed mass", 1000, 1, 1e6, "kg");
            AddRealParameter("EBCT", "Empty-bed contact time (drinking water 5-30)", 15, 1, 120, "min");
            AddRealParameter("TauContact", "Characteristic removal time tau", 6, 0.5, 60, "min");

            AddOutputParameter("Removal", "Contaminant removal", "%");
            AddOutputParameter("LogRemoval", "Log removal", "log");
            AddOutputParameter("AdsorbedLoad", "Adsorbed load", "mol/s");
            AddOutputParameter("CUR", "Carbon usage rate", "g/L");
            AddOutputParameter("BedLife", "Bed life at this load", "days");
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

            double[] mw;
            bool haveMw = feed.TryGetMolecularWeightsGmol(out mw);

            // feed contaminant concentration (all non-water species) in mg/L
            double soluteMgS = 0, soluteMolS = 0;
            for (int i = 0; i < f.Length; i++)
                if (i != wi) { soluteMolS += f[i]; if (haveMw) soluteMgS += f[i] * mw[i] * 1000.0; }
            double mF, rhoF, feedM3s;
            if (feed.TryGetTotalMassFlowKgS(out mF) && mF > 1e-30 && feed.TryGetMassDensityKgM3(out rhoF))
                feedM3s = mF / rhoF;
            else
                feedM3s = (wi >= 0 ? f[wi] : 0) * 0.0180153 / 1000.0;
            double c0MgL = feedM3s > 1e-30 && haveMw ? soluteMgS / (feedM3s * 1000.0) : 0.0;

            if (soluteMolS <= 1e-15)
                ReportWarning("The feed carries no contaminant besides water — nothing to adsorb. " +
                              "Set the micropollutant flow in the feed composition.");
            if (!haveMw)
                ReportWarning("The property package did not supply molecular weights — the carbon-usage-rate and " +
                              "bed-life results are unavailable this run.");

            var spec = new GacModel.Spec
            {
                FreundlichK = R("FreundlichK"),
                FreundlichInvN = R("FreundlichInvN"),
                BedMassKg = R("BedMass"),
                EbctMin = R("EBCT"),
                TauMin = R("TauContact"),
            };
            GacModel.Perf x = GacModel.Solve(spec, c0MgL, feedM3s);

            if (x.BedLifeDays > 0 && x.BedLifeDays < 30)
                ReportWarning(string.Format(
                    "Bed life ({0:0.#} days) is short — the contaminant load is heavy for this bed mass; " +
                    "GAC replacement/regeneration will dominate the operating cost.", x.BedLifeDays));

            var tf = (double[])f.Clone(); double adsorbed = 0;
            for (int i = 0; i < f.Length; i++)
                if (i != wi) { double m = f[i] * x.RemovalFrac; tf[i] -= m; adsorbed += m; }
            treated.SetOutletTP(tf, feed.Temperature, feed.Pressure);
            ReportWarning("Adsorbed contaminant is retained on the carbon bed (not exported as a stream).");

            Result("Contaminant removal", x.RemovalFrac * 100, "%", "0.##");
            Result("Log removal", x.LogRemoval, "log", "0.##");
            Result("Adsorbed load", adsorbed, "mol/s", "0.######");
            if (haveMw)
            {
                Result("Carbon usage rate", x.CurGL, "g/L", "0.####");
                Result("Bed life", x.BedLifeDays, "days", "0.#");
            }

            SetOutputParameter("Removal", x.RemovalFrac * 100);
            SetOutputParameter("LogRemoval", x.LogRemoval);
            SetOutputParameter("AdsorbedLoad", adsorbed);
            SetOutputParameter("CUR", x.CurGL);
            SetOutputParameter("BedLife", x.BedLifeDays);
        }

        protected override string BuildReport()
        {
            var sb = new System.Text.StringBuilder(base.BuildReport());
            sb.AppendLine();
            sb.AppendLine("Model & References");
            sb.AppendLine("  Freundlich capacity at the feed concentration:");
            sb.AppendLine("    q0 = K_F * C0^(1/n)   (q0 = K_F exactly at C0 = 1 mg/L)");
            sb.AppendLine("  Removal over the bed:  R = 1 - exp(-EBCT / tau).");
            sb.AppendLine("  Carbon usage rate (Crittenden):  CUR = (C0 - Ce)/q0 [g/L];");
            sb.AppendLine("    bed life = m_bed / (CUR * Q).");
            sb.AppendLine("  Adsorbed contaminant is retained on the bed (no sludge stream).");
            sb.AppendLine("  Thermo from the selected Property Package.");
            sb.AppendLine("  Validity: trace organics, favourable isotherms (1/n 0.2-0.8), EBCT 5-30 min.");
            sb.AppendLine("  Refs: Freundlich, Z. Phys. Chem. 57 (1906) 385-470;");
            sb.AppendLine("        Crittenden et al. (MWH), Water Treatment 3e (2012) ch.15;");
            sb.AppendLine("        Sontheimer, Crittenden & Summers, Activated Carbon for Water Treatment 2e (1988).");
            return sb.ToString();
        }
    }

    /// <summary>OP-CRYST — Crystallizer / salt precipitation.</summary>
    [ComVisible(true), Guid("e66a4bc8-e322-419e-befd-ddabf14310cc"), ProgId("OPBlocks.Cryst")]
    [CapeName("OP-CRYST"), CapeVersion("1.0"), CapeVendorURL("https://oneprocess.sim")]
    [CapeDescription("Crystallizer: solubility-limited crystal yield with optional solvent evaporation.")]
    [CapeAbout("ONE PROCESS Blocks — OP-CRYST. (c) ONE PROCESS Simulation.")]
    [CapeConsumesThermo(true), CapeSupportsThermodynamics11(true)]
    public class Crystallizer : UnitBase
    {
        public Crystallizer() : base()
        {
            ComponentName = "OP-CRYST"; ComponentDescription = "Crystallizer / Salt Precipitation";
            AddMaterialPort("Feed", "Feed", CapePortDirection.CAPE_INLET);
            AddMaterialPort("MotherLiquor", "Mother liquor out (saturated)", CapePortDirection.CAPE_OUTLET);
            AddMaterialPort("Crystals", "Crystal product out", CapePortDirection.CAPE_OUTLET);

            AddRealParameter("Solubility", "Solubility of the target salt at T (NaCl 36.0 at 25 C)", 36.0, 0.01, 500, "g/100g water");
            AddRealParameter("EvapFrac", "Fraction of feed water evaporated (0 = cooling crystallizer)", 0, 0, 0.95, "-");
            AddRealParameter("Temperature", "Crystallizer temperature", 25, -20, 120, "C");

            AddOutputParameter("CrystalProd", "Crystal production", "mol/s");
            AddOutputParameter("Yield", "Crystallization yield", "%");
            AddOutputParameter("MLSaltConc", "Mother-liquor salt held at saturation", "mol/s");
            AddOutputParameter("VaporWater", "Water evaporated", "mol/s");
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
            double tOutK = R("Temperature") + 273.15;

            // target salt = most abundant non-water component
            int salt = -1; double best = -1;
            for (int i = 0; i < f.Length; i++) if (i != wi && f[i] > best) { best = f[i]; salt = i; }

            double[] mw;
            bool haveMw = feed.TryGetMolecularWeightsGmol(out mw);
            double saltMw = haveMw && salt >= 0 ? mw[salt] : 58.44;
            double waterMw = haveMw && wi >= 0 ? mw[wi] : 18.0153;
            if (!haveMw)
                ReportWarning("The property package did not supply molecular weights — NaCl/water values assumed " +
                              "for the solubility balance.");

            var spec = new CrystModel.Spec
            {
                SolubilityG100g = R("Solubility"),
                EvapFrac = R("EvapFrac"),
                TempC = R("Temperature"),
            };
            CrystModel.Perf x = CrystModel.Solve(spec, salt >= 0 ? f[salt] : 0.0, saltMw,
                                                 wi >= 0 ? f[wi] : 0.0, waterMw);

            if (x.Undersaturated)
                ReportWarning(string.Format(
                    "The feed is UNDERSATURATED at {0:0.#} C (solubility {1:0.#} g/100g) — nothing crystallizes. " +
                    "Cool further, evaporate more water (EvapFrac), or feed a stronger brine.",
                    spec.TempC, spec.SolubilityG100g));
            if (spec.EvapFrac > 0 && x.VaporWaterMolS > 0)
                ReportWarning(string.Format(
                    "{0:0.####} mol/s of water leaves as vapour (EvapFrac = {1:0.##}); it is removed from the " +
                    "liquid streams — close the balance with a condenser block if the vapour matters downstream.",
                    x.VaporWaterMolS, spec.EvapFrac));

            var lf = (double[])f.Clone();
            if (salt >= 0) lf[salt] -= x.CrystalMolS;
            if (wi >= 0) lf[wi] -= x.VaporWaterMolS;
            var cf = new double[f.Length]; if (salt >= 0) cf[salt] = x.CrystalMolS;
            liquor.SetOutletTP(lf, tOutK, feed.Pressure);
            crystals.SetOutletTP(cf, tOutK, feed.Pressure);

            Result("Crystal production", x.CrystalMolS, "mol/s", "0.######");
            Result("Crystallization yield", x.YieldFrac * 100, "%", "0.##");
            Result("Salt held at saturation", x.SaturationMolS, "mol/s", "0.######");
            Result("Water evaporated", x.VaporWaterMolS, "mol/s", "0.######");

            SetOutputParameter("CrystalProd", x.CrystalMolS);
            SetOutputParameter("Yield", x.YieldFrac * 100);
            SetOutputParameter("MLSaltConc", x.SaturationMolS);
            SetOutputParameter("VaporWater", x.VaporWaterMolS);
        }

        protected override string BuildReport()
        {
            var sb = new System.Text.StringBuilder(base.BuildReport());
            sb.AppendLine();
            sb.AppendLine("Model & References");
            sb.AppendLine("  Solubility-limited yield (Mullin): the mother liquor leaves SATURATED");
            sb.AppendLine("  at the crystallizer temperature —");
            sb.AppendLine("    crystals = max(0, m_salt - S/100 * m_water*(1 - EvapFrac))");
            sb.AppendLine("    yield    = crystals / m_salt");
            sb.AppendLine("  S = solubility [g/100 g water] at T (user input from solubility tables;");
            sb.AppendLine("    NaCl: 36.0 at 25 C, CRC Handbook). Undersaturated feed -> warning.");
            sb.AppendLine("  Thermo from the selected Property Package.");
            sb.AppendLine("  Refs: Mullin, Crystallization 4e (2001) ch.3;");
            sb.AppendLine("        Myerson, Handbook of Industrial Crystallization 2e (2002) ch.1;");
            sb.AppendLine("        CRC Handbook, aqueous solubility tables.");
            return sb.ToString();
        }
    }

    /// <summary>OP-PPT — Chemical precipitation reactor.</summary>
    [ComVisible(true), Guid("d820ba95-3e24-4e85-81ef-6e8df1f503c3"), ProgId("OPBlocks.PPT")]
    [CapeName("OP-PPT"), CapeVersion("1.0"), CapeVendorURL("https://oneprocess.sim")]
    [CapeDescription("Chemical precipitation reactor: stoichiometric, reagent-limited removal to sludge.")]
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

            AddRealParameter("RemovalEfficiency", "Target species removal (kinetic/settling ceiling)", 90, 10, 99.9, "%");
            AddRealParameter("ReagentDose", "Reagent stoichiometric dose (mol reagent per mol target)", 1.1, 0.5, 5, "mol/mol");
            AddRealParameter("pH", "Operating pH (hydroxide softening 10.3-11)", 10.5, 4, 13, "-");

            AddOutputParameter("Removal", "Achieved removal", "%");
            AddOutputParameter("Precipitate", "Precipitate formed", "mol/s");
            AddOutputParameter("ReagentUsed", "Reagent consumed", "mol/s");
            AddOutputParameter("SludgeFlow", "Total sludge flow", "mol/s");
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
            int wiF = ProcessOps.IndexOf(feed.ComponentIds, "WATER", "H2O");
            int wiR = ProcessOps.IndexOf(reagent.ComponentIds, "WATER", "H2O");
            double[] f = feed.GetOverallMoleFlows(); double[] rf = reagent.GetOverallMoleFlows();

            double targetMolS = 0;
            for (int i = 0; i < f.Length; i++) if (i != wiF) targetMolS += f[i];
            double reagentMolS = 0;
            for (int i = 0; i < rf.Length; i++) if (i != wiR) reagentMolS += rf[i];

            if (targetMolS <= 1e-15)
                ReportWarning("The feed carries no dissolved target species — nothing to precipitate.");
            if (reagentMolS <= 1e-15)
                ReportWarning("The reagent stream carries no reagent species — no precipitation can occur. " +
                              "Set the precipitant flow in the Reagent stream.");

            var spec = new PptModel.Spec
            {
                RemovalPct = R("RemovalEfficiency"),
                DoseMolMol = R("ReagentDose"),
                pH = R("pH"),
            };
            PptModel.Perf x = PptModel.Solve(spec, targetMolS, reagentMolS);

            if (x.ReagentLimited)
                ReportWarning(string.Format(
                    "Reagent-limited: the dosed reagent can precipitate only {0:0.####} mol/s of the target " +
                    "({1:0.#}% removal instead of the requested {2:0.#}%). Increase the reagent flow or dose.",
                    x.RemovedMolS, x.AchievedRemovalPct, spec.RemovalPct));

            // treated = feed minus removed target, plus reagent carrier water and unreacted reagent
            var tf = (double[])f.Clone();
            var sf = new double[Math.Max(f.Length, rf.Length)];
            double removedFrac = targetMolS > 1e-30 ? x.RemovedMolS / targetMolS : 0.0;
            for (int i = 0; i < f.Length; i++)
                if (i != wiF) { double m = f[i] * removedFrac; tf[i] -= m; sf[i] += m; }
            double reagentFrac = reagentMolS > 1e-30 ? x.ReagentConsumedMolS / reagentMolS : 0.0;
            for (int i = 0; i < rf.Length; i++)
            {
                if (i == wiR) { if (wiF >= 0 && wiF < tf.Length) tf[wiF] += rf[i]; continue; }
                double cons = rf[i] * reagentFrac;
                if (i < sf.Length) sf[i] += cons;                    // consumed reagent precipitates
                if (i < tf.Length) tf[i] += rf[i] - cons;            // excess stays dissolved
            }
            treated.SetOutletTP(tf, feed.Temperature, feed.Pressure);
            sludge.SetOutletTP(sf, feed.Temperature, feed.Pressure);

            Result("Achieved removal", x.AchievedRemovalPct, "%", "0.##");
            Result("Precipitate formed", x.RemovedMolS, "mol/s", "0.######");
            Result("Reagent consumed", x.ReagentConsumedMolS, "mol/s", "0.######");
            Result("Total sludge flow", ProcessOps.Sum(sf), "mol/s", "0.######");
            Result("Operating pH", R("pH"), "-", "0.#");

            SetOutputParameter("Removal", x.AchievedRemovalPct);
            SetOutputParameter("Precipitate", x.RemovedMolS);
            SetOutputParameter("ReagentUsed", x.ReagentConsumedMolS);
            SetOutputParameter("SludgeFlow", ProcessOps.Sum(sf));
        }

        protected override string BuildReport()
        {
            var sb = new System.Text.StringBuilder(base.BuildReport());
            sb.AppendLine();
            sb.AppendLine("Model & References");
            sb.AppendLine("  Stoichiometric, reagent-limited precipitation:");
            sb.AppendLine("    removable_by_reagent = reagent_fed / dose");
            sb.AppendLine("    removed = min(target * removal_eff, removable_by_reagent)");
            sb.AppendLine("  Consumed reagent (removed * dose) reports to the sludge with the");
            sb.AppendLine("  precipitate; unreacted excess stays dissolved in the treated water;");
            sb.AppendLine("  reagent carrier water joins the treated stream (4-stream balance).");
            sb.AppendLine("  pH is an advisory input (hydroxide softening optimum 10.3-11).");
            sb.AppendLine("  Thermo from the selected Property Package.");
            sb.AppendLine("  Refs: Metcalf & Eddy/AECOM, Wastewater Engineering 5e (2014) ch.6;");
            sb.AppendLine("        Crittenden et al. (MWH), Water Treatment 3e (2012) ch.13.");
            return sb.ToString();
        }
    }
}
