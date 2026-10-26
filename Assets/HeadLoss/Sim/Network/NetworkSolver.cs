using System;
using System.Collections.Generic;
using HeadLoss.Sim.Fluids;
using HeadLoss.Sim.Hydraulics;

namespace HeadLoss.Sim.Network
{
    /// <summary>
    /// Steady-state network solver: the global gradient algorithm (Todini &amp; Pilati 1988, as in
    /// EPANET 2, Appendix D), Newton-Raphson on nodal pressures (<c>05-simulation.md</c> §1.3).
    /// Pure and deterministic: same graph and inputs give bit-identical results.
    /// </summary>
    public sealed class NetworkSolver
    {
        readonly SolverSettings _s;
        readonly ISparseSolver _linear;

        // Structure cache: reused while the topology and the set of active links stay the same
        // (drift re-solves, equipment setting changes), so the ordering/symbolic analysis runs once.
        long _cacheKey;
        SparseMatrix? _matrix;
        int[] _unknown = Array.Empty<int>();
        int[] _posII = Array.Empty<int>(), _posJJ = Array.Empty<int>(), _posIJ = Array.Empty<int>(), _posJI = Array.Empty<int>();

        public NetworkSolver(SolverSettings settings, ISparseSolver? linearSolver = null)
        {
            _s = settings;
            _linear = linearSolver ?? new SparseCholeskySolver();
        }

