using System;

namespace HeadLoss.Sim.Hydraulics
{
    /// <summary>
    /// Darcy friction factor (<c>05-simulation.md</c> §1.2): laminar 64/Re, Swamee-Jain above the
    /// turbulent threshold, linear interpolation in between.
    /// </summary>
    public readonly struct FrictionModel
    {
        /// <summary>Reynolds number at or below which flow is laminar (2300).</summary>
        public readonly double LaminarRe;
        /// <summary>Reynolds number at or above which Swamee-Jain applies (4000).</summary>
        public readonly double TurbulentRe;

        public FrictionModel(double laminarRe, double turbulentRe)
        {
            LaminarRe = laminarRe;
            TurbulentRe = turbulentRe;
        }

        /// <summary>Darcy friction factor.</summary>
        /// <param name="reynolds">Reynolds number (≥ 0).</param>
        /// <param name="relativeRoughness">ε / D.</param>
        public double Factor(double reynolds, double relativeRoughness)
        {
            if (reynolds <= LaminarRe) return reynolds > 0 ? 64.0 / reynolds : double.PositiveInfinity;
            if (reynolds >= TurbulentRe) return SwameeJain(reynolds, relativeRoughness);
            double f0 = 64.0 / LaminarRe;
            double f1 = SwameeJain(TurbulentRe, relativeRoughness);
            return f0 + (reynolds - LaminarRe) / (TurbulentRe - LaminarRe) * (f1 - f0);
        }

        /// <summary>Re · df/dRe (used for the exact Newton derivative of Δp).</summary>
        public double ReTimesDerivative(double reynolds, double relativeRoughness)
        {
            if (reynolds <= LaminarRe) return -Factor(reynolds, relativeRoughness);
            if (reynolds >= TurbulentRe)
            {
                double t = 5.74 * Math.Pow(reynolds, -0.9);
                double u = relativeRoughness / 3.7 + t;
                double lg = Math.Log10(u);
                double f = 0.25 / (lg * lg);
                // f = 0.25 lg^-2, dlg/dRe = (1/(u ln10)) · (−0.9 t / Re)
                return -2.0 * f / lg * (-0.9 * t) / (u * Math.Log(10.0));
            }
            double f0 = 64.0 / LaminarRe;
            double f1 = SwameeJain(TurbulentRe, relativeRoughness);
            return reynolds * (f1 - f0) / (TurbulentRe - LaminarRe);
        }

        /// <summary>Swamee-Jain: f = 0.25 / [log10(ε/(3.7D) + 5.74/Re^0.9)]².</summary>
        public static double SwameeJain(double reynolds, double relativeRoughness)
        {
            double lg = Math.Log10(relativeRoughness / 3.7 + 5.74 / Math.Pow(reynolds, 0.9));
            return 0.25 / (lg * lg);
        }

        /// <summary>Colebrook-White solved by fixed-point iteration (reference for tests).</summary>
        public static double Colebrook(double reynolds, double relativeRoughness)
        {
            double x = 1.0 / Math.Sqrt(SwameeJain(reynolds, relativeRoughness));
            for (int i = 0; i < 100; i++)
            {
                double next = -2.0 * Math.Log10(relativeRoughness / 3.7 + 2.51 * x / reynolds);
                if (Math.Abs(next - x) < 1e-14) { x = next; break; }
                x = next;
            }
            return 1.0 / (x * x);
        }
    }
}
