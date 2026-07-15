using System;

namespace OPBlocks.Core
{
    /// <summary>
    /// The pure electrodialysis engine — no CAPE-OPEN, no host objects, just arrays
    /// and doubles. Single source of truth for OP-ED physics, shared by the block
    /// (<c>OPBlocks.Electro.Electrodialysis</c>) and the validation tests so the two
    /// can never drift.
    ///
    /// This is deliberately DIFFERENT physics from the membrane blocks (no pressure,
    /// no osmotic driving force) — it exercises the factory on an electrochemical
    /// model. Ion transport is set by **Faraday's law** across a stack of cell pairs:
    ///
    ///     N_salt = eta * I * N_cp / (z * F)      [mol/s]
    ///
    /// eta = current efficiency, I = stack current, N_cp = cell pairs, z = ion
    /// valence, F = Faraday constant. Water crosses by **electro-osmotic drag**,
    /// t_w mol H2O per mol ion. The stack current is Ohmic, I = V / R_stack (Rating),
    /// or is solved from a target removal (Design). Salt transfer is capped by the
    /// salt actually present in the diluate (depletion / limiting-current regime).
    ///
    /// References:
    ///  • H. Strathmann, Ion-Exchange Membrane Separation Processes, Membrane
    ///    Science and Technology Series 9, Elsevier (2004).
    ///  • H. Strathmann, "Electrodialysis, a mature technology with a multitude of
    ///    new applications," Desalination 264 (2010) 268-288.
    ///  • R. W. Baker, Membrane Technology and Applications, 3rd ed. (2012), ch. 10.
    /// </summary>
    public static class EdModel
    {
        public enum Mode { Rating, Design }

        public const double WaterMwKgMol = 0.0180153;   // kg/mol
        public const double MaxDepletion = 0.98;         // salt transfer cap: diluate can't go fully to zero
        public const double MaxWaterFraction = 0.5;      // electro-osmotic water cap (fraction of diluate water)

        public struct Spec
        {
            public Mode CalcMode;
            public double CellPairs;            // integer-coded real
            public double AppliedVoltageV;      // V (Rating input)
            public double StackResistanceOhm;   // ohm
            public double CurrentEfficiencyPct; // %
            public double Valence;              // z (integer-coded real)
            public double WaterTransport;       // mol H2O / mol ion (electro-osmotic drag)
            public double TargetRemovalPct;     // % (Design)
        }

        public struct Result
        {
            public double StackCurrentA;
            public double StackVoltageV;
            public double FaradaicSaltMol;   // Faraday's-law transfer at the stack current (uncapped)
            public double SaltMovedMol;      // after the depletion cap
            public double WaterMovedMol;
            public bool DepletionLimited;    // true when the salt cap bit
            public double RemovalPct;        // salt removed / salt in diluate feed
            public double[] DiluateOut, ConcentrateOut; // mol/s, per component
            public double TdsDiluateInPpm, TdsDiluateOutPpm, TdsConcentrateOutPpm;
            public double SaltInDiluateMol;
        }

        public struct Energy
        {
            public double StackPowerKW;
            public double SEC;               // kWh/m3 of product (diluate out)
        }

        /// <summary>Ohmic stack current [A] from applied voltage and stack resistance.</summary>
        public static double StackCurrent(double voltageV, double resistanceOhm)
        {
            return resistanceOhm > 1e-12 ? voltageV / resistanceOhm : 0.0;
        }

        /// <summary>Mode from the integer-coded parameter (0 = Rating, 1 = Design).</summary>
        public static Mode ModeFromCode(double code) { return code >= 0.5 ? Mode.Design : Mode.Rating; }

        /// <summary>
        /// Solves the cell-pair ion/water balance. Diluate and concentrate feed mole
        /// flows come from the host; molecular weights (for TDS) are optional.
        /// </summary>
        public static Result Solve(Spec s, double[] df, double[] cf, int wi, double[] mwGmol)
        {
            int n = df.Length;
            double saltDil = ProcessOps.Sum(df) - (wi >= 0 ? df[wi] : 0.0);
            double eta = Clamp(s.CurrentEfficiencyPct, 0, 100) / 100.0;
            double z = Math.Max(1.0, Math.Round(s.Valence));
            double ncp = Math.Max(0.0, Math.Round(s.CellPairs));
            double F = ProcessOps.Faraday;

            double currentA, voltageV, faradaic, saltMove;
            if (s.CalcMode == Mode.Design)
            {
                double target = Clamp(s.TargetRemovalPct, 0, 100) / 100.0;
                double targetSalt = Math.Min(target * saltDil, saltDil * MaxDepletion);
                // invert Faraday's law for the current that delivers targetSalt
                currentA = (eta > 0 && ncp > 0) ? targetSalt * z * F / (eta * ncp) : 0.0;
                voltageV = currentA * s.StackResistanceOhm;
                faradaic = targetSalt;             // by construction
                saltMove = targetSalt;
            }
            else
            {
                currentA = StackCurrent(s.AppliedVoltageV, s.StackResistanceOhm);
                voltageV = s.AppliedVoltageV;
                faradaic = eta * currentA * ncp / (z * F);
                saltMove = Math.Min(faradaic, saltDil * MaxDepletion);
            }

            double waterMove = wi >= 0 ? Math.Min(s.WaterTransport * saltMove, df[wi] * MaxWaterFraction) : 0.0;

            var dof = (double[])df.Clone();
            var cof = (double[])cf.Clone();
            for (int i = 0; i < n; i++)
            {
                if (i == wi) { if (wi >= 0) { dof[i] -= waterMove; cof[i] += waterMove; } }
                else if (saltDil > 1e-30) { double m = saltMove * df[i] / saltDil; dof[i] -= m; cof[i] += m; }
            }

            var r = new Result
            {
                StackCurrentA = currentA,
                StackVoltageV = voltageV,
                FaradaicSaltMol = faradaic,
                SaltMovedMol = saltMove,
                WaterMovedMol = waterMove,
                DepletionLimited = faradaic > saltDil * MaxDepletion + 1e-30,
                RemovalPct = saltDil > 1e-30 ? saltMove / saltDil * 100.0 : 0.0,
                DiluateOut = dof,
                ConcentrateOut = cof,
                SaltInDiluateMol = saltDil,
            };

            if (mwGmol != null)
            {
                r.TdsDiluateInPpm = Tds(df, wi, mwGmol);
                r.TdsDiluateOutPpm = Tds(dof, wi, mwGmol);
                r.TdsConcentrateOutPpm = Tds(cof, wi, mwGmol);
            }
            return r;
        }

        /// <summary>Stack electrical power and specific energy per m3 of product (diluate out).</summary>
        public static Energy CalcEnergy(double voltageV, double currentA, double productM3h)
        {
            double kW = voltageV * currentA / 1000.0;
            return new Energy { StackPowerKW = kW, SEC = productM3h > 1e-12 ? kW / productM3h : 0.0 };
        }

        private static double Tds(double[] flows, int wi, double[] mwGmol)
        {
            double kg = 0, salt = 0;
            for (int i = 0; i < flows.Length; i++)
            {
                double m = flows[i] * mwGmol[i];
                kg += m;
                if (i != wi) salt += m;
            }
            return kg > 1e-30 ? salt / kg * 1e6 : 0.0;
        }

        private static double Clamp(double x, double lo, double hi) { return x < lo ? lo : (x > hi ? hi : x); }
    }
}
