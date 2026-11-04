using System;
using System.Collections.Generic;
using HeadLoss.Core.Data;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Sim.Fluids;
using HeadLoss.Sim.Network;

namespace HeadLoss.Game.City
{
    /// <summary>A building's city water connection after the city solve.</summary>
    public readonly struct WaterConnection
    {
        public readonly bool Connected;
        /// <summary>Pressure at the connection, Pa gauge.</summary>
        public readonly double PressurePa;
        /// <summary>Share of its demand the mains can carry (1 = all; less when a main upstream is over capacity).</summary>
        public readonly double Share;

        public WaterConnection(bool connected, double pressurePa, double share)
        {
            Connected = connected;
            PressurePa = pressurePa;
            Share = share;
        }
    }

    /// <summary>A building's power after the city allocation.</summary>
    public readonly struct PowerConnection
    {
        /// <summary>A cable reaches the plot (else the basic connection).</summary>
        public readonly bool Cabled;
        /// <summary>Connection capacity, kW.</summary>
        public readonly double ConnectionKW;
        /// <summary>What the city can give it now, kW (less than asked in a brownout).</summary>
        public readonly double AllocatedKW;

        public PowerConnection(bool cabled, double connectionKW, double allocatedKW)
        {
            Cabled = cabled;
            ConnectionKW = connectionKW;
            AllocatedKW = allocatedKW;
        }
    }

    /// <summary>The city solve: connections, utilisation per main and cable, substation loads.</summary>
    public sealed class CityNetworkResult
    {
        public readonly Dictionary<string, WaterConnection> Water = new Dictionary<string, WaterConnection>();
        /// <summary>Gas connections: pressure at the plot, and the share of its demand the mains carry.</summary>
        public readonly Dictionary<string, WaterConnection> Gas = new Dictionary<string, WaterConnection>();
        public readonly Dictionary<string, PowerConnection> Power = new Dictionary<string, PowerConnection>();
        /// <summary>Requested load ÷ capacity per main or cable id (above 1 = overloaded).</summary>
        public readonly Dictionary<int, double> Utilisation = new Dictionary<int, double>();
        /// <summary>Load per substation (facility id), kW.</summary>
        public readonly Dictionary<string, double> SubstationKW = new Dictionary<string, double>();
        public bool Converged = true;
    }

    /// <summary>
    /// Solves the city's utilities (<c>03-city.md</c> §City network solve, §Electricity). Water: each facility is a
    /// fixed-pressure source, each connected building a demand equal to its building network's inflow, solved with the
    /// building solver; a main over capacity scales the demands of every building downstream (proportional share,
    /// up to <c>capacityPasses</c> passes). Power: each cable's load is the draw downstream of it; where a cable or a
    /// substation is over capacity the furthest buildings are cut first.
    /// </summary>
    public static class CityNetwork
    {
        /// <param name="waterDemand">A plot's water inflow, m³/s.</param>
        /// <param name="powerDemand">A plot's requested electrical draw, kW.</param>
        /// <param name="gasDemand">A plot's gas inflow, m³/s at its own (low) pressure; null = none.</param>
        public static CityNetworkResult Solve(DataRepository data, CityLayout city, UtilityState u, Func<string, double> waterDemand, Func<string, double> powerDemand, Func<string, double>? gasDemand = null)
        {
            var r = new CityNetworkResult();
            SolveWater(data, city, u, waterDemand, r);
            SolveGas(data, city, u, gasDemand ?? (_ => 0), r);
            SolvePower(data, city, u, powerDemand, r);
            return r;
        }

        sealed class Graph
        {
            public readonly NetworkGraph G = new NetworkGraph();
            public readonly Dictionary<(int, int), int> Node = new Dictionary<(int, int), int>();
            /// <summary>Graph link → (main id, capacity).</summary>
            public readonly List<(int main, double capacity)> LinkMain = new List<(int, double)>();
            public readonly Dictionary<string, int> PlotNode = new Dictionary<string, int>();
            /// <summary>Path length per link, tiles.</summary>
            public readonly List<int> LinkTiles = new List<int>();
        }

