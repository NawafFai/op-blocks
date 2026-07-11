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
        private TableLayoutPanel _table;
        private Panel _scroll;

        private static readonly Color Accent = ColorTranslator.FromHtml("#0E7C66");
        private static readonly Color AccentSoft = Color.FromArgb(220, 239, 234);
        private static readonly Color PanelBg = Color.FromArgb(244, 246, 245);

        public OpBlockEditor(UnitBase unit)
        {
            _unit = unit;
            _errors.BlinkStyle = ErrorBlinkStyle.NeverBlink;
            InitializeForm();
            BuildRows();
        }

        private void InitializeForm()
        {
            Text = _unit.BlockCode + "  —  Configure / إعداد";
            Font = new Font("Segoe UI", 9f);
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(600, 440);
            Size = new Size(660, 540);
            BackColor = Color.White;
            ShowIcon = false;
            MaximizeBox = false;
            MinimizeBox = false;

            // --- header (brand bar) ---
            var header = new Panel { Dock = DockStyle.Top, Height = 66, BackColor = Accent };
            Control logo = BuildLogo();
            logo.Size = new Size(42, 42);
            logo.Location = new Point(14, 12);
            var title = new Label
            {
                Text = _unit.ComponentName,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 13f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(62, 11),
                BackColor = Color.Transparent
            };
            var subtitle = new Label
            {
                Text = "ONE PROCESS Blocks  ·  " + _unit.BlockCode,
                ForeColor = AccentSoft,
                Font = new Font("Segoe UI", 8.5f),
                AutoSize = true,
                Location = new Point(62, 38),
                BackColor = Color.Transparent
            };
            header.Controls.Add(logo);
            header.Controls.Add(title);
            header.Controls.Add(subtitle);

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

            // --- scrollable parameter area ---
            _scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(16, 14, 16, 14) };
            _table = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 4,
                Dock = DockStyle.Top,
                CellBorderStyle = TableLayoutPanelCellBorderStyle.None
            };
            _table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
            _table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
            _table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));
            _table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _scroll.Controls.Add(_table);

            Controls.Add(_scroll);
            Controls.Add(footer);
            Controls.Add(header);
            AcceptButton = close;
        }

        private void BuildRows()
        {
            _table.SuspendLayout();
            _table.Controls.Clear();
            _table.RowStyles.Clear();
            _table.RowCount = 0;

            AddHeaderCell("Parameter", 0);
            AddHeaderCell("Value", 1);
            AddHeaderCell("Unit", 2);
            AddHeaderCell("Allowed / range", 3);

            foreach (CapeParameter p in _unit.Parameters)
                AddParameterRow(p);

            _table.ResumeLayout();
        }

        private void AddHeaderCell(string text, int col)
        {
            var lbl = new Label
            {
                Text = text,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(90, 106, 102),
                AutoSize = true,
                Margin = new Padding(3, 4, 3, 8)
            };
            _table.Controls.Add(lbl, col, _table.RowCount);
            if (col == 3) _table.RowCount++;
        }

        private void AddParameterRow(CapeParameter p)
        {
            int row = _table.RowCount;

            var name = new Label
            {
                Text = p.ComponentName,
                AutoSize = true,
                Margin = new Padding(3, 8, 3, 6),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(34, 48, 44)
            };
            _table.Controls.Add(name, 0, row);

            Control input = CreateInput(p, out string unit, out string info);
            input.Margin = new Padding(3, 5, 3, 4);
            _table.Controls.Add(input, 1, row);

            var unitLbl = new Label { Text = unit ?? "", AutoSize = true, Margin = new Padding(3, 8, 3, 6), ForeColor = Color.FromArgb(91, 106, 102) };
            _table.Controls.Add(unitLbl, 2, row);

            var infoLbl = new Label
            {
                Text = info ?? p.ComponentDescription ?? "",
                AutoSize = true,
                MaximumSize = new Size(180, 0),
                Margin = new Padding(3, 8, 3, 6),
                ForeColor = Color.FromArgb(140, 150, 143),
                Font = new Font("Segoe UI", 8f)
            };
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
