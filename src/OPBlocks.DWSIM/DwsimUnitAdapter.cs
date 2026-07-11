using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using CapeOpen;
using DWSIM.Drawing.SkiaSharp.GraphicObjects;
using DWSIM.Interfaces;
using DWSIM.Interfaces.Enums;
using DWSIM.Interfaces.Enums.GraphicObjects;
using DWSIM.UnitOperations.UnitOperations;
using Newtonsoft.Json;
using OPBlocks.Core;
using OPBlocks.Core.Diagnostics;
using SkiaSharp;

namespace OPBlocks.DWSIM
{
    /// <summary>
    /// DWSIM-native adapter for ONE PROCESS blocks (session prompt §1A).
    ///
    /// Each concrete adapter wraps exactly one <see cref="UnitBase"/> block from the
    /// existing CAPE-OPEN family DLLs and drives it in-process — zero physics
    /// duplication. The bridge works because DWSIM's MaterialStream natively
    /// implements ICapeThermoMaterialObject (the same CapeOpen.dll assembly DWSIM
    /// ships), so the block's ThermoProxy calls write straight into DWSIM streams
    /// with no COM involved.
    ///
    /// The adapter also renders the block's own icon on the flowsheet via
    /// <see cref="Draw"/> (SkiaSharp canvas) — the competitive feature CAPE-OPEN
    /// hosting cannot give us — and anchors connectors to the icon edges.
    /// </summary>
    public abstract class DwsimUnitAdapter : UnitOpBaseClass, IExternalUnitOperation
    {
        // Flowsheet icons decoded once per block code, shared by all instances.
        private static readonly Dictionary<string, SKImage> IconCache =
            new Dictionary<string, SKImage>(StringComparer.OrdinalIgnoreCase);
        private static readonly object IconCacheLock = new object();

        private readonly string _prefix;
        private UnitBase _inner;
        private UnitBase.ResultEntry[] _loadedResults;
        private string[] _loadedWarnings;

        protected DwsimUnitAdapter(string prefix, SimulationObjectClass objectClass)
        {
            _prefix = prefix;
            ObjectClass = objectClass;
            ComponentName = Inner.ComponentName;
            ComponentDescription = Inner.ComponentDescription;
        }

        /// <summary>Creates the wrapped CAPE-OPEN block (the single source of physics).</summary>
        protected abstract UnitBase CreateBlock();

        /// <summary>The wrapped block. Created lazily; parameters live here.</summary>
        public UnitBase Inner
        {
            get
            {
                if (_inner == null) _inner = CreateBlock();
                return _inner;
            }
        }

        public override SimulationObjectClass ObjectClass { get; set; }

        public override bool MobileCompatible { get { return false; } }

        // ------------------------------------------------------------------
        //  IExternalUnitOperation — identity
        // ------------------------------------------------------------------

        /// <summary>Palette display name, e.g. "Solar Evaporation Pond (OP-EVAPPOND)".</summary>
        string IExternalUnitOperation.Name
        {
            get { return Inner.ComponentDescription + " (" + Inner.BlockCode + ")"; }
        }

        /// <summary>
        /// Unique, stable key: DWSIM keys its ExternalUnitOperations dictionary on
        /// this string and re-binds saved flowsheets by it. Never change it between
        /// releases or saved files stop resolving.
        /// </summary>
        public string Description
        {
            get { return "ONE PROCESS " + Inner.BlockCode + " — " + Inner.ComponentDescription; }
        }

        public string Prefix { get { return _prefix; } }

        public object ReturnInstance(string typename)
        {
            return Activator.CreateInstance(GetType());
        }

        public override string GetDisplayName() { return ((IExternalUnitOperation)this).Name; }
        public override string GetDisplayDescription() { return Description; }

        // ------------------------------------------------------------------
        //  Flowsheet rendering — the custom icon (P1A goal)
        // ------------------------------------------------------------------