        /// <summary>Nodes at every main's ends and where another main of the utility starts or ends on it; links between them.</summary>
        /// <param name="pressureClass">Gas: only the mains of this class ("" = all of the utility).</param>
        static Graph Build(CityLayout city, UtilityState u, string utility, Func<MainState, double> capacity, Func<MainState, double> diameter, double roughness, string pressureClass = "")
        {
            var g = new Graph();
            var mains = u.Mains.FindAll(m => m.Utility == utility && m.Tiles.Count >= 2 && (pressureClass.Length == 0 || m.PressureClass == pressureClass));
            var splits = new HashSet<(int, int)>();
            foreach (MainState m in mains) { splits.Add(m.Start); splits.Add(m.End); }
            int NodeAt((int, int) t)
            {
                if (!g.Node.TryGetValue(t, out int n)) g.Node[t] = n = g.G.AddJunction(0, $"{t.Item1},{t.Item2}");
                return n;
            }
            foreach (MainState m in mains)
            {
                int from = NodeAt(m.Start), steps = 0;
                for (int i = 1; i < m.Tiles.Count; i++)
                {
                    steps++;
                    var t = (m.Tiles[i][0], m.Tiles[i][1]);
                    if (!splits.Contains(t) && i < m.Tiles.Count - 1) continue;
                    int to = NodeAt(t);
                    double d = diameter(m);
                    g.G.AddPipe(from, to, steps * city.TileMetres, d > 0 ? d : 0.1, roughness, 0, $"main-{m.Id}");
                    g.LinkMain.Add((m.Id, capacity(m)));
                    g.LinkTiles.Add(steps);
                    from = to;
                    steps = 0;
                }
                if (m.Destination.Length > 0 && !g.PlotNode.ContainsKey(m.Destination)) g.PlotNode[m.Destination] = NodeAt(m.End);
            }
            return g;
        }

        static bool OnFacility(CityLayout city, (int x, int y) t, string utility, out FacilityPlacement? facility)
        {
            foreach (FacilityPlacement f in city.Facilities)
                if (f.Utility == utility && t.x >= f.X && t.y >= f.Y && t.x < f.X + f.Width && t.y < f.Y + f.Height)
                {
                    facility = f;
                    return true;
                }
            facility = null;
            return false;
        }

        static void SolveWater(DataRepository data, CityLayout city, UtilityState u, Func<string, double> demand, CityNetworkResult r)
        {
            PipeFamilyDef? family = Utilities.FamilyFor(data, "water");
            if (family == null) return;
            Graph g = Build(city, u, "water", m => Utilities.CapacityM3s(family, m.DiameterMm), m => m.DiameterMm / 1000, family.RoughnessMm / 1000);
            double source = data.Balancing.City.FacilityPressure.TryGetValue("water", out double p) ? p : 0;
            var fluid = new TableFluid(data.Fluid("water"), data.FluidTables["water"], data.Balancing.Physics.GasConstant);
            SolvePiped(data, city, "water", g, source, demand, fluid, r.Water, r);
        }

        /// <summary>
        /// Gas: one graph per pressure class, each from the receiving station at its class's pressure. A building's demand is
        /// its flow at its own low pressure; mass is conserved, so at line conditions it is smaller by the density ratio
        /// (MP and HP lines use the table's real-gas density, D21).
        /// </summary>
        static void SolveGas(DataRepository data, CityLayout city, UtilityState u, Func<string, double> demand, CityNetworkResult r)
        {
            PipeFamilyDef? family = Utilities.FamilyFor(data, "gas");
            if (family == null) return;
            FluidDef def = data.Fluid(Buildings.BuildingState.GasFluid);
            FluidTableDef table = data.FluidTables[def.Id];
            double atm = data.Balancing.Physics.AtmosphericPressurePa;
            double t = Core.Units.CToK(data.Balancing.Gas.SupplyTemperatureC);
            foreach (PressureClassDef c in family.PressureClasses)
            {
                Graph g = Build(city, u, "gas", m => Utilities.CapacityM3s(family, m.DiameterMm, m.PressureClass), m => m.DiameterMm / 1000, family.RoughnessMm / 1000, c.Id);
                double source = data.Balancing.City.FacilityPressure.TryGetValue(c.FacilityPressureKey, out double p) ? p : 0;
                var fluid = new TableFluid(def, table, data.Balancing.Physics.GasConstant, c.Id != "lp");
                double ratio = fluid.Density(atm + data.Balancing.City.FacilityPressure["gas-lp"] / 3, t) / fluid.Density(atm + source, t);
                SolvePiped(data, city, "gas", g, source, plot => demand(plot) * ratio, fluid, r.Gas, r);
            }
        }

