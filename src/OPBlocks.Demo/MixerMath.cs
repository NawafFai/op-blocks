using System;

namespace OPBlocks.Demo
{
    /// <summary>Outcome of a mixer combination, in SI units.</summary>
    public struct MixResult
    {
        public double[] MoleFlows;      // per-component outlet mole flows [mol/s]
        public double TotalMoleFlow;    // Σ MoleFlows [mol/s]
        public double Pressure;         // outlet pressure [Pa]
        public double MolarEnthalpy;    // outlet molar enthalpy target [J/mol]
    }

    /// <summary>
    /// Pure mixer arithmetic, deliberately free of any CAPE-OPEN / COM types so it
    /// can be unit-tested directly (spec §9 layer 1). The only thermodynamics here
    /// is the adiabatic enthalpy balance Q = 0 ⇒ H_out = ΣH_in (spec §5 rule 1);
    /// the molar enthalpy values themselves come from the host property package.
    /// </summary>
    public static class MixerMath
    {
        /// <summary>
        /// Combines two inlets. <paramref name="h1"/>/<paramref name="h2"/> are
        /// molar enthalpies [J/mol] as returned by the property package.
        /// </summary>
        public static MixResult Mix(
            double[] flows1, double h1, double p1,
            double[] flows2, double h2, double p2,
            bool specifiedPressure, double pSpecified)
        {
            if (flows1 == null || flows2 == null)
                throw new ArgumentNullException("flows1/flows2", "Inlet flow arrays are required.");
            if (flows1.Length != flows2.Length)
                throw new ArgumentException("Inlet streams have different component counts (" +
                    flows1.Length + " vs " + flows2.Length + ").");

            int n = flows1.Length;
            var outFlows = new double[n];
            double f1 = 0.0, f2 = 0.0;
            for (int i = 0; i < n; i++)
            {
                outFlows[i] = flows1[i] + flows2[i];
                f1 += flows1[i];
                f2 += flows2[i];
            }

            double total = f1 + f2;
            // Adiabatic enthalpy balance: total enthalpy flow is conserved.
            double enthalpyFlow = h1 * f1 + h2 * f2;
            double molarEnthalpyOut = total > 0.0 ? enthalpyFlow / total : 0.0;

            double pressureOut = specifiedPressure ? pSpecified : Math.Min(p1, p2);

            return new MixResult
            {
                MoleFlows = outFlows,
                TotalMoleFlow = total,
                Pressure = pressureOut,
                MolarEnthalpy = molarEnthalpyOut
            };
        }
    }
}
