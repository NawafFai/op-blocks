using System;
using System.Runtime.InteropServices;
using CapeOpen;
using OPBlocks.Core;

namespace OPBlocks.Desalination
{
    // ===================================================================
    //  Family B — Pressure- & Osmotic-Driven Membranes
    // ===================================================================

    /// <summary>OP-RO — Reverse Osmosis.</summary>
    [ComVisible(true), Guid("3eb2efdd-d0a2-4e21-b9bb-53e1e25ea11f"), ProgId("OPBlocks.RO")]
    [CapeName("OP-RO"), CapeVersion("1.0"), CapeVendorURL("https://oneprocess.sim")]
    [CapeDescription("Reverse osmosis: solution-diffusion flux with average-osmotic-pressure driving force; Rating/Design modes; optional energy-recovery device.")]
    [CapeAbout("ONE PROCESS Blocks — OP-RO. (c) ONE PROCESS Simulation. See the block report's 'Model & References' section for equations and literature.")]
    [CapeConsumesThermo(true), CapeSupportsThermodynamics11(true)]
    public class ReverseOsmosis : UnitBase
    {
        public ReverseOsmosis() : base()
        {
            ComponentName = "OP-RO"; ComponentDescription = "Reverse Osmosis";
            // Outlet order = flowsheet anchor order (top first): brine continues
            // on top, product water is drawn off below — standard PFD convention.
            AddMaterialPort("Feed", "Pressurised feed", CapePortDirection.CAPE_INLET);
            AddMaterialPort("Concentrate", "Concentrate / brine (reject)", CapePortDirection.CAPE_OUTLET);
            AddMaterialPort("Permeate", "Permeate (product water)", CapePortDirection.CAPE_OUTLET);

            // --- calculation mode ---
            // NOTE: mode/type selectors are REAL parameters with integer codes, NOT
            // CAPE-OPEN option parameters. Aspen Plus's parameter grid renders a
            // parameter only through the AspenTech extension IATCapeXRealParameterSpec,
            // which the CO-LaN library implements ONLY on RealParameter. An
            // OptionParameter (no AT spec) as the first parameter makes Aspen blank the
            // ENTIRE grid — inputs and results alike (diagnosed live 2026-07-14). Integer
            // codes keep every row visible in Aspen while DWSIM shows them too.
            AddRealParameter("CalcMode",
                "Calculation mode: 0 = Rating (area & pressure given -> performance); 1 = Design (target recovery given -> area & pressure)",
                0, 0, 1, "-");

            // --- membrane & operating inputs (Rating) ---
            AddRealParameter("Area", "Total membrane area (Rating input; computed in Design)", 40, 0.1, 1e5, "m2");
            AddRealParameter("WaterPermA", "Water permeability A (seawater ≈ 1, brackish ≈ 3–8)", 1.0, 0.05, 20, "L/m2/h/bar");
            AddRealParameter("SaltRejection", "Intrinsic salt rejection", 99.5, 50, 99.99, "%");
            AddRealParameter("AppliedPressure", "Applied feed pressure (Rating input; computed in Design)", 60, 5, 120, "bar");
            AddRealParameter("VantHoffI", "van 't Hoff dissociation factor (2 for NaCl)", 2.0, 1, 4, "-");
            AddRealParameter("PumpEff", "High-pressure pump efficiency", 80, 30, 95, "%");
            AddRealParameter("MaxRecovery", "Maximum design water recovery (single-stage SWRO ≈ 45–50%)", 50, 5, 95, "%");

            // --- design-mode inputs (ignored in Rating) ---
            AddRealParameter("TargetRecovery", "Target water recovery (Design mode)", 45, 5, 95, "%");
            AddRealParameter("DesignFlux", "Design average permeate flux (Design mode)", 15, 2, 50, "L/m2/h");

            // --- energy recovery device (optional) ---
            AddRealParameter("ERDType",
                "Energy-recovery device on the brine: 0 = None; 1 = PX (pressure exchanger); 2 = Turbine",
                0, 0, 2, "-");
            AddRealParameter("ERDEff", "ERD efficiency (used only when ERDType > 0; PX ~ 96%, Turbine ~ 80%)", 96, 40, 99, "%");

            // --- results table (host-rendered CAPE-OPEN output grid) ---
            AddOutputParameter("Recovery", "Water recovery", "%");
            AddOutputParameter("PermeateFlow", "Permeate volumetric flow", "m3/h");
            AddOutputParameter("PermeateTDS", "Permeate TDS", "ppm");
            AddOutputParameter("SaltRejObs", "Observed salt rejection", "%");
            AddOutputParameter("PumpPower", "Gross high-pressure pump power", "kW");
            AddOutputParameter("SEC", "Gross specific energy (no ERD)", "kWh/m3");
            AddOutputParameter("ERDRecoveredPower", "ERD recovered power", "kW");
            AddOutputParameter("NetPumpPower", "Net pump power (after ERD)", "kW");
            AddOutputParameter("NetSEC", "Net specific energy (after ERD)", "kWh/m3");
            AddOutputParameter("EnergySaving", "Energy saving from ERD", "%");
            AddOutputParameter("PermeateFlux", "Permeate water flux", "L/m2/h");
            AddOutputParameter("OsmoticPress", "Feed osmotic pressure", "bar");
            AddOutputParameter("OsmoticPressAvg", "Average osmotic pressure (feed–brine)", "bar");
            AddOutputParameter("NDP", "Net driving pressure", "bar");
            AddOutputParameter("ConcentrateTDS", "Concentrate TDS", "ppm");
            AddOutputParameter("FeedTDS", "Feed TDS", "ppm");
            AddOutputParameter("RequiredArea", "Required membrane area (Design mode)", "m2");
            AddOutputParameter("RequiredPressure", "Required applied pressure (Design mode)", "bar");
        }
        public override string BlockCode => "OP-RO";

