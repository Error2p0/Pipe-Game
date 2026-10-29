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

        /// <param name="boundaryK">Temperature of air entering from fixed-pressure nodes (outside, rooms), per node, K.</param>
        /// <param name="ambientK">Surroundings of each link, K.</param>
        /// <param name="u">U per link, W/(m²·K) (0 = no exchange).</param>
        /// <param name="outerDiameter">Per link, m.</param>
        /// <param name="cp">Specific heat, J/(kg·K).</param>
        public static ThermalResult Solve(NetworkGraph g, SolveResult r, double[] boundaryK, double[] ambientK, double[] u, double[] outerDiameter, double cp)
        {
            int nodes = g.Nodes.Count, links = g.Links.Count;
            var res = new ThermalResult { NodeT = new double[nodes], LinkInT = new double[links], LinkOutT = new double[links], HeatToAmbient = new double[links] };
            for (int i = 0; i < nodes; i++) res.NodeT[i] = boundaryK[i];

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
            var order = new List<int>(nodes);
            var queue = new Queue<int>();
            for (int i = 0; i < nodes; i++) if (indegree[i] == 0) queue.Enqueue(i);
            var left = (int[])indegree.Clone();
            while (queue.Count > 0)
            {
                int u0 = queue.Dequeue();
                order.Add(u0);
                foreach (int k in outLinks[u0])
                {
                    int to = Direction(g, r, k).to;
                    if (g.Nodes[to].Kind == NodeKind.FixedPressure) continue;
                    if (--left[to] == 0) queue.Enqueue(to);
                }
            }
            bool loops = order.Count < nodes;
            if (loops)
            {
                var placed = new bool[nodes];
                foreach (int i in order) placed[i] = true;
                for (int i = 0; i < nodes; i++) if (!placed[i]) order.Add(i);
            }

            var mixHeat = new double[nodes];
            var mixMass = new double[nodes];
            for (int pass = 0; pass < (loops ? 3 : 1); pass++)
            {
                Array.Clear(mixHeat, 0, nodes);
                Array.Clear(mixMass, 0, nodes);
                foreach (int node in order)
                {
                    if (g.Nodes[node].Kind != NodeKind.FixedPressure && mixMass[node] > 0) res.NodeT[node] = mixHeat[node] / mixMass[node];
                    foreach (int k in outLinks[node])
                    {
                        int to = Direction(g, r, k).to;
                        double m = Math.Abs(r.MassFlow[k]), tin = res.NodeT[node];
                        NetworkLink l = g.Links[k];
                        double tout = l.Kind == LinkKind.Pipe && u[k] > 0 ? OutletTemperature(tin, ambientK[k], u[k], outerDiameter[k], l.Length, m, cp) : tin;
                        res.LinkInT[k] = tin;
                        res.LinkOutT[k] = tout;
                        res.HeatToAmbient[k] = m * cp * (tin - tout);
                        mixHeat[to] += m * tout;
                        mixMass[to] += m;
                    }
                }
            }
            for (int k = 0; k < links; k++)
                if (res.LinkInT[k] == 0) res.LinkInT[k] = res.LinkOutT[k] = res.NodeT[g.Links[k].From];
            return res;
        }

        static (int from, int to) Direction(NetworkGraph g, SolveResult r, int k) =>
            r.Flow[k] >= 0 ? (g.Links[k].From, g.Links[k].To) : (g.Links[k].To, g.Links[k].From);
    }
}
