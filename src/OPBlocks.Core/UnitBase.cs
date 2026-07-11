using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Windows.Forms;
using CapeOpen;
using OPBlocks.Core.Diagnostics;
using OPBlocks.Core.Persistence;

namespace OPBlocks.Core
{
    /// <summary>
    /// Abstract base for every ONE PROCESS block. Builds on the CO-LaN
    /// <see cref="CapeUnitBase"/> (which supplies ICapeUnit / ICapeUtilities /
    /// ICapeIdentification / ICapeUnitReport and auto COM registration of the
    /// CapeUnitOperation CATID) and adds the three cross-cutting guarantees the
    /// spec demands of every block:
    ///
    ///  • the zero-error boundary guard (§8.8) — no raw .NET exception ever
    ///    crosses the COM boundary; unexpected ones are logged and returned as an
    ///    actionable <see cref="CapeUnknownException"/> carrying the log path;
    ///  • parameter/mapping persistence (§8.6) via IPersistStream(Init) so state
    ///    survives save → close host → reopen;
    ///  • convenience accessors that route all thermodynamics through
    ///    <see cref="ThermoProxy"/> (§5), so subclasses never touch a raw
    ///    material object.
    ///
    /// A concrete block supplies <see cref="BlockCode"/>, adds its ports and
    /// parameters in its constructor, and implements <see cref="Compute"/> and
    /// <see cref="BuildReport"/>. It must carry <c>[ComVisible(true)]</c>, a stable
    /// <c>[Guid]</c>, and the <c>Cape*</c> registration attributes.
    /// </summary>
    [ComVisible(true)]
    [Serializable]
    public abstract class UnitBase : CapeUnitBase, IPersistStream, IPersistStreamInit
    {
        private const string PersistMagic = "OPB1";
        private const int PersistVersion = 1;

        protected UnitBase() : base()
        {
        }

        /// <summary>Short block code shown in reports and logs, e.g. "OP-MIXER-DEMO".</summary>
        public abstract string BlockCode { get; }

        // ------------------------------------------------------------------
        //  Calculation — boundary guard (§8.8)
        // ------------------------------------------------------------------
        // Result rows collected during Compute and rendered by the default report.
        private struct ResultRow { public string Label; public double Value; public string Unit; public string Format; }
        private readonly List<ResultRow> _results = new List<ResultRow>();
        private readonly List<string> _reportWarnings = new List<string>();

        /// <summary>Records a numeric output for the calculation report.</summary>
        protected void Result(string label, double value, string unit = "", string format = "0.####")
        {
            _results.RemoveAll(r => r.Label == label);
            _results.Add(new ResultRow { Label = label, Value = value, Unit = unit, Format = format });
        }

        /// <summary>Records a non-fatal assumption/warning shown at the top of the report (§5 rule 5).</summary>
        protected void ReportWarning(string text)
        {
            if (!_reportWarnings.Contains(text)) _reportWarnings.Add(text);
        }

        public sealed override void OnCalculate()
        {
            _results.Clear();
            _reportWarnings.Clear();
            try
            {
                Compute();
            }
            catch (CapeUserException)
            {
                // Already an actionable ECapeUser error with a clear message.
                throw;
            }
            catch (Exception ex)
            {
                string logPath = OpLog.Error(BlockCode, "Calculate", ex);
                throw new CapeUnknownException(
                    "Unexpected error in " + BlockCode + ": " + ex.Message +
                    ". Full details were written to " + logPath + ".", ex);
            }
        }

        /// <summary>
        /// Block physics. Delegate all thermodynamics through the material objects
        /// obtained from <see cref="GetConnectedMaterial"/>. Throw a
        /// <see cref="CapeUserException"/> subtype (e.g.
        /// <see cref="CapeSolvingErrorException"/>) with a clear message on
        /// expected failures; anything else is caught by the boundary guard.
        /// </summary>
        protected abstract void Compute();

