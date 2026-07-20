using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using CapeOpen;

namespace OPBlocks.Core
{
    /// <summary>
    /// The ONE PROCESS block editor (spec §4). A branded WinForms dialog — so it
    /// loads inside Aspen's and DWSIM's own process (no WPF loader conflicts) —
    /// that replaces the library's generic BaseUnitEditor. It shows a bilingual
    /// (EN / AR) header and a typed, validated grid of the block's parameters:
    /// numbers are range-checked, options are drop-downs, and "Restore defaults"
    /// resets every parameter. All input is guarded so a bad entry shows an inline
    /// message rather than ever crashing the host (Requirement R3).
    /// </summary>
    public sealed class OpBlockEditor : Form
    {
        private readonly UnitBase _unit;
        private readonly ErrorProvider _errors = new ErrorProvider();
        private TableLayoutPanel _table;        // input parameters
        private Panel _scroll;
        private ToolTip _tips;
        private TableLayoutPanel _resultsTable; // output parameters (read-only)
        private Panel _resultsScroll;
        private Label _resultsHint;

        // ONE PROCESS brand navy + blue (owner-chosen 2026-07-18; matches the Manager)
        private static readonly Color Accent = ColorTranslator.FromHtml("#1B3A5C");
        private static readonly Color AccentSoft = Color.FromArgb(191, 219, 245);
        private static readonly Color PanelBg = Color.FromArgb(244, 246, 248);

        public OpBlockEditor(UnitBase unit)
        {
            _unit = unit;
            _errors.BlinkStyle = ErrorBlinkStyle.NeverBlink;
            InitializeForm();
            BuildRows();
        }

        /// <summary>
        /// The purpose sentence: the rich [CapeDescription] attribute text
        /// ("Reverse osmosis: solution-diffusion flux ...") stripped of its name
        /// prefix. ComponentDescription itself only carries the short name.
        /// </summary>
        private string PurposeLine()
        {
            string d = null;
            object[] attrs = _unit.GetType().GetCustomAttributes(typeof(CapeDescriptionAttribute), false);
            if (attrs.Length > 0) d = ((CapeDescriptionAttribute)attrs[0]).Description;
            if (string.IsNullOrEmpty(d)) d = _unit.ComponentDescription ?? "";
            int i = d.IndexOf(':');
            string p = i >= 0 && i + 1 < d.Length ? d.Substring(i + 1).Trim() : d;
            return p.Length > 0 ? char.ToUpperInvariant(p[0]) + p.Substring(1) : "ONE PROCESS block";
        }

        private void InitializeForm()
        {
            Text = BlockCatalog.DisplayTitle(_unit.BlockCode) + "  —  Configure / إعداد";
            Font = new Font("Segoe UI", 9f);
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(600, 440);
            Size = new Size(660, 540);
            BackColor = Color.White;
            ShowIcon = false;
            MaximizeBox = false;
            MinimizeBox = false;

            // --- header (brand bar): who am I, what do I do, when to use me (P2) ---
            var header = new Panel { Dock = DockStyle.Top, Height = 92, BackColor = Accent };
            Control logo = BuildLogo();
            logo.Size = new Size(42, 42);
            logo.Location = new Point(14, 24);
            BlockCatalog.Info id = BlockCatalog.For(_unit.BlockCode);
            var title = new Label
            {
                Text = BlockCatalog.DisplayTitle(_unit.BlockCode),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 13f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(62, 10),
                BackColor = Color.Transparent
            };
            var purpose = new Label
            {
                Text = PurposeLine(),
                ForeColor = AccentSoft,
                Font = new Font("Segoe UI", 8.5f),
                AutoSize = false,
                AutoEllipsis = true,
                Size = new Size(578, 16),
                Location = new Point(62, 38),
                BackColor = Color.Transparent
            };
            var hint = new Label
            {
                Text = id.TypicalUse == null
                    ? "ONE PROCESS Blocks  ·  " + id.Family
                    : id.Family + "  ·  Typical use: " + id.TypicalUse,
                ForeColor = AccentSoft,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Italic),
                AutoSize = false,
                AutoEllipsis = true,
                Size = new Size(578, 16),
                Location = new Point(62, 58),
                BackColor = Color.Transparent
            };
            header.Controls.Add(logo);
            header.Controls.Add(title);
            header.Controls.Add(purpose);
            header.Controls.Add(hint);