        public void Draw(object g)
        {
            try
            {
                var canvas = g as SKCanvas;
                var go = GraphicObject;
                if (canvas == null || go == null) return;

                var rect = new SKRect(go.X, go.Y, go.X + go.Width, go.Y + go.Height);
                SKImage icon = GetFlowsheetIcon(Inner.BlockCode);
                if (icon != null)
                {
                    using (var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true })
                        canvas.DrawImage(icon, rect, paint);
                }
                else
                {
                    DrawFallback(canvas, rect);
                }
            }
            catch (Exception ex)
            {
                // Icon failure must never break the flowsheet (spec §7: R3 > R2).
                try { OpLog.Info(Inner.BlockCode, "DWSIM Draw failed: " + ex.Message); } catch { }
            }
        }

        private void DrawFallback(SKCanvas canvas, SKRect rect)
        {
            using (var fill = new SKPaint { Color = new SKColor(0x0E, 0x7C, 0x66), Style = SKPaintStyle.Stroke, StrokeWidth = 2, IsAntialias = true })
            using (var text = new SKPaint { Color = new SKColor(0x0E, 0x7C, 0x66), TextSize = 9, IsAntialias = true, TextAlign = SKTextAlign.Center })
            {
                canvas.DrawRoundRect(rect, 4, 4, fill);
                canvas.DrawText("OP", rect.MidX, rect.MidY + 3, text);
            }
        }

        private static SKImage GetFlowsheetIcon(string blockCode)
        {
            lock (IconCacheLock)
            {
                SKImage cached;
                if (IconCache.TryGetValue(blockCode, out cached)) return cached;

                SKImage img = null;
                try
                {
                    using (Stream s = typeof(DwsimUnitAdapter).Assembly.GetManifestResourceStream(blockCode + ".png"))
                    {
                        if (s != null)
                        {
                            using (var ms = new MemoryStream())
                            {
                                s.CopyTo(ms);
                                using (var bmp = SKBitmap.Decode(ms.ToArray()))
                                    if (bmp != null) img = SKImage.FromBitmap(bmp);
                            }
                        }
                    }
                }
                catch { img = null; }
                IconCache[blockCode] = img; // cache misses too — don't retry every frame
                return img;
            }
        }

        /// <summary>Palette / object-tree icon (System.Drawing bitmap).</summary>
        public override object GetIconBitmap()
        {
            try
            {
                using (Stream s = typeof(DwsimUnitAdapter).Assembly.GetManifestResourceStream(Inner.BlockCode + ".png"))
                    if (s != null) return new Bitmap(s);
            }
            catch { }
            return new Bitmap(32, 32);
        }

        // ------------------------------------------------------------------
        //  Connectors — mirror the block's CAPE-OPEN port list
        // ------------------------------------------------------------------

        public void CreateConnectors()
        {
            var go = GraphicObject;
            if (go == null) return;

            var inlets = new List<UnitBase.PortInfo>();
            var outlets = new List<UnitBase.PortInfo>();
            foreach (UnitBase.PortInfo p in Inner.PortLayout)
            {
                if (p.IsEnergy) continue; // no OP block declares energy ports (duties are results)
                if (p.IsInlet) inlets.Add(p); else outlets.Add(p);
            }

            float x = go.X, y = go.Y;
            int w = go.Width, h = go.Height;

            for (int i = 0; i < inlets.Count; i++)
            {
                var pos = new global::DWSIM.DrawingTools.Point.Point(x, y + h * (i + 1) / (float)(inlets.Count + 1));
                if (go.InputConnectors.Count <= i)
                {
                    go.InputConnectors.Add(new ConnectionPoint
                    {
                        IsEnergyConnector = false,
                        Type = ConType.ConIn,
                        Direction = ConDir.Right,
                        ConnectorName = inlets[i].Name,
                        Active = true,
                        Position = pos
                    });
                }
                else
                {
                    go.InputConnectors[i].Position = pos;
                    go.InputConnectors[i].ConnectorName = inlets[i].Name;
                    go.InputConnectors[i].Active = true;
                }
            }

            for (int i = 0; i < outlets.Count; i++)
            {
                var pos = new global::DWSIM.DrawingTools.Point.Point(x + w, y + h * (i + 1) / (float)(outlets.Count + 1));
                if (go.OutputConnectors.Count <= i)
                {
                    go.OutputConnectors.Add(new ConnectionPoint
                    {
                        IsEnergyConnector = false,
                        Type = ConType.ConOut,
                        Direction = ConDir.Right,
                        ConnectorName = outlets[i].Name,
                        Active = true,
                        Position = pos
                    });
                }
                else
                {
                    go.OutputConnectors[i].Position = pos;
                    go.OutputConnectors[i].ConnectorName = outlets[i].Name;
                    go.OutputConnectors[i].Active = true;
                }
            }

            go.EnergyConnector.Active = false;
        }

