using System;
using System.Runtime.InteropServices;
using CapeOpen;
using OPBlocks.Core;

namespace OPBlocks.Desalination
{
    // ===================================================================
    //  Family A — Thermal Desalination & Evaporation
    //  Steady-state rating models. Stream flows / T / P / enthalpy come from
    //  the host material objects (ThermoProxy); block parameters drive the
    //  performance and therefore the outlet split and reported duties.
    // ===================================================================

    /// <summary>OP-EVAPPOND — Solar Evaporation Pond.</summary>
    [ComVisible(true), Guid("23c4d15d-67f2-40f3-ac66-215bde083047"), ProgId("OPBlocks.EvapPond")]
    [CapeName("OP-EVAPPOND"), CapeVersion("1.0"), CapeVendorURL("https://oneprocess.sim")]
    [CapeDescription("Solar evaporation pond: climate-driven brine concentration (Dalton-type flux).")]
    [CapeAbout("ONE PROCESS Blocks — OP-EVAPPOND. (c) ONE PROCESS Simulation.")]
    [CapeConsumesThermo(true), CapeSupportsThermodynamics11(true)]
    public class EvapPond : UnitBase
    {
        public EvapPond() : base()
        {
            ComponentName = "OP-EVAPPOND"; ComponentDescription = "Solar Evaporation Pond";
            AddMaterialPort("BrineFeed", "Brine feed", CapePortDirection.CAPE_INLET);
            AddMaterialPort("Concentrate", "Concentrated brine", CapePortDirection.CAPE_OUTLET);
            AddMaterialPort("Vapor", "Evaporated water (to atmosphere)", CapePortDirection.CAPE_OUTLET);
            AddRealParameter("Area", "Pond surface area", 10000, 1, 1e7, "m2");
            AddRealParameter("Depth", "Pond depth", 0.5, 0.05, 5, "m");
            AddRealParameter("Irradiance", "Solar irradiance", 600, 0, 1200, "W/m2");
            AddRealParameter("AirTemp", "Ambient air temperature", 30, -20, 60, "C");
            AddRealParameter("RH", "Relative humidity", 40, 0, 100, "%");
            AddRealParameter("WindSpeed", "Wind speed", 3, 0, 30, "m/s");
            AddRealParameter("WaterActivity", "Surface water activity in brine", 0.98, 0.5, 1.0, "-");
            AddRealParameter("CoeffA", "Dalton coefficient a", 2.0e-9, 0, 1e-6, "kg/m2/s/Pa");
            AddRealParameter("CoeffB", "Dalton wind coefficient b", 1.5e-9, 0, 1e-6, "kg/m2/s/Pa/(m/s)");
        }
        public override string BlockCode => "OP-EVAPPOND";

        protected override bool ValidateModel(out string message)
        {
            message = null;
            var f = GetConnectedMaterial("BrineFeed");
            if (f == null) { message = "Connect a brine stream to BrineFeed."; return false; }
            if (ProcessOps.IndexOf(f.ComponentIds, "WATER", "H2O") < 0)
            { message = "Feed must contain water. Add WATER to the component list."; return false; }
            return true;
        }

        protected override void Compute()
        {
            var feed = RequireMaterial("BrineFeed");
            var conc = RequireMaterial("Concentrate");
            var vap = RequireMaterial("Vapor");
            string[] ids = feed.ComponentIds;
            int wi = ProcessOps.IndexOf(ids, "WATER", "H2O");
            double[] f = feed.GetOverallMoleFlows();
            double tK = feed.Temperature, p = feed.Pressure;

            double surfC = ProcessOps.Clamp(tK - 273.15 + R("Irradiance") * 0.008, 0, 90);
            double pSurf = R("WaterActivity") * ProcessOps.PsatWaterPa(surfC);
            double pAir = (R("RH") / 100.0) * ProcessOps.PsatWaterPa(R("AirTemp"));
            double flux = (R("CoeffA") + R("CoeffB") * R("WindSpeed")) * Math.Max(0, pSurf - pAir); // kg/m2/s
            double evapKgS = flux * R("Area");
            double evapMol = Math.Min(evapKgS / 0.0180153, f[wi] * 0.999);

            var vf = new double[f.Length]; vf[wi] = evapMol;
            var cf = (double[])f.Clone(); cf[wi] -= evapMol;
            vap.SetOutletTP(vf, R("AirTemp") + 273.15, p);
            conc.SetOutletTP(cf, tK, p);

            double cf_factor = f[wi] > evapMol ? f[wi] / (f[wi] - evapMol) : 999;
            Result("Evaporation rate", evapKgS * 86.4, "m3/day", "0.###");     // kg/s water ->  m3/day
            Result("Evaporation flux", flux * 86400, "mm/day", "0.###");
            Result("Concentration factor", cf_factor, "x", "0.###");
            Result("Vapour-pressure driving force", pSurf - pAir, "Pa", "0.#");
            if (cf_factor > 5) ReportWarning("High concentration factor — salt saturation / solids onset likely; add a solids handling step.");
        }
    }

