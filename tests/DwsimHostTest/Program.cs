using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DWSIM.Drawing.SkiaSharp.GraphicObjects.Shapes;
using DWSIM.Interfaces;
using SkiaSharp;

namespace OPBlocks.DwsimHostTest
{
    /// <summary>
    /// Evidence harness for P1A: replays DWSIM's own unitops loading pipeline
    /// (SharedClasses.Utility.LoadAdditionalUnitOperations) against the built
    /// OPBlocks.DWSIM adapter, then exercises identity, connectors, flowsheet
    /// drawing and state persistence for every block. Exit code 0 = all pass.
    ///
    /// Usage: DwsimHostTest.exe [path-to-staged-unitops-folder]
    /// </summary>
    internal static class Program
    {
        private static readonly string DwsimDir =
            Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\DWSIM");

        private static int _failures;

        [STAThread]
        private static int Main(string[] args)
        {
            // Resolve DWSIM's own assemblies from its install folder, exactly as
            // they resolve inside DWSIM's process (application base).
            AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
            {
                try
                {
                    string name = new AssemblyName(e.Name).Name;
                    string p = Path.Combine(DwsimDir, name + ".dll");
                    return File.Exists(p) ? Assembly.LoadFrom(p) : null;
                }
                catch { return null; }
            };
            return Run(args);
        }

        private static int Run(string[] args)
        {
            string unitopsDir = args.Length > 0
                ? Path.GetFullPath(args[0])
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "staged-unitops");

            Console.WriteLine("== OP-Blocks DWSIM host test ==");
            Console.WriteLine("DWSIM:   " + DwsimDir);
            Console.WriteLine("unitops: " + unitopsDir);
            Console.WriteLine();

            string adapterPath = Path.Combine(unitopsDir, "OPBlocks.DWSIM.dll");
            Check(File.Exists(adapterPath), "adapter DLL staged", adapterPath);
            if (!File.Exists(adapterPath)) return Fail();

            // --- DWSIM's exact discovery: scan EVERY dll in the folder alphabetically
            //     (SharedClasses.Utility.LoadAdditionalUnitOperations), LoadFile each,
            //     take exported IExternalUnitOperation types. The OPBlocks family DLLs
            //     are probed before the adapter, which must not break anything.
            Assembly asm = null;
            var types = new List<Type>();
            foreach (string dll in Directory.GetFiles(unitopsDir, "*.dll", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    Assembly a = Assembly.LoadFile(dll);
                    var found = a.ExportedTypes
                        .Where(t => t.GetInterfaces().Contains(typeof(IExternalUnitOperation)) && !t.IsAbstract)
                        .ToList();
                    types.AddRange(found);
                    if (found.Count > 0) asm = a;
                    Console.WriteLine("  scanned " + Path.GetFileName(dll) + ": " + found.Count + " unit op(s)");
                }
                catch (Exception ex)
                {
                    // DWSIM logs and continues; the family DLLs legitimately fail
                    // ExportedTypes here when probed before the adapter's resolver.
                    Console.WriteLine("  scanned " + Path.GetFileName(dll) + ": skipped (" + ex.GetType().Name + ")");
                }
            }

            Check(types.Count == 25, "25 external unit operations discovered", "found " + types.Count);
            if (asm == null) return Fail();

            var instances = new List<IExternalUnitOperation>();
            foreach (Type t in types)
            {
                try { instances.Add((IExternalUnitOperation)Activator.CreateInstance(t)); }
                catch (Exception ex)
                {
                    Check(false, "instantiate " + t.Name, Root(ex).Message);
                }
            }
            Check(instances.Count == types.Count, "all types instantiate", instances.Count + "/" + types.Count);

            // --- identity: unique, non-empty (Description is DWSIM's dictionary key) ---
            Check(instances.All(i => !string.IsNullOrWhiteSpace(i.Name)), "all Names set", "");
            Check(instances.All(i => !string.IsNullOrWhiteSpace(i.Prefix)), "all Prefixes set", "");
            Check(instances.Select(i => i.Description).Distinct().Count() == instances.Count,
                  "Descriptions unique (palette dictionary key)", "");
            Check(instances.Select(i => i.Prefix).Distinct().Count() == instances.Count,
                  "Prefixes unique", "");

            // --- per-block: graphic attach, connectors, draw, icon, state roundtrip ---
            foreach (IExternalUnitOperation uo in instances)
                ExerciseBlock(uo);

            // --- end-to-end: real DWSIM engine, headless, one full calculation ---
            if (!args.Contains("--no-e2e"))
                RunEndToEnd(asm);

            Console.WriteLine();
            if (_failures == 0)
            {
                Console.WriteLine("ALL CHECKS PASSED (" + instances.Count + " blocks)");
                return 0;
            }
            return Fail();
        }