        static void SolvePiped(DataRepository data, CityLayout city, string utility, Graph g, double source, Func<string, double> demand, IFluidProperties fluid,
            Dictionary<string, WaterConnection> into, CityNetworkResult r)
        {
            if (g.G.Links.Count == 0) return;
            foreach (var kv in g.Node)
                if (OnFacility(city, kv.Key, utility, out _))
                {
                    g.G.Nodes[kv.Value].Kind = NodeKind.FixedPressure;
                    g.G.Nodes[kv.Value].FixedPressure = source;
                }
            var requested = new Dictionary<string, double>();
            var share = new Dictionary<string, double>();
            foreach (var kv in g.PlotNode)
            {
                if (g.G.Nodes[kv.Value].Kind == NodeKind.FixedPressure) continue;
                requested[kv.Key] = Math.Max(0, demand(kv.Key));
                share[kv.Key] = 1;
                g.G.Nodes[kv.Value].Kind = NodeKind.Demand;
            }
            var solver = new NetworkSolver(SolverSettings.FromBalancing(data.Balancing));
            SolveResult? res = null;
            Dictionary<int, int>? parent = null;
            for (int pass = 0; pass < Math.Max(1, data.Balancing.City.CapacityPasses); pass++)
            {
                foreach (var kv in g.PlotNode)
                    if (requested.ContainsKey(kv.Key)) g.G.Nodes[kv.Value].Demand = requested[kv.Key] * share[kv.Key];
                res = solver.Solve(g.G, fluid);
                r.Converged &= res.Converged;
                if (pass == 0)
                    for (int k = 0; k < g.G.Links.Count; k++)
                    {
                        (int main, double cap) = g.LinkMain[k];
                        double use = cap > 0 ? Math.Abs(res.Flow[k]) / cap : 0;
                        r.Utilisation[main] = Math.Max(r.Utilisation.TryGetValue(main, out double was) ? was : 0, use);
                    }
                parent ??= Tree(g.G, res);
                bool over = false;
                foreach (var kv in g.PlotNode)
                {
                    if (!requested.ContainsKey(kv.Key)) continue;
                    double worst = 1;
                    foreach (int k in PathLinks(g.G, parent, kv.Value))
                    {
                        double cap = g.LinkMain[k].capacity, q = Math.Abs(res.Flow[k]);
                        if (q > cap * 1.0001 && q > 0) worst = Math.Min(worst, cap / q);
                    }
                    if (worst < 1) { share[kv.Key] *= worst; over = true; }
                }
                if (!over) break;
            }
            foreach (var kv in g.PlotNode)
            {
                bool live = res!.NodeActive[kv.Value];
                double s = share.TryGetValue(kv.Key, out double v) ? v : 1;
                double pressure = live ? res.NodePressure[kv.Value] : 0;
                // A starved connection: the same piping pushes only the share through, so the pressure the building sees
                // falls with the square of the share (D90).
                into[kv.Key] = new WaterConnection(live, Math.Max(0, pressure * s * s), s);
            }
        }

        /// <summary>Breadth-first tree from the fixed-pressure sources: each reached node's parent link.</summary>
        static Dictionary<int, int> Tree(NetworkGraph g, SolveResult res)
        {
            var parent = new Dictionary<int, int>();
            var queue = new Queue<int>();
            var seen = new HashSet<int>();
            for (int i = 0; i < g.Nodes.Count; i++)
                if (g.Nodes[i].Kind == NodeKind.FixedPressure) { queue.Enqueue(i); seen.Add(i); }
            while (queue.Count > 0)
            {
                int n = queue.Dequeue();
                for (int k = 0; k < g.Links.Count; k++)
                {
                    NetworkLink l = g.Links[k];
                    int v = l.From == n ? l.To : l.To == n ? l.From : -1;
                    if (v < 0 || !seen.Add(v)) continue;
                    parent[v] = k;
                    queue.Enqueue(v);
                }
            }
            return parent;
        }

        static IEnumerable<int> PathLinks(NetworkGraph g, Dictionary<int, int> parent, int node)
        {
            for (int guard = 0; guard < 10000 && parent.TryGetValue(node, out int k); guard++)
            {
                yield return k;
                NetworkLink l = g.Links[k];
                node = l.From == node ? l.To : l.From;
            }
        }

