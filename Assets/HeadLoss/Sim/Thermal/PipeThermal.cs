using System;
using System.Collections.Generic;
using HeadLoss.Sim.Network;

namespace HeadLoss.Sim.Thermal
{
    /// <summary>Temperatures along a solved network (<c>05-simulation.md</c> §4).</summary>
    public sealed class ThermalResult
    {
        /// <summary>Node temperature, K.</summary>
        public double[] NodeT = Array.Empty<double>();
        /// <summary>Fluid temperature entering / leaving each link (in its flow direction), K.</summary>
        public double[] LinkInT = Array.Empty<double>(), LinkOutT = Array.Empty<double>();
        /// <summary>Heat the fluid gives to the surroundings along each link, W (negative = gains).</summary>
        public double[] HeatToAmbient = Array.Empty<double>();
        /// <summary>Heat a heater link adds to the fluid, W (negative for a cooler).</summary>
        public double[] HeatAdded = Array.Empty<double>();
        /// <summary>The network has loops (relaxed <see cref="PipeThermal.LoopPasses"/> times).</summary>
        public bool Loops;
    }

    /// <summary>
    /// A link that moves the fluid toward a setpoint within a capacity: a heater (boiler, water heater, heat exchanger) when
    /// <see cref="CapacityW"/> is positive, a cooler when negative (<c>05-simulation.md</c> §4).
    /// </summary>
    public readonly struct Heater
    {
        /// <summary>Outlet setpoint, K.</summary>
        public readonly double SetpointK;
        /// <summary>Most heat it adds (positive) or takes out (negative), W.</summary>
        public readonly double CapacityW;

        public Heater(double setpointK, double capacityW)
        {
            SetpointK = setpointK;
            CapacityW = capacityW;
        }

        /// <summary>Outlet temperature for fluid arriving at <paramref name="inletK"/>, K.</summary>
        /// <param name="massFlow">kg/s.</param>
        public double Apply(double inletK, double massFlow, double cp)
        {
            if (massFlow <= 1e-12) return inletK;
            double reach = inletK + CapacityW / (massFlow * cp);
            return CapacityW >= 0 ? Math.Max(inletK, Math.Min(SetpointK, reach)) : Math.Min(inletK, Math.Max(SetpointK, reach));
        }
    }

    /// <summary>
    /// Steady-state temperature along pipes and ducts: T_out = T_amb + (T_in − T_amb)·exp(−U·π·D_o·L / (ṁ·c_p)), mixing by
    /// mass flow at junctions, solved in flow order (topological sort; loops relax 3 times).
    /// </summary>
    public static class PipeThermal
    {
        /// <summary>Outlet temperature of one segment (test T9).</summary>
        /// <param name="inletK">Inlet fluid temperature, K (or °C, consistently).</param>
        /// <param name="ambientK">Surroundings, same unit.</param>
        /// <param name="u">Overall heat transfer coefficient per outer area, W/(m²·K).</param>
        /// <param name="outerDiameter">m.</param>
        /// <param name="length">m.</param>
        /// <param name="massFlow">kg/s (≥ 0).</param>
        /// <param name="cp">Specific heat, J/(kg·K).</param>
        public static double OutletTemperature(double inletK, double ambientK, double u, double outerDiameter, double length, double massFlow, double cp)
        {
            if (massFlow <= 1e-12) return ambientK;
            return ambientK + (inletK - ambientK) * Math.Exp(-u * Math.PI * outerDiameter * length / (massFlow * cp));
        }

        /// <param name="boundaryK">Temperature of fluid entering from fixed-pressure nodes (outside, rooms, the city), per node, K.</param>
        /// <param name="ambientK">Surroundings of each link, K.</param>
        /// <param name="u">U per link, W/(m²·K) (0 = no exchange); only pipe links exchange here.</param>
        /// <param name="outerDiameter">Per link, m.</param>
        /// <param name="cp">Specific heat, J/(kg·K).</param>
        public static ThermalResult Solve(NetworkGraph g, SolveResult r, double[] boundaryK, double[] ambientK, double[] u, double[] outerDiameter, double cp)
        {
            var ua = new double[g.Links.Count];
            for (int k = 0; k < ua.Length; k++)
            {
                NetworkLink l = g.Links[k];
                if (l.Kind == LinkKind.Pipe && u[k] > 0) ua[k] = u[k] * Math.PI * outerDiameter[k] * l.Length;
            }
            return Solve(g, r, boundaryK, ambientK, ua, cp, null);
        }

