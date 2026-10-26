using System.Collections.Generic;

namespace HeadLoss.Core.Data.Defs
{
    /// <summary>Entry in <c>equipment.json</c>.</summary>
    public sealed class EquipmentDef
    {
        public string Id = "";
        public string DisplayName = "";
        public int Tier;
        /// <summary>air-mover, water-mover, gas-supply, climate, control, safety, panel.</summary>
        public string Category = "";
        public FootprintDef Footprint = new FootprintDef();
        public List<string> Fluids = new List<string>();
        /// <summary>none (hand crank) or curve (P = Q·Δp/η).</summary>
        public string PowerModel = "";
        public bool IsIgnitionSource;
        /// <summary>crank, on-off, variable.</summary>
        public string SpeedControl = "";
        /// <summary>intake (at an entry opening) or inline.</summary>
        public string Placement = "";
        /// <summary>Performance curve at full speed, 5–8 points from Q = 0 to Q_max.</summary>
        public List<CurvePointDef> Curve = new List<CurvePointDef>();
        /// <summary>Loss coefficient for reverse flow through the device.</summary>
        public double ReverseK;
        /// <summary>Connection diameter used for the reverse-flow K and the initial guess, mm.</summary>
        public double ConnectionDiameterMm;
        public string Notes = "";
    }

    public sealed class FootprintDef
    {
        public int Width = 1;
        public int Height = 1;
        /// <summary>floor or ceiling.</summary>
        public string Layer = "";
    }

    /// <summary>One curve point (<c>05-simulation.md</c> §2).</summary>
    public sealed class CurvePointDef
    {
        /// <summary>Flow, m³/h.</summary>
        public double FlowM3h;
        /// <summary>Pressure rise, Pa.</summary>
        public double PressurePa;
        /// <summary>Efficiency, 0–1.</summary>
        public double Efficiency;
    }
}
