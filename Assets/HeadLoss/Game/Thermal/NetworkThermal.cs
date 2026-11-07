using System;
using System.Collections.Generic;
using HeadLoss.Core;
using HeadLoss.Core.Data;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.Session;
using HeadLoss.Sim.Fluids;
using HeadLoss.Sim.Network;
using HeadLoss.Sim.Thermal;

namespace HeadLoss.Game.Thermal
{
    /// <summary>Heat a network gives to (or takes from) the rooms it runs through, per tile and layer.</summary>
    public readonly struct HeatInjection
    {
        public readonly TilePos Tile;
        /// <summary>Heat into the room air, W (negative = taken out).</summary>
        public readonly double W;
        /// <summary>Into the upper layer (ceiling pipes) rather than the lower one (radiators, floor equipment).</summary>
        public readonly bool Upper;

        public HeatInjection(TilePos tile, double w, bool upper)
        {
            Tile = tile;
            W = w;
            Upper = upper;
        }
    }

    /// <summary>A piped network's temperatures from its last thermal solve (fed back into the next flow solve: viscosity falls as water warms).</summary>
    public sealed class PipeTemperatures
    {
        /// <summary>Mean fluid temperature per segment, K.</summary>
        public readonly Dictionary<int, double> SegmentK = new Dictionary<int, double>();
        /// <summary>Fluid temperature per duct node, K.</summary>
        public readonly Dictionary<int, double> NodeK = new Dictionary<int, double>();
    }

    /// <summary>
    /// The thermal solve of a piped liquid network (<c>05-simulation.md</c> §4): the city water enters at the mains temperature, each
    /// pipe exchanges heat with the rooms it runs over (U from its material or insulation, per outer area), junctions mix by mass flow,
    /// heaters raise the water toward their setpoint, and loops (circulation, heating) relax 3 times. Runs with every solve of the
    /// network, on the solve's thread, from room temperatures captured on the main thread.
    /// </summary>
    public static class NetworkThermal
    {
        /// <summary>True for networks whose temperatures are solved here: piped liquids (water, hot water, heating).</summary>
        public static bool Applies(DataRepository data, string network)
        {
            NetworkDef? n = data.FindNetwork(network);
            return n != null && !n.IsDuct && !data.Fluid(n.Fluid).IdealGas;
        }

        /// <summary>Temperature fluid enters a network at from the city or the building's source, K.</summary>
        public static double SupplyK(DataRepository data, string network) => Units.CToK(data.Balancing.Water.MainsTemperatureC);

