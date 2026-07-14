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
    [CapeDescription("Reverse osmosis: pressure-driven desalination (solution-diffusion, van 't Hoff osmotic).")]
    [CapeAbout("ONE PROCESS Blocks — OP-RO. (c) ONE PROCESS Simulation.")]
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
            AddRealParameter("Area", "Total membrane area", 40, 0.1, 1e5, "m2");
            AddRealParameter("WaterPermA", "Water permeability A", 1.0, 0.05, 20, "L/m2/h/bar");
            AddRealParameter("SaltRejection", "Observed salt rejection", 99.0, 50, 99.9, "%");
            AddRealParameter("AppliedPressure", "Applied feed pressure", 55, 5, 120, "bar");
            AddRealParameter("VantHoffI", "van 't Hoff dissociation factor", 2.0, 1, 4, "-");
            AddRealParameter("PumpEff", "High-pressure pump efficiency", 80, 30, 95, "%");
            // The block's results table (owner spec): rendered by Aspen as the
            // CAPE-OPEN output-parameter grid, values published in Compute.
            AddOutputParameter("Recovery", "Water recovery", "%");
            AddOutputParameter("PermeateFlow", "Permeate volumetric flow", "m3/h");
            AddOutputParameter("PermeateTDS", "Permeate TDS", "ppm");
            AddOutputParameter("SaltRejObs", "Observed salt rejection", "%");
            AddOutputParameter("PumpPower", "High-pressure pump power", "kW");
            AddOutputParameter("SEC", "Specific energy consumption", "kWh/m3");
            AddOutputParameter("PermeateFlux", "Permeate water flux", "L/m2/h");
            AddOutputParameter("OsmoticPress", "Feed osmotic pressure", "bar");
            AddOutputParameter("NDP", "Net driving pressure", "bar");
            AddOutputParameter("ConcentrateTDS", "Concentrate TDS", "ppm");
            AddOutputParameter("FeedTDS", "Feed TDS", "ppm");
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

        protected override void Compute()
        {
            var feed = RequireMaterial("Feed"); var perm = RequireMaterial("Permeate"); var conc = RequireMaterial("Concentrate");
            double[] f = feed.GetOverallMoleFlows(); int wi = ProcessOps.IndexOf(feed.ComponentIds, "WATER", "H2O");
            double tK = feed.Temperature, p = feed.Pressure;
            if (wi < 0)
                ReportWarning("Feed contains no water component — nothing permeates; the whole feed leaves as concentrate.");

            var osmNotes = new System.Collections.Generic.List<string>();
            double piBar = ProcessOps.OsmoticPressureBar(feed, f, wi, R("VantHoffI"), tK, osmNotes);
            foreach (string n in osmNotes) ReportWarning(n);
            double ndp = Math.Max(0, R("AppliedPressure") - piBar);
            double Jw = R("WaterPermA") * ndp;                    // L/m2/h
            if (ndp <= 0)
                ReportWarning(string.Format(
                    "No permeation: applied pressure ({0:0.#} bar) is at or below the feed osmotic pressure ({1:0.#} bar). " +
                    "Raise AppliedPressure above {1:0.#} bar or dilute the feed.",
                    R("AppliedPressure"), piBar));
            // CAPE-OPEN mole flows are mol/s (spec; Aspen V14 and DWSIM both comply).
            // The split physics below is ratio-based; the flux-vs-feed comparison
            // uses these mol/s magnitudes directly.
            double feedWaterMol = wi >= 0 ? f[wi] : 0.0;               // mol/s
            double permWaterMol = Math.Min(Jw * R("Area") / 3600.0 / 0.0180153, feedWaterMol * 0.95);
            double recovery = feedWaterMol > 0 ? permWaterMol / feedWaterMol : 0;
            double saltPass = 1.0 - R("SaltRejection") / 100.0;

            var frac = new double[f.Length];
            for (int i = 0; i < f.Length; i++) frac[i] = (i == wi) ? recovery : saltPass * recovery;
            var permMol = new double[f.Length];
            var concMol = new double[f.Length];
            ProcessOps.SplitFlows(f, frac, permMol, concMol);
            ProcessOps.SetSplitOutlets(perm, conc, permMol, concMol, tK, 101325, tK, p);

            // ---- mass-based results, thermo from the host package (R4) ----
            // TDS figures are MASS RATIOS of mole flows × package molecular weights,
            // so they are immune to any host unit convention.
            double[] mw;
            bool haveMw = feed.TryGetMolecularWeightsGmol(out mw);
            if (!haveMw)
                ReportWarning("The property package did not supply molecular weights — " +
                              "TDS and salt-rejection results are unavailable this run.");

            double feedKgS = 0, permKgS = 0, concKgS = 0, permSaltKgS = 0, concSaltKgS = 0, feedSaltKgS = 0;
            if (haveMw)
            {
                for (int i = 0; i < f.Length; i++)
                {
                    double kgMol = mw[i] / 1000.0;               // g/mol -> kg/mol
                    feedKgS += f[i] * kgMol;
                    permKgS += permMol[i] * kgMol;
                    concKgS += concMol[i] * kgMol;
                    if (i != wi)
                    {
                        feedSaltKgS += f[i] * kgMol;
                        permSaltKgS += permMol[i] * kgMol;
                        concSaltKgS += concMol[i] * kgMol;
                    }
                }
            }

            // ---- volumetric flows: package mass flow ÷ package density ----
            // Some hosts return mass-basis quantities in their own mass unit (Aspen
            // V14's socket answers g/s and g/m3 — proven live 2026-07-14, where
            // mixing our kg/s with its g/m3 made volumetric results 1000× small).
            // Dividing the package's own mass flow by its own density cancels the
            // unit, so V̇ is correct on every host. Our mol/s × MW mass is only the
            // fallback, paired with the 1000 kg/m3 fallback density (§5 rule 5).
            double feedM3s = VolumetricM3S(feed, feedKgS, "feed", haveMw);
            double permM3h = 0.0;
            double permTotalMol = ProcessOps.Sum(permMol);
            if (permTotalMol > 1e-30)
                permM3h = VolumetricM3S(perm, permKgS, "permeate", haveMw) * 3600.0;

            // High-pressure pump duty: pressurise the whole feed from atmospheric
            // intake to the applied pressure (no energy-recovery device modelled).
            double pumpPa = Math.Max(0, R("AppliedPressure") * 1e5 - 101325.0);
            double pumpKW = R("PumpEff") > 0 ? pumpPa * feedM3s / (R("PumpEff") / 100.0) / 1000.0 : 0.0;
            double sec = permM3h > 1e-12 ? pumpKW / permM3h : 0.0;

            double tdsPerm = permKgS > 1e-30 ? permSaltKgS / permKgS * 1e6 : 0.0;   // ppm = mg/kg
            double tdsConc = concKgS > 1e-30 ? concSaltKgS / concKgS * 1e6 : 0.0;
            double tdsFeed = feedKgS > 1e-30 ? feedSaltKgS / feedKgS * 1e6 : 0.0;
            double rejObs = tdsFeed > 1e-12 ? (1.0 - tdsPerm / tdsFeed) * 100.0 : 0.0;

            Result("Water recovery", recovery * 100, "%", "0.##");
            Result("Permeate flow", permM3h, "m3/h", "0.####");
            if (haveMw)
            {
                Result("Permeate TDS", tdsPerm, "ppm", "0.#");
                Result("Salt rejection (observed)", rejObs, "%", "0.##");
            }
            Result("Pump power", pumpKW, "kW", "0.###");
            Result("Specific energy (SEC)", sec, "kWh/m3", "0.###");
            Result("Permeate flux", Jw, "L/m2/h", "0.###");
            Result("Feed osmotic pressure", piBar, "bar", "0.##");
            Result("Net driving pressure", ndp, "bar", "0.##");
            if (haveMw)
            {
                Result("Concentrate TDS", tdsConc, "ppm", "0.#");
                Result("Feed TDS", tdsFeed, "ppm", "0.#");
            }

            // publish the same figures to the host-rendered results table
            SetOutputParameter("Recovery", recovery * 100);
            SetOutputParameter("PermeateFlow", permM3h);
            SetOutputParameter("PermeateTDS", tdsPerm);
            SetOutputParameter("SaltRejObs", rejObs);
            SetOutputParameter("PumpPower", pumpKW);
            SetOutputParameter("SEC", sec);
            SetOutputParameter("PermeateFlux", Jw);
            SetOutputParameter("OsmoticPress", piBar);
            SetOutputParameter("NDP", ndp);
            SetOutputParameter("ConcentrateTDS", tdsConc);
            SetOutputParameter("FeedTDS", tdsFeed);
        }
    }

    /// <summary>OP-NF — Nanofiltration.</summary>
    [ComVisible(true), Guid("74927f7d-a0a8-4b31-b6df-3a283d5582a5"), ProgId("OPBlocks.NF")]
    [CapeName("OP-NF"), CapeVersion("1.0"), CapeVendorURL("https://oneprocess.sim")]
    [CapeDescription("Nanofiltration: selective rejection of multivalent ions / small organics.")]
    [CapeAbout("ONE PROCESS Blocks — OP-NF. (c) ONE PROCESS Simulation.")]
    [CapeConsumesThermo(true), CapeSupportsThermodynamics11(true)]
    public class Nanofiltration : UnitBase
    {
        public Nanofiltration() : base()
        {
            ComponentName = "OP-NF"; ComponentDescription = "Nanofiltration";
            AddMaterialPort("Feed", "Feed", CapePortDirection.CAPE_INLET);
            AddMaterialPort("Concentrate", "Concentrate (reject)", CapePortDirection.CAPE_OUTLET);
            AddMaterialPort("Permeate", "Permeate", CapePortDirection.CAPE_OUTLET);
            AddRealParameter("Area", "Membrane area", 40, 0.1, 1e5, "m2");
            AddRealParameter("WaterPermA", "Water permeability A", 6.0, 0.5, 30, "L/m2/h/bar");
            AddRealParameter("SaltRejection", "Rejection (multivalent)", 90.0, 20, 99, "%");
            AddRealParameter("AppliedPressure", "Applied feed pressure", 12, 2, 40, "bar");
            AddRealParameter("VantHoffI", "van 't Hoff dissociation factor", 2.0, 1, 4, "-");
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

        protected override void Compute()
        {
            var feed = RequireMaterial("Feed"); var perm = RequireMaterial("Permeate"); var conc = RequireMaterial("Concentrate");
            double[] f = feed.GetOverallMoleFlows(); int wi = ProcessOps.IndexOf(feed.ComponentIds, "WATER", "H2O");
            double tK = feed.Temperature, p = feed.Pressure;
            var osmNotes = new System.Collections.Generic.List<string>();
            double piBar = ProcessOps.OsmoticPressureBar(feed, f, wi, R("VantHoffI"), tK, osmNotes) * 0.5; // partial rejection
            foreach (string n in osmNotes) ReportWarning(n);
            double ndp = Math.Max(0, R("AppliedPressure") - piBar);
            if (ndp <= 0)
                ReportWarning(string.Format(
                    "No permeation: applied pressure ({0:0.#} bar) is at or below the effective osmotic pressure ({1:0.#} bar).",
                    R("AppliedPressure"), piBar));
            double Jw = R("WaterPermA") * ndp;
            double permWaterMol = Math.Min(Jw * R("Area") / 3600.0 / 0.0180153, f[wi] * 0.95);
            double recovery = f[wi] > 0 ? permWaterMol / f[wi] : 0;
            double saltPass = 1.0 - R("SaltRejection") / 100.0;

            var frac = new double[f.Length];
            for (int i = 0; i < f.Length; i++) frac[i] = (i == wi) ? recovery : saltPass * recovery;
            ProcessOps.SplitByRecovery(feed, perm, conc, frac, tK, 101325, tK, p);

            Result("Water recovery", recovery * 100, "%", "0.##");
            Result("Permeate flux", Jw, "L/m2/h", "0.###");
            Result("Multivalent rejection", R("SaltRejection"), "%", "0.#");
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
