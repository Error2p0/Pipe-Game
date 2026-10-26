using System;
using HeadLoss.Core;
using HeadLoss.Core.Data.Defs;

namespace HeadLoss.Game.Economy
{
    /// <summary>Where an outlet's delivery sits against its requirement (<c>02-economy.md</c> §Outlet payout bands).</summary>
    public enum OutletZone
    {
        /// <summary>No duct reaches the outlet (counts as below minimum).</summary>
        NotConnected,
        /// <summary>Under the minimum: the whole building stays offline.</summary>
        BelowMinimum,
        /// <summary>Minimum to ideal: base rate.</summary>
        Working,
        /// <summary>Ideal to code maximum: bonus with diminishing returns.</summary>
        Overdelivery,
        /// <summary>Above the code maximum: the outlet pays 0 and shows a warning.</summary>
        AboveCodeMax,
    }

    /// <summary>An outlet requirement in SI delivery units (flow m³/s, pressure Pa, heat W).</summary>
    public readonly struct OutletRequirement
    {
        public readonly double Min;
        public readonly double Ideal;
        /// <summary>Code maximum; 0 = none.</summary>
        public readonly double Max;

        public OutletRequirement(double min, double ideal, double max)
        {
            Min = min;
            Ideal = ideal;
            Max = max;
        }

        public bool HasMax => Max > 0;

        /// <summary>Converts a data requirement (in the outlet's display unit) to SI.</summary>
        public static OutletRequirement FromDef(OutletDef outlet, OutletRequirementDef r)
        {
            double f = ToSiFactor(outlet.Unit);
            return new OutletRequirement(r.Min * f, r.Ideal * f, r.Max * f);
        }

        /// <summary>SI value per display unit (m3/h, bar, mbar, W).</summary>
        public static double ToSiFactor(string unit) => unit switch
        {
            "m3/h" => 1.0 / Units.SecondsPerHour,
            "bar" => Units.PaPerBar,
            "mbar" => Units.PaPerMbar,
            "W" => 1.0,
            _ => throw new ArgumentException($"Unknown outlet unit '{unit}'"),
        };
    }

    /// <summary>The payout multiplier formula (<c>02-economy.md</c> §Payout multiplier formula).</summary>
    public static class Payout
    {
        /// <summary>Zone and payout multiplier m for a delivery.</summary>
        /// <param name="req">Requirement, SI.</param>
        /// <param name="delivery">Measured delivery, SI (same unit as <paramref name="req"/>).</param>
        /// <param name="risky">Risky fluids use the higher cap multiplier.</param>
        /// <returns>m (0 below minimum and above code maximum).</returns>
        public static double Multiplier(OutletRequirement req, double delivery, bool risky, EconomyDef e, out OutletZone zone)
        {
            double r = delivery / req.Min;
            if (r < 1)
            {
                zone = OutletZone.BelowMinimum;
                return 0;
            }
            if (req.HasMax && delivery > req.Max)
            {
                zone = OutletZone.AboveCodeMax;
                return 0;
            }
            double rIdeal = req.Ideal / req.Min;
            if (r <= rIdeal)
            {
                zone = OutletZone.Working;
                return 1;
            }
            zone = OutletZone.Overdelivery;
            double rCap = req.HasMax ? Math.Min(e.OverdeliveryCapRatio, req.Max / req.Min) : e.OverdeliveryCapRatio;
            if (rCap <= rIdeal) return 1;
            double cap = risky ? e.CapMultiplierRisky : e.CapMultiplier;
            double x = (r - rIdeal) / (rCap - rIdeal);
            x = x < 0 ? 0 : x > 1 ? 1 : x;
            return 1 + (cap - 1) * Math.Sqrt(x);
        }
    }
}