        protected override bool ValidateModel(out string message)
        {
            message = null;
            var f = GetConnectedMaterial("Feed");
            if (f == null) { message = "Connect a feed stream."; return false; }
            if (ProcessOps.IndexOf(f.ComponentIds, "WATER", "H2O") < 0) { message = "Feed must contain water."; return false; }
            return true;
        }

        /// <summary>
        /// Volumetric flow [m3/s] of a stream: package mass flow ÷ package density
        /// (their units cancel, whatever mass unit the host uses); falls back to our
        /// mole-derived kg/s over 1000 kg/m3 with a report warning.
        /// </summary>
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
            var feed = RequireMaterial("Feed");
            var perm = RequireMaterial("Permeate");
            var conc = RequireMaterial("Concentrate");
            double[] f = feed.GetOverallMoleFlows();
            int wi = ProcessOps.IndexOf(feed.ComponentIds, "WATER", "H2O");
            double tK = feed.Temperature, p = feed.Pressure;
            if (wi < 0)
                ReportWarning("Feed contains no water component — nothing permeates; the whole feed leaves as concentrate.");

            // feed osmotic pressure: package water activity if available, else van 't Hoff
            var osmNotes = new System.Collections.Generic.List<string>();
            double piFeed = ProcessOps.OsmoticPressureBar(feed, f, wi, R("VantHoffI"), tK, osmNotes);
            foreach (string n in osmNotes) ReportWarning(n);

            double[] mw;
            bool haveMw = feed.TryGetMolecularWeightsGmol(out mw);
            if (!haveMw)
                ReportWarning("The property package did not supply molecular weights — " +
                              "TDS and salt-rejection results are unavailable this run.");

            var spec = new RoModel.Spec
            {
                CalcMode = RoModel.ModeFromCode(R("CalcMode")),
                AreaM2 = R("Area"),
                WaterPermA = R("WaterPermA"),
                SaltRejPct = R("SaltRejection"),
                AppliedBar = R("AppliedPressure"),
                VantHoffI = R("VantHoffI"),
                PumpEffPct = R("PumpEff"),
                MaxRecoveryPct = R("MaxRecovery"),
                TargetRecoveryPct = R("TargetRecovery"),
                DesignFluxLMH = R("DesignFlux"),
                ErdType = RoModel.ErdFromCode(R("ERDType")),
                ErdEffPct = R("ERDEff"),
            };

            RoModel.Split split = RoModel.Solve(spec, f, wi, haveMw ? mw : null, tK, piFeed);

            // write the outlet streams (host re-flashes at T,P)
            ProcessOps.SetSplitOutlets(perm, conc, split.PermMol, split.ConcMol, tK, 101325, tK, p);

            // volumetric flows from the property package (unit-safe: mass ÷ density)
            double feedM3s = VolumetricM3S(feed, MoleDerivedKgS(f, mw), "feed", haveMw);
            double permM3s = ProcessOps.Sum(split.PermMol) > 1e-30
                ? VolumetricM3S(perm, MoleDerivedKgS(split.PermMol, mw), "permeate", haveMw) : 0.0;
            double concM3s = ProcessOps.Sum(split.ConcMol) > 1e-30
                ? VolumetricM3S(conc, MoleDerivedKgS(split.ConcMol, mw), "concentrate", haveMw) : 0.0;

            RoModel.Energy e = RoModel.CalcEnergy(spec, split.AppliedBarUsed, feedM3s, permM3s, concM3s);
            double permM3h = permM3s * 3600.0;

            EmitWarnings(spec, split, piFeed);

            // ---- results (human report rows + host output-parameter grid) ----
            Result("Water recovery", split.Recovery * 100, "%", "0.##");
            Result("Permeate flow", permM3h, "m3/h", "0.####");
            if (haveMw)
            {
                Result("Permeate TDS", split.TdsPermPpm, "ppm", "0.#");
                Result("Salt rejection (observed)", split.SaltRejObsPct, "%", "0.##");
            }
            Result("Gross pump power", e.GrossPumpKW, "kW", "0.###");
            Result("Gross SEC", e.SecGross, "kWh/m3", "0.###");
            if (spec.ErdType != RoModel.Erd.None)
            {
                Result("ERD recovered power", e.ErdRecoveredKW, "kW", "0.###");
                Result("Net pump power", e.NetPumpKW, "kW", "0.###");
                Result("Net SEC", e.SecNet, "kWh/m3", "0.###");
                Result("Energy saving", e.EnergySavingPct, "%", "0.#");
            }
            Result("Permeate flux", split.FluxLMH, "L/m2/h", "0.###");
            Result("Feed osmotic pressure", split.PiFeedBar, "bar", "0.##");
            Result("Average osmotic pressure", split.PiAvgBar, "bar", "0.##");
            Result("Net driving pressure", split.NdpBar, "bar", "0.##");
            if (haveMw)
            {
                Result("Concentrate TDS", split.TdsConcPpm, "ppm", "0.#");
                Result("Feed TDS", split.TdsFeedPpm, "ppm", "0.#");
            }
            if (spec.CalcMode == RoModel.Mode.Design)
            {
                Result("Required membrane area", split.RequiredAreaM2, "m2", "0.##");
                Result("Required applied pressure", split.RequiredPressureBar, "bar", "0.##");
            }

