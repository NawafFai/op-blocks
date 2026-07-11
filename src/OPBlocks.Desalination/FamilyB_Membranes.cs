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
            AddMaterialPort("Feed", "Pressurised feed", CapePortDirection.CAPE_INLET);
            AddMaterialPort("Permeate", "Permeate (product water)", CapePortDirection.CAPE_OUTLET);
            AddMaterialPort("Concentrate", "Concentrate (reject)", CapePortDirection.CAPE_OUTLET);
            AddRealParameter("Area", "Total membrane area", 40, 0.1, 1e5, "m2");
            AddRealParameter("WaterPermA", "Water permeability A", 1.0, 0.05, 20, "L/m2/h/bar");
            AddRealParameter("SaltRejection", "Observed salt rejection", 99.0, 50, 99.9, "%");
            AddRealParameter("AppliedPressure", "Applied feed pressure", 55, 5, 120, "bar");
            AddRealParameter("VantHoffI", "van 't Hoff dissociation factor", 2.0, 1, 4, "-");
            AddRealParameter("PumpEff", "High-pressure pump efficiency", 80, 30, 95, "%");
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

        protected override void Compute()
        {
            var feed = RequireMaterial("Feed"); var perm = RequireMaterial("Permeate"); var conc = RequireMaterial("Concentrate");
            double[] f = feed.GetOverallMoleFlows(); int wi = ProcessOps.IndexOf(feed.ComponentIds, "WATER", "H2O");
            double tK = feed.Temperature, p = feed.Pressure;
            double saltMol = ProcessOps.Sum(f) - f[wi];

            double molarity = ProcessOps.MolarityMolL(saltMol, f[wi]);
            double piBar = ProcessOps.OsmoticBar(molarity, R("VantHoffI"), tK);
            double ndp = Math.Max(0, R("AppliedPressure") - piBar);
            double Jw = R("WaterPermA") * ndp;                    // L/m2/h
            if (ndp <= 0)
                ReportWarning(string.Format(
                    "No permeation: applied pressure ({0:0.#} bar) is at or below the feed osmotic pressure ({1:0.#} bar). " +
                    "Raise AppliedPressure above {1:0.#} bar or dilute the feed.",
                    R("AppliedPressure"), piBar));
            double permWaterMol = Math.Min(Jw * R("Area") / 3600.0 / 0.0180153, f[wi] * 0.95);
            double recovery = f[wi] > 0 ? permWaterMol / f[wi] : 0;
            double saltPass = 1.0 - R("SaltRejection") / 100.0;

            var frac = new double[f.Length];
            for (int i = 0; i < f.Length; i++) frac[i] = (i == wi) ? recovery : saltPass * recovery;
            ProcessOps.SplitByRecovery(feed, perm, conc, frac, tK, 101325, tK, p);

            double permKgS = permWaterMol * 0.0180153;
            double permM3h = permKgS / 1000.0 * 3600.0;
            double pumpKW = R("AppliedPressure") * 1e5 * (permKgS / 1000.0) / (R("PumpEff") / 100.0) / 1000.0;
            Result("Water recovery", recovery * 100, "%", "0.##");
            Result("Permeate flux", Jw, "L/m2/h", "0.###");
            Result("Permeate flow", permM3h, "m3/h", "0.####");
            Result("Feed osmotic pressure", piBar, "bar", "0.##");
            Result("Specific energy (pump)", permM3h > 0 ? pumpKW / permM3h : 0, "kWh/m3", "0.##");
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
            AddMaterialPort("Permeate", "Permeate", CapePortDirection.CAPE_OUTLET);
            AddMaterialPort("Concentrate", "Concentrate", CapePortDirection.CAPE_OUTLET);
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
            double saltMol = ProcessOps.Sum(f) - f[wi];
            double piBar = ProcessOps.OsmoticBar(ProcessOps.MolarityMolL(saltMol, f[wi]), R("VantHoffI"), tK) * 0.5; // partial rejection
            double Jw = R("WaterPermA") * Math.Max(0, R("AppliedPressure") - piBar);
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
            AddMaterialPort("Permeate", "Permeate", CapePortDirection.CAPE_OUTLET);
            AddMaterialPort("Retentate", "Retentate", CapePortDirection.CAPE_OUTLET);
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

            double piFeed = ProcessOps.OsmoticBar(ProcessOps.MolarityMolL(ProcessOps.Sum(ff) - ff[wi], ff[wi]), R("VantHoffI"), Tf);
            double piDraw = ProcessOps.OsmoticBar(ProcessOps.MolarityMolL(ProcessOps.Sum(dd) - dd[wi], dd[wi]), R("VantHoffI"), Td);
            double dPi = Math.Max(0, R("Reflection") * (piDraw - piFeed));
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

            double piFeed = ProcessOps.OsmoticBar(ProcessOps.MolarityMolL(ProcessOps.Sum(ff) - ff[wi], ff[wi]), R("VantHoffI"), Tf);
            double piDraw = ProcessOps.OsmoticBar(ProcessOps.MolarityMolL(ProcessOps.Sum(dd) - dd[wi], dd[wi]), R("VantHoffI"), Td);
            double dP = R("AppliedPressure");
            double ndf = Math.Max(0, (piDraw - piFeed) - dP);                 // net driving pressure, bar
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