        /// <param name="net">The state the solve was compiled from.</param>
        /// <param name="roomTemperatureC">Mean temperature per room index, °C (null or 0 = the default air temperature).</param>
        /// <param name="heaters">Heater links of the compiled graph (null = none).</param>
        /// <param name="boundaryK">Temperature entering at particular fixed nodes, by node index (others: the supply temperature).</param>
        /// <param name="previous">The network's last solve: its temperatures are where loops start relaxing from (matched by graph label).</param>
        public static void Solve(DataRepository data, BuildingLayout layout, DuctNetworkState net, FluidSolve fs, IFluidProperties props,
            float[]? roomTemperatureC, IReadOnlyDictionary<int, Heater>? heaters = null, IReadOnlyDictionary<int, double>? boundaryK = null,
            FluidSolve? previous = null)
        {
            NetworkGraph g = fs.Compiled.Graph;
            int nodes = g.Nodes.Count, links = g.Links.Count;
            double supply = SupplyK(data, fs.Fluid);
            double fallback = Units.CToK(data.Balancing.Physics.DefaultAirTemperatureC);
            double RoomK(int room) =>
                roomTemperatureC != null && room >= 0 && room < roomTemperatureC.Length && roomTemperatureC[room] != 0 ? Units.CToK(roomTemperatureC[room]) : fallback;

            var boundary = new double[nodes];
            for (int i = 0; i < nodes; i++) boundary[i] = boundaryK != null && boundaryK.TryGetValue(i, out double b) ? b : supply;
            var ambient = new double[links];
            for (int k = 0; k < links; k++) ambient[k] = fallback;
            var ua = new double[links];
            foreach (DuctSegment s in net.Segments)
            {
                if (!fs.Compiled.SegmentLink.TryGetValue(s.Id, out int k)) continue;
                List<TilePos> tiles = DuctEditor.Tiles(net, s);
                double sum = 0;
                foreach (TilePos t in tiles) sum += RoomK(layout.RoomIndexAt(t));
                if (tiles.Count > 0) ambient[k] = sum / tiles.Count;
                (double u, double outer) = Exchange(data, s);
                ua[k] = u * Math.PI * outer * g.Links[k].Length;
            }
            double cp = props.SpecificHeat(data.Balancing.Physics.AtmosphericPressurePa, supply);
            ThermalResult t0 = PipeThermal.Solve(g, fs.Result, boundary, ambient, ua, cp, heaters, Guess(g, previous, heaters, supply));
            fs.Thermal = t0;
            foreach (var kv in fs.Compiled.SegmentLink) fs.Temperatures.SegmentK[kv.Key] = 0.5 * (t0.LinkInT[kv.Value] + t0.LinkOutT[kv.Value]);
            foreach (var kv in fs.Compiled.NodeIndex) fs.Temperatures.NodeK[kv.Key] = t0.NodeT[kv.Value];
        }

        /// <summary>
        /// Where loop temperatures start: the last solve's, node by node (graph labels are stable across compiles); without one, the
        /// hottest working heater's setpoint (a heated loop settles near it), else the supply temperature.
        /// </summary>
        static double[] Guess(NetworkGraph g, FluidSolve? previous, IReadOnlyDictionary<int, Heater>? heaters, double supplyK)
        {
            double start = supplyK;
            if (heaters != null)
                foreach (Heater h in heaters.Values) if (h.CapacityW > 0) start = Math.Max(start, h.SetpointK);
            Dictionary<string, double>? last = null;
            if (previous?.Thermal != null)
            {
                NetworkGraph pg = previous.Compiled.Graph;
                last = new Dictionary<string, double>(pg.Nodes.Count);
                for (int i = 0; i < pg.Nodes.Count; i++) last[pg.Nodes[i].Label] = previous.Thermal.NodeT[i];
            }
            var guess = new double[g.Nodes.Count];
            for (int i = 0; i < guess.Length; i++) guess[i] = last != null && last.TryGetValue(g.Nodes[i].Label, out double t) ? t : start;
            return guess;
        }

        /// <summary>U per outer area, W/(m²·K), and the outer diameter, m, of a pipe from its material or insulation.</summary>
        public static (double u, double outer) Exchange(DataRepository data, DuctSegment s)
        {
            double outer = Units.MmToM(s.DiameterMm);
            InsulationDef? ins = s.Insulation.Length > 0 ? data.FindInsulation(s.Insulation) : null;
            if (ins != null) return (ins.U, outer + 2 * Units.MmToM(ins.ThicknessMm));
            return (data.Material(s.MaterialId).UUninsulated, outer);
        }

        /// <summary>Heat the network's pipes give to the rooms they run over, spread evenly along each segment (upper layer).</summary>
        public static void AddPipeHeat(FluidSolve fs, DuctNetworkState net, List<HeatInjection> into)
        {
            if (fs.Thermal == null) return;
            foreach (DuctSegment s in net.Segments)
            {
                if (!fs.Compiled.SegmentLink.TryGetValue(s.Id, out int k)) continue;
                double heat = fs.Thermal.HeatToAmbient[k];
                if (Math.Abs(heat) < 1e-6) continue;
                List<TilePos> tiles = DuctEditor.Tiles(net, s);
                foreach (TilePos t in tiles) into.Add(new HeatInjection(t, heat / tiles.Count, true));
            }
        }
    }
}