        /// <summary>Solves the graph.</summary>
        /// <param name="fluid">Fluid properties (density updated per iteration for compressible fluids).</param>
        /// <param name="initialFlows">Warm start (previous solution for the same topology), m³/s per link; null = 1 m/s everywhere.</param>
        public SolveResult Solve(NetworkGraph graph, IFluidProperties fluid, double[]? initialFlows = null)
        {
            int nodeCount = graph.Nodes.Count;
            int linkCount = graph.Links.Count;
            var res = new SolveResult
            {
                TopologyKey = graph.TopologyKey(),
                NodePressure = new double[nodeCount],
                NodeActive = new bool[nodeCount],
                Flow = new double[linkCount],
                Velocity = new double[linkCount],
                Reynolds = new double[linkCount],
                FrictionFactor = new double[linkCount],
                DpFriction = new double[linkCount],
                DpFittings = new double[linkCount],
                DpElevation = new double[linkCount],
                DpDevice = new double[linkCount],
                Density = new double[linkCount],
                MassFlow = new double[linkCount],
                Efficiency = new double[linkCount],
                Power = new double[linkCount],
                MachWarning = new bool[linkCount],
                LinkActive = new bool[linkCount],
            };

            MarkActive(graph, res);

            // Unknown-pressure node numbering and matrix structure (cached per topology + active set).
            long key = res.TopologyKey;
            unchecked
            {
                for (int k = 0; k < linkCount; k++) key = (key ^ (res.LinkActive[k] ? k + 1 : -(k + 1))) * 1099511628211L;
            }
            if (_matrix == null || key != _cacheKey || _unknown.Length != nodeCount) BuildStructure(graph, res, key);
            int[] unknown = _unknown;
            SparseMatrix a = _matrix!;
            int n = a.N;
            int[] posII = _posII, posJJ = _posJJ, posIJ = _posIJ, posJI = _posJI;
            for (int i = 0; i < nodeCount; i++)
            {
                NetworkNode node = graph.Nodes[i];
                res.NodePressure[i] = node.Kind == NodeKind.FixedPressure ? node.FixedPressure : 0;
            }

            double[] q = res.Flow;
            double[] rho = res.Density;
            // Viscosity depends on temperature (pressure effect negligible in the game's range): once per solve.
            var mu = new double[linkCount];
            bool warm = initialFlows != null && initialFlows.Length == linkCount;
            for (int k = 0; k < linkCount; k++)
            {
                NetworkLink l = graph.Links[k];
                rho[k] = fluid.Density(_s.AtmosphericPressure, l.Temperature);
                mu[k] = fluid.Viscosity(_s.AtmosphericPressure, l.Temperature);
                if (!res.LinkActive[k]) continue;
                q[k] = warm ? initialFlows![k] : _s.InitialVelocity * l.Area;
            }

            var dp = new double[linkCount];
            var gk = new double[linkCount];
            var rhs = new double[n];
            var p = new double[n];
            double gMin = 0;
            double relax = 1.0;
            double prevResidual = double.PositiveInfinity;
            int growing = 0;
            res.Residual = double.PositiveInfinity;

            for (int iter = 1; iter <= _s.MaxIterations; iter++)
            {
                res.Iterations = iter;

                // 1. Link heads and derivatives.
                double scale = 0;
                for (int k = 0; k < linkCount; k++)
                {
                    if (!res.LinkActive[k]) continue;
                    dp[k] = Evaluate(graph, k, q[k], rho[k], mu[k], out gk[k], out _);
                    if (iter == 1) scale = Math.Max(scale, Math.Abs(gk[k]));
                }
                if (iter == 1) gMin = _s.GMinFactor * (scale > 0 ? scale : 1.0);

                // 2–3. Assemble A·P = F.
                a.Clear();
                Array.Clear(rhs, 0, n);
                for (int k = 0; k < linkCount; k++)
                {
                    if (!res.LinkActive[k]) continue;
                    NetworkLink l = graph.Links[k];
                    double g = Math.Max(gk[k], gMin);
                    double pk = 1.0 / g;
                    double yk = pk * dp[k];
                    int ui = unknown[l.From], uj = unknown[l.To];
                    if (posII[k] >= 0) a.Values[posII[k]] += pk;
                    if (posJJ[k] >= 0) a.Values[posJJ[k]] += pk;
                    if (posIJ[k] >= 0) { a.Values[posIJ[k]] -= pk; a.Values[posJI[k]] -= pk; }
                    if (ui >= 0)
                    {
                        rhs[ui] -= q[k] - yk;
                        if (uj < 0) rhs[ui] += pk * res.NodePressure[l.To];
                    }
                    if (uj >= 0)
                    {
                        rhs[uj] += q[k] - yk;
                        if (ui < 0) rhs[uj] += pk * res.NodePressure[l.From];
                    }
                }
                for (int i = 0; i < nodeCount; i++)
                {
                    if (unknown[i] >= 0 && graph.Nodes[i].Kind == NodeKind.Demand) rhs[unknown[i]] -= graph.Nodes[i].Demand;
                }

                // 4. Solve for unknown pressures (warm-started from the last iterate).
                for (int i = 0; i < nodeCount; i++) if (unknown[i] >= 0) p[unknown[i]] = res.NodePressure[i];
                if (n > 0) res.LinearIterations += _linear.Solve(a, rhs, p);
                for (int i = 0; i < nodeCount; i++) if (unknown[i] >= 0) res.NodePressure[i] = p[unknown[i]];

                // 5. Update flows.
                double sumDq = 0, sumQ = 0;
                for (int k = 0; k < linkCount; k++)
                {
                    if (!res.LinkActive[k]) continue;
                    NetworkLink l = graph.Links[k];
                    double g = Math.Max(gk[k], gMin);
                    double pk = 1.0 / g;
                    double dq = -(pk * dp[k] - pk * (res.NodePressure[l.From] - res.NodePressure[l.To]));
                    q[k] += relax * dq;
                    sumDq += Math.Abs(relax * dq);
                    sumQ += Math.Abs(q[k]);
                }

                // Density follows the ideal gas law at each link's mean pressure.
                if (fluid.IsCompressible)
                {
                    for (int k = 0; k < linkCount; k++)
                        if (res.LinkActive[k]) rho[k] = fluid.Density(MeanAbsPressure(graph, res, k), graph.Links[k].Temperature);
                }

                // 6. Convergence.
                double residual = sumQ > 0 ? sumDq / sumQ : (sumDq > 0 ? double.PositiveInfinity : 0);
                res.Residual = residual;
                res.MaxImbalance = MaxImbalance(graph, res, unknown);
                if (residual < _s.FlowTolerance && res.MaxImbalance < _s.MassImbalanceTolerance)
                {
                    res.Converged = true;
                    break;
                }
                growing = residual > prevResidual ? growing + 1 : 0;
                if (growing >= _s.DivergenceIterations)
                {
                    relax = _s.Relaxation;
                    growing = 0;
                }
                prevResidual = residual;
            }

            res.Unstable = !res.Converged;
            FillResults(graph, fluid, res, mu);
            return res;
        }

