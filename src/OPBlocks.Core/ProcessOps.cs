using System;
using System.Collections.Generic;

namespace OPBlocks.Core
{
    /// <summary>
    /// Shared steady-state helpers used by the block library so every block does its
    /// material balance and outlet-setting the same, spec-compliant way (§5): split
    /// the host-supplied feed flows, then set each outlet through the material object
    /// (composition → T,P → flash). Physical property lookups that the package can
    /// supply stay in <see cref="ThermoProxy"/>; only genuinely block-specific
    /// correlations (ambient water vapour pressure, etc.) live here.
    /// </summary>
    public static class ProcessOps
    {
        public static double Sum(double[] a)
        {
            double s = 0.0;
            if (a != null) for (int i = 0; i < a.Length; i++) s += a[i];
            return s;
        }

        /// <summary>
        /// Index of the first component whose id contains any keyword (case-insensitive),
        /// or -1. Used to locate water / a salt / a gas in the flowsheet component list.
        /// </summary>
        public static int IndexOf(string[] ids, params string[] keywords)
        {
            if (ids == null) return -1;
            for (int i = 0; i < ids.Length; i++)
            {
                string id = ids[i] == null ? "" : ids[i].ToUpperInvariant();
                foreach (string k in keywords)
                    if (id.IndexOf(k.ToUpperInvariant(), StringComparison.Ordinal) >= 0)
                        return i;
            }
            return -1;
        }

        /// <summary>
        /// Splits <paramref name="feed"/> into <paramref name="product"/> and
        /// <paramref name="reject"/> by a per-component fraction routed to product,
        /// setting each outlet at the given T,P. Conserves every component (mass balance
        /// closes exactly). Skips a zero-flow outlet gracefully.
        /// </summary>
        public static void SplitByRecovery(
            ThermoProxy feed, ThermoProxy product, ThermoProxy reject,
            double[] fracToProduct, double productTempK, double productPressPa,
            double rejectTempK, double rejectPressPa)
        {
            double[] f = feed.GetOverallMoleFlows();
            int n = f.Length;
            var prod = new double[n];
            var rej = new double[n];
            for (int i = 0; i < n; i++)
            {
                double frac = i < fracToProduct.Length ? Clamp01(fracToProduct[i]) : 0.0;
                prod[i] = f[i] * frac;
                rej[i] = f[i] - prod[i];
            }
            if (product != null && Sum(prod) > 1e-30) product.SetOutletTP(prod, productTempK, productPressPa);
            if (reject != null && Sum(rej) > 1e-30) reject.SetOutletTP(rej, rejectTempK, rejectPressPa);
        }

        public static double Clamp01(double x) { return x < 0 ? 0 : (x > 1 ? 1 : x); }
        public static double Clamp(double x, double lo, double hi) { return x < lo ? lo : (x > hi ? hi : x); }

        /// <summary>
        /// Saturation vapour pressure of water [Pa] from the Antoine equation
        /// (1–100 °C). Block-specific ambient correlation for evaporation/flash models;
        /// stream-side phase equilibria still go through the property package.
        /// </summary>
        public static double PsatWaterPa(double tempC)
        {
            double t = Clamp(tempC, 0.01, 200.0);
            // Antoine, water, P in mmHg, T in °C (NIST 1–100 °C set).
            double log10p = 8.07131 - 1730.63 / (233.426 + t);
            double mmHg = Math.Pow(10.0, log10p);
            return mmHg * 133.322;
        }

        /// <summary>Approximate molarity [mol/L] of a solute given solute and water mole flows (ρ≈1000).</summary>
        public static double MolarityMolL(double soluteMol, double waterMol)
        {
            double waterL = waterMol * 0.0180153; // kg/s ≈ L/s
            return waterL > 1e-30 ? soluteMol / waterL : 0.0;
        }