        // ------------------------------------------------------------------
        //  Calculation — bind DWSIM streams to the block's ports, run physics
        // ------------------------------------------------------------------

        public override void Calculate(object args = null)
        {
            UnitBase inner = Inner;
            BindPorts(inner);

            try
            {
                // Friendlier than the generic CAPE-OPEN port message: name every
                // missing connection at once, with direction, so the user fixes
                // the flowsheet in one pass.
                var missing = new List<string>();
                foreach (UnitBase.PortInfo pi in inner.PortLayout)
                {
                    if (pi.IsEnergy) continue;
                    if (pi.Port.connectedObject == null)
                        missing.Add(pi.Name + (pi.IsInlet ? " (inlet)" : " (outlet)"));
                }
                if (missing.Count > 0)
                    throw new Exception(inner.BlockCode + " needs a stream on every port. Missing: " +
                                        string.Join(", ", missing) + ".");

                string msg = "";
                if (!inner.Validate(ref msg))
                    throw new Exception(string.IsNullOrEmpty(msg)
                        ? inner.BlockCode + " is not ready to calculate."
                        : msg);

                // Blank connected outlets first (as DWSIM's own CAPE-OPEN wrapper
                // does) so stale defaults can't survive a partial write.
                ForEachConnectedOutlet(inner, PrepareOutlet);

                inner.OnCalculate();

                // The CAPE-OPEN SetProp("flow") path only writes per-compound molar
                // flows; DWSIM's flash and solver read fractions and phase totals.
                // Normalize each outlet so the native stream state is consistent.
                ForEachConnectedOutlet(inner, FinalizeOutlet);

                _loadedResults = null; // live results now supersede any loaded snapshot
                _loadedWarnings = null;
                Calculated = true;
            }
            catch (CapeUserException cex)
            {
                // Present the actionable CAPE-OPEN message as a plain host error.
                throw new Exception(cex.Message);
            }
        }

        private void ForEachConnectedOutlet(UnitBase inner, Action<global::DWSIM.Thermodynamics.Streams.MaterialStream> action)
        {
            foreach (UnitBase.PortInfo p in inner.PortLayout)
            {
                if (p.IsEnergy || p.IsInlet) continue;
                var ms = p.Port.connectedObject as global::DWSIM.Thermodynamics.Streams.MaterialStream;
                if (ms != null) action(ms);
            }
        }

        private static void PrepareOutlet(global::DWSIM.Thermodynamics.Streams.MaterialStream ms)
        {
            double t = ms.Phases[0].Properties.temperature.GetValueOrDefault(298.15);
            double pr = ms.Phases[0].Properties.pressure.GetValueOrDefault(101325.0);
            ms.ClearAllProps();
            foreach (ICompound c in ms.Phases[0].Compounds.Values)
            {
                c.MolarFlow = 0.0;
                c.MassFlow = 0.0;
            }
            // Keep a sane T/P so an outlet the block leaves untouched (allowed for
            // optional ports) still flashes instead of erroring on null specs.
            ms.Phases[0].Properties.temperature = t;
            ms.Phases[0].Properties.pressure = pr;
        }

