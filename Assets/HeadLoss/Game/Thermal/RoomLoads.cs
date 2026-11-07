using System;
using HeadLoss.Core.Data;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Buildings;

namespace HeadLoss.Game.Thermal
{
    /// <summary>A room's heating and cooling loads at the design outside temperatures, W.</summary>
    public readonly struct RoomLoad
    {
        /// <summary>Heat the room loses at the design winter temperature with the room at the heating setpoint, W.</summary>
        public readonly double HeatingW;
        /// <summary>Heat that must be taken out at the design summer temperature with the room at the cooling setpoint, W.</summary>
        public readonly double CoolingW;
        /// <summary>Exterior wall area of the room, m².</summary>
        public readonly double ExteriorWallM2;

        public RoomLoad(double heatingW, double coolingW, double exteriorWallM2)
        {
            HeatingW = heatingW;
            CoolingW = coolingW;
            ExteriorWallM2 = exteriorWallM2;
        }
    }

    /// <summary>
    /// Room loads that heat outlets are sized against (<c>13-balancing.md</c> §Outlets: radiator and cooling-terminal requirements
    /// are the room load ×1.0 / 1.2 / 1.6, at −5 °C for heating and +32 °C for cooling, shifted by climate modifiers). They use
    /// the same walls as the room grid (<c>05-simulation.md</c> §3.2 step 6), so a room that meets its load holds its setpoint
    /// on the temperature heatmap at the design temperature.
    /// <list type="bullet">
    /// <item>Heating: <c>U_wall · A_ext · (T_set,heat − T_design,winter) × heatingFactor</c>. Internal gains are left out, as in a
    /// heating design: the room has to stay warm when it is empty at night.</item>
    /// <item>Cooling: <c>U_wall · A_ext · (T_design,summer − T_set,cool)</c> + the sun at its peak through each window (facade
    /// factor) + occupants (sensible) + machines.</item>
    /// </list>
    /// </summary>
    public static class RoomLoads
    {
        /// <summary>The loads of one room.</summary>
        /// <param name="roomIndex">Index into <see cref="BuildingLayout.Rooms"/>.</param>
        /// <param name="climate">The city's climate (modifiers shift the design temperatures, cold-climate raises heating).</param>
        public static RoomLoad For(DataRepository data, BuildingLayout layout, int roomIndex, Climate climate)
        {
            ClimateDef c = data.Balancing.Climate;
            RoomDef room = layout.Rooms[roomIndex];
            double area = ExteriorWallArea(layout, roomIndex, data.Balancing.RoomGrid.RoomHeight);
            double u = climate.WallU;
            double winter = c.DesignHeatingC + climate.TemperatureShiftK, summer = c.DesignCoolingC + climate.TemperatureShiftK;
            double heating = u * area * Math.Max(0, c.HeatingSetpointC - winter) * climate.HeatingFactor;

            double sun = 0;
            double window = data.Buildings.Generation.WindowAreaM2;
            foreach (WindowDef w in layout.Windows)
            {
                if (layout.RoomIndexAt(new TilePos(w.X, w.Y, w.Floor)) != roomIndex) continue;
                sun += c.SolarPeakWm2 * (c.FacadeFactors.TryGetValue(w.Side, out double f) ? f : 0) * window;
            }
            double cooling = u * area * Math.Max(0, summer - c.CoolingSetpointC) + sun + room.Occupants * c.OccupantSensibleW + room.MachinesW;
            return new RoomLoad(heating, cooling, area);
        }

        /// <summary>Area of the room's walls to the outside, m²: each tile edge facing a tile no room is on, × the room height.</summary>
        public static double ExteriorWallArea(BuildingLayout layout, int roomIndex, double roomHeight)
        {
            RoomDef room = layout.Rooms[roomIndex];
            int edges = 0;
            for (int x = room.X; x < room.X + room.Width; x++)
                for (int y = room.Y; y < room.Y + room.Height; y++)
                {
                    var t = new TilePos(x, y, room.Floor);
                    if (layout.RoomIndexAt(t) != roomIndex) continue;
                    foreach (string side in Sides)
                        if (!layout.IsInside(BuildingLayout.Neighbour(t, side))) edges++;
                }
            return edges * roomHeight;
        }

        static readonly string[] Sides = { "north", "south", "east", "west" };
    }
}