    /// <summary>OP-MD — Direct-Contact Membrane Distillation.</summary>
    [ComVisible(true), Guid("091f27e3-70d1-4885-8032-0080c7b214b6"), ProgId("OPBlocks.MembraneDistillation")]
    [CapeName("OP-MD"), CapeVersion("1.0"), CapeVendorURL("https://oneprocess.sim")]
    [CapeDescription("Direct-contact membrane distillation (DCMD): thermally-driven water vapour transport.")]
    [CapeAbout("ONE PROCESS Blocks — OP-MD. (c) ONE PROCESS Simulation.")]
    [CapeConsumesThermo(true), CapeSupportsThermodynamics11(true)]
    public class MembraneDistillation : UnitBase
    {
        public MembraneDistillation() : base()
        {
            ComponentName = "OP-MD"; ComponentDescription = "Membrane Distillation (DCMD)";
            AddMaterialPort("HotIn", "Hot feed in", CapePortDirection.CAPE_INLET);
            AddMaterialPort("HotOut", "Hot feed out", CapePortDirection.CAPE_OUTLET);
            AddMaterialPort("ColdIn", "Cold permeate in", CapePortDirection.CAPE_INLET);
            AddMaterialPort("ColdOut", "Cold permeate out", CapePortDirection.CAPE_OUTLET);
            AddRealParameter("Area", "Membrane area", 1.0, 0.01, 1e5, "m2");
            AddRealParameter("PoreDia", "Mean pore diameter", 0.2, 0.01, 2, "um");
            AddRealParameter("Porosity", "Membrane porosity", 0.75, 0.1, 0.95, "-");
            AddRealParameter("Tortuosity", "Pore tortuosity", 1.5, 1, 4, "-");
            AddRealParameter("Thickness", "Membrane thickness", 100, 10, 500, "um");
            AddRealParameter("HotActivity", "Water activity, hot side", 0.96, 0.5, 1.0, "-");
            AddRealParameter("Kmembrane", "Membrane permeability calibration", 3.5e-4, 1e-6, 1e-1, "-");
        }
        public override string BlockCode => "OP-MD";

        protected override bool ValidateModel(out string message)
        {
            message = null;
            var h = GetConnectedMaterial("HotIn"); var c = GetConnectedMaterial("ColdIn");
            if (h == null || c == null) { message = "Connect both HotIn and ColdIn streams."; return false; }
            if (ProcessOps.IndexOf(h.ComponentIds, "WATER", "H2O") < 0) { message = "Streams must contain water."; return false; }
            return true;
        }

        protected override void Compute()
        {
            var hIn = RequireMaterial("HotIn"); var hOut = RequireMaterial("HotOut");
            var cIn = RequireMaterial("ColdIn"); var cOut = RequireMaterial("ColdOut");
            int wi = ProcessOps.IndexOf(hIn.ComponentIds, "WATER", "H2O");
            double[] hf = hIn.GetOverallMoleFlows(); double[] cf = cIn.GetOverallMoleFlows();
            double Th = hIn.Temperature, Tc = cIn.Temperature, Ph = hIn.Pressure, Pc = cIn.Pressure;

            double Bm = R("Kmembrane") * R("Porosity") * R("PoreDia") / (R("Tortuosity") * R("Thickness")); // kg/m2/s/Pa
            double pHot = R("HotActivity") * ProcessOps.PsatWaterPa(Th - 273.15);
            double pCold = ProcessOps.PsatWaterPa(Tc - 273.15);
            double J = Bm * Math.Max(0, pHot - pCold);           // kg/m2/s
            double permMol = Math.Min(J * R("Area") / 0.0180153, hf[wi] * 0.999);

            var hof = (double[])hf.Clone(); hof[wi] -= permMol;
            var cof = (double[])cf.Clone(); cof[wi] += permMol;
            hOut.SetOutletTP(hof, Th, Ph);
            cOut.SetOutletTP(cof, Tc, Pc);

            Result("Permeate flux (LMH)", J * 3600.0, "L/m2/h", "0.###");
            Result("Permeate rate", permMol * 0.0180153, "kg/s", "0.#####");
            Result("Membrane permeability Bm", Bm, "kg/m2/s/Pa", "0.###E+0");
            Result("Vapour-pressure driving force", pHot - pCold, "Pa", "0.#");
        }
    }