        private static void FinalizeOutlet(global::DWSIM.Thermodynamics.Streams.MaterialStream ms)
        {
            double totalMol = 0.0, totalMass = 0.0;
            foreach (ICompound c in ms.Phases[0].Compounds.Values)
            {
                double mol = c.MolarFlow.GetValueOrDefault();
                if (mol < 0) mol = 0;
                double mass = mol * c.ConstantProperties.Molar_Weight / 1000.0; // g/mol -> kg/s
                c.MassFlow = mass;
                totalMol += mol;
                totalMass += mass;
            }
            foreach (ICompound c in ms.Phases[0].Compounds.Values)
            {
                c.MoleFraction = totalMol > 0 ? c.MolarFlow.GetValueOrDefault() / totalMol : 0.0;
                c.MassFraction = totalMass > 0 ? c.MassFlow.GetValueOrDefault() / totalMass : 0.0;
            }
            ms.Phases[0].Properties.molarflow = totalMol;
            ms.Phases[0].Properties.massflow = totalMass;

            // All 25 blocks set outlets via SetOutletTP (T and P written by SetProp);
            // the demo-only PH path leaves temperature to the flash, so spec on P&H.
            ms.SpecType = ms.Phases[0].Properties.temperature.HasValue
                ? StreamSpec.Temperature_and_Pressure
                : StreamSpec.Pressure_and_Enthalpy;

            // Run the stream's own native solve so per-phase compositions, phase
            // fractions and enthalpies all match the new overall state. Without
            // this the block's mid-calculation flash leaves phase-level data from
            // a stale composition (users saw 1/3-1/3-1/3 phase fractions under a
            // 0.9999/0.0001 overall — technically harmless, visibly wrong).
            if (totalMol > 0)
            {
                try { ms.Calculate(); }
                catch { /* downstream solver will flash it again; never fail the block */ }
            }
        }

        /// <summary>
        /// Connects the DWSIM material streams attached to this object's graphic
        /// connectors to the wrapped block's CAPE-OPEN ports (in declaration order).
        /// DWSIM streams implement ICapeThermoMaterialObject, so the block's
        /// ThermoProxy reads and writes them directly.
        /// </summary>
        private void BindPorts(UnitBase inner)
        {
            var go = GraphicObject;
            if (go == null) return;

            int iIn = 0, iOut = 0;
            foreach (UnitBase.PortInfo p in inner.PortLayout)
            {
                if (p.IsEnergy) continue;

                IConnectionPoint cp = null;
                string attachedName = null;
                if (p.IsInlet)
                {
                    if (iIn < go.InputConnectors.Count) cp = go.InputConnectors[iIn];
                    iIn++;
                    if (cp != null && cp.IsAttached && cp.AttachedConnector != null && cp.AttachedConnector.AttachedFrom != null)
                        attachedName = cp.AttachedConnector.AttachedFrom.Name;
                }
                else
                {
                    if (iOut < go.OutputConnectors.Count) cp = go.OutputConnectors[iOut];
                    iOut++;
                    if (cp != null && cp.IsAttached && cp.AttachedConnector != null && cp.AttachedConnector.AttachedTo != null)
                        attachedName = cp.AttachedConnector.AttachedTo.Name;
                }

                object stream = null;
                if (attachedName != null && FlowSheet != null && FlowSheet.SimulationObjects.ContainsKey(attachedName))
                    stream = FlowSheet.SimulationObjects[attachedName];

                try { p.Port.Disconnect(); } catch { }
                if (stream != null) p.Port.Connect(stream);
            }
        }

        // ------------------------------------------------------------------
        //  Host property table
        // ------------------------------------------------------------------

        // Unit handling — two classes of parameter/result units:
        //
        //  1. Units DWSIM's unit systems know (temperature, pressure, area, ...):
        //     these convert with the user's selected unit set (SI/CGS/ENG/custom),
        //     exactly like a built-in DWSIM block. GetPropertyUnit returns the
        //     CURRENT set's unit string and values convert both ways.
        //
        //  2. Domain units DWSIM has no type for ("W/m2", "mg/g", "V", "%"):
        //     the unit is baked into the property NAME and GetPropertyUnit returns
        //     "" — handing DWSIM an unknown unit string pops a "There is no unit
        //     named ..." warning dialog per property (seen on-machine, v1.1 test).

        private sealed class UnitMap
        {
            public Func<IUnitsOfMeasure, string> CurrentUnit;
            public Func<double, double> ToSI;
            public Func<double, double> FromSI;
        }