        static void SolvePower(DataRepository data, CityLayout city, UtilityState u, Func<string, double> demand, CityNetworkResult r)
        {
            Graph g = Build(city, u, "power", m => m.CapacityKW, _ => 0.1, 0);
            if (g.G.Links.Count == 0) return;
            // Sources: cable starts on a substation.
            var sourceOf = new Dictionary<int, string>();
            foreach (var kv in g.Node)
                if (OnFacility(city, kv.Key, "power", out FacilityPlacement? f))
                {
                    g.G.Nodes[kv.Value].Kind = NodeKind.FixedPressure;
                    sourceOf[kv.Value] = f!.Id;
                }
            var dummy = new SolveResult { NodeActive = new bool[g.G.Nodes.Count] };
            Dictionary<int, int> parent = Tree(g.G, dummy);
            var requested = new Dictionary<string, double>();
            var allocated = new Dictionary<string, double>();
            var pathLength = new Dictionary<string, int>();
            var paths = new Dictionary<string, List<int>>();
            var substationOf = new Dictionary<string, string>();
            foreach (var kv in g.PlotNode)
            {
                if (!parent.ContainsKey(kv.Value) && !sourceOf.ContainsKey(kv.Value)) continue;
                double want = Math.Max(0, demand(kv.Key));
                requested[kv.Key] = want;
                allocated[kv.Key] = want;
                var links = new List<int>(PathLinks(g.G, parent, kv.Value));
                paths[kv.Key] = links;
                int tiles = 0;
                foreach (int k in links) tiles += g.LinkTiles[k];
                pathLength[kv.Key] = tiles;
                // The substation at the root of its path.
                int node = kv.Value;
                foreach (int k in links) { NetworkLink l = g.G.Links[k]; node = l.From == node ? l.To : l.From; }
                substationOf[kv.Key] = sourceOf.TryGetValue(node, out string sub) ? sub : "";
            }
            // Requested loads per cable link (utilisation shows the request, so overloads read above 100%).
            var load = new double[g.G.Links.Count];
            foreach (var kv in paths) foreach (int k in kv.Value) load[k] += requested[kv.Key];
            for (int k = 0; k < load.Length; k++)
            {
                (int main, double cap) = g.LinkMain[k];
                double use = cap > 0 ? load[k] / cap : 0;
                r.Utilisation[main] = Math.Max(r.Utilisation.TryGetValue(main, out double was) ? was : 0, use);
            }
            // Brownouts: for every cable over capacity, cut the furthest buildings it feeds first.
            for (int k = 0; k < load.Length; k++)
            {
                double cap = g.LinkMain[k].capacity;
                var fed = new List<string>();
                foreach (var kv in paths) if (kv.Value.Contains(k)) fed.Add(kv.Key);
                Cut(fed, cap, allocated, pathLength);
            }
            double subCap = data.Balancing.Power.SubstationKW;
            var bySub = new Dictionary<string, List<string>>();
            foreach (var kv in substationOf)
            {
                if (!bySub.TryGetValue(kv.Value, out var list)) bySub[kv.Value] = list = new List<string>();
                list.Add(kv.Key);
            }
            foreach (var kv in bySub)
            {
                Cut(kv.Value, subCap, allocated, pathLength);
                double sum = 0;
                foreach (string plot in kv.Value) sum += requested[plot];
                r.SubstationKW[kv.Key] = sum;
            }
            foreach (var kv in paths)
            {
                double connection = kv.Value.Count > 0 ? g.LinkMain[kv.Value[0]].capacity : 0;
                r.Power[kv.Key] = new PowerConnection(true, connection, allocated[kv.Key]);
            }
        }

        /// <summary>Reduces the allocations of the given buildings, furthest first, until they fit the capacity.</summary>
        static void Cut(List<string> fed, double capacity, Dictionary<string, double> allocated, Dictionary<string, int> distance)
        {
            double total = 0;
            foreach (string p in fed) total += allocated[p];
            if (total <= capacity) return;
            fed.Sort((a, b) => distance[b] != distance[a] ? distance[b].CompareTo(distance[a]) : string.CompareOrdinal(a, b));
            double excess = total - capacity;
            foreach (string p in fed)
            {
                double take = Math.Min(allocated[p], excess);
                allocated[p] -= take;
                excess -= take;
                if (excess <= 1e-12) break;
            }
        }
    }
}
