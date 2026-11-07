namespace HeadLoss.Core.Data.Defs
{
    /// <summary>
    /// Entry in <c>networks.json</c>: one fluid system a building can have (<c>05-simulation.md</c> §1.5). Each is its own graph.
    /// The id is the key everything else uses (states, solves, failures, outlets' <c>fluid</c>); it equals the fluid id except
    /// where two systems carry the same fluid (air supply and air exhaust).
    /// </summary>
    public sealed class NetworkDef
    {
        public string Id = "";
        /// <summary>Fluid id in <c>fluids.json</c> (properties, risk, hazards).</summary>
        public string Fluid = "";
        /// <summary>Name in the toolbox and readouts ("Cold water").</summary>
        public string DisplayName = "";
        /// <summary>Short tab label ("Water").</summary>
        public string ShortName = "";
        /// <summary>Entry opening utility that starts this network (air, water, gas, heating, …); "" = started by a device inside the building.</summary>
        public string Utility = "";
        /// <summary>duct (air) or pipe.</summary>
        public string Conduit = "";
        /// <summary>Display unit of pressure: Pa, mbar or bar.</summary>
        public string PressureUnit = "";
        /// <summary>Display unit of flow: m3/h, L/s or L/min.</summary>
        public string FlowUnit = "";
        /// <summary>First tier a building can have this network.</summary>
        public int Tier;
        /// <summary>A closed loop (heating): no outlet draws water out of it; a device keeps its pressure.</summary>
        public bool ClosedLoop;
        /// <summary>Plain-view colour (hex); "" = the fluid's.</summary>
        public string Colour = "";
        /// <summary>Line pattern (solid, dashed, dotted, double); "" = the fluid's.</summary>
        public string LinePattern = "";
        /// <summary>Material the drawing tool starts with.</summary>
        public string DefaultMaterial = "";
        /// <summary>Size the drawing tool starts with, mm.</summary>
        public double DefaultSizeMm;

        public bool IsDuct => Conduit == "duct";
    }
}
