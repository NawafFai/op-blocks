using System;
using System.Collections.Generic;
using System.Linq;
using CapeOpen;
using OPBlocks.Core;
using Xunit;

namespace OPBlocks.UnitTests
{
    /// <summary>
    /// P3 (v1.1.3): the editor's section map must stay true to the real blocks —
    /// every mapped section anchor must name an existing parameter of its block,
    /// on the right tab (input vs. result). A renamed parameter fails here, not
    /// as a silently missing header in a user's form.
    /// </summary>
    public class P3FormTests
    {
        static readonly Dictionary<string, UnitBase> Blocks = BuildBlocks();

        static Dictionary<string, UnitBase> BuildBlocks()
        {
            var map = new Dictionary<string, UnitBase>(StringComparer.OrdinalIgnoreCase);
            var assemblies = new[]
            {
                typeof(OPBlocks.Desalination.ReverseOsmosis).Assembly,
                typeof(OPBlocks.Electro.Electrodialysis).Assembly,
                typeof(OPBlocks.Lithium.DirectLithiumExtraction).Assembly,
                typeof(OPBlocks.Energy.PemElectrolyzer).Assembly,
            };
            foreach (var asm in assemblies)
                foreach (var t in asm.GetExportedTypes())
                    if (!t.IsAbstract && typeof(UnitBase).IsAssignableFrom(t))
                    {
                        var b = (UnitBase)Activator.CreateInstance(t);
                        map[b.BlockCode] = b;
                    }
            return map;
        }

        [Fact]
        public void Every_Section_Anchor_Names_A_Real_Parameter_On_The_Right_Tab()
        {
            int checked_ = 0;
            foreach (Tuple<string, string, bool> m in ParamGroups.AllMappings())
            {
                Assert.True(Blocks.ContainsKey(m.Item1), "unknown block code in ParamGroups: " + m.Item1);
                UnitBase b = Blocks[m.Item1];
                CapeParameter hit = null;
                foreach (CapeParameter p in b.Parameters)
                    if (string.Equals(p.ComponentName, m.Item2, StringComparison.OrdinalIgnoreCase)) { hit = p; break; }
                Assert.True(hit != null, m.Item1 + ": section anchor '" + m.Item2 + "' is not a parameter");
                bool isInput = UnitBase.IsInputParameter(hit);
                Assert.True(isInput == !m.Item3,
                    m.Item1 + ": '" + m.Item2 + "' is on the wrong tab (input=" + isInput + ", mappedAsResults=" + m.Item3 + ")");
                checked_++;
            }
            Assert.True(checked_ >= 40, "expected a substantial section map, found " + checked_);
        }

        /// <summary>
        /// The editor must BUILD AND RENDER for every block without hanging — a
        /// WinForms layout feedback loop (e.g. a width-anchored child inside the
        /// auto-size grid) hangs the form instead of throwing, so each block gets
        /// an STA thread with a watchdog. Also captures the OP-RO tabs as the P3
        /// visual evidence.
        /// </summary>
        [Fact]
        public void Editor_Builds_And_Renders_For_Every_Block_Without_Hanging()
        {
            var problems = new List<string>();
            foreach (var kv in Blocks.OrderBy(k => k.Key))
            {
                Exception fail = null;
                var t = new System.Threading.Thread(() =>
                {
                    try
                    {
                        using (var f = new OpBlockEditor(kv.Value))
                        {
                            f.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
                            f.Location = new System.Drawing.Point(-4000, -4000);
                            f.Height = 780;
                            f.Show();
                            System.Windows.Forms.Application.DoEvents();
                            Capture(f, kv.Key, "p3_input_tab.png");
                            if (kv.Key == "OP-RO")
                            {
                                foreach (System.Windows.Forms.Control c in f.Controls)
                                    if (c is System.Windows.Forms.TabControl tc)
                                    {
                                        tc.SelectedIndex = 1;
                                        System.Windows.Forms.Application.DoEvents();
                                        Capture(f, kv.Key, "p3_results_tab.png");
                                    }
                            }
                            f.Close();
                        }
                    }
                    catch (Exception ex) { fail = ex; }
                });
                t.SetApartmentState(System.Threading.ApartmentState.STA);
                t.IsBackground = true;
                t.Start();
                if (!t.Join(TimeSpan.FromSeconds(12))) problems.Add(kv.Key + ": HUNG");
                else if (fail != null) problems.Add(kv.Key + ": " + fail.Message);
            }
            Assert.True(problems.Count == 0, string.Join(" | ", problems));
        }

        static void Capture(System.Windows.Forms.Form f, string code, string name)
        {
            if (code != "OP-RO") return; // evidence for the reference block only
            try
            {
                using (var bmp = new System.Drawing.Bitmap(f.Width, f.Height))
                {
                    f.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, f.Width, f.Height));
                    System.IO.Directory.CreateDirectory(@"C:\Users\Public\OPBlocks\p2-20260720");
                    bmp.Save(@"C:\Users\Public\OPBlocks\p2-20260720\" + name);
                }
            }
            catch { /* evidence capture is best-effort; the render itself is the test */ }
        }

        [Fact]
        public void Section_Lookup_Answers_Only_At_The_Anchor()
        {
            Assert.Equal("Membrane", ParamGroups.SectionStartingAt("OP-RO", "Area", false));
            Assert.Null(ParamGroups.SectionStartingAt("OP-RO", "WaterPermA", false));
            Assert.Equal("Performance", ParamGroups.SectionStartingAt("OP-RO", "Recovery", true));
            Assert.Null(ParamGroups.SectionStartingAt("OP-XYZ", "Area", false));
        }
    }
}