        // ------------------------------------------------------------------
        //  Validation (§8.4, §5 rule 4)
        // ------------------------------------------------------------------
        public override bool Validate(ref string message)
        {
            try
            {
                // Standard checks first (ports connected, parameters within bounds);
                // this also maintains ValStatus.
                if (!base.Validate(ref message))
                    return false;

                string modelMessage;
                if (!ValidateModel(out modelMessage))
                {
                    message = modelMessage;
                    return false;
                }
                return true;
            }
            catch (CapeUserException ex)
            {
                message = ex.Message;
                return false;
            }
            catch (Exception ex)
            {
                string logPath = OpLog.Error(BlockCode, "Validate", ex);
                message = "Validation error in " + BlockCode + ": " + ex.Message +
                          " (see " + logPath + ").";
                return false;
            }
        }

        /// <summary>
        /// Block-specific validation — e.g. required components present in the list
        /// (§5 rule 4). Runs only after ports and parameters already validated.
        /// Return false with an actionable message to block the run gracefully.
        /// </summary>
        protected virtual bool ValidateModel(out string message)
        {
            message = null;
            return true;
        }

        // ------------------------------------------------------------------
        //  Report (§4) — guarded
        // ------------------------------------------------------------------
        public sealed override void ProduceReport(ref string message)
        {
            try
            {
                message = BuildReport();
            }
            catch (Exception ex)
            {
                string logPath = OpLog.Error(BlockCode, "ProduceReport", ex);
                message = "Report generation failed for " + BlockCode + ": " + ex.Message +
                          " (see " + logPath + ").";
            }
        }

        /// <summary>
        /// Builds the human-readable results report. The default renders the rows a block
        /// records via <see cref="Result"/> plus any <see cref="ReportWarning"/> lines;
        /// blocks may override for a fully custom layout.
        /// </summary>
        protected virtual string BuildReport()
        {
            var rep = new ReportBuilder(ComponentName ?? BlockCode, BlockCode);
            foreach (string w in _reportWarnings) rep.Warning(w);
            if (_results.Count == 0)
            {
                rep.Section("Status").Line("  Not yet calculated. Run the flowsheet to populate results.");
                return rep.Build();
            }
            rep.Section("Results");
            foreach (ResultRow row in _results) rep.Value(row.Label, row.Value, row.Unit, row.Format);
            return rep.Build();
        }

