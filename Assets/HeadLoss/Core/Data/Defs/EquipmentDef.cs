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
        /// <summary>Fans and pumps with a switch: time to slow from full speed to a stop, s (<c>05-simulation.md</c> §2).</summary>
        public double RunDownSeconds;
        /// <summary>Fans and pumps with a switch: time to reach full speed from a stop, s.</summary>
        public double SpinUpSeconds;
        public FootprintDef Footprint = new FootprintDef();
        public List<string> Fluids = new List<string>();
        /// <summary>none (hand crank) or curve (P = Q·Δp/η).</summary>
        public string PowerModel = "";
        public bool IsIgnitionSource;
        /// <summary>crank, on-off, variable.</summary>
        public string SpeedControl = "";
        /// <summary>intake (at an entry opening), inline, addon (fitted to another device, e.g. a variable-speed drive), room (any room tile) or wall (an exterior wall).</summary>
        public string Placement = "";
        /// <summary>Add-ons: efficiency multiplier on the device they are fitted to (VSD 0.97: "costs 3% efficiency").</summary>
        public double EfficiencyFactor = 1;
        /// <summary>Add-ons: device categories they fit (e.g. air-mover).</summary>
        public List<string> FitsCategories = new List<string>();
        /// <summary>Performance curve at full speed, 5–8 points from Q = 0 to Q_max.</summary>
        public List<CurvePointDef> Curve = new List<CurvePointDef>();
        /// <summary>Loss coefficient for reverse flow through the device.</summary>
        public double ReverseK;
        /// <summary>Connection diameter used for the reverse-flow K and the initial guess, mm.</summary>
        public double ConnectionDiameterMm;
        /// <summary>Purchase price, $.</summary>
        public double Price;
        /// <summary>Electrical panels: total kW limit.</summary>
        public double PanelLimitKW;
        /// <summary>Gas regulators: outlet pressure, Pa gauge (the default when the setpoint can be adjusted).</summary>
        public double SetpointPa;
        /// <summary>Gas regulators: from this tier the setpoint is adjustable between the min and max, Pa (0 = fixed; <c>07</c>: 18–30 mbar from tier 4).</summary>
        public int SetpointFromTier;
        public double SetpointMinPa;
        public double SetpointMaxPa;
        /// <summary>Setpoint step, Pa (<c>07</c>: 1 mbar).</summary>
        public double SetpointStepPa;
        /// <summary>Gas regulators: loss coefficient when fully open, referenced to the connecting pipe.</summary>
        public double RegulatorK;
        /// <summary>
        /// Devices that join networks (water heater, boiler, heat exchanger): where pipes connect, as offsets in the footprint before
        /// it is turned (<c>05-simulation.md</c> §1.5 coupling nodes).
        /// </summary>
        public List<PortDef> Ports = new List<PortDef>();
        /// <summary>Heat devices: the most heat they add, W.</summary>
        public double HeatOutputW;
        /// <summary>Heat devices: outlet temperature they heat toward, °C.</summary>
        public double SetpointC;
        /// <summary>Heat devices: share of the fuel or electricity that becomes heat in the water (boiler 0.92, water heater 0.98).</summary>
        public double ThermalEfficiency;
        /// <summary>Devices with ports: loss coefficient of the water passing through, referenced to the connection diameter.</summary>
        public double ResistanceK;
        /// <summary>Room devices: must stand against an exterior wall (a boiler's flue).</summary>
        public bool NeedsExteriorWall;
        public string Notes = "";
    }

    /// <summary>One pipe connection of a device (<see cref="EquipmentDef.Ports"/>).</summary>
    public sealed class PortDef
    {
        /// <summary>The network this port is on (<c>networks.json</c>).</summary>
        public string Network = "";
        /// <summary>
        /// in: the device draws water from the network (the same flow it sends out of its out port); fuel: it draws gas for the heat
        /// it makes; out: it feeds the network at the pressure its in port has; return: water comes back into it; flow: it feeds a
        /// closed loop (with its return port and an expansion vessel).
        /// </summary>
        public string Role = "";
        /// <summary>Tile offset in the footprint before turning (dx along its width, dy along its depth).</summary>
        public int Dx;
        public int Dy;
    }

    public sealed class FootprintDef
    {
        public int Width = 1;
        public int Height = 1;
        /// <summary>floor, ceiling or none (an opening fitting that takes no space).</summary>
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
