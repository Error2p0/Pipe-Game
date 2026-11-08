using System;
using System.Collections.Generic;
using HeadLoss.Core;
using HeadLoss.Core.Data;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.Economy;
using HeadLoss.Game.Session;

namespace HeadLoss.Game.Codes
{
    /// <summary>
    /// The indoor air quality code (<c>09-codes.md</c> §Advanced codes): a room whose mean CO₂ is above the limit gets nothing
    /// for its air outlets. It is an operating code, so the building stays online. The room mean it reads is the well-mixed
    /// steady state of the room's built ventilation, the same in game, offline and in design previews (D158); the room grid,
    /// which shows the air on the heatmap, settles within about 10% of it over an hour or so.
    /// </summary>
    public static class IndoorAirQuality
    {
        public const string CodeId = "indoor-air-quality";

        /// <summary>The code is in force in this building.</summary>
        public static bool InForce(DataRepository data, BuildingLayout layout) => CodeChecker.InForce(data, layout).Contains(CodeId);

        /// <summary>The room mean CO₂ limit, ppm.</summary>
        public static double MaxPpm(DataRepository data) =>
            data.FindCode(CodeId)?.Parameters.GetValueOrDefault("maxCo2Ppm", 1000) ?? 1000;

        /// <summary>CO₂ the occupants of a room breathe out, m³/s.</summary>
        public static double Generation(DataRepository data, double occupants) =>
            Math.Max(0, occupants) * data.Balancing.Climate.OccupantCo2Lps * 1e-3;

        /// <summary>
        /// Well-mixed steady state of a room: <c>C = C_out + G / Q</c>, ppm. Infinite when people breathe in a room no fresh
        /// air reaches (the room grid then fills it from its neighbours, which this estimate leaves out).
        /// </summary>
        /// <param name="occupants">People in the room.</param>
        /// <param name="supply">Fresh air into the room, m³/s.</param>
        public static double SteadyPpm(DataRepository data, double occupants, double supply)
        {
            double outdoor = data.Balancing.Climate.OutdoorCo2Ppm;
            double g = Generation(data, occupants);
            if (g <= 0) return outdoor;
            return supply > 0 ? outdoor + g / supply * 1e6 : double.PositiveInfinity;
        }

        /// <summary>Fresh air a room needs to stay at the limit, m³/s.</summary>
        public static double RequiredSupply(DataRepository data, double occupants)
        {
            double margin = MaxPpm(data) - data.Balancing.Climate.OutdoorCo2Ppm;
            return margin > 0 ? Generation(data, occupants) / (margin * 1e-6) : double.PositiveInfinity;
        }

        /// <summary>Fresh air into each room from a solve, m³/s: its air outlets' flow plus its passive vents (wind factor 1).</summary>
        public static double[] RoomSupply(DataRepository data, BuildingLayout layout, BuildingState state, NetworkSnapshot snap)
        {
            var supply = new double[layout.Rooms.Count];
            FluidSolve air = snap.Air;
            foreach (OutletPlacementDef o in layout.Outlets)
            {
                if (!air.Compiled.OutletLink.TryGetValue(o.Id, out int link)) continue;
                int room = layout.RoomIndexAt(BuildingLayout.PosOf(o));
                if (room >= 0) supply[room] += Math.Max(0, air.Result.Flow[link]);
            }
            double passive = Units.M3hToM3s(data.Balancing.Gas.PassiveVentFlowM3h);
            foreach (SafetyDevice d in state.Safety)
            {
                if (d.EquipmentId != Failures.GasSafetyIds.PassiveVent) continue;
                int room = layout.RoomIndexAt(d.Pos);
                if (room >= 0) supply[room] += passive;
            }
            return supply;
        }

        /// <summary>Well-mixed steady CO₂ of every room for a solve, ppm.</summary>
        public static double[] SteadyPpm(DataRepository data, BuildingLayout layout, BuildingState state, NetworkSnapshot snap)
        {
            double[] supply = RoomSupply(data, layout, state, snap);
            var ppm = new double[supply.Length];
            for (int r = 0; r < ppm.Length; r++) ppm[r] = SteadyPpm(data, layout.Rooms[r].Occupants, supply[r]);
            return ppm;
        }

        /// <summary>Applies the code to the evaluation of a solve, reading each room's steady state.</summary>
        public static void Apply(DataRepository data, BuildingLayout layout, BuildingEvaluation ev, BuildingState state, NetworkSnapshot snap)
        {
            if (InForce(data, layout)) Apply(data, layout, ev, SteadyPpm(data, layout, state, snap));
        }

        /// <summary>
        /// Applies the code to an evaluation: every paying air outlet in a room above the limit pays 0 (zone
        /// <see cref="OutletZone.AirQuality"/>), and the room goes on the code list (not blocking). Does nothing where the code
        /// is not in force.
        /// </summary>
        /// <param name="roomPpm">Mean CO₂ per room, ppm.</param>
        public static void Apply(DataRepository data, BuildingLayout layout, BuildingEvaluation ev, IReadOnlyList<double> roomPpm)
        {
            if (!InForce(data, layout)) return;
            double max = MaxPpm(data);
            for (int r = 0; r < layout.Rooms.Count && r < roomPpm.Count; r++)
            {
                RoomDef room = layout.Rooms[r];
                if (room.Occupants <= 0) continue;
                double ppm = roomPpm[r];
                if (!(ppm > max)) continue;
                var v = new Violation { CodeId = CodeId, Blocking = false, Fluid = "air" };
                int cut = 0;
                foreach (OutletPlacementDef o in layout.Outlets)
                {
                    if (layout.RoomIndexAt(BuildingLayout.PosOf(o)) != r) continue;
                    OutletStatus? st = ev.Find(o.Id);
                    if (st == null || st.Def.Fluid != "air") continue;
                    v.Tiles.Add(BuildingLayout.PosOf(o));
                    if (st.Zone != OutletZone.Working && st.Zone != OutletZone.Overdelivery) continue;
                    ev.PotentialGross -= st.BaseRate * st.Multiplier;
                    ev.Gross -= st.Pay;
                    st.Zone = OutletZone.AirQuality;
                    st.Multiplier = 0;
                    st.Pay = 0;
                    cut++;
                }
                if (v.Tiles.Count == 0) continue;
                string name = data.RoomType(room.Type).DisplayName.ToLowerInvariant();
                double need = Units.M3sToM3h(RequiredSupply(data, room.Occupants));
                string people = $"{room.Occupants:0.#} {(room.Occupants == 1 ? "person needs" : "people need")} {need:0} m³/h";
                v.Reason = double.IsPositiveInfinity(ppm)
                    ? $"Indoor air quality: no fresh air reaches the {name}, so its air outlets pay nothing · {people}"
                    : $"Indoor air quality: {name} at {ppm:0} ppm CO₂ (max {max:0}), so its air outlets pay nothing · {people}";
                ev.AirQualityCut += cut;
                ev.Violations.Add(v);
            }
            ev.Gross = Math.Max(0, ev.Gross);
            ev.PotentialGross = Math.Max(0, ev.PotentialGross);
        }
    }
}