            SetOutputParameter("Recovery", split.Recovery * 100);
            SetOutputParameter("PermeateFlow", permM3h);
            SetOutputParameter("PermeateTDS", split.TdsPermPpm);
            SetOutputParameter("SaltRejObs", split.SaltRejObsPct);
            SetOutputParameter("PumpPower", e.GrossPumpKW);
            SetOutputParameter("SEC", e.SecGross);
            SetOutputParameter("ERDRecoveredPower", e.ErdRecoveredKW);
            SetOutputParameter("NetPumpPower", e.NetPumpKW);
            SetOutputParameter("NetSEC", e.SecNet);
            SetOutputParameter("EnergySaving", e.EnergySavingPct);
            SetOutputParameter("PermeateFlux", split.FluxLMH);
            SetOutputParameter("OsmoticPress", split.PiFeedBar);
            SetOutputParameter("OsmoticPressAvg", split.PiAvgBar);
            SetOutputParameter("NDP", split.NdpBar);
            SetOutputParameter("ConcentrateTDS", split.TdsConcPpm);
            SetOutputParameter("FeedTDS", split.TdsFeedPpm);
            SetOutputParameter("RequiredArea", split.RequiredAreaM2);
            SetOutputParameter("RequiredPressure", split.RequiredPressureBar);
        }

        /// <summary>Non-blocking engineering advisories (owner spec §4).</summary>
        private void EmitWarnings(RoModel.Spec spec, RoModel.Split split, double piFeed)
        {
            if (split.NdpBar <= 0)
                ReportWarning(string.Format(
                    "No net driving pressure: the applied pressure ({0:0.#} bar) does not exceed the average " +
                    "osmotic pressure ({1:0.#} bar). Raise the pressure or dilute/reduce recovery.",
                    split.AppliedBarUsed, split.PiAvgBar));

            if (spec.CalcMode == RoModel.Mode.Rating && split.RecoveryCapped)
                ReportWarning(string.Format(
                    "Water recovery limited to the MaxRecovery design cap ({0:0.#}%); the membrane/pressure could " +
                    "drive {1:0.#}%. The area may be oversized for this feed, or raise MaxRecovery if the design allows.",
                    spec.MaxRecoveryPct, split.NaturalRecovery * 100));

            if (spec.CalcMode == RoModel.Mode.Design && split.RecoveryCapped)
                ReportWarning(string.Format(
                    "Target recovery ({0:0.#}%) exceeds MaxRecovery ({1:0.#}%); designed at the cap instead.",
                    spec.TargetRecoveryPct, spec.MaxRecoveryPct));

            double pressUsed = split.AppliedBarUsed;
            if (pressUsed > RoModel.MembraneMaxBar)
                ReportWarning(string.Format(
                    "Applied pressure ({0:0.#} bar) exceeds the typical seawater membrane element limit " +
                    "(~{1:0} bar). Check the element rating or reduce recovery/flux.",
                    pressUsed, RoModel.MembraneMaxBar));

            if (split.Recovery > RoModel.LowBrineRecovery)
                ReportWarning(string.Format(
                    "Recovery ({0:0.#}%) is very high — the concentrate flow is small, raising the scaling/fouling " +
                    "risk. Confirm the brine flow and antiscalant dosing.", split.Recovery * 100));
        }