        /// <summary>van 't Hoff osmotic pressure [bar]. i = dissociation factor (≈2 for NaCl).</summary>
        public static double OsmoticBar(double molarityMolL, double vantHoffI, double tempK)
        {
            return vantHoffI * molarityMolL * 0.0831446 * tempK;
        }

        private const double GasConstant = 8.314462618;      // J/mol/K
        private const double WaterMolarVolume = 1.8068e-5;   // m3/mol (liquid, 25 C)

        /// <summary>
        /// Physical ceiling for reported osmotic pressure [bar]: saturated NaCl
        /// brine is ~390 bar; anything above means the composition left the
        /// validity range of an aqueous osmotic model.
        /// </summary>
        public const double OsmoticClampBar = 500.0;

        /// <summary>
        /// Osmotic pressure [bar] of the aqueous stream, valid for ANY user feed
        /// (any salts, any concentration, any package — spec R4):
        ///
        ///  1. Preferred: π = −(RT/V̄w)·ln(a_w) with the water activity a_w = γ_w·x_w
        ///     taken from the host property package (electrolyte packages give real
        ///     brine behaviour here, e.g. Red Sea water compositions).
        ///  2. Fallback (package cannot supply γ): ideal solution with the van 't
        ///     Hoff factor, π = −i·(RT/V̄w)·ln(x_w) — correct in the dilute limit,
        ///     stated in the report.
        ///
        /// Results are clamped to <see cref="OsmoticClampBar"/> with a warning
        /// instead of ever reporting absurd numbers (a 50/50 water/salt feed used
        /// to yield 272,000+ bar).
        /// </summary>
        public static double OsmoticPressureBar(
            ThermoProxy stream, double[] moleFlows, int waterIndex,
            double vantHoffI, double tempK, ICollection<string> warnings)
        {
            double total = Sum(moleFlows);
            double xw = total > 1e-30 && waterIndex >= 0 ? moleFlows[waterIndex] / total : 0.0;
            if (xw >= 1.0 - 1e-12) return 0.0;               // pure water
            if (xw <= 1e-12)
            {
                if (warnings != null) warnings.Add(
                    "Feed contains essentially no water — osmotic model not applicable; " +
                    "osmotic pressure reported at the " + OsmoticClampBar + " bar validity ceiling.");
                return OsmoticClampBar;
            }

            double rtOverVw = GasConstant * Math.Max(tempK, 1.0) / WaterMolarVolume; // Pa

            double piPa;
            double gammaW;
            if (stream != null && stream.TryGetLiquidActivityCoefficient(waterIndex, out gammaW))
            {
                double aw = Clamp(gammaW * xw, 1e-12, 1.0);
                piPa = -rtOverVw * Math.Log(aw);
            }
            else
            {
                piPa = -vantHoffI * rtOverVw * Math.Log(xw);
                if (warnings != null) warnings.Add(
                    "Osmotic pressure estimated from ideal solution × van 't Hoff factor " +
                    "(the selected property package did not supply a water activity). " +
                    "For brines, an electrolyte-capable package gives more accurate results.");
            }

            double piBar = piPa / 1e5;
            if (piBar > OsmoticClampBar)
            {
                if (warnings != null) warnings.Add(string.Format(
                    "Osmotic pressure estimate ({0:0} bar) exceeds the aqueous-model validity range — " +
                    "clamped to {1:0} bar. Check the feed composition (water fraction {2:0.###}).",
                    piBar, OsmoticClampBar, xw));
                piBar = OsmoticClampBar;
            }
            return piBar;
        }

        /// <summary>Faraday constant [C/mol].</summary>
        public const double Faraday = 96485.33212;

        /// <summary>Molar production rate [mol/s] from current [A] for a z-electron process at efficiency η.</summary>
        public static double FaradayMoles(double currentA, int z, double efficiency)
        {
            if (z <= 0) return 0.0;
            return efficiency * currentA / (z * Faraday);
        }
    }
}