        void BuildStructure(NetworkGraph graph, SolveResult res, long key)
        {
            int nodeCount = graph.Nodes.Count;
            int linkCount = graph.Links.Count;
            _cacheKey = key;
            _unknown = new int[nodeCount];
            int n = 0;
            for (int i = 0; i < nodeCount; i++)
                _unknown[i] = res.NodeActive[i] && graph.Nodes[i].Kind != NodeKind.FixedPressure ? n++ : -1;
            var pairs = new List<(int, int)>();
            for (int k = 0; k < linkCount; k++)
            {
                if (!res.LinkActive[k]) continue;
                int ui = _unknown[graph.Links[k].From], uj = _unknown[graph.Links[k].To];
                if (ui >= 0 && uj >= 0 && ui != uj) pairs.Add((ui, uj));
            }
            _matrix = new SparseMatrix(n, pairs);
            _posII = new int[linkCount];
            _posJJ = new int[linkCount];
            _posIJ = new int[linkCount];
            _posJI = new int[linkCount];
            for (int k = 0; k < linkCount; k++)
            {
                _posII[k] = _posJJ[k] = _posIJ[k] = _posJI[k] = -1;
                if (!res.LinkActive[k]) continue;
                int ui = _unknown[graph.Links[k].From], uj = _unknown[graph.Links[k].To];
                if (ui >= 0) _posII[k] = _matrix.Diagonal[ui];
                if (uj >= 0) _posJJ[k] = _matrix.Diagonal[uj];
                if (ui >= 0 && uj >= 0 && ui != uj)
                {
                    _posIJ[k] = _matrix.IndexOf(ui, uj);
                    _posJI[k] = _matrix.IndexOf(uj, ui);
                }
            }
        }

        /// <summary>Marks nodes reachable from a fixed-pressure node through open links, and the links between them.</summary>
        static void MarkActive(NetworkGraph graph, SolveResult res)
        {
            int nodeCount = graph.Nodes.Count;
            var adjacencyStart = new int[nodeCount + 1];
            foreach (NetworkLink l in graph.Links)
            {
                if (!l.IsOpen) continue;
                adjacencyStart[l.From + 1]++;
                adjacencyStart[l.To + 1]++;
            }
            for (int i = 0; i < nodeCount; i++) adjacencyStart[i + 1] += adjacencyStart[i];
            var adjacency = new int[adjacencyStart[nodeCount]];
            var fill = new int[nodeCount];
            foreach (NetworkLink l in graph.Links)
            {
                if (!l.IsOpen) continue;
                adjacency[adjacencyStart[l.From] + fill[l.From]++] = l.To;
                adjacency[adjacencyStart[l.To] + fill[l.To]++] = l.From;
            }
            var stack = new Stack<int>();
            for (int i = 0; i < nodeCount; i++)
            {
                if (graph.Nodes[i].Kind != NodeKind.FixedPressure || res.NodeActive[i]) continue;
                res.NodeActive[i] = true;
                stack.Push(i);
                while (stack.Count > 0)
                {
                    int u = stack.Pop();
                    for (int e = adjacencyStart[u]; e < adjacencyStart[u + 1]; e++)
                    {
                        int v = adjacency[e];
                        if (res.NodeActive[v]) continue;
                        res.NodeActive[v] = true;
                        stack.Push(v);
                    }
                }
            }
            for (int k = 0; k < graph.Links.Count; k++)
            {
                NetworkLink l = graph.Links[k];
                res.LinkActive[k] = l.IsOpen && res.NodeActive[l.From] && res.NodeActive[l.To] && l.From != l.To;
            }
        }

        double MeanAbsPressure(NetworkGraph graph, SolveResult res, int k)
        {
            NetworkLink l = graph.Links[k];
            return 0.5 * (res.NodePressure[l.From] + res.NodePressure[l.To]) + _s.AtmosphericPressure;
        }

        static double MaxImbalance(NetworkGraph graph, SolveResult res, int[] unknown)
        {
            int nodeCount = graph.Nodes.Count;
            var balance = new double[nodeCount];
            for (int k = 0; k < graph.Links.Count; k++)
            {
                if (!res.LinkActive[k]) continue;
                NetworkLink l = graph.Links[k];
                balance[l.From] -= res.Flow[k];
                balance[l.To] += res.Flow[k];
            }
            double max = 0;
            for (int i = 0; i < nodeCount; i++)
            {
                if (unknown[i] < 0) continue;
                double b = balance[i] - (graph.Nodes[i].Kind == NodeKind.Demand ? graph.Nodes[i].Demand : 0);
                max = Math.Max(max, Math.Abs(b));
            }
            return max;
        }

