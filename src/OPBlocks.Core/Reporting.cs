using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace OPBlocks.Core
{
    /// <summary>
    /// Fluent builder for the human-readable calculation report a block exposes
    /// through <c>ICapeUnitReport</c> (spec §4). Aspen shows this under the
    /// block's Results; DWSIM shows it in the unit report tab.
    ///
    /// The <see cref="Warning"/> channel carries the delegation assumptions
    /// required by spec §5 rule 5 (e.g. "package is not electrolyte-capable,
    /// running in apparent-component mode") — surfaced as text, never as an error.
    /// </summary>
    public sealed class ReportBuilder
    {
        private readonly StringBuilder _sb = new StringBuilder();
        private readonly List<string> _warnings = new List<string>();
        private readonly string _title;

        public ReportBuilder(string blockName, string blockCode)
        {
            _title = blockName + "  (" + blockCode + ")";
        }

        public ReportBuilder Section(string name)
        {
            _sb.AppendLine();
            _sb.AppendLine(name);
            _sb.AppendLine(new string('-', name.Length));
            return this;
        }

        public ReportBuilder Line(string text)
        {
            _sb.AppendLine(text);
            return this;
        }

        /// <summary>Adds an aligned "label : value unit" row.</summary>
        public ReportBuilder Value(string label, double value, string unit, string format = "0.####")
        {
            _sb.Append("  ")
               .Append(label.PadRight(34))
               .Append(": ")
               .Append(value.ToString(format, CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(unit)) _sb.Append(' ').Append(unit);
            _sb.AppendLine();
            return this;
        }

        public ReportBuilder Value(string label, string value)
        {
            _sb.Append("  ").Append(label.PadRight(34)).Append(": ").AppendLine(value);
            return this;
        }

        /// <summary>Records a non-fatal assumption/warning (spec §5 rule 5, R3).</summary>
        public ReportBuilder Warning(string text)
        {
            _warnings.Add(text);
            return this;
        }

        public bool HasWarnings { get { return _warnings.Count > 0; } }

        public string Build()
        {
            var head = new StringBuilder();
            head.AppendLine("================================================================");
            head.AppendLine("  ONE PROCESS Blocks — " + _title);
            head.AppendLine("  generated " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            head.AppendLine("================================================================");

            if (_warnings.Count > 0)
            {
                head.AppendLine();
                head.AppendLine("WARNINGS / MODEL ASSUMPTIONS");
                head.AppendLine("----------------------------");
                foreach (string w in _warnings)
                    head.AppendLine("  ! " + w);
            }

            return head.ToString() + _sb.ToString();
        }
    }
}
