using System;

namespace HeadLoss.Sim.Hydraulics
{
    /// <summary>Evaluation of one segment's loss at a given flow.</summary>
    public struct PipeLossResult
    {
        /// <summary>Mean velocity, m/s (signed like Q).</summary>
        public double Velocity;
        public double Reynolds;
        /// <summary>Darcy friction factor (0 at zero flow).</summary>
        public double FrictionFactor;
        /// <summary>Friction loss f·L/D·ρv|v|/2, Pa (signed like Q).</summary>
        public double FrictionDp;
        /// <summary>Fitting loss ΣK·ρv|v|/2, Pa (signed like Q).</summary>
        public double FittingsDp;
        /// <summary>d(FrictionDp + FittingsDp)/dQ, Pa·s/m³.</summary>
        public double Derivative;

        /// <summary>Total resistance loss, Pa.</summary>
        public double TotalDp => FrictionDp + FittingsDp;
    }

    /// <summary>
    /// Darcy-Weisbach segment loss: Δp = (f·L/D + ΣK)·ρv²/2 (<c>05-simulation.md</c> §1).
    /// The elevation term ρ·g·Δh is added by the caller.
    /// </summary>
    public static class PipeLoss
    {
        /// <param name="flow">Volumetric flow, m³/s (sign = direction).</param>
        /// <param name="length">Length, m.</param>
        /// <param name="hydraulicDiameter">Hydraulic diameter, m.</param>
        /// <param name="area">Flow area, m².</param>
        /// <param name="roughness">Absolute roughness, m.</param>
        /// <param name="sumK">Sum of fitting loss coefficients referenced to this segment's velocity.</param>
        /// <param name="density">kg/m³.</param>
        /// <param name="viscosity">Pa·s.</param>
        public static PipeLossResult Evaluate(double flow, double length, double hydraulicDiameter, double area,
            double roughness, double sumK, double density, double viscosity, in FrictionModel friction)
        {
            var r = new PipeLossResult();
            double absQ = Math.Abs(flow);
            double c = density / (2.0 * area * area);
            r.Velocity = flow / area;
            r.Reynolds = density * absQ * hydraulicDiameter / (area * viscosity);
            double lOverD = length / hydraulicDiameter;

            if (r.Reynolds <= friction.LaminarRe)
            {
                // Hagen-Poiseuille: f L/D ρ v|v|/2 with f = 64/Re  ⇒  32 μ L v / D² (linear, finite at Q = 0).
                double gLam = 32.0 * viscosity * length / (area * hydraulicDiameter * hydraulicDiameter);
                r.FrictionDp = gLam * flow;
                r.FrictionFactor = r.Reynolds > 0 ? 64.0 / r.Reynolds : 0;
                r.Derivative = gLam;
            }
            else
            {
                double f = friction.FactorAndDerivative(r.Reynolds, roughness / hydraulicDiameter, out double reDf);
                r.FrictionFactor = f;
                r.FrictionDp = f * lOverD * c * flow * absQ;
                r.Derivative = c * absQ * lOverD * (2.0 * f + reDf);
            }

            r.FittingsDp = sumK * c * flow * absQ;
            r.Derivative += 2.0 * sumK * c * absQ;
            return r;
        }
    }

    /// <summary>Hydrostatic pressure.</summary>
    public static class Hydrostatics
    {
        /// <summary>ρ·g·Δh, Pa.</summary>
        /// <param name="density">kg/m³.</param>
        /// <param name="gravity">m/s².</param>
        /// <param name="heightDifference">m.</param>
        public static double Pressure(double density, double gravity, double heightDifference) =>
            density * gravity * heightDifference;
    }
}
