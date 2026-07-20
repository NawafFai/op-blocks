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

    /// <summary>
    /// OP-EVAPPOND — Solar Evaporation Pond. Climate-driven brine concentration by a
    /// Dalton / aerodynamic mass-transfer law with a brine water-activity (salinity)
    /// reduction and a solar surface-heating closure. Physics lives in
    /// <see cref="EvapPondModel"/> (shared with the validation tests); see
    /// docs/OP-EVAPPOND_MODEL.md.
    /// </summary>
    [ComVisible(true), Guid("23c4d15d-67f2-40f3-ac66-215bde083047"), ProgId("OPBlocks.EvapPond")]
    [CapeName("OP-EVAPPOND"), CapeVersion("1.0"), CapeVendorURL("https://oneprocess.sim")]
    [CapeDescription("Solar evaporation pond: Dalton/aerodynamic evaporation flux with brine water-activity reduction; climate-driven brine concentration.")]
    [CapeAbout("ONE PROCESS Blocks — OP-EVAPPOND. (c) ONE PROCESS Simulation. See the block report's 'Model & References' section for equations and literature.")]
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
            AddRealParameter("WaterActivity", "Surface water activity of the brine (salinity reduction; 1 = fresh)", 0.98, 0.5, 1.0, "-");
            AddRealParameter("CoeffA", "Dalton coefficient a (still-air)", 1.2e-8, 0, 1e-6, "kg/m2/s/Pa");
            AddRealParameter("CoeffB", "Dalton wind coefficient b", 2.5e-9, 0, 1e-6, "kg/m2/s/Pa/(m/s)");
            AddRealParameter("SolarHeating", "Surface warming above air per unit irradiance", 0.012, 0, 0.05, "C/(W/m2)");

            AddOutputParameter("EvapRate", "Evaporation rate", "m3/day");
            AddOutputParameter("EvapFlux", "Evaporation flux (depth basis)", "mm/day");
            AddOutputParameter("ConcFactor", "Brine concentration factor", "x");
            AddOutputParameter("DrivingForce", "Vapour-pressure driving force", "Pa");
            AddOutputParameter("SurfaceTemp", "Estimated surface temperature", "C");
            AddOutputParameter("SurfaceVP", "Brine surface vapour pressure", "Pa");
            AddOutputParameter("AirVP", "Ambient vapour pressure", "Pa");
            AddOutputParameter("ConcentrateTDS", "Concentrated brine TDS", "ppm");
            AddOutputParameter("FeedTDS", "Feed brine TDS", "ppm");
            AddOutputParameter("ResidenceTime", "Pond hydraulic residence time", "day");
            AddOutputParameter("WaterEvaporated", "Water evaporated", "kg/s");
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
            var feed = RequireMaterial("BrineFeed");
            var conc = RequireMaterial("Concentrate");
            var vap = RequireMaterial("Vapor");
            string[] ids = feed.ComponentIds;
            int wi = ProcessOps.IndexOf(ids, "WATER", "H2O");
            double[] f = feed.GetOverallMoleFlows();
            double tK = feed.Temperature, p = feed.Pressure;
            if (wi < 0)
                ReportWarning("Feed contains no water component — nothing evaporates; the whole feed leaves as concentrate.");

            double[] mw;
            bool haveMw = feed.TryGetMolecularWeightsGmol(out mw);
            if (!haveMw)
                ReportWarning("The property package did not supply molecular weights — TDS results are unavailable this run.");
            if (haveMw) WarnIfPureWaterMethod(feed, f, wi, mw, tK);

            double feedM3s = VolumetricM3S(feed, MoleDerivedKgS(f, mw), "feed", haveMw);

            var spec = new EvapPondModel.Spec
            {
                AreaM2 = R("Area"),
                DepthM = R("Depth"),
                IrradianceWm2 = R("Irradiance"),
                AirTempC = R("AirTemp"),
                RHpct = R("RH"),
                WindSpeedMs = R("WindSpeed"),
                WaterActivity = R("WaterActivity"),
                CoeffA = R("CoeffA"),
                CoeffB = R("CoeffB"),
                SolarHeating = R("SolarHeating"),
            };

            EvapPondModel.Result res = EvapPondModel.Solve(spec, f, wi, haveMw ? mw : null, feedM3s);

            // vapour leaves at ambient; brine stays at the feed temperature/pressure.
            // Always set the vapour outlet, even at zero evaporation — an unset
            // outlet fails the host's post-Calculate stream handling (P1 2026-07-20).
            vap.SetOutletTP(res.VaporMol, R("AirTemp") + 273.15, p);
            conc.SetOutletTP(res.ConcMol, tK, p);

            EmitWarnings(res);

            Result("Evaporation rate", res.EvapM3Day, "m3/day", "0.###");
            Result("Evaporation flux", res.EvapMmDay, "mm/day", "0.###");
            Result("Concentration factor", res.ConcentrationFactor, "x", "0.###");
            Result("Vapour-pressure driving force", res.DrivingForcePa, "Pa", "0.#");
            Result("Surface temperature", res.SurfaceTempC, "C", "0.##");
            Result("Surface vapour pressure", res.ESurfPa, "Pa", "0.#");
            Result("Ambient vapour pressure", res.EAirPa, "Pa", "0.#");
            Result("Pond residence time", res.ResidenceDays, "day", "0.##");
            Result("Water evaporated", res.EvapKgS, "kg/s", "0.#####");
            if (haveMw)
            {
                Result("Concentrate TDS", res.TdsConcPpm, "ppm", "0.#");
                Result("Feed TDS", res.TdsFeedPpm, "ppm", "0.#");
            }

            SetOutputParameter("EvapRate", res.EvapM3Day);
            SetOutputParameter("EvapFlux", res.EvapMmDay);
            SetOutputParameter("ConcFactor", double.IsInfinity(res.ConcentrationFactor) ? 0.0 : res.ConcentrationFactor);
            SetOutputParameter("DrivingForce", res.DrivingForcePa);
            SetOutputParameter("SurfaceTemp", res.SurfaceTempC);
            SetOutputParameter("SurfaceVP", res.ESurfPa);
            SetOutputParameter("AirVP", res.EAirPa);
            SetOutputParameter("ConcentrateTDS", res.TdsConcPpm);
            SetOutputParameter("FeedTDS", res.TdsFeedPpm);
            SetOutputParameter("ResidenceTime", res.ResidenceDays);
            SetOutputParameter("WaterEvaporated", res.EvapKgS);
        }

        private void EmitWarnings(EvapPondModel.Result res)
        {
            if (res.DrivingForcePa <= 0)
                ReportWarning("No evaporation: the ambient vapour pressure is at or above the brine surface vapour " +
                              "pressure (humid/cool conditions, or a very saline low-activity brine). Check RH, air " +
                              "temperature, irradiance and the brine water activity.");
            if (res.FeedLimited)
                ReportWarning("The pond would evaporate essentially all the feed water (climate flux x area exceeds the " +
                              "feed). It is running as a batch evaporator, not a steady concentrator — check the pond " +
                              "area against the feed rate, or add a solids-handling / harvest step.");
            if (res.ConcentrationFactor > EvapPondModel.HighConcentrationFactor)
                ReportWarning(string.Format(
                    "High concentration factor ({0:0.#}x) — salt saturation / solids onset likely (halite, gypsum). " +
                    "Add a solids-handling step and confirm the brine water activity at this salinity.",
                    res.ConcentrationFactor));
        }

        /// <summary>Model &amp; References — travels with the simulation (ASCII, for Aspen's report viewer).</summary>
        protected override string BuildReport()
        {
            string body = base.BuildReport();
            var sb = new System.Text.StringBuilder(body);
            sb.AppendLine();
            sb.AppendLine("Model & References");
            sb.AppendLine("  Evaporation (Dalton / aerodynamic mass transfer):");
            sb.AppendLine("    E = (a + b*u) * (e_s - e_a)   [kg/m2/s]");
            sb.AppendLine("    e_s = a_w * Psat(T_surf)   (brine surface vapour pressure; a_w < 1 lowers it),");
            sb.AppendLine("    e_a = RH * Psat(T_air)     (ambient vapour pressure), u = wind speed.");
            sb.AppendLine("    Psat = Antoine (water). Surface temperature closure:");
            sb.AppendLine("    T_surf = T_air + SolarHeating * Irradiance.");
            sb.AppendLine("  Water only evaporates (salts stay); concentration factor");
            sb.AppendLine("    CF = feed water / (feed water - evaporated water).");
            sb.AppendLine("  Evap flux [mm/day] = E * 86400 (1 kg/m2 water = 1 mm depth).");
            sb.AppendLine("  Residence time = pond volume (Area*Depth) / feed volumetric flow.");
            sb.AppendLine("  All stream thermodynamics come from the selected Property Package.");
            sb.AppendLine("  Refs: Penman, Proc. R. Soc. A 193 (1948) 120-145;");
            sb.AppendLine("        Sartori, Solar Energy 68 (2000) 77-89;");
            sb.AppendLine("        Salhotra et al., Water Resour. Res. 21 (1985) 1336-1344;");
            sb.AppendLine("        Al-Shammiri, Desalination 150 (2002) 189-203.");
            return sb.ToString();
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

            AddRealParameter("Area", "Total membrane area", 1.0, 0.01, 1e5, "m2");
            AddRealParameter("PoreDia", "Mean pore diameter (DCMD ~ 0.1-1)", 0.2, 0.01, 2, "um");
            AddRealParameter("Porosity", "Membrane porosity", 0.75, 0.1, 0.95, "-");
            AddRealParameter("Tortuosity", "Pore tortuosity", 1.5, 1, 4, "-");
            AddRealParameter("Thickness", "Membrane thickness", 100, 10, 500, "um");
            AddRealParameter("HotActivity", "Water activity of the hot brine a_w", 0.96, 0.5, 1.0, "-");
            AddRealParameter("Kmembrane", "Membrane coefficient calibration K (lumps diffusivity + polarization)", 3.5e-4, 1e-6, 1e-1, "-");
            AddRealParameter("MaxTransfer", "Maximum fraction of hot water transferred", 95, 5, 99.9, "%");

            AddOutputParameter("PermFlux", "Permeate flux", "L/m2/h");
            AddOutputParameter("PermRate", "Permeate rate", "kg/s");
            AddOutputParameter("Bm", "Membrane coefficient Bm", "kg/m2/s/Pa");
            AddOutputParameter("DrivingForce", "Vapour-pressure driving force", "Pa");
            AddOutputParameter("LatentDuty", "Latent heat duty", "kW");
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

            var spec = new MdModel.Spec
            {
                AreaM2 = R("Area"),
                Kcal = R("Kmembrane"),
                PorosityFrac = R("Porosity"),
                PoreDiaUm = R("PoreDia"),
                TortuosityFac = R("Tortuosity"),
                ThicknessUm = R("Thickness"),
                HotActivity = R("HotActivity"),
                MaxTransferPct = R("MaxTransfer"),
            };
            MdModel.Flux x = MdModel.Solve(spec, wi >= 0 ? hf[wi] : 0.0, Th, Tc);

            if (Th <= Tc)
                ReportWarning(string.Format(
                    "No thermal driving force: the hot side ({0:0.#} C) is not hotter than the cold side ({1:0.#} C) — " +
                    "no vapour crosses the membrane.", Th - 273.15, Tc - 273.15));
            else if (x.DrivingPa <= 0)
                ReportWarning("The brine's reduced water activity cancels the temperature advantage — " +
                              "no net vapour-pressure driving force. Raise the hot temperature or dilute the brine.");
            if (x.TransferCapped)
                ReportWarning(string.Format(
                    "Vapour transfer limited to the MaxTransfer cap ({0:0.#}% of the hot-side water) — " +
                    "the membrane area is oversized for this feed flow.", spec.MaxTransferPct));

            // only water vapour crosses; non-volatile solutes stay on the hot side (complete rejection)
            var hof = (double[])hf.Clone(); if (wi >= 0) hof[wi] -= x.WaterMolS;
            var cof = (double[])cf.Clone(); if (wi >= 0) cof[wi] += x.WaterMolS;
            hOut.SetOutletTP(hof, Th, Ph);
            cOut.SetOutletTP(cof, Tc, Pc);

            Result("Permeate flux", x.FluxLMH, "L/m2/h", "0.###");
            Result("Permeate rate", x.WaterMolS * 0.0180153, "kg/s", "0.#####");
            Result("Membrane coefficient Bm", x.BmKgM2sPa, "kg/m2/s/Pa", "0.###E+0");
            Result("Vapour-pressure driving force", x.DrivingPa, "Pa", "0.#");
            Result("Latent heat duty", x.LatentKW, "kW", "0.###");

            SetOutputParameter("PermFlux", x.FluxLMH);
            SetOutputParameter("PermRate", x.WaterMolS * 0.0180153);
            SetOutputParameter("Bm", x.BmKgM2sPa);
            SetOutputParameter("DrivingForce", x.DrivingPa);
            SetOutputParameter("LatentDuty", x.LatentKW);
        }

        protected override string BuildReport()
        {
            var sb = new System.Text.StringBuilder(base.BuildReport());
            sb.AppendLine();
            sb.AppendLine("Model & References");
            sb.AppendLine("  Vapour flux across the hydrophobic membrane (DCMD):");
            sb.AppendLine("    J  = Bm * (a_w * Psat(T_hot) - Psat(T_cold))   [kg/m2/s]");
            sb.AppendLine("    Bm = K * porosity * d_pore / (tortuosity * thickness)");
            sb.AppendLine("    (Knudsen/molecular scaling; K lumps diffusivity + polarization).");
            sb.AppendLine("  Psat from the Antoine correlation (ProcessOps.PsatWaterPa).");
            sb.AppendLine("  Non-volatile solutes cannot evaporate -> complete salt rejection;");
            sb.AppendLine("    the brine's water activity a_w lowers the hot-side vapour pressure.");
            sb.AppendLine("  Latent duty = J * A * lambda (~2333 kJ/kg at 60 C).");
            sb.AppendLine("  All stream thermodynamics come from the selected Property Package.");
            sb.AppendLine("  Validity: Th 40-85 C, Tc 15-35 C, pore 0.1-1 um, flux ~ 5-60 LMH.");
            sb.AppendLine("  Refs: Schofield, Fane & Fell, J. Membr. Sci. 33 (1987) 299-313;");
            sb.AppendLine("        Lawson & Lloyd, J. Membr. Sci. 124 (1997) 1-25;");
            sb.AppendLine("        Khayet, Adv. Colloid Interface Sci. 164 (2011) 56-88.");
            return sb.ToString();
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

            // NOTE: effect count is a REAL parameter with an integer code — the
            // golden rule (Integer/Option parameters blank Aspen's grid).
            AddRealParameter("NEffects", "Number of effects (integer code; industrial MED 4-14)", 8, 1, 16, "-");
            AddRealParameter("WaterRecovery", "Water recovery", 40, 1, 60, "%");
            AddRealParameter("TopBrineTemp", "Top brine temperature (scaling limit ~70 C)", 65, 40, 110, "C");
            AddRealParameter("DistillateTemp", "Distillate (last effect) temperature", 40, 25, 60, "C");
            AddRealParameter("GorPerEffect", "GOR per effect k (GOR = k*N; 0.8-0.9)", 0.85, 0.5, 1.0, "-");
            AddRealParameter("LatentHeat", "Latent heat of vaporisation", 2326, 2000, 2500, "kJ/kg");

            AddOutputParameter("DistFlow", "Distillate flow", "m3/h");
            AddOutputParameter("GOR", "Gained output ratio", "kg/kg");
            AddOutputParameter("SteamDuty", "Motive steam duty", "kW");
            AddOutputParameter("STE", "Specific thermal energy", "kWh/m3");
            AddOutputParameter("BrineCF", "Brine concentration factor", "-");
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

            var spec = new MedModel.Spec
            {
                NEffects = R("NEffects"),
                RecoveryPct = R("WaterRecovery"),
                TopBrineTempC = R("TopBrineTemp"),
                GorPerEffect = R("GorPerEffect"),
                LatentKJKg = R("LatentHeat"),
            };
            MedModel.Perf perf = MedModel.Solve(spec, wi >= 0 ? f[wi] : 0.0);

            if (spec.TopBrineTempC > MedModel.ScalingTbtC)
                ReportWarning(string.Format(
                    "Top brine temperature ({0:0.#} C) exceeds the MED scaling limit (~{1:0} C) — CaSO4/CaCO3 " +
                    "scale risk on the tube bundles. Industrial MED runs at 60-70 C.", spec.TopBrineTempC, MedModel.ScalingTbtC));
            double recFrac = ProcessOps.Clamp(spec.RecoveryPct, 0, 100) / 100.0;
            double brineCF = recFrac < 1.0 ? 1.0 / (1.0 - recFrac) : 0.0;
            if (brineCF > 2.5)
                ReportWarning(string.Format(
                    "Brine concentration factor ({0:0.##}) is high — seawater MED usually rejects at CF < 2.5 to " +
                    "control scaling; reduce the recovery.", brineCF));

            // distillate is pure water (salts are non-volatile)
            var df = new double[f.Length]; if (wi >= 0) df[wi] = perf.DistWaterMolS;
            var bf = (double[])f.Clone(); if (wi >= 0) bf[wi] -= perf.DistWaterMolS;
            dist.SetOutletTP(df, R("DistillateTemp") + 273.15, p);
            brine.SetOutletTP(bf, R("TopBrineTemp") + 273.15, p);

            Result("Distillate flow", perf.DistM3h, "m3/h", "0.####");
            Result("Gained Output Ratio (GOR)", perf.Gor, "kg/kg", "0.##");
            Result("Motive steam duty", perf.SteamKW, "kW", "0.##");
            Result("Specific thermal energy", perf.SteKWhM3, "kWh/m3", "0.#");
            Result("Brine concentration factor", brineCF, "-", "0.###");

            SetOutputParameter("DistFlow", perf.DistM3h);
            SetOutputParameter("GOR", perf.Gor);
            SetOutputParameter("SteamDuty", perf.SteamKW);
            SetOutputParameter("STE", perf.SteKWhM3);
            SetOutputParameter("BrineCF", brineCF);
        }

        protected override string BuildReport()
        {
            var sb = new System.Text.StringBuilder(base.BuildReport());
            sb.AppendLine();
            sb.AppendLine("Model & References");
            sb.AppendLine("  Industrial MED shortcut (El-Dessouky & Ettouney):");
            sb.AppendLine("    GOR = k * N_effects           (k ~ 0.8-0.9)");
            sb.AppendLine("    Q_steam = D * lambda / GOR;   STE = Q_steam / Q_distillate");
            sb.AppendLine("  Distillate D = recovery * feed water; salts are non-volatile so the");
            sb.AppendLine("    distillate is pure water and the brine carries every solute.");
            sb.AppendLine("  Brine concentration factor CF = 1/(1 - recovery); CF > 2.5 flagged.");
            sb.AppendLine("  TBT above ~70 C flagged (CaSO4/CaCO3 scaling on MED bundles).");
            sb.AppendLine("  All stream thermodynamics come from the selected Property Package.");
            sb.AppendLine("  Validity: N 2-16, TBT 55-70 C; N=8 -> GOR ~ 6.8, STE ~ 95 kWh/m3.");
            sb.AppendLine("  Refs: El-Dessouky & Ettouney, Fundamentals of Salt Water Desalination");
            sb.AppendLine("        (2002) ch.8;  Al-Shammiri & Safar, Desalination 126 (1999) 45-59.");
            return sb.ToString();
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

            // golden rule: stage count as a REAL integer code
            AddRealParameter("NStages", "Number of flash stages (integer code; industrial 16-28)", 24, 3, 40, "-");
            AddRealParameter("TopBrineTemp", "Top brine temperature", 110, 60, 120, "C");
            AddRealParameter("LastStageTemp", "Last-stage brine temperature", 40, 30, 55, "C");
            AddRealParameter("ThermoLoss", "Per-stage thermodynamic losses (BPE + NEA)", 2.5, 0.5, 5, "K");
            AddRealParameter("SpecificHeat", "Brine specific heat", 4.0, 3.5, 4.3, "kJ/kg/K");
            AddRealParameter("LatentHeat", "Latent heat of vaporisation", 2330, 2000, 2500, "kJ/kg");

            AddOutputParameter("DistFlow", "Distillate flow", "m3/h");
            AddOutputParameter("Recovery", "Once-through water recovery", "%");
            AddOutputParameter("PR", "Performance ratio", "kg/kg");
            AddOutputParameter("HeaterDuty", "Brine heater duty", "kW");
            AddOutputParameter("STE", "Specific thermal energy", "kWh/m3");
        }
        public override string BlockCode => "OP-MSF";

        protected override bool ValidateModel(out string message)
        {
            message = null;
            var f = GetConnectedMaterial("Feed");
            if (f == null) { message = "Connect a feed stream."; return false; }
            if (ProcessOps.IndexOf(f.ComponentIds, "WATER", "H2O") < 0) { message = "Feed must contain water."; return false; }
            if (R("TopBrineTemp") <= R("LastStageTemp")) { message = "TopBrineTemp must exceed LastStageTemp (the flash range)."; return false; }
            return true;
        }

        protected override void Compute()
        {
            var feed = RequireMaterial("Feed"); var dist = RequireMaterial("Distillate"); var brine = RequireMaterial("Brine");
            int wi = ProcessOps.IndexOf(feed.ComponentIds, "WATER", "H2O");
            double[] f = feed.GetOverallMoleFlows(); double p = feed.Pressure;

            double[] mw;
            bool haveMw = feed.TryGetMolecularWeightsGmol(out mw);
            if (haveMw) WarnIfPureWaterMethod(feed, f, wi, mw, feed.Temperature);
            double feedKgS;
            if (!feed.TryGetTotalMassFlowKgS(out feedKgS) || feedKgS <= 1e-30)
            {
                feedKgS = 0;
                if (haveMw) for (int i = 0; i < f.Length; i++) feedKgS += f[i] * mw[i] / 1000.0;
                else
                {
                    feedKgS = (wi >= 0 ? f[wi] : 0.0) * 0.0180153;
                    ReportWarning("The property package supplied neither a total mass flow nor molecular weights — " +
                                  "the brine-heater duty uses the water mass only.");
                }
            }

            var spec = new MsfModel.Spec
            {
                NStages = R("NStages"),
                TopBrineTempC = R("TopBrineTemp"),
                LastStageTempC = R("LastStageTemp"),
                ThermoLossK = R("ThermoLoss"),
                CpKJKgK = R("SpecificHeat"),
                LatentKJKg = R("LatentHeat"),
            };
            MsfModel.Perf perf = MsfModel.Solve(spec, wi >= 0 ? f[wi] : 0.0, feedKgS);

            ReportWarning(string.Format(
                "MSF is a low-recovery process by nature: the once-through recovery from the {0:0.#} K flash range " +
                "is {1:0.#}% — plants recirculate brine to raise the apparent recovery.",
                perf.FlashRangeK, perf.RecoveryFrac * 100));
            if (spec.TopBrineTempC > 112)
                ReportWarning(string.Format(
                    "Top brine temperature ({0:0.#} C) exceeds the usual MSF limit (~112 C with antiscalant) — scale risk.",
                    spec.TopBrineTempC));

            var df = new double[f.Length]; if (wi >= 0) df[wi] = perf.DistWaterMolS;
            var bf = (double[])f.Clone(); if (wi >= 0) bf[wi] -= perf.DistWaterMolS;
            dist.SetOutletTP(df, R("LastStageTemp") + 273.15, p);
            brine.SetOutletTP(bf, R("LastStageTemp") + 273.15, p);

            Result("Distillate flow", perf.DistM3h, "m3/h", "0.####");
            Result("Once-through recovery", perf.RecoveryFrac * 100, "%", "0.##");
            Result("Performance ratio", perf.PerfRatio, "kg/kg", "0.##");
            Result("Brine heater duty", perf.HeaterKW, "kW", "0.##");
            Result("Specific thermal energy", perf.SteKWhM3, "kWh/m3", "0.#");

            SetOutputParameter("DistFlow", perf.DistM3h);
            SetOutputParameter("Recovery", perf.RecoveryFrac * 100);
            SetOutputParameter("PR", perf.PerfRatio);
            SetOutputParameter("HeaterDuty", perf.HeaterKW);
            SetOutputParameter("STE", perf.SteKWhM3);
        }

        protected override string BuildReport()
        {
            var sb = new System.Text.StringBuilder(base.BuildReport());
            sb.AppendLine();
            sb.AppendLine("Model & References");
            sb.AppendLine("  Once-through MSF (El-Dessouky & Ettouney ch.6):");
            sb.AppendLine("    y  = cp * dT_stage / lambda     (flash fraction per stage)");
            sb.AppendLine("    D  = Mf * [1 - (1 - y)^N];      dT_stage = (TBT - T_last)/N");
            sb.AppendLine("    Q_heater = Mf * cp * (dT_stage + dT_loss)   (BPE + NEA losses)");
            sb.AppendLine("    PR = D * lambda / Q_heater      (performance ratio ~ GOR)");
            sb.AppendLine("  Distillate is pure water (salts non-volatile); brine keeps every solute.");
            sb.AppendLine("  All stream thermodynamics come from the selected Property Package.");
            sb.AppendLine("  Validity: N 10-40, TBT 90-112 C, dT_loss 1.5-3 K; 24 stages -> PR ~ 8-12,");
            sb.AppendLine("    once-through recovery ~ 8-12% (industrial plants recirculate).");
            sb.AppendLine("  Refs: El-Dessouky & Ettouney, Fundamentals of Salt Water Desalination");
            sb.AppendLine("        (2002) ch.6;  Khawaji et al., Desalination 221 (2008) 47-69.");
            return sb.ToString();
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
            AddRealParameter("CompressionRatio", "Vapour compression ratio (must clear the BPE; 1.1-2)", 1.3, 1.05, 3, "-");
            AddRealParameter("CompressorEff", "Compressor isentropic efficiency", 75, 30, 95, "%");
            AddRealParameter("EvapTemp", "Evaporator saturation temperature", 60, 45, 80, "C");

            AddOutputParameter("DistFlow", "Distillate flow", "m3/h");
            AddOutputParameter("CompPower", "Compressor power", "kW");
            AddOutputParameter("SpecWork", "Specific compression work", "kJ/kg");
            AddOutputParameter("SEC", "Specific electrical energy", "kWh/m3");
            AddOutputParameter("BrineCF", "Brine concentration factor", "-");
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
            double[] f = feed.GetOverallMoleFlows(); double p = feed.Pressure;

            var spec = new MvcModel.Spec
            {
                RecoveryPct = R("WaterRecovery"),
                CompressionRatio = R("CompressionRatio"),
                CompressorEffPct = R("CompressorEff"),
                EvapTempC = R("EvapTemp"),
            };
            MvcModel.Perf perf = MvcModel.Solve(spec, wi >= 0 ? f[wi] : 0.0);

            if (spec.CompressionRatio < MvcModel.MinUsefulCR)
                ReportWarning(string.Format(
                    "Compression ratio ({0:0.##}) is below the practical MVC minimum (~{1:0.#}) — after the brine's " +
                    "boiling-point elevation there may be no temperature driving force left in the evaporator.",
                    spec.CompressionRatio, MvcModel.MinUsefulCR));
            double recFrac = ProcessOps.Clamp(spec.RecoveryPct, 0, 100) / 100.0;
            double brineCF = recFrac < 1.0 ? 1.0 / (1.0 - recFrac) : 0.0;
            if (brineCF > 3.0)
                ReportWarning(string.Format(
                    "Brine concentration factor ({0:0.##}) is very high — MVC brine concentrators run to CF ~ 3; " +
                    "beyond that expect scaling and a rising BPE.", brineCF));

            double tOutK = spec.EvapTempC + 273.15;
            var df = new double[f.Length]; if (wi >= 0) df[wi] = perf.VaporMolS;
            var bf = (double[])f.Clone(); if (wi >= 0) bf[wi] -= perf.VaporMolS;
            dist.SetOutletTP(df, tOutK, p);
            brine.SetOutletTP(bf, tOutK, p);

            Result("Distillate flow", perf.DistM3h, "m3/h", "0.####");
            Result("Compressor power", perf.PowerKW, "kW", "0.##");
            Result("Specific compression work", perf.SpecWorkKJKg, "kJ/kg", "0.##");
            Result("Specific electrical energy", perf.SecKWhM3, "kWh/m3", "0.##");
            Result("Brine concentration factor", brineCF, "-", "0.###");

            SetOutputParameter("DistFlow", perf.DistM3h);
            SetOutputParameter("CompPower", perf.PowerKW);
            SetOutputParameter("SpecWork", perf.SpecWorkKJKg);
            SetOutputParameter("SEC", perf.SecKWhM3);
            SetOutputParameter("BrineCF", brineCF);
        }

        protected override string BuildReport()
        {
            var sb = new System.Text.StringBuilder(base.BuildReport());
            sb.AppendLine();
            sb.AppendLine("Model & References");
            sb.AppendLine("  All-electric evaporation: the vapour is compressed and returned as");
            sb.AppendLine("  the heat source. Specific compression work (steam, ideal-gas form):");
            sb.AppendLine("    w = cp_v * T_sat * [ CR^((g-1)/g) - 1 ] / eta_isentropic");
            sb.AppendLine("    cp_v = 1.88 kJ/kg K, (g-1)/g = 0.248 for steam.");
            sb.AppendLine("  Power = w * m_vapour;  SEC = Power / Q_distillate.");
            sb.AppendLine("  CR must clear the brine's boiling-point elevation (advisory at CR<1.1).");
            sb.AppendLine("  Distillate is pure water; the brine keeps every solute (CF = 1/(1-r)).");
            sb.AppendLine("  All stream thermodynamics come from the selected Property Package.");
            sb.AppendLine("  Validity: CR 1.1-2, recovery 30-60%, T_evap 50-75 C -> SEC ~ 8-16 kWh/m3.");
            sb.AppendLine("  Refs: El-Dessouky & Ettouney, Fundamentals of Salt Water Desalination");
            sb.AppendLine("        (2002) ch.7;  Aly & El-Fiqi, Desalination 158 (2003) 143-150.");
            return sb.ToString();
        }
    }
}