    /// <summary>OP-MED — Multi-Effect Distillation.</summary>
    [ComVisible(true), Guid("32e353bd-1854-447b-a49f-da2c22af130c"), ProgId("OPBlocks.MED")]
    [CapeName("OP-MED"), CapeVersion("1.0"), CapeVendorURL("https://oneprocess.sim")]
    [CapeDescription("Multi-effect distillation: multi-stage evaporation reusing vapour as heat.")]
    [CapeAbout("ONE PROCESS Blocks — OP-MED. (c) ONE PROCESS Simulation.")]
    [CapeConsumesThermo(true), CapeSupportsThermodynamics11(true)]
    public class MultiEffectDistillation : UnitBase
    {
        public MultiEffectDistillation() : base()
        {
            ComponentName = "OP-MED"; ComponentDescription = "Multi-Effect Distillation";
            AddMaterialPort("Feed", "Seawater feed", CapePortDirection.CAPE_INLET);
            AddMaterialPort("Distillate", "Distillate (product water)", CapePortDirection.CAPE_OUTLET);
            AddMaterialPort("Brine", "Reject brine", CapePortDirection.CAPE_OUTLET);
            AddIntParameter("NEffects", "Number of effects", 4, 1, 16);
            AddRealParameter("WaterRecovery", "Water recovery", 40, 1, 80, "%");
            AddRealParameter("TopBrineTemp", "Top brine temperature", 65, 40, 110, "C");
            AddRealParameter("LatentHeat", "Latent heat of vaporisation", 2320, 2000, 2500, "kJ/kg");
        }
        public override string BlockCode => "OP-MED";

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
            var feed = RequireMaterial("Feed"); var dist = RequireMaterial("Distillate"); var brine = RequireMaterial("Brine");
            int wi = ProcessOps.IndexOf(feed.ComponentIds, "WATER", "H2O");
            double[] f = feed.GetOverallMoleFlows(); double p = feed.Pressure;
            double distWater = f[wi] * R("WaterRecovery") / 100.0;

            var df = new double[f.Length]; df[wi] = distWater;
            var bf = (double[])f.Clone(); bf[wi] -= distWater;
            dist.SetOutletTP(df, 40 + 273.15, p);
            brine.SetOutletTP(bf, R("TopBrineTemp") + 273.15, p);