        // ------------------------------------------------------------------
        //  Edit GUI — guarded (§8.3: must never crash the host)
        // ------------------------------------------------------------------
        public override void Edit()
        {
            try
            {
                using (var editor = new OpBlockEditor(this))
                    editor.ShowDialog();
            }
            catch (Exception ex)
            {
                string logPath = OpLog.Error(BlockCode, "Edit", ex);
                try
                {
                    MessageBox.Show(
                        "The editor for " + BlockCode + " could not open: " + ex.Message +
                        "\n\nDetails: " + logPath,
                        "ONE PROCESS Blocks", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                catch { /* never let the editor failure escalate */ }
            }
        }

        // ------------------------------------------------------------------
        //  Helpers for subclasses
        // ------------------------------------------------------------------
        protected UnitPort AddMaterialPort(string name, string description, CapePortDirection direction)
        {
            var port = new UnitPort(name, description, direction, CapePortType.CAPE_MATERIAL);
            Ports.Add(port);
            return port;
        }

        protected UnitPort AddEnergyPort(string name, string description, CapePortDirection direction)
        {
            var port = new UnitPort(name, description, direction, CapePortType.CAPE_ENERGY);
            Ports.Add(port);
            return port;
        }

        protected RealParameter AddRealParameter(string name, string description, double defaultValue,
                                                 double lowerBound, double upperBound, string unit)
        {
            var p = new RealParameter(name, description, defaultValue, defaultValue,
                                      lowerBound, upperBound, CapeParamMode.CAPE_INPUT, unit);
            Parameters.Add(p);
            return p;
        }

        protected IntegerParameter AddIntParameter(string name, string description, int defaultValue,
                                                   int minValue, int maxValue)
        {
            var p = new IntegerParameter(name, description, defaultValue, defaultValue,
                                         minValue, maxValue, CapeParamMode.CAPE_INPUT);
            Parameters.Add(p);
            return p;
        }

        protected OptionParameter AddOptionParameter(string name, string description,
                                                     string defaultValue, string[] options)
        {
            var p = new OptionParameter(name, description, defaultValue, defaultValue, options, true, CapeParamMode.CAPE_INPUT);
            Parameters.Add(p);
            return p;
        }

        /// <summary>
        /// Value of a real parameter as entered, in its own display unit (block physics
        /// define defaults in the unit they compute with, converting explicitly in code).
        /// </summary>
        protected double R(string name)
        {
            var p = FindParameter(name) as RealParameter;
            if (p == null) return 0.0;
            try { return p.DimensionedValue; } catch { return 0.0; }
        }

        /// <summary>Value of an integer parameter.</summary>
        protected int I(string name)
        {
            var p = FindParameter(name);
            object v = p == null ? null : ((ICapeParameter)p).value;
            try { return v == null ? 0 : Convert.ToInt32(v); } catch { return 0; }
        }

        /// <summary>Value of an option parameter (selected string).</summary>
        protected string Opt(string name)
        {
            var p = FindParameter(name);
            object v = p == null ? null : ((ICapeParameter)p).value;
            return v == null ? "" : v.ToString();
        }

        protected UnitPort FindPort(string name)
        {
            foreach (UnitPort p in Ports)
                if (string.Equals(p.ComponentName, name, StringComparison.OrdinalIgnoreCase))
                    return p;
            return null;
        }

        protected CapeParameter FindParameter(string name)
        {
            foreach (CapeParameter p in Parameters)
                if (string.Equals(p.ComponentName, name, StringComparison.OrdinalIgnoreCase))
                    return p;
            return null;
        }

        /// <summary>
        /// Returns a <see cref="ThermoProxy"/> for the material connected to
        /// <paramref name="portName"/>, or null if the port is unconnected.
        /// </summary>
        protected ThermoProxy GetConnectedMaterial(string portName)
        {
            UnitPort port = FindPort(portName);
            if (port == null)
                throw new CapeInvalidArgumentException("Port '" + portName + "' does not exist.", 1);
            object connected = port.connectedObject;
            if (connected == null)
                return null;
            return new ThermoProxy(connected, portName);
        }

        /// <summary>Like <see cref="GetConnectedMaterial"/> but throws a clear error if unconnected.</summary>
        protected ThermoProxy RequireMaterial(string portName)
        {
            ThermoProxy m = GetConnectedMaterial(portName);
            if (m == null)
                throw new CapeSolvingErrorException(
                    "Connect a stream to port '" + portName + "' before running " + BlockCode + ".");
            return m;
        }

        // ------------------------------------------------------------------
        //  Persistence (§8.6)
        // ------------------------------------------------------------------

        /// <summary>
        /// Hook for block-specific state beyond the standard parameter set (e.g.
        /// component-role mappings, §6). Write in the same order you read it back
        /// in <see cref="LoadExtra"/>.
        /// </summary>
        protected virtual void SaveExtra(BinaryWriter writer) { }

        /// <summary>Reads block-specific state written by <see cref="SaveExtra"/>.</summary>
        protected virtual void LoadExtra(BinaryReader reader, int version) { }

        private void SaveState(IStream stream)
        {
            using (var ms = new MemoryStream())
            {
                using (var bw = new BinaryWriter(ms, Encoding.UTF8, true))
                {
                    bw.Write(PersistMagic);
                    bw.Write(PersistVersion);

                    bw.Write(Parameters.Count);
                    foreach (CapeParameter p in Parameters)
                    {
                        bw.Write(p.ComponentName ?? string.Empty);
                        WriteParameterValue(bw, p);
                    }

                    SaveExtra(bw);
                    bw.Flush();
                }
                StreamPersist.WriteBlock(stream, ms.ToArray());
            }
        }

        private void LoadState(IStream stream)
        {
            byte[] data = StreamPersist.ReadBlock(stream);
            if (data == null || data.Length == 0)
                return; // fresh/empty stream — keep constructor defaults

            using (var ms = new MemoryStream(data))
            using (var br = new BinaryReader(ms, Encoding.UTF8))
            {
                string magic = br.ReadString();
                if (magic != PersistMagic)
                    return; // unknown format; keep defaults rather than corrupting state
                int version = br.ReadInt32();

                int count = br.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    string name = br.ReadString();
                    ReadParameterValue(br, name);
                }

                LoadExtra(br, version);
            }
        }

        private static void WriteParameterValue(BinaryWriter bw, CapeParameter p)
        {
            object v = ((ICapeParameter)p).value;
            if (p is RealParameter) { bw.Write((byte)1); bw.Write(v == null ? 0.0 : Convert.ToDouble(v)); }
            else if (p is IntegerParameter) { bw.Write((byte)2); bw.Write(v == null ? 0 : Convert.ToInt32(v)); }
            else if (p is BooleanParameter) { bw.Write((byte)3); bw.Write(v != null && Convert.ToBoolean(v)); }
            else if (p is OptionParameter) { bw.Write((byte)4); bw.Write(v == null ? string.Empty : Convert.ToString(v)); }
            else { bw.Write((byte)0); }
        }

        private void ReadParameterValue(BinaryReader br, string name)
        {
            byte tag = br.ReadByte();
            object val;
            switch (tag)
            {
                case 1: val = br.ReadDouble(); break;
                case 2: val = br.ReadInt32(); break;
                case 3: val = br.ReadBoolean(); break;
                case 4: val = br.ReadString(); break;
                default: return; // tag 0 — nothing stored
            }

            CapeParameter p = FindParameter(name);
            if (p == null) return; // parameter no longer exists; ignore silently
            try { ((ICapeParameter)p).value = val; }
            catch (Exception ex) { OpLog.Info(BlockCode, "Ignored out-of-range persisted value for '" + name + "': " + ex.Message); }
        }

        // ---- IPersistStream ----
        void IPersistStream.GetClassID(out Guid pClassID) { pClassID = GetType().GUID; }
        int IPersistStream.IsDirty() { return 0; /* S_OK: always persist */ }
        void IPersistStream.Load(IStream pstm) { PersistLoad(pstm); }
        void IPersistStream.Save(IStream pstm, bool fClearDirty) { PersistSave(pstm); }
        void IPersistStream.GetSizeMax(out long pcbSize) { pcbSize = 1 << 20; }

        // ---- IPersistStreamInit ----
        void IPersistStreamInit.GetClassID(out Guid pClassID) { pClassID = GetType().GUID; }
        int IPersistStreamInit.IsDirty() { return 0; }
        void IPersistStreamInit.Load(IStream pstm) { PersistLoad(pstm); }
        void IPersistStreamInit.Save(IStream pstm, bool fClearDirty) { PersistSave(pstm); }
        void IPersistStreamInit.GetSizeMax(out long pcbSize) { pcbSize = 1 << 20; }
        void IPersistStreamInit.InitNew() { /* constructor already set defaults */ }

        private void PersistSave(IStream stream)
        {
            try { SaveState(stream); }
            catch (Exception ex) { OpLog.Error(BlockCode, "Save", ex); throw; }
        }

        private void PersistLoad(IStream stream)
        {
            try { LoadState(stream); }
            catch (Exception ex)
            {
                // Never crash the host on reload; fall back to defaults and log.
                OpLog.Error(BlockCode, "Load", ex);
            }
        }
    }
}