        private static readonly Dictionary<string, UnitMap> DwsimUnitTypes =
            new Dictionary<string, UnitMap>(StringComparer.Ordinal)
            {
                ["C"] = new UnitMap { CurrentUnit = su => su.temperature, ToSI = v => v + 273.15, FromSI = v => v - 273.15 },
                ["bar"] = new UnitMap { CurrentUnit = su => su.pressure, ToSI = v => v * 1e5, FromSI = v => v / 1e5 },
                ["Pa"] = new UnitMap { CurrentUnit = su => su.pressure, ToSI = v => v, FromSI = v => v },
                ["m2"] = new UnitMap { CurrentUnit = su => su.area, ToSI = v => v, FromSI = v => v },
                ["m"] = new UnitMap { CurrentUnit = su => su.distance, ToSI = v => v, FromSI = v => v },
                ["m/s"] = new UnitMap { CurrentUnit = su => su.velocity, ToSI = v => v, FromSI = v => v },
                ["kW"] = new UnitMap { CurrentUnit = su => su.heatflow, ToSI = v => v, FromSI = v => v },
                ["kJ/kg"] = new UnitMap { CurrentUnit = su => su.enthalpy, ToSI = v => v, FromSI = v => v },
                ["kg"] = new UnitMap { CurrentUnit = su => su.mass, ToSI = v => v, FromSI = v => v },
                ["kg/s"] = new UnitMap { CurrentUnit = su => su.massflow, ToSI = v => v, FromSI = v => v },
                ["s"] = new UnitMap { CurrentUnit = su => su.time, ToSI = v => v, FromSI = v => v },
                ["min"] = new UnitMap { CurrentUnit = su => su.time, ToSI = v => v * 60.0, FromSI = v => v / 60.0 },
            };

        // "C" on a temperature DIFFERENCE (e.g. MVC's DeltaT) must convert without
        // the 273.15 offset and uses the deltaT unit type.
        private static readonly UnitMap DeltaTemperature =
            new UnitMap { CurrentUnit = su => su.deltaT, ToSI = v => v, FromSI = v => v };