            // --- footer (buttons) ---
            var footer = new Panel { Dock = DockStyle.Bottom, Height = 54, BackColor = PanelBg };
            var close = new Button
            {
                Text = "Close  /  إغلاق",
                DialogResult = DialogResult.OK,
                Width = 140,
                Height = 30,
                FlatStyle = FlatStyle.Flat,
                BackColor = Accent,
                ForeColor = Color.White
            };
            close.FlatAppearance.BorderSize = 0;
            var restore = new Button
            {
                Text = "Restore defaults  /  استرجاع الافتراضي",
                Width = 220,
                Height = 30,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(107, 122, 118),
                ForeColor = Color.White
            };
            restore.FlatAppearance.BorderSize = 0;
            restore.Click += (s, e) => RestoreDefaults();
            footer.Controls.Add(close);
            footer.Controls.Add(restore);
            footer.Resize += (s, e) =>
            {
                close.Left = footer.Width - close.Width - 16; close.Top = 12;
                restore.Left = 16; restore.Top = 12;
            };

            // --- tabs: Input | Results ---
            var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(14, 4) };

            _tips = new ToolTip { AutoPopDelay = 20000, InitialDelay = 350, ReshowDelay = 150 };
            var inputPage = new TabPage("Input  /  المدخلات") { BackColor = Color.White };
            _scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(16, 14, 16, 14) };
            _table = NewGrid(4);
            _table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 286));
            _table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
            _table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
            _table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _scroll.Controls.Add(_table);
            inputPage.Controls.Add(_scroll);

            var resultsPage = new TabPage("Results  /  النتائج") { BackColor = Color.White };
            _resultsScroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(16, 12, 16, 14) };
            _resultsHint = new Label
            {
                Dock = DockStyle.Top,
                AutoSize = false,
                Height = 34,
                Padding = new Padding(2, 4, 2, 6),
                ForeColor = Color.FromArgb(120, 132, 128),
                Font = new Font("Segoe UI", 8.5f),
                Text = ""
            };
            _resultsTable = NewGrid(3);
            _resultsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _resultsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            _resultsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            _resultsScroll.Controls.Add(_resultsTable);
            _resultsScroll.Controls.Add(_resultsHint);
            resultsPage.Controls.Add(_resultsScroll);

            tabs.TabPages.Add(inputPage);
            tabs.TabPages.Add(resultsPage);

            Controls.Add(tabs);
            Controls.Add(footer);
            Controls.Add(header);
            AcceptButton = close;
        }

        private static TableLayoutPanel NewGrid(int cols)
        {
            return new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = cols,
                Dock = DockStyle.Top,
                CellBorderStyle = TableLayoutPanelCellBorderStyle.None
            };
        }

        private void BuildRows()
        {
            // --- Input tab: editable CAPE_INPUT parameters only ---
            _table.SuspendLayout();
            _table.Controls.Clear();
            _table.RowStyles.Clear();
            _table.RowCount = 0;
            AddHeaderCell(_table, "Parameter", 0);
            AddHeaderCell(_table, "Value", 1);
            AddHeaderCell(_table, "Unit", 2);
            AddHeaderCell(_table, "Allowed range", 3, advanceRow: true);
            foreach (CapeParameter p in _unit.Parameters)
                if (UnitBase.IsInputParameter(p))
                {
                    string section = ParamGroups.SectionStartingAt(_unit.BlockCode, p.ComponentName, resultsTab: false);
                    if (section != null) AddSectionRow(_table, section, 4);
                    AddParameterRow(p);
                }
            _table.ResumeLayout();

            // --- Results tab: read-only CAPE_OUTPUT parameters (from the last run) ---
            _resultsTable.SuspendLayout();
            _resultsTable.Controls.Clear();
            _resultsTable.RowStyles.Clear();
            _resultsTable.RowCount = 0;
            AddHeaderCell(_resultsTable, "Result", 0);
            AddHeaderCell(_resultsTable, "Value", 1);
            AddHeaderCell(_resultsTable, "Unit", 2, advanceRow: true);
            bool anyComputed = false;
            foreach (CapeParameter p in _unit.Parameters)
            {
                if (UnitBase.IsInputParameter(p)) continue;
                double v = ToDouble(((ICapeParameter)p).value);
                if (Math.Abs(v) > 0) anyComputed = true;
                string section = ParamGroups.SectionStartingAt(_unit.BlockCode, p.ComponentName, resultsTab: true);
                if (section != null) AddSectionRow(_resultsTable, section, 3);
                AddResultRow(p, v);
            }
            _resultsHint.Text = anyComputed
                ? "Results from the last calculation. Re-run the flowsheet (Run / F5) to refresh, then reopen."
                : "Not calculated yet — run the flowsheet (Run / F5), then reopen this block to see results.\n"
                  + "لم تُحسب بعد — شغّل المخطط ثم أعد فتح البلوك لرؤية النتائج.";
            _resultsHint.Height = anyComputed ? 26 : 40;
            _resultsTable.ResumeLayout();
        }

        /// <summary>
        /// Short display label: the parameter's human description trimmed at its
        /// first parenthesis/colon (the tail moves to the tooltip), falling back
        /// to the raw name. "Water permeability A (seawater ~1...)" -> "Water permeability A".
        /// </summary>
        private static string ShortLabel(CapeParameter p)
        {
            string d = p.ComponentDescription;
            if (string.IsNullOrWhiteSpace(d)) return p.ComponentName;
            int cut = d.IndexOfAny(new[] { '(', ':' , ';' });
            string s = (cut > 0 ? d.Substring(0, cut) : d).Trim().TrimEnd(',', '.', ' ');
            return s.Length > 0 ? s : p.ComponentName;
        }

        /// <summary>
        /// Full tooltip: complete description + allowed range. Bounds are read
        /// ONLY through ICapeRealParameterSpec (raw stored values) — the public
        /// RealParameter getters run the library's unit-conversion layer, which
        /// mangles non-SI ranges and pops modal "Unit Warning!" boxes for exotic
        /// units (the 2026-07-20 dialog-storm / editor-"hang" incident). Never
        /// read DefaultValue here for the same reason.
        /// </summary>
        private static string FullTip(CapeParameter p)
        {
            string tip = string.IsNullOrWhiteSpace(p.ComponentDescription)
                ? p.ComponentName : p.ComponentDescription;
            var rp = p as RealParameter;
            if (rp != null)
            {
                var spec = (ICapeRealParameterSpec)p;
                string unit = string.Equals(rp.Unit, "-") ? "" : (" " + (rp.Unit ?? "")).TrimEnd();
                tip += "\nAllowed: " + Trim(spec.LowerBound) + " … " + Trim(spec.UpperBound) + unit;
            }
            return tip;
        }

        /// <summary>
        /// Navy section header row (P3 grouping). A single-cell auto-sized label —
        /// no docking, no anchoring, no column span: any width-negotiating child
        /// (incl. a span into the percent column) makes this auto-size table hang
        /// in a layout feedback loop (caught by the render watchdog test).
        /// </summary>
        private void AddSectionRow(TableLayoutPanel table, string title, int columns)
        {
            var lbl = new Label
            {
                Text = title.ToUpperInvariant(),
                UseMnemonic = false, // titles contain '&' ("RECOVERY & DESIGN TARGETS")
                AutoSize = true,
                Margin = new Padding(1, 14, 3, 3),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = Accent
            };
            table.Controls.Add(lbl, 0, table.RowCount);
            table.RowCount++;
        }

        private void AddResultRow(CapeParameter p, double value)
        {
            int row = _resultsTable.RowCount;
            var name = new Label
            {
                Text = ShortLabel(p),
                AutoSize = true,
                MaximumSize = new Size(320, 0),
                Margin = new Padding(3, 7, 3, 6),
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.FromArgb(34, 44, 56)
            };
            _tips.SetToolTip(name, FullTip(p));
            _resultsTable.Controls.Add(name, 0, row);

            var val = new Label
            {
                Text = value.ToString("0.####", CultureInfo.InvariantCulture),
                AutoSize = true,
                Margin = new Padding(3, 7, 3, 6),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Accent
            };
            _resultsTable.Controls.Add(val, 1, row);

            string unit = (p is RealParameter rp) ? rp.Unit : "";
            var unitLbl = new Label
            {
                Text = string.Equals(unit, "-") ? "" : (unit ?? ""),
                AutoSize = true,
                Margin = new Padding(3, 7, 3, 6),
                ForeColor = Color.FromArgb(91, 106, 102)
            };
            _resultsTable.Controls.Add(unitLbl, 2, row);
            _resultsTable.RowCount++;
        }

        private static void AddHeaderCell(TableLayoutPanel table, string text, int col, bool advanceRow = false)
        {
            var lbl = new Label
            {
                Text = text,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(90, 106, 102),
                AutoSize = true,
                Margin = new Padding(3, 4, 3, 8)
            };
            table.Controls.Add(lbl, col, table.RowCount);
            if (advanceRow) table.RowCount++;
        }

        private void AddParameterRow(CapeParameter p)
        {
            int row = _table.RowCount;

            var name = new Label
            {
                Text = ShortLabel(p),
                AutoSize = true,
                MaximumSize = new Size(276, 0), // wraps instead of touching the value column
                Margin = new Padding(3, 8, 3, 6),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(34, 44, 56)
            };
            string tip = FullTip(p);
            _tips.SetToolTip(name, tip);
            _table.Controls.Add(name, 0, row);

            Control input = CreateInput(p, out string unit, out string info);
            input.Margin = new Padding(3, 5, 3, 4);
            _tips.SetToolTip(input, tip);
            _table.Controls.Add(input, 1, row);

            var unitLbl = new Label
            {
                Text = string.Equals(unit, "-") ? "" : (unit ?? ""),
                AutoSize = true,
                MaximumSize = new Size(84, 0),
                Margin = new Padding(3, 8, 3, 6),
                ForeColor = Color.FromArgb(96, 112, 128)
            };
            _table.Controls.Add(unitLbl, 2, row);

            var infoLbl = new Label
            {
                Text = info ?? "",
                AutoSize = true,
                MaximumSize = new Size(170, 0),
                Margin = new Padding(3, 8, 3, 6),
                ForeColor = Color.FromArgb(140, 148, 158),
                Font = new Font("Segoe UI", 8f)
            };
            _tips.SetToolTip(infoLbl, tip);
            _table.Controls.Add(infoLbl, 3, row);

            _table.RowCount++;
        }

        private Control CreateInput(CapeParameter p, out string unit, out string info)
        {
            unit = null;
            info = p.ComponentDescription;

            if (p is RealParameter rp)
            {
                unit = rp.Unit;
                // Read bounds through ICapeRealParameterSpec: it exposes the raw
                // stored values. The public LowerBound/UpperBound getters convert
                // them as if they were SI (a 5…120 bar range displays as
                // 0.00005…0.0012 and rejects every legal entry).
                var spec = (ICapeRealParameterSpec)p;
                double lo = spec.LowerBound, hi = spec.UpperBound;
                info = "range " + Trim(lo) + " … " + Trim(hi);
                return NumericBox(p, lo, hi, isInteger: false);
            }
            if (p is IntegerParameter)
            {
                double lo = GetBound(p, "LowerBound", int.MinValue);
                double hi = GetBound(p, "UpperBound", int.MaxValue);
                info = "integer " + Trim(lo) + " … " + Trim(hi);
                return NumericBox(p, lo, hi, isInteger: true);
            }
            if (p is BooleanParameter)
            {
                var cb = new ComboBox { Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
                cb.Items.AddRange(new object[] { "True", "False" });
                cb.SelectedItem = ToBool(((ICapeParameter)p).value) ? "True" : "False";
                cb.SelectedIndexChanged += (s, e) => SafeSet(p, cb.SelectedItem as string == "True", cb);
                return cb;
            }
            if (p is OptionParameter)
            {
                string[] options = GetOptions(p);
                var cb = new ComboBox { Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
                if (options != null && options.Length > 0)
                {
                    cb.Items.AddRange(options);
                    string cur = Convert.ToString(((ICapeParameter)p).value);
                    if (cb.Items.Contains(cur)) cb.SelectedItem = cur; else cb.SelectedIndex = 0;
                    cb.SelectedIndexChanged += (s, e) => SafeSet(p, cb.SelectedItem as string, cb);
                    return cb;
                }
                // Unknown option list — fall back to a free text box.
                var tb = new TextBox { Width = 150, Text = Convert.ToString(((ICapeParameter)p).value) };
                tb.Validating += (s, e) => SafeSet(p, tb.Text, tb);
                return tb;
            }

            // Unknown parameter type — read-only display.
            return new TextBox { Width = 150, ReadOnly = true, Text = Convert.ToString(((ICapeParameter)p).value) };
        }

        private TextBox NumericBox(CapeParameter p, double lo, double hi, bool isInteger)
        {
            var tb = new TextBox { Width = 150, Text = Trim(ToDouble(((ICapeParameter)p).value)) };
            tb.Validating += (s, e) =>
            {
                if (!double.TryParse(tb.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double v))
                {
                    _errors.SetError(tb, "Enter a valid number / أدخل رقمًا صحيحًا");
                    e.Cancel = true;
                    return;
                }
                if (isInteger) v = Math.Round(v);
                if (v < lo || v > hi)
                {
                    _errors.SetError(tb, "Must be between " + Trim(lo) + " and " + Trim(hi));
                    e.Cancel = true;
                    return;
                }
                _errors.SetError(tb, "");
                object boxed = isInteger ? (object)(int)v : v;
                SafeSet(p, boxed, tb);
                tb.Text = Trim(v);
            };
            return tb;
        }

        private void SafeSet(CapeParameter p, object value, Control ctl)
        {
            try
            {
                ((ICapeParameter)p).value = value;
                _errors.SetError(ctl, "");
            }
            catch (Exception ex)
            {
                _errors.SetError(ctl, ex.Message);
            }
        }

        private void RestoreDefaults()
        {
            foreach (CapeParameter p in _unit.Parameters)
            {
                try { ((ICapeParameter)p).Reset(); }
                catch { /* leave as-is */ }
            }
            _errors.Clear();
            BuildRows();
        }

        // ---- helpers ----
        private static string Trim(double v)
        {
            if (double.IsInfinity(v) || Math.Abs(v) >= 1e12) return v > 0 ? "∞" : "-∞";
            return v.ToString("0.######", CultureInfo.InvariantCulture);
        }

        private static double ToDouble(object o) { try { return Convert.ToDouble(o, CultureInfo.InvariantCulture); } catch { return 0.0; } }
        private static bool ToBool(object o) { try { return Convert.ToBoolean(o); } catch { return false; } }

        private static double GetBound(CapeParameter p, string prop, double fallback)
        {
            try
            {
                PropertyInfo pi = p.GetType().GetProperty(prop);
                if (pi != null) return Convert.ToDouble(pi.GetValue(p), CultureInfo.InvariantCulture);
            }
            catch { }
            return fallback;
        }

        private static string[] GetOptions(CapeParameter p)
        {
            try
            {
                PropertyInfo pi = p.GetType().GetProperty("OptionList");
                object val = pi?.GetValue(p);
                if (val is string[] arr) return arr;
                if (val is IEnumerable en)
                {
                    var list = new List<string>();
                    foreach (object o in en) list.Add(Convert.ToString(o));
                    return list.ToArray();
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Header logo: the block's equipment icon (a PNG deployed next to the block
        /// DLL as &lt;BlockCode&gt;.png) on a white tile, so it reads inside Aspen and
        /// DWSIM. Falls back to the drawn hexagon if the PNG is missing.
        /// </summary>
        private Control BuildLogo()
        {
            try
            {
                string dir = Path.GetDirectoryName(_unit.GetType().Assembly.Location);
                string png = Path.Combine(dir ?? "", _unit.BlockCode + ".png");
                if (File.Exists(png))
                {
                    var tile = new Panel { BackColor = Color.White, Padding = new Padding(3) };
                    var pb = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.White };
                    using (var img = Image.FromFile(png)) pb.Image = new Bitmap(img); // copy so the file isn't locked
                    tile.Controls.Add(pb);
                    return tile;
                }
            }
            catch { /* fall back to the drawn hexagon */ }
            return new BrandHex { BackColor = Accent };
        }

        /// <summary>Small vector hexagon logo drawn with GDI+ (no image asset needed).</summary>
        private sealed class BrandHex : Control
        {
            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                float w = Width, h = Height, cx = w / 2f, cy = h / 2f, r = Math.Min(w, h) / 2f - 3f;
                var pts = new PointF[6];
                for (int i = 0; i < 6; i++)
                {
                    double a = Math.PI / 180.0 * (60 * i - 30);
                    pts[i] = new PointF(cx + r * (float)Math.Cos(a), cy + r * (float)Math.Sin(a));
                }
                using (var pen = new Pen(Color.White, 2f))
                    e.Graphics.DrawPolygon(pen, pts);
                using (var font = new Font("Segoe UI", 8f, FontStyle.Bold))
                using (var br = new SolidBrush(Color.White))
                {
                    var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    e.Graphics.DrawString("OP", font, br, new RectangleF(0, 0, w, h), sf);
                }
            }
        }
    }
}