        /// <summary>
        /// Drives the actual DWSIM calculation engine (Automation2, headless):
        /// water feed -> OP-EVAPPOND -> concentrate + vapour. Proves the wrapped
        /// CAPE-OPEN physics reads and writes native DWSIM streams in-process —
        /// the core P1A architecture claim.
        /// </summary>
        private static void RunEndToEnd(Assembly adapterAsm)
        {
            Console.WriteLine();
            Console.WriteLine("-- end-to-end calculation (DWSIM engine, headless) --");
            try
            {
                var interf = new global::DWSIM.Automation.Automation2();
                var sim = interf.CreateFlowsheet();
                sim.AddCompound("Water");

                var feed = (global::DWSIM.Thermodynamics.Streams.MaterialStream)
                    sim.AddObject(global::DWSIM.Interfaces.Enums.GraphicObjects.ObjectType.MaterialStream, 0, 50, "FEED");
                var conc = (global::DWSIM.Thermodynamics.Streams.MaterialStream)
                    sim.AddObject(global::DWSIM.Interfaces.Enums.GraphicObjects.ObjectType.MaterialStream, 300, 20, "CONC");
                var vap = (global::DWSIM.Thermodynamics.Streams.MaterialStream)
                    sim.AddObject(global::DWSIM.Interfaces.Enums.GraphicObjects.ObjectType.MaterialStream, 300, 80, "VAP");

                var pps = sim.GetAvailablePropertyPackages();
                string ppname = pps.FirstOrDefault(p => p.ToLower().Contains("steam")) ?? pps.First();
                sim.CreateAndAddPropertyPackage(ppname);
                Console.WriteLine("  property package: " + ppname);

                Type pondType = adapterAsm.ExportedTypes.First(t => t.Name == "EvapPondDW");
                var pond = (IExternalUnitOperation)Activator.CreateInstance(pondType);
                var fb = (global::DWSIM.FlowsheetBase.FlowsheetBase)sim;
                fb.AddObjectToSurface(global::DWSIM.Interfaces.Enums.GraphicObjects.ObjectType.External,
                                      150, 50, "POND1", "", pond, false);
                var pondSim = (ISimulationObject)pond;
                pondSim.SetFlowsheet(sim);
                // The GUI assigns the graphic's Owner after AddObjectToSurface and the
                // connectors materialize on the next CreateConnectors call — mirror that.
                pondSim.GraphicObject.Owner = pondSim;
                pond.CreateConnectors();

                pondSim.ConnectFeedMaterialStream(feed, 0);
                pondSim.ConnectProductMaterialStream(conc, 0);
                pondSim.ConnectProductMaterialStream(vap, 1);

                feed.SetTemperature(303.15);   // K
                feed.SetPressure(101325.0);    // Pa
                feed.SetMassFlow(10.0);        // kg/s

                List<Exception> errors = interf.CalculateFlowsheet2(sim);
                Check(errors == null || errors.Count == 0, "e2e: flowsheet solved",
                      errors != null && errors.Count > 0 ? Root(errors[0]).Message : "");

                double fIn = feed.GetMassFlow(), fConc = conc.GetMassFlow(), fVap = vap.GetMassFlow();
                Console.WriteLine("  feed=" + fIn.ToString("0.####") + " kg/s  concentrate=" +
                                  fConc.ToString("0.####") + " kg/s  vapour=" + fVap.ToString("0.####") + " kg/s");
                Check(fVap > 0, "e2e: evaporation occurred (vapour flow > 0)", fVap.ToString("0.######"));
                Check(Math.Abs(fIn - fConc - fVap) < 1e-4 * Math.Max(fIn, 1e-12),
                      "e2e: mass balance closes", "err=" + Math.Abs(fIn - fConc - fVap).ToString("0.#E+0"));

                // Quantitative guard for the parameter-unit chain: with defaults
                // (10,000 m2, 600 W/m2, air 30 C, RH 40%, wind 3 m/s, feed 30 C)
                // the Dalton model gives ~0.24 kg/s evaporation. The v1.0 defect
                // (DimensionedValue treating authored values as SI) fed the model
                // -243 C air and produced ~0.34 kg/s — this catches any regression
                // anywhere in the parameter->physics chain.
                Check(fVap > 0.20 && fVap < 0.30,
                      "e2e: evaporation rate quantitatively correct (~0.24 kg/s)",
                      fVap.ToString("0.####") + " kg/s");

                // results surfaced as host properties
                var props = pondSim.GetProperties(global::DWSIM.Interfaces.Enums.PropertyType.RO);
                Check(props.Length > 0, "e2e: result properties exposed", props.Length + " outputs");

                // QA §3: the block's reported outlet effect must MATCH what the host
                // streams actually carry. EvapPond reports "Evaporation rate" in
                // m3/day of water (kg/s x 86.4); the vapour stream holds the same
                // water in kg/s. Any drift between report and stream is a defect.
                object reported = pondSim.GetPropertyValue("Evaporation rate [m3/day]")
                               ?? pondSim.GetPropertyValue("Evaporation rate");
                if (reported is double repM3Day)
                {
                    double streamM3Day = fVap * 86.4;
                    // 0.1% tolerance: the block uses MW(water)=18.0153 g/mol while the
                    // stream mass comes from DWSIM's compound database — sub-0.01%
                    // rounding, but structural mismatches are orders of magnitude.
                    Check(Math.Abs(repM3Day - streamM3Day) < 1e-3 * Math.Max(streamM3Day, 1e-12),
                          "e2e: block-reported outlet matches host stream",
                          repM3Day.ToString("0.####") + " vs " + streamM3Day.ToString("0.####") + " m3/day");
                }
                else
                {
                    Check(false, "e2e: block-reported outlet matches host stream", "result row not found");
                }

                interf.ReleaseResources();
            }
            catch (Exception ex)
            {
                Check(false, "e2e: engine run", Root(ex).GetType().Name + ": " + Root(ex).Message);
            }
        }