        /// <summary>
        /// Head function of a link: returns p_from − p_to that the link requires at flow <paramref name="flow"/>
        /// (resistance loss − device rise + elevation term) and its derivative.
        /// </summary>
        double Evaluate(NetworkGraph graph, int k, double flow, double density, double viscosity, out double derivative, out PipeLossResult loss)
        {
            NetworkLink l = graph.Links[k];
            double elevation = density * _s.Gravity * (graph.Nodes[l.To].Elevation - graph.Nodes[l.From].Elevation);
            loss = default;
            switch (l.Kind)
            {
                case LinkKind.Fan:
                {
                    double rise = l.Fan!.PressureRise(flow, l.Speed, density, l.Area, out double d);
                    derivative = -d;
                    return -rise + elevation;
                }
                case LinkKind.Pipe:
                {
                    double q0 = _s.ZeroFlowThreshold;
                    if (Math.Abs(flow) < q0)
                    {
                        PipeLossResult at = PipeLoss.Evaluate(q0, l.Length, l.Diameter, l.Area, l.Roughness, l.SumK, density, viscosity, _s.Friction);
                        derivative = at.TotalDp / q0;
                        loss = PipeLoss.Evaluate(flow, l.Length, l.Diameter, l.Area, l.Roughness, l.SumK, density, viscosity, _s.Friction);
                        return derivative * flow + elevation;
                    }
                    loss = PipeLoss.Evaluate(flow, l.Length, l.Diameter, l.Area, l.Roughness, l.SumK, density, viscosity, _s.Friction);
                    derivative = loss.Derivative;
                    return loss.TotalDp + elevation;
                }
                default:
                {
                    // Vent grille or valve: pure K·ρ/(2A²)·Q|Q|.
                    double kEff = l.Kind == LinkKind.Valve ? l.SumK / (l.Opening * l.Opening) : l.SumK;
                    double c = kEff * density / (2.0 * l.Area * l.Area);
                    double absQ = Math.Max(Math.Abs(flow), _s.ZeroFlowThreshold);
                    derivative = 2.0 * c * absQ;
                    double resistance = Math.Abs(flow) < _s.ZeroFlowThreshold ? c * _s.ZeroFlowThreshold * flow : c * flow * Math.Abs(flow);
                    loss.FittingsDp = resistance;
                    loss.Velocity = flow / l.Area;
                    loss.Reynolds = density * Math.Abs(flow) * l.Diameter / (l.Area * viscosity);
                    return resistance + elevation;
                }
            }
        }

        void FillResults(NetworkGraph graph, IFluidProperties fluid, SolveResult res, double[] viscosity)
        {
            for (int k = 0; k < graph.Links.Count; k++)
            {
                NetworkLink l = graph.Links[k];
                if (!res.LinkActive[k])
                {
                    res.Flow[k] = 0;
                    continue;
                }
                double q = res.Flow[k];
                double rho = res.Density[k];
                double mu = viscosity[k];
                Evaluate(graph, k, q, rho, mu, out _, out PipeLossResult loss);
                res.DpElevation[k] = rho * _s.Gravity * (graph.Nodes[l.To].Elevation - graph.Nodes[l.From].Elevation);
                res.MassFlow[k] = rho * q;
                res.Velocity[k] = q / l.Area;
                if (l.Kind == LinkKind.Fan)
                {
                    double rise = l.Fan!.PressureRise(q, l.Speed, rho, l.Area, out _);
                    res.DpDevice[k] = rise;
                    res.Efficiency[k] = l.Fan.Efficiency(q, l.Speed);
                    res.Power[k] = l.Fan.Power(q, rise, l.Speed);
                    res.Reynolds[k] = rho * Math.Abs(q) * l.Diameter / (l.Area * mu);
                }
                else
                {
                    res.Reynolds[k] = loss.Reynolds;
                    res.FrictionFactor[k] = loss.FrictionFactor;
                    res.DpFriction[k] = loss.FrictionDp;
                    res.DpFittings[k] = loss.FittingsDp;
                }
                double c = fluid.SpeedOfSound(l.Temperature);
                res.MachWarning[k] = Math.Abs(res.Velocity[k]) > 0.3 * c;
            }
        }
    }
}