        /// <summary>
        /// Temperatures in flow order with any link exchanging heat: <c>T_out = T_amb + (T_in − T_amb)·exp(−UA / (ṁ c_p))</c> (a pipe's UA
        /// is U·π·D_o·L; a radiator's is its own, <c>05-simulation.md</c> §4), and heater links that move the fluid toward a setpoint
        /// within a capacity. Junctions mix by mass flow. Loops (circulation, heating) relax 3 times.
        /// </summary>
        /// <param name="ua">Exchange with the surroundings per link, W/K (0 = none).</param>
        /// <param name="heaters">Heaters and coolers by link (null = none).</param>
        /// <param name="guessK">
        /// Starting temperature of the nodes that are not fixed, K (null = their boundary value). Loops relax from it, so a warm start
        /// (the last solve) settles them; trees do not depend on it.
        /// </param>
        public static ThermalResult Solve(NetworkGraph g, SolveResult r, double[] boundaryK, double[] ambientK, double[] ua, double cp,
            IReadOnlyDictionary<int, Heater>? heaters, double[]? guessK = null)
        {
            int nodes = g.Nodes.Count, links = g.Links.Count;
            var res = new ThermalResult { NodeT = new double[nodes], LinkInT = new double[links], LinkOutT = new double[links], HeatToAmbient = new double[links], HeatAdded = new double[links] };
            for (int i = 0; i < nodes; i++) res.NodeT[i] = guessK != null && g.Nodes[i].Kind != NodeKind.FixedPressure ? guessK[i] : boundaryK[i];

            // Flow order: Kahn over links directed by their flow.
            var indegree = new int[nodes];
            var outLinks = new List<int>[nodes];
            for (int i = 0; i < nodes; i++) outLinks[i] = new List<int>();
            for (int k = 0; k < links; k++)
            {
                if (!r.LinkActive[k] || Math.Abs(r.MassFlow[k]) < 1e-12) continue;
                (int from, int to) = Direction(g, r, k);
                outLinks[from].Add(k);
                if (g.Nodes[to].Kind != NodeKind.FixedPressure) indegree[to]++;
            }
            // Kahn's order; when only loops are left, one loop node is taken as known (a heater's outlet first, else the node with the
            // fewest unknown inflows) and the order carries on from it, so each pass goes round the whole loop in flow order.
            var heaterOut = new HashSet<int>();
            if (heaters != null)
                foreach (var kv in heaters)
                    if (kv.Key < links && r.LinkActive[kv.Key] && Math.Abs(r.MassFlow[kv.Key]) >= 1e-12) heaterOut.Add(Direction(g, r, kv.Key).to);
            var order = new List<int>(nodes);
            var queue = new Queue<int>();
            var placed = new bool[nodes];
            for (int i = 0; i < nodes; i++) if (indegree[i] == 0) { queue.Enqueue(i); placed[i] = true; }
            var left = (int[])indegree.Clone();
            bool loops = false;
            while (order.Count < nodes)
            {
                if (queue.Count == 0)
                {
                    loops = true;
                    int pick = -1;
                    for (int i = 0; i < nodes; i++)
                    {
                        if (placed[i]) continue;
                        if (pick < 0) { pick = i; continue; }
                        bool hi = heaterOut.Contains(i), hp = heaterOut.Contains(pick);
                        if (hi != hp ? hi : left[i] < left[pick]) pick = i;
                    }
                    placed[pick] = true;
                    queue.Enqueue(pick);
                }
                int u0 = queue.Dequeue();
                order.Add(u0);
                foreach (int k in outLinks[u0])
                {
                    int to = Direction(g, r, k).to;
                    if (g.Nodes[to].Kind == NodeKind.FixedPressure || placed[to]) continue;
                    if (--left[to] <= 0)
                    {
                        placed[to] = true;
                        queue.Enqueue(to);
                    }
                }
            }
            res.Loops = loops;

            // Each node mixes what its incoming links last delivered (Gauss–Seidel): in flow order a tree is exact in one pass, and
            // a loop picks up its return from the previous pass. Before a link is first computed it carries its start node's guess.
            var inLinks = new List<int>[nodes];
            for (int i = 0; i < nodes; i++) inLinks[i] = new List<int>();
            for (int i = 0; i < nodes; i++)
                foreach (int k in outLinks[i])
                {
                    inLinks[Direction(g, r, k).to].Add(k);
                    res.LinkOutT[k] = res.NodeT[i];
                }
            for (int pass = 0; pass < (loops ? LoopPasses : 1); pass++)
            {
                foreach (int node in order)
                {
                    if (g.Nodes[node].Kind != NodeKind.FixedPressure && inLinks[node].Count > 0)
                    {
                        double heat = 0, mass = 0;
                        foreach (int k in inLinks[node])
                        {
                            double mk = Math.Abs(r.MassFlow[k]);
                            heat += mk * res.LinkOutT[k];
                            mass += mk;
                        }
                        if (mass > 0) res.NodeT[node] = heat / mass;
                    }
                    foreach (int k in outLinks[node])
                    {
                        double m = Math.Abs(r.MassFlow[k]), tin = res.NodeT[node];
                        double tout = ua[k] > 0 ? OutletTemperature(tin, ambientK[k], ua[k], m, cp) : tin;
                        res.HeatToAmbient[k] = m * cp * (tin - tout);
                        double added = 0;
                        if (heaters != null && heaters.TryGetValue(k, out Heater h))
                        {
                            double before = tout;
                            tout = h.Apply(tout, m, cp);
                            added = m * cp * (tout - before);
                        }
                        res.HeatAdded[k] = added;
                        res.LinkInT[k] = tin;
                        res.LinkOutT[k] = tout;
                    }
                }
            }
            for (int k = 0; k < links; k++)
                if (res.LinkInT[k] == 0) res.LinkInT[k] = res.LinkOutT[k] = res.NodeT[g.Links[k].From];
            return res;
        }

        /// <summary>Passes over a network with loops (<c>05-simulation.md</c> §4: loops iterate 3 times).</summary>
        public const int LoopPasses = 3;

        /// <summary>Outlet temperature of a link exchanging UA with its surroundings.</summary>
        /// <param name="ua">W/K.</param>
        /// <param name="massFlow">kg/s.</param>
        public static double OutletTemperature(double inletK, double ambientK, double ua, double massFlow, double cp)
        {
            if (massFlow <= 1e-12) return ambientK;
            return ambientK + (inletK - ambientK) * Math.Exp(-ua / (massFlow * cp));
        }

        static (int from, int to) Direction(NetworkGraph g, SolveResult r, int k) =>
            r.Flow[k] >= 0 ? (g.Links[k].From, g.Links[k].To) : (g.Links[k].To, g.Links[k].From);
    }
}
