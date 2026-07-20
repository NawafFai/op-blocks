using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DWSIM.Interfaces;
using DWSIM.Interfaces.Enums.GraphicObjects;
using OPBlocks.Core;

namespace OPBlocks.DWSIM
{
    /// <summary>
    /// DWSIM editor for ONE PROCESS blocks: a Connections section (pick or create
    /// the stream for every port, like DWSIM's built-in unit editors) on top of
    /// the branded OP-Blocks parameter editor embedded below it.
    /// </summary>
    internal sealed class DwsimBlockEditor : Form
    {
        private static readonly Color Accent = ColorTranslator.FromHtml("#0E7C66");

        private readonly DwsimUnitAdapter _adapter;
        private readonly List<ComboBox> _combos = new List<ComboBox>();
        private readonly List<UnitBase.PortInfo> _ports = new List<UnitBase.PortInfo>();
        private bool _loading;

        private const string NotConnected = "<not connected>";

        public DwsimBlockEditor(DwsimUnitAdapter adapter)
        {
            _adapter = adapter;
            BuildForm();
        }

        private ISimulationObject Sim { get { return _adapter; } }
        private IFlowsheet Flowsheet { get { return _adapter.FlowSheet; } }

        private void BuildForm()
        {
            Text = BlockCatalog.DisplayTitle(_adapter.Inner.BlockCode) + "  (" +
                   (_adapter.GraphicObject != null ? _adapter.GraphicObject.Tag : "?") +
                   ")  —  Configure / إعداد";
            Font = new Font("Segoe UI", 9f);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.White;
            ShowIcon = false;
            MaximizeBox = false;
            MinimizeBox = false;

            foreach (UnitBase.PortInfo p in _adapter.Inner.PortLayout)
                if (!p.IsEnergy) _ports.Add(p);

            // Native-DWSIM layout: Connections | Parameters | Results tabs.
            var tabs = new TabControl { Dock = DockStyle.Fill };
            var tabConn = new TabPage("Connections / التوصيلات") { BackColor = Color.White, Padding = new Padding(8) };
            var tabParams = new TabPage("Parameters / البارامترات") { BackColor = Color.White };
            var tabResults = new TabPage("Results / النتائج") { BackColor = Color.White, Padding = new Padding(8) };
            tabs.TabPages.Add(tabConn);
            tabs.TabPages.Add(tabParams);
            tabs.TabPages.Add(tabResults);

            var conn = new GroupBox
            {
                Text = "Connections / التوصيلات",
                Dock = DockStyle.Top,
                ForeColor = Accent,
                Padding = new Padding(10, 6, 10, 8),
                // Generous per-row height — a clipped last row hides exactly the
                // port users forget to connect.
                Height = 46 + _ports.Count * 36
            };

            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = _ports.Count
            };
            for (int r = 0; r < _ports.Count; r++)
                table.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 66));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));

            for (int i = 0; i < _ports.Count; i++)
            {
                UnitBase.PortInfo port = _ports[i];

                table.Controls.Add(new Label
                {
                    Text = port.Name,
                    AutoSize = true,
                    ForeColor = Color.FromArgb(34, 48, 44),
                    Font = new Font("Segoe UI", 9f, System.Drawing.FontStyle.Bold),
                    Margin = new Padding(3, 8, 3, 3)
                }, 0, i);

                table.Controls.Add(new Label
                {
                    Text = port.IsInlet ? "inlet →" : "→ outlet",
                    AutoSize = true,
                    ForeColor = Color.FromArgb(120, 132, 128),
                    Margin = new Padding(3, 8, 3, 3)
                }, 1, i);

                var combo = new ComboBox
                {
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(3, 5, 3, 3),
                    Tag = i
                };
                combo.SelectedIndexChanged += OnStreamPicked;
                _combos.Add(combo);
                table.Controls.Add(combo, 2, i);

                var create = new Button
                {
                    Text = "+ create stream",
                    Tag = i,
                    Dock = DockStyle.Fill,
                    FlatStyle = FlatStyle.Flat,
                    ForeColor = Accent,
                    Margin = new Padding(3, 4, 3, 3)
                };
                create.FlatAppearance.BorderColor = Accent;
                create.Click += OnCreateStream;
                table.Controls.Add(create, 3, i);
            }
            conn.Controls.Add(table);
            conn.Dock = DockStyle.Top;
            tabConn.Controls.Add(new Label
            {
                Dock = DockStyle.Top,
                Height = 34,
                Padding = new Padding(4, 8, 4, 0),
                ForeColor = Color.FromArgb(120, 132, 128),
                Text = "Pick an existing stream for each port, or click  + create stream  to add and connect one."
            });
            tabConn.Controls.Add(conn);
            tabConn.Controls.SetChildIndex(conn, 1); // connections group above the hint

            // Parameters: the shared branded editor, embedded borderless.
            var paramHost = new Panel { Dock = DockStyle.Fill };
            var paramEditor = new OpBlockEditor(_adapter.Inner)
            {
                TopLevel = false,
                FormBorderStyle = FormBorderStyle.None,
                Dock = DockStyle.Fill
            };
            paramHost.Controls.Add(paramEditor);
            paramEditor.Show();
            tabParams.Controls.Add(paramHost);

            BuildResultsTab(tabResults);
            tabs.SelectedIndexChanged += (s, e) =>
            {
                if (tabs.SelectedTab == tabResults) RefreshResults();
            };

            Controls.Add(tabs);

            Size = new Size(760, 560);
            MinimumSize = new Size(660, 480);

            RefreshStreamLists();
            RefreshResults();
        }

        // ------------------------------------------------------------------
        //  Results tab — warnings + calculated outputs, like native blocks
        // ------------------------------------------------------------------

        private ListView _resultsList;
        private Label _warningsLabel;
        private Label _statusLabel;

        private void BuildResultsTab(TabPage tab)
        {
            _statusLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 26,
                Padding = new Padding(4, 6, 4, 0),
                ForeColor = Color.FromArgb(120, 132, 128)
            };

            _warningsLabel = new Label
            {
                Dock = DockStyle.Top,
                AutoSize = false,
                Height = 0,
                Padding = new Padding(8),
                BackColor = Color.FromArgb(255, 248, 225),
                ForeColor = Color.FromArgb(130, 90, 0),
                Visible = false
            };

            _resultsList = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable
            };
            _resultsList.Columns.Add("Result", 300);
            _resultsList.Columns.Add("Value", 140, HorizontalAlignment.Right);
            _resultsList.Columns.Add("Unit", 120);

            tab.Controls.Add(_resultsList);
            tab.Controls.Add(_warningsLabel);
            tab.Controls.Add(_statusLabel);
        }

        private void RefreshResults()
        {
            UnitBase.ResultEntry[] results = _adapter.DisplayResults;
            string[] warnings = _adapter.DisplayWarnings;

            _resultsList.Items.Clear();
            foreach (UnitBase.ResultEntry r in results)
            {
                var item = new ListViewItem(r.Label);
                string fmt = string.IsNullOrEmpty(r.Format) ? "0.####" : r.Format;
                string val;
                try { val = r.Value.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture); }
                catch { val = r.Value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture); }
                item.SubItems.Add(val);
                item.SubItems.Add(string.IsNullOrEmpty(r.Unit) || r.Unit == "-" ? "" : r.Unit);
                _resultsList.Items.Add(item);
            }

            if (warnings.Length > 0)
            {
                _warningsLabel.Text = "⚠  " + string.Join("\n⚠  ", warnings);
                int lines = warnings.Length + warnings.Sum(w => w.Length / 90);
                _warningsLabel.Height = 18 + lines * 18;
                _warningsLabel.Visible = true;
            }
            else
            {
                _warningsLabel.Visible = false;
                _warningsLabel.Height = 0;
            }

            _statusLabel.Text = results.Length > 0
                ? "Calculated outputs of " + _adapter.Inner.BlockCode + ":"
                : "Not calculated yet — connect the ports and press Solve, then reopen this tab.";
        }

        // ------------------------------------------------------------------
        //  Connection plumbing
        // ------------------------------------------------------------------

        private IConnectionPoint ConnectorFor(int portIdx, out int hostIndex)
        {
            // Same mapping BindPorts uses: inlets in declaration order onto
            // InputConnectors, outlets onto OutputConnectors.
            var go = _adapter.GraphicObject;
            hostIndex = 0;
            int iIn = 0, iOut = 0;
            for (int i = 0; i < _ports.Count; i++)
            {
                UnitBase.PortInfo p = _ports[i];
                if (p.IsInlet)
                {
                    if (i == portIdx) { hostIndex = iIn; return iIn < go.InputConnectors.Count ? go.InputConnectors[iIn] : null; }
                    iIn++;
                }
                else
                {
                    if (i == portIdx) { hostIndex = iOut; return iOut < go.OutputConnectors.Count ? go.OutputConnectors[iOut] : null; }
                    iOut++;
                }
            }
            return null;
        }

        private string AttachedStreamTag(int portIdx)
        {
            int hostIndex;
            IConnectionPoint cp = ConnectorFor(portIdx, out hostIndex);
            if (cp == null || !cp.IsAttached || cp.AttachedConnector == null) return null;
            IGraphicObject other = _ports[portIdx].IsInlet
                ? cp.AttachedConnector.AttachedFrom
                : cp.AttachedConnector.AttachedTo;
            return other != null ? other.Tag : null;
        }

        private List<ISimulationObject> MaterialStreams()
        {
            var list = new List<ISimulationObject>();
            if (Flowsheet == null) return list;
            foreach (ISimulationObject o in Flowsheet.SimulationObjects.Values)
                if (o.GraphicObject != null && o.GraphicObject.ObjectType == ObjectType.MaterialStream)
                    list.Add(o);
            return list.OrderBy(o => o.GraphicObject.Tag).ToList();
        }

        private void RefreshStreamLists()
        {
            _loading = true;
            try
            {
                List<ISimulationObject> streams = MaterialStreams();
                for (int i = 0; i < _ports.Count; i++)
                {
                    ComboBox combo = _combos[i];
                    combo.Items.Clear();
                    combo.Items.Add(NotConnected);
                    foreach (ISimulationObject s in streams) combo.Items.Add(s.GraphicObject.Tag);
                    string attached = AttachedStreamTag(i);
                    combo.SelectedItem = attached != null && combo.Items.Contains(attached) ? attached : NotConnected;
                }
            }
            finally { _loading = false; }
        }

        private void OnStreamPicked(object sender, EventArgs e)
        {
            if (_loading) return;
            var combo = (ComboBox)sender;
            int portIdx = (int)combo.Tag;
            string pick = combo.SelectedItem as string;

            try
            {
                DisconnectPort(portIdx);
                if (pick != null && pick != NotConnected)
                {
                    ISimulationObject stream = MaterialStreams()
                        .FirstOrDefault(s => s.GraphicObject.Tag == pick);
                    if (stream != null) ConnectPort(portIdx, stream);
                }
                RepaintFlowsheet();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "ONE PROCESS Blocks",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally { RefreshStreamLists(); }
        }

        private void OnCreateStream(object sender, EventArgs e)
        {
            int portIdx = (int)((Button)sender).Tag;
            try
            {
                UnitBase.PortInfo port = _ports[portIdx];
                var go = _adapter.GraphicObject;
                int hostIndex;
                ConnectorFor(portIdx, out hostIndex);
                int x = (int)(port.IsInlet ? go.X - 100 : go.X + go.Width + 60);
                int y = (int)(go.Y + hostIndex * 50);
                ISimulationObject stream = Flowsheet.AddObject(ObjectType.MaterialStream, x, y, "");
                DisconnectPort(portIdx);
                ConnectPort(portIdx, stream);
                RepaintFlowsheet();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "ONE PROCESS Blocks",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally { RefreshStreamLists(); }
        }

        private void ConnectPort(int portIdx, ISimulationObject stream)
        {
            int hostIndex;
            ConnectorFor(portIdx, out hostIndex);
            if (_ports[portIdx].IsInlet)
                Sim.ConnectFeedMaterialStream(stream, hostIndex);
            else
                Sim.ConnectProductMaterialStream(stream, hostIndex);
        }

        private void DisconnectPort(int portIdx)
        {
            int hostIndex;
            IConnectionPoint cp = ConnectorFor(portIdx, out hostIndex);
            if (cp == null || !cp.IsAttached || cp.AttachedConnector == null) return;
            IGraphicObject mine = _adapter.GraphicObject;
            IGraphicObject other = _ports[portIdx].IsInlet
                ? cp.AttachedConnector.AttachedFrom
                : cp.AttachedConnector.AttachedTo;
            if (other == null) return;
            if (_ports[portIdx].IsInlet)
                Flowsheet.DisconnectObjects(other, mine);
            else
                Flowsheet.DisconnectObjects(mine, other);
        }

        private void RepaintFlowsheet()
        {
            try { Flowsheet.UpdateInterface(); } catch { }
        }
    }
}