            double gor = 0.85 * I("NEffects");
            double distKgS = distWater * 0.0180153;
            double steamKW = distKgS * R("LatentHeat") / Math.Max(gor, 0.1);
            double distM3h = distKgS / 1000.0 * 3600.0;
            Result("Distillate flow", distM3h, "m3/h", "0.####");
            Result("Gained Output Ratio (GOR)", gor, "kg/kg", "0.##");
            Result("Motive steam duty", steamKW, "kW", "0.##");
            Result("Specific thermal energy", distM3h > 0 ? steamKW / distM3h : 0, "kWh/m3", "0.#");
        }
    }

    /// <summary>OP-MSF — Multi-Stage Flash.</summary>
    [ComVisible(true), Guid("de8a249a-e088-4f5a-a8c7-73daf61dd337"), ProgId("OPBlocks.MSF")]
    [CapeName("OP-MSF"), CapeVersion("1.0"), CapeVendorURL("https://oneprocess.sim")]
    [CapeDescription("Multi-stage flash: staged flashing of recirculated brine at decreasing pressure.")]
    [CapeAbout("ONE PROCESS Blocks — OP-MSF. (c) ONE PROCESS Simulation.")]
    [CapeConsumesThermo(true), CapeSupportsThermodynamics11(true)]
    public class MultiStageFlash : UnitBase
    {
        public MultiStageFlash() : base()
        {
            ComponentName = "OP-MSF"; ComponentDescription = "Multi-Stage Flash";
            AddMaterialPort("Feed", "Seawater feed", CapePortDirection.CAPE_INLET);
            AddMaterialPort("Distillate", "Distillate (product water)", CapePortDirection.CAPE_OUTLET);
            AddMaterialPort("Brine", "Reject brine", CapePortDirection.CAPE_OUTLET);
            AddIntParameter("NStages", "Number of flash stages", 20, 3, 40);
            AddRealParameter("TopBrineTemp", "Top brine temperature", 90, 60, 120, "C");
            AddRealParameter("WaterRecovery", "Water recovery", 40, 1, 70, "%");
            AddRealParameter("LatentHeat", "Latent heat of vaporisation", 2330, 2000, 2500, "kJ/kg");
        }
        public override string BlockCode => "OP-MSF";

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
            var feed = RequireMaterial("Feed"); var dist = RequireMaterial("Distillate"); var brine = RequireMaterial("Brine");
            int wi = ProcessOps.IndexOf(feed.ComponentIds, "WATER", "H2O");
            double[] f = feed.GetOverallMoleFlows(); double p = feed.Pressure;
            double distWater = f[wi] * R("WaterRecovery") / 100.0;

            var df = new double[f.Length]; df[wi] = distWater;
            var bf = (double[])f.Clone(); bf[wi] -= distWater;
            dist.SetOutletTP(df, 40 + 273.15, p);
            brine.SetOutletTP(bf, R("TopBrineTemp") + 273.15, p);

            double gor = 0.9 * Math.Sqrt(I("NStages"));
            double distKgS = distWater * 0.0180153;
            double steamKW = distKgS * R("LatentHeat") / Math.Max(gor, 0.1);
            double distM3h = distKgS / 1000.0 * 3600.0;
            Result("Distillate flow", distM3h, "m3/h", "0.####");
            Result("Gained Output Ratio (GOR)", gor, "kg/kg", "0.##");
            Result("Performance ratio", gor, "-", "0.##");
            Result("Motive steam duty", steamKW, "kW", "0.##");
            Result("Specific thermal energy", distM3h > 0 ? steamKW / distM3h : 0, "kWh/m3", "0.#");
        }
    }

    /// <summary>OP-MVC — Mechanical Vapour Compression Evaporator.</summary>
    [ComVisible(true), Guid("3f1cc3f7-a38a-45f9-93a8-4962e02378e5"), ProgId("OPBlocks.MVC")]
    [CapeName("OP-MVC"), CapeVersion("1.0"), CapeVendorURL("https://oneprocess.sim")]
    [CapeDescription("Mechanical vapour compression evaporator: compressed vapour as the heat source.")]
    [CapeAbout("ONE PROCESS Blocks — OP-MVC. (c) ONE PROCESS Simulation.")]
    [CapeConsumesThermo(true), CapeSupportsThermodynamics11(true)]
    public class MechanicalVaporCompression : UnitBase
    {
        public MechanicalVaporCompression() : base()
        {
            ComponentName = "OP-MVC"; ComponentDescription = "Mechanical Vapour Compression";
            AddMaterialPort("Feed", "Feed", CapePortDirection.CAPE_INLET);
            AddMaterialPort("Distillate", "Distillate (product water)", CapePortDirection.CAPE_OUTLET);
            AddMaterialPort("Brine", "Concentrated brine", CapePortDirection.CAPE_OUTLET);
            AddRealParameter("WaterRecovery", "Water recovery", 45, 1, 90, "%");
            AddRealParameter("CompressionRatio", "Vapour compression ratio", 1.3, 1.05, 3, "-");
            AddRealParameter("CompressorEff", "Compressor isentropic efficiency", 75, 30, 95, "%");
            AddRealParameter("DeltaT", "Temperature rise across compression", 5, 1, 20, "C");
        }
        public override string BlockCode => "OP-MVC";

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
            var feed = RequireMaterial("Feed"); var dist = RequireMaterial("Distillate"); var brine = RequireMaterial("Brine");
            int wi = ProcessOps.IndexOf(feed.ComponentIds, "WATER", "H2O");
            double[] f = feed.GetOverallMoleFlows(); double p = feed.Pressure; double tK = feed.Temperature;
            double distWater = f[wi] * R("WaterRecovery") / 100.0;

            var df = new double[f.Length]; df[wi] = distWater;
            var bf = (double[])f.Clone(); bf[wi] -= distWater;
            dist.SetOutletTP(df, tK + R("DeltaT"), p);
            brine.SetOutletTP(bf, tK + R("DeltaT"), p);

            double vaporKgS = distWater * 0.0180153;
            // Polytropic-style specific compression work for steam (cp~2 kJ/kgK, (k-1)/k~0.2).
            double wKjKg = 2.0 * tK * (Math.Pow(R("CompressionRatio"), 0.2) - 1.0) / (R("CompressorEff") / 100.0);
            double powerKW = vaporKgS * wKjKg;
            double distM3h = vaporKgS / 1000.0 * 3600.0;
            Result("Distillate flow", distM3h, "m3/h", "0.####");
            Result("Compressor power", powerKW, "kW", "0.##");
            Result("Specific electrical energy", distM3h > 0 ? powerKW / distM3h : 0, "kWh/m3", "0.##");
        }
    }
}