        private static void ExerciseBlock(IExternalUnitOperation uo)
        {
            string code = uo.Name;
            var sim = (ISimulationObject)uo;

            try
            {
                // Attach the same graphic DWSIM creates for external UOs (40x40).
                var graphic = new ExternalUnitOperationGraphic(10, 10, 40, 40) { Owner = sim };
                sim.GraphicObject = graphic;

                uo.CreateConnectors();
                int nIn = graphic.InputConnectors.Count, nOut = graphic.OutputConnectors.Count;
                Check(nIn > 0 && nOut > 0, code + ": connectors created", "in=" + nIn + " out=" + nOut);

                // CreateConnectors must be idempotent (DWSIM calls it every frame).
                uo.CreateConnectors();
                Check(graphic.InputConnectors.Count == nIn && graphic.OutputConnectors.Count == nOut,
                      code + ": connectors idempotent", "");

                // Offscreen flowsheet draw — the custom icon path.
                using (var surface = SKSurface.Create(new SKImageInfo(120, 120)))
                {
                    surface.Canvas.Clear(SKColors.White);
                    uo.Draw(surface.Canvas);
                    surface.Canvas.Flush();

                    using (SKImage snap = surface.Snapshot())
                    using (SKBitmap bmp = SKBitmap.FromImage(snap))
                    {
                        bool painted = false;
                        for (int x = 10; x < 50 && !painted; x++)
                            for (int y = 10; y < 50 && !painted; y++)
                                if (bmp.GetPixel(x, y) != SKColors.White) painted = true;
                        Check(painted, code + ": flowsheet icon painted pixels", "");
                    }
                }

                // Palette icon.
                object bmpObj = sim.GetIconBitmap();
                Check(bmpObj is System.Drawing.Bitmap, code + ": palette icon bitmap", "");

                // DWSIM's unit converter must never see a unit string it doesn't
                // know — it pops "There is no unit named ..." dialogs per property.
                // Allowed: "" (domain units live in the property name) or a string
                // from the active unit set (temperature, pressure, area, ...).
                var si = new global::DWSIM.SharedClasses.SystemsOfUnits.SI();
                var known = new HashSet<string>(new[]
                {
                    "", si.temperature, si.deltaT, si.pressure, si.area, si.distance,
                    si.velocity, si.heatflow, si.enthalpy, si.mass, si.massflow, si.time
                });
                var props = sim.GetProperties(global::DWSIM.Interfaces.Enums.PropertyType.ALL);
                bool unitsClean = true;
                foreach (string prop in props)
                    if (!known.Contains(sim.GetPropertyUnit(prop) ?? ""))
                        unitsClean = false;
                Check(unitsClean, code + ": only DWSIM-known unit strings exposed", "");

                // Unit-system conversion must round-trip: reading a value and writing
                // it back must not drift the underlying block parameter.
                bool roundtrip = true;
                foreach (string prop in sim.GetProperties(global::DWSIM.Interfaces.Enums.PropertyType.RW))
                {
                    object v0 = sim.GetPropertyValue(prop);
                    if (!(v0 is double d0)) continue;
                    sim.SetPropertyValue(prop, d0);
                    double d1 = Convert.ToDouble(sim.GetPropertyValue(prop));
                    if (Math.Abs(d1 - d0) > 1e-9 * Math.Max(1.0, Math.Abs(d0))) roundtrip = false;
                }
                Check(roundtrip, code + ": unit conversion round-trips", "");

                // State roundtrip through the persisted property.
                PropertyInfo state = uo.GetType().GetProperty("BlockStateData");
                string s1 = (string)state.GetValue(uo);
                var clone = (IExternalUnitOperation)uo.ReturnInstance(uo.GetType().FullName);
                state.SetValue(clone, s1);
                string s2 = (string)state.GetValue(clone);
                Check(s1 == s2, code + ": parameter state roundtrip", "");
            }
            catch (Exception ex)
            {
                Check(false, code + ": exercise", Root(ex).GetType().Name + ": " + Root(ex).Message);
            }
        }

        private static void Check(bool ok, string what, string detail)
        {
            if (!ok) _failures++;
            Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + what +
                              (string.IsNullOrEmpty(detail) ? "" : "  [" + detail + "]"));
        }

        private static Exception Root(Exception ex)
        {
            while (ex.InnerException != null) ex = ex.InnerException;
            return ex;
        }

        private static int Fail()
        {
            Console.WriteLine(_failures + " FAILURE(S)");
            return 1;
        }
    }
}