        /// <summary>
        /// Model &amp; References — rendered in the block report so the equations and
        /// literature travel with the simulation (owner spec §2 documentation).
        /// </summary>
        protected override string BuildReport()
        {
            // ASCII only — Aspen's report viewer must render this cleanly.
            string body = base.BuildReport();
            var sb = new System.Text.StringBuilder(body);
            sb.AppendLine();
            sb.AppendLine("Model & References");
            sb.AppendLine("  Water flux (solution-diffusion):  Jw = A * (dP - dPi)   [L/m2/h]");
            sb.AppendLine("    A = water permeability, dP = applied - permeate pressure,");
            sb.AppendLine("    dPi = 0.5*(pi_feed + pi_conc)  (average osmotic pressure over the module).");
            sb.AppendLine("  Osmotic pressure:  pi = i * c * R * T  (van 't Hoff), or -(R*T/Vw)*ln(a_w)");
            sb.AppendLine("    when the property package supplies the water activity a_w.");
            sb.AppendLine("  Recovery:  r = permeate water / feed water; capped at MaxRecovery.");
            sb.AppendLine("  Rating mode solves r vs pi_avg by a deterministic bisection.");
            sb.AppendLine("  Design mode:  required area = Q_perm / DesignFlux,");
            sb.AppendLine("    required pressure = pi_avg + DesignFlux / A.");
            sb.AppendLine("  Pump power (whole feed from atmospheric):  W = Q_feed * dP / eff_pump.");
            sb.AppendLine("  Energy recovery:  W_ERD = Q_conc * dP * eff_ERD  (PX ~96%, turbine ~80%);");
            sb.AppendLine("    net pump = W - W_ERD;  SEC = W / Q_perm;  saving = (W - W_net)/W.");
            sb.AppendLine("  All stream thermodynamics come from the selected Property Package.");
            sb.AppendLine("  Refs: Baker, Membrane Technology & Applications 3e (2012) ch.5;");
            sb.AppendLine("        Fritzmann et al., Desalination 216 (2007) 1-76;");
            sb.AppendLine("        Voutchkov, Desalination Engineering (2013) ch.8.");
            return sb.ToString();
        }
    }

    /// <summary>
    /// OP-NF — Nanofiltration. Selective, "leaky" membrane: multivalent ions are
    /// rejected almost completely while monovalent ions pass substantially. Water
    /// flux is solution-diffusion with a Spiegler-Kedem reflection coefficient on
    /// the average osmotic pressure. Physics lives in <see cref="NfModel"/> (shared
    /// with the validation tests); see docs/OP-NF_MODEL.md.
    /// </summary>
    [ComVisible(true), Guid("74927f7d-a0a8-4b31-b6df-3a283d5582a5"), ProgId("OPBlocks.NF")]
    [CapeName("OP-NF"), CapeVersion("1.0"), CapeVendorURL("https://oneprocess.sim")]
    [CapeDescription("Nanofiltration: selective (Spiegler-Kedem) rejection of multivalent ions with a reflection coefficient; Rating/Design modes.")]
    [CapeAbout("ONE PROCESS Blocks — OP-NF. (c) ONE PROCESS Simulation. See the block report's 'Model & References' section for equations and literature.")]
    [CapeConsumesThermo(true), CapeSupportsThermodynamics11(true)]
    public class Nanofiltration : UnitBase
    {
        public Nanofiltration() : base()
        {
            ComponentName = "OP-NF"; ComponentDescription = "Nanofiltration";
            AddMaterialPort("Feed", "Feed", CapePortDirection.CAPE_INLET);
            AddMaterialPort("Concentrate", "Concentrate (reject)", CapePortDirection.CAPE_OUTLET);
            AddMaterialPort("Permeate", "Permeate", CapePortDirection.CAPE_OUTLET);

            // NOTE: mode selector is a REAL parameter with an integer code, NOT a
            // CAPE-OPEN option/integer parameter — Aspen's grid renders only
            // RealParameter (IATCapeXRealParameterSpec). Any other type blanks the
            // whole grid (diagnosed live on OP-RO 2026-07-14).
            AddRealParameter("CalcMode",
                "Calculation mode: 0 = Rating (area & pressure given -> performance); 1 = Design (target recovery given -> area & pressure)",
                0, 0, 1, "-");

            AddRealParameter("Area", "Total membrane area (Rating input; computed in Design)", 40, 0.1, 1e5, "m2");
            AddRealParameter("WaterPermA", "Water permeability A (NF ~ 5-15)", 8.0, 0.5, 30, "L/m2/h/bar");
            AddRealParameter("MultivalRejection", "Rejection of multivalent ions (Mg, Ca, SO4)", 97.0, 50, 99.9, "%");
            AddRealParameter("MonovalRejection", "Rejection of monovalent ions (Na, Cl, K)", 50.0, 0, 95, "%");
            AddRealParameter("AppliedPressure", "Applied feed pressure (Rating input; computed in Design)", 10, 1, 40, "bar");
            AddRealParameter("ReflectionSigma", "Reflection coefficient sigma (Spiegler-Kedem; 1 = RO limit)", 0.95, 0.5, 1.0, "-");
            AddRealParameter("VantHoffI", "van 't Hoff dissociation factor (2 for NaCl)", 2.0, 1, 4, "-");
            AddRealParameter("PumpEff", "Feed pump efficiency", 80, 30, 95, "%");
            AddRealParameter("MaxRecovery", "Maximum design water recovery (brackish NF ~ 80-90%)", 80, 5, 95, "%");
            AddRealParameter("TargetRecovery", "Target water recovery (Design mode)", 75, 5, 95, "%");
            AddRealParameter("DesignFlux", "Design average permeate flux (Design mode)", 40, 5, 120, "L/m2/h");

            AddOutputParameter("Recovery", "Water recovery", "%");
            AddOutputParameter("PermeateFlow", "Permeate volumetric flow", "m3/h");
            AddOutputParameter("PermeateTDS", "Permeate TDS", "ppm");
            AddOutputParameter("SaltRejObs", "Observed overall salt rejection", "%");
            AddOutputParameter("PermeateFlux", "Permeate water flux", "L/m2/h");
            AddOutputParameter("OsmoticPress", "Feed osmotic pressure", "bar");
            AddOutputParameter("OsmoticPressAvg", "Average osmotic pressure (feed-conc)", "bar");
            AddOutputParameter("EffectiveOsm", "Effective osmotic barrier (sigma x avg)", "bar");
            AddOutputParameter("NDP", "Net driving pressure", "bar");
            AddOutputParameter("ConcentrateTDS", "Concentrate TDS", "ppm");
            AddOutputParameter("FeedTDS", "Feed TDS", "ppm");
            AddOutputParameter("PumpPower", "Feed pump power", "kW");
            AddOutputParameter("SEC", "Specific energy consumption", "kWh/m3");
            AddOutputParameter("RequiredArea", "Required membrane area (Design mode)", "m2");
            AddOutputParameter("RequiredPressure", "Required applied pressure (Design mode)", "bar");
        }
        public override string BlockCode => "OP-NF";

        protected override bool ValidateModel(out string message)
        {
            message = null;
            var f = GetConnectedMaterial("Feed");
            if (f == null) { message = "Connect a feed stream."; return false; }
            if (ProcessOps.IndexOf(f.ComponentIds, "WATER", "H2O") < 0) { message = "Feed must contain water."; return false; }
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
            var feed = RequireMaterial("Feed");
            var perm = RequireMaterial("Permeate");
            var conc = RequireMaterial("Concentrate");
            string[] ids = feed.ComponentIds;
            double[] f = feed.GetOverallMoleFlows();
            int wi = ProcessOps.IndexOf(ids, "WATER", "H2O");
            double tK = feed.Temperature, p = feed.Pressure;
            if (wi < 0)
                ReportWarning("Feed contains no water component — nothing permeates; the whole feed leaves as concentrate.");

            var osmNotes = new System.Collections.Generic.List<string>();
            double piFeed = ProcessOps.OsmoticPressureBar(feed, f, wi, R("VantHoffI"), tK, osmNotes);
            foreach (string n in osmNotes) ReportWarning(n);

            double[] mw;
            bool haveMw = feed.TryGetMolecularWeightsGmol(out mw);
            if (!haveMw)
                ReportWarning("The property package did not supply molecular weights — TDS and salt-rejection results are unavailable this run.");

            // per-component passage: multivalent ions rejected strongly, monovalent weakly
            double multiPass = 1.0 - ProcessOps.Clamp(R("MultivalRejection"), 0, 100) / 100.0;
            double monoPass = 1.0 - ProcessOps.Clamp(R("MonovalRejection"), 0, 100) / 100.0;
            var saltPass = new double[f.Length];
            bool anyMulti = false, anyMono = false;
            for (int i = 0; i < f.Length; i++)
            {
                if (i == wi) { saltPass[i] = 0.0; continue; }
                if (NfModel.IsMultivalent(ids != null && i < ids.Length ? ids[i] : null)) { saltPass[i] = multiPass; anyMulti = true; }
                else { saltPass[i] = monoPass; anyMono = true; }
            }

            var spec = new NfModel.Spec
            {
                CalcMode = NfModel.ModeFromCode(R("CalcMode")),
                AreaM2 = R("Area"),
                WaterPermA = R("WaterPermA"),
                ReflectionSigma = R("ReflectionSigma"),
                AppliedBar = R("AppliedPressure"),
                VantHoffI = R("VantHoffI"),
                PumpEffPct = R("PumpEff"),
                MaxRecoveryPct = R("MaxRecovery"),
                TargetRecoveryPct = R("TargetRecovery"),
                DesignFluxLMH = R("DesignFlux"),
            };

            NfModel.Split split = NfModel.Solve(spec, f, wi, saltPass, haveMw ? mw : null, tK, piFeed);
            ProcessOps.SetSplitOutlets(perm, conc, split.PermMol, split.ConcMol, tK, 101325, tK, p);

            double feedM3s = VolumetricM3S(feed, MoleDerivedKgS(f, mw), "feed", haveMw);
            double permM3s = ProcessOps.Sum(split.PermMol) > 1e-30
                ? VolumetricM3S(perm, MoleDerivedKgS(split.PermMol, mw), "permeate", haveMw) : 0.0;
            NfModel.Energy e = NfModel.CalcEnergy(spec, split.AppliedBarUsed, feedM3s, permM3s);
            double permM3h = permM3s * 3600.0;
            double effOsm = ProcessOps.Clamp(R("ReflectionSigma"), 0, 1) * split.PiAvgBar;

            EmitWarnings(spec, split, effOsm, anyMulti, anyMono);

            Result("Water recovery", split.Recovery * 100, "%", "0.##");
            Result("Permeate flow", permM3h, "m3/h", "0.####");
            if (haveMw)
            {
                Result("Permeate TDS", split.TdsPermPpm, "ppm", "0.#");
                Result("Salt rejection (observed)", split.SaltRejObsPct, "%", "0.##");
            }
            Result("Permeate flux", split.FluxLMH, "L/m2/h", "0.###");
            Result("Feed osmotic pressure", split.PiFeedBar, "bar", "0.##");
            Result("Average osmotic pressure", split.PiAvgBar, "bar", "0.##");
            Result("Effective osmotic barrier", effOsm, "bar", "0.##");
            Result("Net driving pressure", split.NdpBar, "bar", "0.##");
            if (haveMw)
            {
                Result("Concentrate TDS", split.TdsConcPpm, "ppm", "0.#");
                Result("Feed TDS", split.TdsFeedPpm, "ppm", "0.#");
            }
            Result("Pump power", e.PumpKW, "kW", "0.###");
            Result("Specific energy (SEC)", e.SEC, "kWh/m3", "0.###");
            if (spec.CalcMode == NfModel.Mode.Design)
            {
                Result("Required membrane area", split.RequiredAreaM2, "m2", "0.##");
                Result("Required applied pressure", split.RequiredPressureBar, "bar", "0.##");
            }

            SetOutputParameter("Recovery", split.Recovery * 100);
            SetOutputParameter("PermeateFlow", permM3h);
            SetOutputParameter("PermeateTDS", split.TdsPermPpm);
            SetOutputParameter("SaltRejObs", split.SaltRejObsPct);
            SetOutputParameter("PermeateFlux", split.FluxLMH);
            SetOutputParameter("OsmoticPress", split.PiFeedBar);
            SetOutputParameter("OsmoticPressAvg", split.PiAvgBar);
            SetOutputParameter("EffectiveOsm", effOsm);
            SetOutputParameter("NDP", split.NdpBar);
            SetOutputParameter("ConcentrateTDS", split.TdsConcPpm);
            SetOutputParameter("FeedTDS", split.TdsFeedPpm);
            SetOutputParameter("PumpPower", e.PumpKW);
            SetOutputParameter("SEC", e.SEC);
            SetOutputParameter("RequiredArea", split.RequiredAreaM2);
            SetOutputParameter("RequiredPressure", split.RequiredPressureBar);
        }

        private void EmitWarnings(NfModel.Spec spec, NfModel.Split split, double effOsm, bool anyMulti, bool anyMono)
        {
            if (split.NdpBar <= 0)
                ReportWarning(string.Format(
                    "No net driving pressure: the applied pressure ({0:0.#} bar) does not exceed the effective osmotic " +
                    "barrier sigma x avg-osmotic ({1:0.#} bar). Raise the pressure or reduce recovery.",
                    split.AppliedBarUsed, effOsm));

            if (spec.CalcMode == NfModel.Mode.Rating && split.RecoveryCapped)
                ReportWarning(string.Format(
                    "Water recovery limited to the MaxRecovery cap ({0:0.#}%); the membrane/pressure could drive {1:0.#}%. " +
                    "The area may be oversized for this feed, or raise MaxRecovery if the design allows.",
                    spec.MaxRecoveryPct, split.NaturalRecovery * 100));

            if (spec.CalcMode == NfModel.Mode.Design && split.RecoveryCapped)
                ReportWarning(string.Format(
                    "Target recovery ({0:0.#}%) exceeds MaxRecovery ({1:0.#}%); designed at the cap instead.",
                    spec.TargetRecoveryPct, spec.MaxRecoveryPct));

            if (split.AppliedBarUsed > NfModel.MembraneMaxBar)
                ReportWarning(string.Format(
                    "Applied pressure ({0:0.#} bar) exceeds the typical NF element limit (~{1:0} bar). " +
                    "Check the element rating or reduce flux/recovery.", split.AppliedBarUsed, NfModel.MembraneMaxBar));

            if (split.Recovery > NfModel.LowConcRecovery)
                ReportWarning(string.Format(
                    "Recovery ({0:0.#}%) is very high — the concentrate flow is small, raising the scaling/fouling risk " +
                    "(sparingly-soluble CaSO4/CaCO3). Confirm reject flow and antiscalant dosing.", split.Recovery * 100));

            if (!anyMulti && anyMono)
                ReportWarning("No multivalent ion (Mg/Ca/SO4...) recognised in the component list — every solute used the " +
                              "monovalent rejection. NF's selectivity advantage appears only with multivalent species present.");
            if (!anyMulti && !anyMono)
                ReportWarning("Feed has no dissolved species besides water — the permeate equals the feed. Add the ions to the stream composition.");
        }

        /// <summary>Model &amp; References — travels with the simulation (ASCII, for Aspen's report viewer).</summary>
        protected override string BuildReport()
        {
            string body = base.BuildReport();
            var sb = new System.Text.StringBuilder(body);
            sb.AppendLine();
            sb.AppendLine("Model & References");
            sb.AppendLine("  Water flux (solution-diffusion, Spiegler-Kedem):");
            sb.AppendLine("    Jw = A * (dP - sigma * dPi_avg)   [L/m2/h]");
            sb.AppendLine("    A = water permeability, dP = applied - permeate pressure,");
            sb.AppendLine("    sigma = reflection coefficient (1 = RO limit; NF ~ 0.9-0.99),");
            sb.AppendLine("    dPi_avg = 0.5*(pi_feed + pi_conc) (average osmotic pressure over the module).");
            sb.AppendLine("  Osmotic pressure:  pi = i * c * R * T (van 't Hoff), or -(R*T/Vw)*ln(a_w)");
            sb.AppendLine("    when the property package supplies the water activity a_w.");
            sb.AppendLine("  Selective rejection: multivalent ions (Mg, Ca, SO4) use MultivalRejection;");
            sb.AppendLine("    monovalent ions (Na, Cl, K) use MonovalRejection. Passage_i = 1 - Rejection_i;");
            sb.AppendLine("    fraction of component i to permeate = Passage_i * recovery (water: recovery).");
            sb.AppendLine("  Recovery r = permeate water / feed water; capped at MaxRecovery.");
            sb.AppendLine("  Rating mode solves r vs pi_avg by a deterministic bisection.");
            sb.AppendLine("  Design mode:  required area = Q_perm / DesignFlux,");
            sb.AppendLine("    required pressure = sigma*pi_avg + DesignFlux / A.");
            sb.AppendLine("  Pump power (whole feed from atmospheric):  W = Q_feed * dP / eff_pump; SEC = W / Q_perm.");
            sb.AppendLine("  All stream thermodynamics come from the selected Property Package.");
            sb.AppendLine("  Refs: Kedem & Katchalsky, BBA 27 (1958) 229-246;");
            sb.AppendLine("        Spiegler & Kedem, Desalination 1 (1966) 311-326;");
            sb.AppendLine("        Mohammad et al., Desalination 356 (2015) 226-254;");
            sb.AppendLine("        Baker, Membrane Technology & Applications 3e (2012) ch.5.");
            return sb.ToString();
        }
    }

    /// <summary>OP-UF — Ultrafiltration.</summary>
    [ComVisible(true), Guid("6981b638-5dbe-4647-a20b-2c6b9809a301"), ProgId("OPBlocks.UF")]
    [CapeName("OP-UF"), CapeVersion("1.0"), CapeVendorURL("https://oneprocess.sim")]
    [CapeDescription("Ultrafiltration: size-exclusion removal of macromolecules and colloids (no osmotic barrier).")]
    [CapeAbout("ONE PROCESS Blocks — OP-UF. (c) ONE PROCESS Simulation.")]
    [CapeConsumesThermo(true), CapeSupportsThermodynamics11(true)]
    public class Ultrafiltration : UnitBase
    {
        public Ultrafiltration() : base()
        {
            ComponentName = "OP-UF"; ComponentDescription = "Ultrafiltration";
            AddMaterialPort("Feed", "Feed", CapePortDirection.CAPE_INLET);
            AddMaterialPort("Retentate", "Retentate (reject)", CapePortDirection.CAPE_OUTLET);
            AddMaterialPort("Permeate", "Permeate", CapePortDirection.CAPE_OUTLET);
            AddRealParameter("Area", "Membrane area", 40, 0.1, 1e5, "m2");
            AddRealParameter("Permeability", "Permeability", 100, 5, 1000, "L/m2/h/bar");
            AddRealParameter("TMP", "Trans-membrane pressure", 1.0, 0.1, 5, "bar");
            AddRealParameter("Rejection", "Macromolecule rejection", 95, 0, 100, "%");
            AddRealParameter("FoulingFactor", "Fouling flux derating", 0.7, 0.2, 1.0, "-");
        }
        public override string BlockCode => "OP-UF";

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
            var feed = RequireMaterial("Feed"); var perm = RequireMaterial("Permeate"); var ret = RequireMaterial("Retentate");
            double[] f = feed.GetOverallMoleFlows(); int wi = ProcessOps.IndexOf(feed.ComponentIds, "WATER", "H2O");
            double tK = feed.Temperature, p = feed.Pressure;
            double Jw = R("Permeability") * R("TMP") * R("FoulingFactor");
            double permWaterMol = Math.Min(Jw * R("Area") / 3600.0 / 0.0180153, f[wi] * 0.95);
            double recovery = f[wi] > 0 ? permWaterMol / f[wi] : 0;
            double pass = 1.0 - R("Rejection") / 100.0;

            var frac = new double[f.Length];
            for (int i = 0; i < f.Length; i++) frac[i] = (i == wi) ? recovery : pass * recovery;
            ProcessOps.SplitByRecovery(feed, perm, ret, frac, tK, 101325, tK, p);

            Result("Water recovery", recovery * 100, "%", "0.##");
            Result("Permeate flux", Jw, "L/m2/h", "0.###");
            Result("Rejection", R("Rejection"), "%", "0.#");
        }
    }

    /// <summary>OP-FO — Forward Osmosis.</summary>
    [ComVisible(true), Guid("762666cd-fc8b-42e3-8d25-b78cf74ee588"), ProgId("OPBlocks.FO")]
    [CapeName("OP-FO"), CapeVersion("1.0"), CapeVendorURL("https://oneprocess.sim")]
    [CapeDescription("Forward osmosis: osmotically-driven water transport into a draw solution.")]
    [CapeAbout("ONE PROCESS Blocks — OP-FO. (c) ONE PROCESS Simulation.")]
    [CapeConsumesThermo(true), CapeSupportsThermodynamics11(true)]
    public class ForwardOsmosis : UnitBase
    {
        public ForwardOsmosis() : base()
        {
            ComponentName = "OP-FO"; ComponentDescription = "Forward Osmosis";
            AddMaterialPort("FeedIn", "Feed in", CapePortDirection.CAPE_INLET);
            AddMaterialPort("FeedOut", "Feed out (concentrated)", CapePortDirection.CAPE_OUTLET);
            AddMaterialPort("DrawIn", "Draw solution in", CapePortDirection.CAPE_INLET);
            AddMaterialPort("DrawOut", "Draw solution out (diluted)", CapePortDirection.CAPE_OUTLET);
            AddRealParameter("Area", "Membrane area", 10, 0.1, 1e5, "m2");
            AddRealParameter("WaterPermA", "Water permeability A", 1.0, 0.1, 10, "L/m2/h/bar");
            AddRealParameter("SaltPermB", "Salt permeability B (reverse flux)", 0.3, 0, 5, "L/m2/h");
            AddRealParameter("Reflection", "Reflection coefficient σ", 0.95, 0.5, 1.0, "-");
            AddRealParameter("VantHoffI", "van 't Hoff dissociation factor", 2.0, 1, 4, "-");
        }
        public override string BlockCode => "OP-FO";

        protected override bool ValidateModel(out string message)
        {
            message = null;
            var f = GetConnectedMaterial("FeedIn"); var d = GetConnectedMaterial("DrawIn");
            if (f == null || d == null) { message = "Connect both FeedIn and DrawIn streams."; return false; }
            if (ProcessOps.IndexOf(f.ComponentIds, "WATER", "H2O") < 0) { message = "Streams must contain water."; return false; }
            return true;
        }

        protected override void Compute()
        {
            var fIn = RequireMaterial("FeedIn"); var fOut = RequireMaterial("FeedOut");
            var dIn = RequireMaterial("DrawIn"); var dOut = RequireMaterial("DrawOut");
            int wi = ProcessOps.IndexOf(fIn.ComponentIds, "WATER", "H2O");
            double[] ff = fIn.GetOverallMoleFlows(); double[] dd = dIn.GetOverallMoleFlows();
            double Tf = fIn.Temperature, Td = dIn.Temperature, Pf = fIn.Pressure, Pd = dIn.Pressure;

            var osmNotes = new System.Collections.Generic.List<string>();
            double piFeed = ProcessOps.OsmoticPressureBar(fIn, ff, wi, R("VantHoffI"), Tf, osmNotes);
            double piDraw = ProcessOps.OsmoticPressureBar(dIn, dd, wi, R("VantHoffI"), Td, osmNotes);
            foreach (string n in osmNotes) ReportWarning(n);
            double dPi = Math.Max(0, R("Reflection") * (piDraw - piFeed));
            if (dPi <= 0)
                ReportWarning(string.Format(
                    "No water transfer: the draw solution's osmotic pressure ({0:0.#} bar) does not exceed the feed's ({1:0.#} bar). " +
                    "The draw solution must be more concentrated than the feed.", piDraw, piFeed));
            double Jw = R("WaterPermA") * dPi;                                        // L/m2/h
            double waterMol = Math.Min(Jw * R("Area") / 3600.0 / 0.0180153, ff[wi] * 0.9);
            double revSaltMol = R("SaltPermB") * R("Area") / 3600.0 / 0.0180153 * 0.05; // small reverse flux

            var fof = (double[])ff.Clone(); fof[wi] -= waterMol;
            var dof = (double[])dd.Clone(); dof[wi] += waterMol;
            fOut.SetOutletTP(fof, Tf, Pf);
            dOut.SetOutletTP(dof, Td, Pd);

            Result("Water flux", Jw, "L/m2/h", "0.###");
            Result("Water transferred", waterMol * 0.0180153, "kg/s", "0.#####");
            Result("Draw osmotic pressure", piDraw, "bar", "0.##");
            Result("Net osmotic driving force", dPi, "bar", "0.##");
        }
    }

    /// <summary>OP-PRO — Pressure-Retarded Osmosis (osmotic power).</summary>
    [ComVisible(true), Guid("34d9ffda-378a-4bb0-9b3b-6437fec531dc"), ProgId("OPBlocks.PRO")]
    [CapeName("OP-PRO"), CapeVersion("1.0"), CapeVendorURL("https://oneprocess.sim")]
    [CapeDescription("Pressure-retarded osmosis: osmotic power from a salinity gradient.")]
    [CapeAbout("ONE PROCESS Blocks — OP-PRO. (c) ONE PROCESS Simulation.")]
    [CapeConsumesThermo(true), CapeSupportsThermodynamics11(true)]
    public class PressureRetardedOsmosis : UnitBase
    {
        public PressureRetardedOsmosis() : base()
        {
            ComponentName = "OP-PRO"; ComponentDescription = "Pressure-Retarded Osmosis";
            AddMaterialPort("FeedIn", "Low-salinity feed in", CapePortDirection.CAPE_INLET);
            AddMaterialPort("FeedOut", "Feed out", CapePortDirection.CAPE_OUTLET);
            AddMaterialPort("DrawIn", "High-salinity draw in", CapePortDirection.CAPE_INLET);
            AddMaterialPort("DrawOut", "Draw out (pressurised, diluted)", CapePortDirection.CAPE_OUTLET);
            AddRealParameter("Area", "Membrane area", 10, 0.1, 1e5, "m2");
            AddRealParameter("WaterPermA", "Water permeability A", 1.0, 0.1, 10, "L/m2/h/bar");
            AddRealParameter("AppliedPressure", "Applied hydraulic pressure ΔP", 12, 0, 40, "bar");
            AddRealParameter("VantHoffI", "van 't Hoff dissociation factor", 2.0, 1, 4, "-");
        }
        public override string BlockCode => "OP-PRO";

        protected override bool ValidateModel(out string message)
        {
            message = null;
            var f = GetConnectedMaterial("FeedIn"); var d = GetConnectedMaterial("DrawIn");
            if (f == null || d == null) { message = "Connect both FeedIn and DrawIn streams."; return false; }
            if (ProcessOps.IndexOf(f.ComponentIds, "WATER", "H2O") < 0) { message = "Streams must contain water."; return false; }
            return true;
        }

        protected override void Compute()
        {
            var fIn = RequireMaterial("FeedIn"); var fOut = RequireMaterial("FeedOut");
            var dIn = RequireMaterial("DrawIn"); var dOut = RequireMaterial("DrawOut");
            int wi = ProcessOps.IndexOf(fIn.ComponentIds, "WATER", "H2O");
            double[] ff = fIn.GetOverallMoleFlows(); double[] dd = dIn.GetOverallMoleFlows();
            double Tf = fIn.Temperature, Td = dIn.Temperature, Pf = fIn.Pressure, Pd = dIn.Pressure;

            var osmNotes = new System.Collections.Generic.List<string>();
            double piFeed = ProcessOps.OsmoticPressureBar(fIn, ff, wi, R("VantHoffI"), Tf, osmNotes);
            double piDraw = ProcessOps.OsmoticPressureBar(dIn, dd, wi, R("VantHoffI"), Td, osmNotes);
            foreach (string n in osmNotes) ReportWarning(n);
            double dP = R("AppliedPressure");
            double ndf = Math.Max(0, (piDraw - piFeed) - dP);                 // net driving pressure, bar
            if (ndf <= 0)
                ReportWarning(string.Format(
                    "No power production: the salinity-gradient driving force (Δπ = {0:0.#} bar) does not exceed " +
                    "the applied hydraulic pressure ({1:0.#} bar).", piDraw - piFeed, dP));
            double Jw = R("WaterPermA") * ndf;                                // L/m2/h
            double waterVolM3s = Jw * R("Area") / 1000.0 / 3600.0;            // m3/s
            double waterMol = Math.Min(waterVolM3s * 1000.0 / 0.0180153, ff[wi] * 0.9);
            double powerKW = dP * 1e5 * waterVolM3s / 1000.0;                  // W -> kW

            var fof = (double[])ff.Clone(); fof[wi] -= waterMol;
            var dof = (double[])dd.Clone(); dof[wi] += waterMol;
            fOut.SetOutletTP(fof, Tf, Pf);
            dOut.SetOutletTP(dof, Td, Pd + dP * 1e5);

            Result("Water flux", Jw, "L/m2/h", "0.###");
            Result("Power density", R("Area") > 0 ? powerKW * 1000.0 / R("Area") : 0, "W/m2", "0.###");
            Result("Gross power", powerKW, "kW", "0.###");
            Result("Net osmotic driving force", ndf, "bar", "0.##");
        }
    }
}