        private static UnitMap ResolveUnitMap(string name, string unit)
        {
            if (string.IsNullOrEmpty(unit)) return null;
            if (unit == "C" && name != null &&
                (name.IndexOf("delta", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 name.IndexOf("approach", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 name.IndexOf("rise", StringComparison.OrdinalIgnoreCase) >= 0))
                return DeltaTemperature;
            UnitMap map;
            return DwsimUnitTypes.TryGetValue(unit, out map) ? map : null;
        }

        private static IUnitsOfMeasure CurrentUnitsOrSI(IUnitsOfMeasure su)
        {
            if (su != null) return su;
            return new global::DWSIM.SharedClasses.SystemsOfUnits.SI();
        }

        private static string ParamUnit(CapeParameter p)
        {
            var rp = p as RealParameter;
            return rp != null ? rp.Unit : null;
        }

        private static string DisplayName(string name, string unit)
        {
            if (string.IsNullOrEmpty(unit) || unit == "-") return name;
            if (ResolveUnitMap(name, unit) != null) return name; // unit shown via GetPropertyUnit
            return name + " [" + unit + "]";
        }

        public override string[] GetProperties(PropertyType proptype)
        {
            var list = new List<string>();
            if (proptype == PropertyType.RW || proptype == PropertyType.WR || proptype == PropertyType.ALL)
                foreach (CapeParameter p in Inner.Parameters) list.Add(DisplayName(p.ComponentName, ParamUnit(p)));
            if (proptype == PropertyType.RO || proptype == PropertyType.ALL)
                foreach (UnitBase.ResultEntry r in CurrentResults()) list.Add(DisplayName(r.Label, r.Unit));
            return list.ToArray();
        }

        private CapeParameter FindParameterByDisplayName(string prop)
        {
            foreach (CapeParameter p in Inner.Parameters)
                if (string.Equals(DisplayName(p.ComponentName, ParamUnit(p)), prop, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p.ComponentName, prop, StringComparison.OrdinalIgnoreCase))
                    return p;
            return null;
        }

        public override object GetPropertyValue(string prop, IUnitsOfMeasure su = null)
        {
            CapeParameter p = FindParameterByDisplayName(prop);
            if (p != null)
            {
                object raw = ((ICapeParameter)p).value;
                UnitMap map = ResolveUnitMap(p.ComponentName, ParamUnit(p));
                if (map == null || !(raw is double || raw is int || raw is float)) return raw;
                double si = map.ToSI(Convert.ToDouble(raw, CultureInfo.InvariantCulture));
                return global::DWSIM.SharedClasses.SystemsOfUnits.Converter.ConvertFromSI(
                    map.CurrentUnit(CurrentUnitsOrSI(su)), si);
            }
            foreach (UnitBase.ResultEntry r in CurrentResults())
            {
                if (!string.Equals(DisplayName(r.Label, r.Unit), prop, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(r.Label, prop, StringComparison.OrdinalIgnoreCase)) continue;
                UnitMap map = ResolveUnitMap(r.Label, r.Unit);
                if (map == null) return r.Value;
                return global::DWSIM.SharedClasses.SystemsOfUnits.Converter.ConvertFromSI(
                    map.CurrentUnit(CurrentUnitsOrSI(su)), map.ToSI(r.Value));
            }
            return base.GetPropertyValue(prop, su);
        }

        public override bool SetPropertyValue(string prop, object propval, IUnitsOfMeasure su = null)
        {
            CapeParameter p = FindParameterByDisplayName(prop);
            if (p == null) return false;
            try
            {
                UnitMap map = ResolveUnitMap(p.ComponentName, ParamUnit(p));
                if (map != null)
                {
                    double entered = Convert.ToDouble(propval, CultureInfo.InvariantCulture);
                    double si = global::DWSIM.SharedClasses.SystemsOfUnits.Converter.ConvertToSI(
                        map.CurrentUnit(CurrentUnitsOrSI(su)), entered);
                    ((ICapeParameter)p).value = map.FromSI(si);
                }
                else
                {
                    ((ICapeParameter)p).value = propval;
                }
                return true;
            }
            catch (Exception ex)
            {
                throw new Exception("Cannot set " + prop + ": " + ex.Message);
            }
        }

        public override string GetPropertyUnit(string prop, IUnitsOfMeasure su = null)
        {
            IUnitsOfMeasure units = CurrentUnitsOrSI(su);
            foreach (CapeParameter p in Inner.Parameters)
                if (string.Equals(DisplayName(p.ComponentName, ParamUnit(p)), prop, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p.ComponentName, prop, StringComparison.OrdinalIgnoreCase))
                {
                    UnitMap map = ResolveUnitMap(p.ComponentName, ParamUnit(p));
                    return map != null ? map.CurrentUnit(units) : "";
                }
            foreach (UnitBase.ResultEntry r in CurrentResults())
                if (string.Equals(DisplayName(r.Label, r.Unit), prop, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(r.Label, prop, StringComparison.OrdinalIgnoreCase))
                {
                    UnitMap map = ResolveUnitMap(r.Label, r.Unit);
                    return map != null ? map.CurrentUnit(units) : "";
                }
            return "";
        }

        private UnitBase.ResultEntry[] CurrentResults()
        {
            UnitBase.ResultEntry[] live = Inner.GetResults();
            if (live != null && live.Length > 0) return live;
            return _loadedResults ?? new UnitBase.ResultEntry[0];
        }

        /// <summary>Results for the editor's Results tab (live or restored from file).</summary>
        public UnitBase.ResultEntry[] DisplayResults { get { return CurrentResults(); } }

        /// <summary>Model warnings for the Results tab (live or restored from file).</summary>
        public string[] DisplayWarnings
        {
            get
            {
                string[] live = Inner.GetReportWarnings();
                if (live != null && live.Length > 0) return live;
                return _loadedWarnings ?? new string[0];
            }
        }

        // ------------------------------------------------------------------
        //  Persistence — parameters + last results survive save/reopen and clone.
        //  A single string property means DWSIM's XMLSerializer (file save,
        //  CloneXML) and Newtonsoft (CloneJSON) both carry it automatically.
        // ------------------------------------------------------------------

        public string BlockStateData
        {
            get { return EncodeState(); }
            set { DecodeState(value); }
        }

        private string EncodeState()
        {
            try
            {
                var sb = new StringBuilder();
                foreach (CapeParameter p in Inner.Parameters)
                {
                    object v = ((ICapeParameter)p).value;
                    sb.Append("P\t").Append(p.ComponentName).Append('\t')
                      .Append(Convert.ToString(v, CultureInfo.InvariantCulture)).Append('\n');
                }
                foreach (UnitBase.ResultEntry r in CurrentResults())
                {
                    sb.Append("R\t").Append(r.Label).Append('\t')
                      .Append(r.Value.ToString("R", CultureInfo.InvariantCulture)).Append('\t')
                      .Append(r.Unit ?? "").Append('\n');
                }
                foreach (string w in DisplayWarnings)
                    sb.Append("W\t").Append(w.Replace('\n', ' ').Replace('\t', ' ')).Append('\n');
                return Convert.ToBase64String(Encoding.UTF8.GetBytes(sb.ToString()));
            }
            catch
            {
                return "";
            }
        }

        private void DecodeState(string data)
        {
            if (string.IsNullOrEmpty(data)) return;
            try
            {
                string text = Encoding.UTF8.GetString(Convert.FromBase64String(data));
                var results = new List<UnitBase.ResultEntry>();
                var warnings = new List<string>();
                foreach (string line in text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] f = line.Split('\t');
                    if (f.Length >= 2 && f[0] == "W") { warnings.Add(f[1]); continue; }
                    if (f.Length >= 3 && f[0] == "P")
                    {
                        foreach (CapeParameter p in Inner.Parameters)
                        {
                            if (!string.Equals(p.ComponentName, f[1], StringComparison.OrdinalIgnoreCase)) continue;
                            try
                            {
                                if (p is RealParameter)
                                    ((ICapeParameter)p).value = double.Parse(f[2], CultureInfo.InvariantCulture);
                                else if (p is IntegerParameter)
                                    ((ICapeParameter)p).value = int.Parse(f[2], CultureInfo.InvariantCulture);
                                else if (p is BooleanParameter)
                                    ((ICapeParameter)p).value = bool.Parse(f[2]);
                                else
                                    ((ICapeParameter)p).value = f[2];
                            }
                            catch { /* out-of-range persisted value — keep default */ }
                            break;
                        }
                    }
                    else if (f.Length >= 3 && f[0] == "R")
                    {
                        double val;
                        if (double.TryParse(f[2], NumberStyles.Any, CultureInfo.InvariantCulture, out val))
                            results.Add(new UnitBase.ResultEntry
                            {
                                Label = f[1],
                                Value = val,
                                Unit = f.Length > 3 ? f[3] : "",
                                Format = "0.####"
                            });
                    }
                }
                if (results.Count > 0) _loadedResults = results.ToArray();
                if (warnings.Count > 0) _loadedWarnings = warnings.ToArray();
            }
            catch { /* corrupt state — keep defaults, never break file load */ }
        }

        // ------------------------------------------------------------------
        //  Cloning (copy/paste, undo/redo internals)
        // ------------------------------------------------------------------

        public override object CloneXML()
        {
            var clone = (DwsimUnitAdapter)ReturnInstance(GetType().FullName);
            clone.BlockStateData = BlockStateData;
            clone.ComponentName = ComponentName;
            clone.ComponentDescription = ComponentDescription;
            return clone;
        }

        public override object CloneJSON()
        {
            return JsonConvert.DeserializeObject(JsonConvert.SerializeObject(this), GetType());
        }

        // ------------------------------------------------------------------
        //  Editor — reuse the branded OP-Blocks parameter dialog
        // ------------------------------------------------------------------

        public override void DisplayEditForm()
        {
            try
            {
                // Connections (pick/create the stream per port, like DWSIM's own
                // unit editors) + the branded parameter grid.
                using (var editor = new DwsimBlockEditor(this))
                    editor.ShowDialog();
                try { SetDirtyStatus(true); } catch { }
            }
            catch (Exception ex)
            {
                try { OpLog.Error(Inner.BlockCode, "DWSIM Edit", ex); } catch { }
                MessageBox.Show("The editor could not open: " + ex.Message,
                                "ONE PROCESS Blocks", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        public override void UpdateEditForm() { }

        public override void CloseEditForm()
        {
            // The editor is modal (ShowDialog) — it cannot outlive its caller.
        }

        public void PopulateEditorPanel(object container) { /* classic WinForms UI uses DisplayEditForm */ }
    }
}
