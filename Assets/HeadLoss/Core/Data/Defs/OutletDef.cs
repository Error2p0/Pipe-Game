using System.Collections.Generic;

namespace HeadLoss.Core.Data.Defs
{
    /// <summary>Entry in <c>outlets.json</c>.</summary>
    public sealed class OutletDef
    {
        public string Id = "";
        public string DisplayName = "";
        public string Fluid = "";
        /// <summary>flow, pressure or heat.</summary>
        public string Measured = "";
        /// <summary>Display unit of min / ideal / max (m3/h, bar, mbar, W).</summary>
        public string Unit = "";
        public string CodeMaxReason = "";
        /// <summary>Pay factor × the tier's base pay (<c>13-balancing.md</c>).</summary>
        public double PayFactor;
        /// <summary>Grille fitting id (vent outlets).</summary>
        public string Grille = "";
        /// <summary>Grille face area, m² (the grille K is referenced to it).</summary>
        public double GrilleFaceAreaM2;
        /// <summary>Requirements per tier (keys "1" to "5"); a missing tier means not available.</summary>
        public Dictionary<string, OutletRequirementDef> Tiers = new Dictionary<string, OutletRequirementDef>();
    }

    public sealed class OutletRequirementDef
    {
        public double Min;
        public double Ideal;
        /// <summary>Code maximum; 0 = none.</summary>
        public double Max;
    }
}
