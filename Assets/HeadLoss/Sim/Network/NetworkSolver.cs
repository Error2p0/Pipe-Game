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
        /// <param name="initialStatus">Control-valve statuses of the warm start (same topology); null = check valves open, relief valves closed, regulators active.</param>
        public SolveResult Solve(NetworkGraph graph, IFluidProperties fluid, double[]? initialFlows = null, LinkStatus[]? initialStatus = null)
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
                Status = new LinkStatus[linkCount],
            };

            LinkStatus[] status = res.Status;
            bool warmStatus = initialStatus != null && initialStatus.Length == linkCount;
            int controlLinks = 0;
            for (int k = 0; k < linkCount; k++)
            {
                LinkKind kind = graph.Links[k].Kind;
                if (!NetworkGraph.IsControl(kind)) continue;
                controlLinks++;
                status[k] = warmStatus ? initialStatus![k] : kind == LinkKind.Relief ? LinkStatus.Closed : kind == LinkKind.Regulator ? LinkStatus.Active : LinkStatus.Open;
            }

            MarkActive(graph, res);
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
                q[k] = warm && IsFinite(initialFlows![k]) ? initialFlows[k] : InitialFlow(l);
            }

            for (int k = 0; k < linkCount; k++)
            {
                if (IsFinite(rho[k]) && IsFinite(mu[k])) continue;
                // A property that is not a number (a bad temperature upstream) has no answer to iterate towards.
                GiveUp(graph, res);
                return res;
            }

            EnsureStructure(graph, res);
            if (!HasDrivingForce(graph, res))
            {
                // Nothing pushes the fluid: the exact answer is zero flow and hydrostatic pressures.
                // (Newton only halves a Q|Q| flow per step on the way to zero, so iterating would never converge.)
                SolveStatic(graph, res, _unknown);
                FillResults(graph, fluid, res, mu);
                return res;
            }

            // Links meeting at each node (continuity of an active regulator's downstream node).
            int[] incidenceStart = new int[nodeCount + 1];
            foreach (NetworkLink l in graph.Links) { incidenceStart[l.From + 1]++; incidenceStart[l.To + 1]++; }
            for (int i = 0; i < nodeCount; i++) incidenceStart[i + 1] += incidenceStart[i];
            var incidence = new int[incidenceStart[nodeCount]];
            var fill = new int[nodeCount];
            for (int k = 0; k < linkCount; k++)
            {
                NetworkLink l = graph.Links[k];
                incidence[incidenceStart[l.From] + fill[l.From]++] = k;
                incidence[incidenceStart[l.To] + fill[l.To]++] = k;
            }

            var dp = new double[linkCount];
            var gk = new double[linkCount];
            double gMin = 0;
            double relax = 1.0;
            double prevResidual = double.PositiveInfinity;
            int growing = 0;
            int statusChangesLeft = 4 * controlLinks + 4;
            bool restarted = false;
            res.Residual = double.PositiveInfinity;

            for (int iter = 1; iter <= _s.MaxIterations; iter++)
            {
                res.Iterations = iter;
                int[] unknown = _unknown;
                SparseMatrix a = _matrix!;
                int n = a.N;
                if (_rhs.Length < n) { _rhs = new double[n]; _p = new double[n]; }
                double[] rhs = _rhs, p = _p;

                // 1. Link heads and derivatives (an active regulator has neither: its flow comes from continuity).
                double scale = 0, maxPk = 0;
                for (int k = 0; k < linkCount; k++)
                {
                    if (!res.LinkActive[k] || IsHeld(graph, status, k)) continue;
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
                    int ui = unknown[l.From], uj = unknown[l.To];
                    if (IsHeld(graph, status, k))
                    {
                        // Its current flow leaves From and enters To; To's own row is replaced below.
                        if (ui >= 0) rhs[ui] -= q[k];
                        if (uj >= 0) rhs[uj] += q[k];
                        continue;
                    }
                    double g = Math.Max(gk[k], gMin);
                    double pk = 1.0 / g;
                    maxPk = Math.Max(maxPk, pk);
                    double yk = pk * dp[k];
                    if (_posII[k] >= 0) a.Values[_posII[k]] += pk;
                    if (_posJJ[k] >= 0) a.Values[_posJJ[k]] += pk;
                    if (_posIJ[k] >= 0) { a.Values[_posIJ[k]] -= pk; a.Values[_posJI[k]] -= pk; }
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
                // An active regulator pins its downstream node at the setpoint (EPANET's large-number method).
                double big = 1e8 * (maxPk > 0 ? maxPk : 1.0);
                for (int k = 0; k < linkCount; k++)
                {
                    if (!res.LinkActive[k] || !IsHeld(graph, status, k)) continue;
                    int uj = unknown[graph.Links[k].To];
                    if (uj < 0) continue;
                    a.Values[a.Diagonal[uj]] += big;
                    rhs[uj] += big * graph.Links[k].Setting;
                }

                // 4. Solve for unknown pressures (warm-started from the last iterate).
                for (int i = 0; i < nodeCount; i++) if (unknown[i] >= 0) p[unknown[i]] = res.NodePressure[i];
                if (n > 0) res.LinearIterations += _linear.Solve(a, rhs, p);
                for (int i = 0; i < nodeCount; i++) if (unknown[i] >= 0) res.NodePressure[i] = p[unknown[i]];

                // 5. Update flows.
                double sumDq = 0, sumQ = 0;
                for (int k = 0; k < linkCount; k++)
                {
                    if (!res.LinkActive[k] || IsHeld(graph, status, k)) continue;
                    NetworkLink l = graph.Links[k];
                    double g = Math.Max(gk[k], gMin);
                    double pk = 1.0 / g;
                    double dq = -(pk * dp[k] - pk * (res.NodePressure[l.From] - res.NodePressure[l.To]));
                    q[k] += relax * dq;
                    sumDq += Math.Abs(relax * dq);
                    sumQ += Math.Abs(q[k]);
                }
                for (int k = 0; k < linkCount; k++)
                {
                    if (!res.LinkActive[k] || !IsHeld(graph, status, k)) continue;
                    int j = graph.Links[k].To;
                    double need = graph.Nodes[j].Kind == NodeKind.Demand ? graph.Nodes[j].Demand : 0;
                    for (int e = incidenceStart[j]; e < incidenceStart[j + 1]; e++)
                    {
                        int m = incidence[e];
                        if (m == k || !res.LinkActive[m]) continue;
                        need += graph.Links[m].From == j ? q[m] : -q[m];
                    }
                    sumDq += Math.Abs(need - q[k]);
                    q[k] = need;
                    sumQ += Math.Abs(need);
                }

                // A wild iterate can leave real numbers behind: start again once from the cold guess, more gently, then give up.
                if (!IsFinite(sumDq) || !IsFinite(sumQ))
                {
                    if (restarted)
                    {
                        GiveUp(graph, res);
                        return res;
                    }
                    restarted = true;
                    relax = _s.Relaxation;
                    prevResidual = double.PositiveInfinity;
                    growing = 0;
                    for (int i = 0; i < nodeCount; i++)
                    {
                        NetworkNode node = graph.Nodes[i];
                        res.NodePressure[i] = node.Kind == NodeKind.FixedPressure ? node.FixedPressure : 0;
                    }
                    for (int k = 0; k < linkCount; k++)
                    {
                        q[k] = res.LinkActive[k] ? InitialFlow(graph.Links[k]) : 0;
                        rho[k] = fluid.Density(_s.AtmosphericPressure, graph.Links[k].Temperature);
                    }
                    continue;
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
                bool settled = residual < _s.FlowTolerance || sumDq < _s.ZeroFlowThreshold;

                // 7. Control-valve statuses (check valves, regulators, relief valves), EPANET style.
                if (controlLinks > 0 && statusChangesLeft > 0 && (settled || residual < 1e-2 || iter % 4 == 0))
                {
                    int changes = UpdateStatuses(graph, res, rho, status);
                    if (changes > 0)
                    {
                        statusChangesLeft -= changes;
                        bool[] before = (bool[])res.LinkActive.Clone();
                        Array.Clear(res.NodeActive, 0, nodeCount);
                        MarkActive(graph, res);
                        for (int k = 0; k < linkCount; k++)
                        {
                            if (res.LinkActive[k] && !before[k]) q[k] = InitialFlow(graph.Links[k]);
                            if (!res.LinkActive[k]) q[k] = 0;
                        }
                        EnsureStructure(graph, res);
                        prevResidual = double.PositiveInfinity;
                        growing = 0;
                        continue;
                    }
                }

                if (settled && res.MaxImbalance < _s.MassImbalanceTolerance)
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

        double[] _rhs = Array.Empty<double>(), _p = Array.Empty<double>();

        static bool IsFinite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);

        /// <summary>No usable answer: nothing flows, pressures are the fixed ones, and <see cref="SolveResult.NonFinite"/> tells the caller.</summary>
        static void GiveUp(NetworkGraph graph, SolveResult res)
        {
            for (int i = 0; i < graph.Nodes.Count; i++)
            {
                NetworkNode node = graph.Nodes[i];
                res.NodePressure[i] = node.Kind == NodeKind.FixedPressure ? node.FixedPressure : 0;
            }
            for (int k = 0; k < graph.Links.Count; k++)
            {
                res.Flow[k] = 0;
                if (!IsFinite(res.Density[k])) res.Density[k] = 0;
            }
            res.Converged = false;
            res.Unstable = true;
            res.NonFinite = true;
            res.Residual = double.PositiveInfinity;
        }

        /// <summary>Initial guess for a link entering the solve: 1 m/s (control valves: a trickle forward).</summary>
        double InitialFlow(NetworkLink l) => l.Kind switch
        {
            LinkKind.Leak => l.LeakCoefficient * Math.Pow(10, l.LeakExponent),
            LinkKind.Check or LinkKind.Regulator or LinkKind.Relief => 0.01 * _s.InitialVelocity * l.Area,
            _ => _s.InitialVelocity * l.Area,
        };

        /// <summary>An active regulator: no head function, its flow follows continuity at its downstream node.</summary>
        static bool IsHeld(NetworkGraph graph, LinkStatus[] status, int k) =>
            graph.Links[k].Kind == LinkKind.Regulator && status[k] == LinkStatus.Active;

        /// <summary>Rebuilds the unknown numbering and matrix structure when the active set changed.</summary>
        void EnsureStructure(NetworkGraph graph, SolveResult res)
        {
            long key = res.TopologyKey;
            unchecked
            {
                for (int k = 0; k < graph.Links.Count; k++) key = (key ^ (res.LinkActive[k] ? k + 1 : -(k + 1))) * 1099511628211L;
            }
            if (_matrix == null || key != _cacheKey || _unknown.Length != graph.Nodes.Count) BuildStructure(graph, res, key);
        }

        /// <summary>
        /// Status checks between iterations (<c>05-simulation.md</c> §1.3 step 7, EPANET 2): check valves close on reverse
        /// flow and reopen when the head across them turns forward; regulators switch between active, open and closed;
        /// relief valves open above their set pressure and close when their flow reverses. Returns the number of changes.
        /// </summary>
        int UpdateStatuses(NetworkGraph graph, SolveResult res, double[] rho, LinkStatus[] status)
        {
            const double hTol = 1.0;  // Pa
            double qTol = 10 * _s.ZeroFlowThreshold;
            int changes = 0;
            for (int k = 0; k < graph.Links.Count; k++)
            {
                NetworkLink l = graph.Links[k];
                if (!NetworkGraph.IsControl(l.Kind)) continue;
                double pFrom = res.NodePressure[l.From], pTo = res.NodePressure[l.To];
                bool fromLive = res.NodeActive[l.From], toLive = res.NodeActive[l.To];
                double elevation = (rho[k] - graph.AmbientDensity) * _s.Gravity * (graph.Nodes[l.To].Elevation - graph.Nodes[l.From].Elevation);
                double flow = res.Flow[k];
                LinkStatus s = status[k];
                LinkStatus next = s;
                switch (l.Kind)
                {
                    case LinkKind.Check:
                        if (s == LinkStatus.Open && flow < -qTol) next = LinkStatus.Closed;
                        else if (s == LinkStatus.Closed && fromLive && (!toLive || pFrom - pTo - elevation > hTol)) next = LinkStatus.Open;
                        break;
                    case LinkKind.Relief:
                        if (s == LinkStatus.Open && flow < -qTol) next = LinkStatus.Closed;
                        else if (s == LinkStatus.Closed && fromLive && pFrom - pTo - elevation > l.Setting + hTol) next = LinkStatus.Open;
                        break;
                    case LinkKind.Regulator:
                    {
                        double set = l.Setting;
                        if (s == LinkStatus.Active)
                        {
                            if (flow < -qTol) next = LinkStatus.Closed;
                            else if (pFrom < set - hTol) next = LinkStatus.Open;
                        }
                        else if (s == LinkStatus.Open)
                        {
                            if (flow < -qTol) next = LinkStatus.Closed;
                            else if (pTo > set + hTol) next = LinkStatus.Active;
                        }
                        else if (fromLive)
                        {
                            if (pFrom > set + hTol && (!toLive || pTo < set - hTol)) next = LinkStatus.Active;
                            else if (pFrom < set - hTol && (!toLive || pFrom > pTo + hTol)) next = LinkStatus.Open;
                        }
                        break;
                    }
                }
                if (next == s) continue;
                status[k] = next;
                changes++;
            }
            return changes;
        }

        /// <summary>
        /// True when something can drive flow: a running fan, a non-zero demand, or active fixed-pressure nodes
        /// at different piezometric heads (p + ρ·g·z).
        /// </summary>
        bool HasDrivingForce(NetworkGraph graph, SolveResult res)
        {
            for (int k = 0; k < graph.Links.Count; k++)
                if (res.LinkActive[k] && graph.Links[k].Kind == LinkKind.Fan) return true;
            double rho = (res.Density.Length > 0 ? res.Density[0] : 0) - graph.AmbientDensity;
            bool first = true;
            double head = 0;
            for (int i = 0; i < graph.Nodes.Count; i++)
            {
                if (!res.NodeActive[i]) continue;
                NetworkNode n = graph.Nodes[i];
                if (n.Kind == NodeKind.Demand && n.Demand != 0) return true;
                if (n.Kind != NodeKind.FixedPressure) continue;
                double h = n.FixedPressure + rho * _s.Gravity * n.Elevation;
                if (first) { head = h; first = false; }
                else if (Math.Abs(h - head) > 1e-9 * (1 + Math.Abs(head))) return true;
            }
            return false;
        }

        /// <summary>Zero flow; every active node sits at the common piezometric head.</summary>
        void SolveStatic(NetworkGraph graph, SolveResult res, int[] unknown)
        {
            double rho = (res.Density.Length > 0 ? res.Density[0] : 0) - graph.AmbientDensity;
            double head = 0;
            for (int i = 0; i < graph.Nodes.Count; i++)
            {
                NetworkNode n = graph.Nodes[i];
                if (res.NodeActive[i] && n.Kind == NodeKind.FixedPressure) { head = n.FixedPressure + rho * _s.Gravity * n.Elevation; break; }
            }
            for (int i = 0; i < graph.Nodes.Count; i++)
                if (unknown[i] >= 0) res.NodePressure[i] = head - rho * _s.Gravity * graph.Nodes[i].Elevation;
            System.Array.Clear(res.Flow, 0, res.Flow.Length);
            res.Iterations = 0;
            res.Residual = 0;
            res.MaxImbalance = 0;
            res.Converged = true;
            res.Unstable = false;
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
            bool Open(int k) => graph.Links[k].IsOpen && (res.Status.Length == 0 || res.Status[k] != LinkStatus.Closed);
            for (int k = 0; k < graph.Links.Count; k++)
            {
                if (!Open(k)) continue;
                NetworkLink l = graph.Links[k];
                adjacencyStart[l.From + 1]++;
                adjacencyStart[l.To + 1]++;
            }
            for (int i = 0; i < nodeCount; i++) adjacencyStart[i + 1] += adjacencyStart[i];
            var adjacency = new int[adjacencyStart[nodeCount]];
            var fill = new int[nodeCount];
            for (int k = 0; k < graph.Links.Count; k++)
            {
                if (!Open(k)) continue;
                NetworkLink l = graph.Links[k];
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
                res.LinkActive[k] = Open(k) && res.NodeActive[l.From] && res.NodeActive[l.To] && l.From != l.To;
            }
        }

        double MeanAbsPressure(NetworkGraph graph, SolveResult res, int k)
        {
            NetworkLink l = graph.Links[k];
            // Floored: an iterate far from the answer can pass below vacuum, and a negative density would poison the next step.
            return Math.Max(0.01 * _s.AtmosphericPressure, 0.5 * (res.NodePressure[l.From] + res.NodePressure[l.To]) + _s.AtmosphericPressure);
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
            double elevation = (density - graph.AmbientDensity) * _s.Gravity * (graph.Nodes[l.To].Elevation - graph.Nodes[l.From].Elevation);
            loss = default;
            switch (l.Kind)
            {
                case LinkKind.Fan:
                {
                    double rise = l.Fan!.PressureRise(flow, l.Speed, density, l.Area, out double d);
                    derivative = -d;
                    if (l.Opening >= 1) return -rise + elevation;
                    // Slowing down or starting up: a loss on top of the curve (05 §2).
                    double c = l.SpinLoss > 0 ? l.SpinLoss : l.Fan.SpinResistance(l.Opening);
                    derivative += 2.0 * c * Math.Max(Math.Abs(flow), _s.ZeroFlowThreshold);
                    double extra = Math.Abs(flow) < _s.ZeroFlowThreshold ? c * _s.ZeroFlowThreshold * flow : c * flow * Math.Abs(flow);
                    loss.FittingsDp = extra;
                    return extra - rise + elevation;
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
                case LinkKind.Leak:
                {
                    // Power law Δp = (|Q| / C)^(1/n), linear below the flow at 1 Pa (Q = C) so Newton stays well posed.
                    double cl = l.LeakCoefficient, inv = 1.0 / l.LeakExponent, absQ = Math.Abs(flow);
                    double dp;
                    if (absQ < cl)
                    {
                        derivative = 1.0 / cl;
                        dp = flow / cl;
                    }
                    else
                    {
                        double mag = Math.Pow(absQ / cl, inv);
                        derivative = inv * mag / absQ;
                        dp = Math.Sign(flow) * mag;
                    }
                    loss.FittingsDp = dp;
                    return dp + elevation;
                }
                default:
                {
                    // Vent grille, valve, check valve or open regulator: pure K·ρ/(2A²)·Q|Q|. An open relief valve adds its
                    // set pressure (it only passes flow above it).
                    double kEff = l.SumK / (l.Opening * l.Opening);
                    double c = kEff * density / (2.0 * l.Area * l.Area);
                    double absQ = Math.Max(Math.Abs(flow), _s.ZeroFlowThreshold);
                    derivative = 2.0 * c * absQ;
                    double resistance = Math.Abs(flow) < _s.ZeroFlowThreshold ? c * _s.ZeroFlowThreshold * flow : c * flow * Math.Abs(flow);
                    loss.FittingsDp = resistance;
                    loss.Velocity = flow / l.Area;
                    loss.Reynolds = density * Math.Abs(flow) * l.Diameter / (l.Area * viscosity);
                    double offset = l.Kind == LinkKind.Relief ? l.Setting : 0;
                    return offset + resistance + elevation;
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
                res.DpElevation[k] = (rho - graph.AmbientDensity) * _s.Gravity * (graph.Nodes[l.To].Elevation - graph.Nodes[l.From].Elevation);
                res.MassFlow[k] = rho * q;
                res.Velocity[k] = q / l.Area;
                if (l.Kind == LinkKind.Fan)
                {
                    double rise = l.Fan!.PressureRise(q, l.Speed, rho, l.Area, out _);
                    res.DpDevice[k] = rise;
                    res.Efficiency[k] = l.Fan.Efficiency(q, l.Speed);
                    res.Power[k] = l.Fan.Power(q, rise, l.Speed);
                    res.Reynolds[k] = rho * Math.Abs(q) * l.Diameter / (l.Area * mu);
                    res.DpFittings[k] = loss.FittingsDp;
                }
                else
                {
                    res.Reynolds[k] = loss.Reynolds;
                    res.FrictionFactor[k] = loss.FrictionFactor;
                    res.DpFriction[k] = loss.FrictionDp;
                    res.DpFittings[k] = IsHeld(graph, res.Status, k)
                        ? res.NodePressure[l.From] - res.NodePressure[l.To] - res.DpElevation[k]
                        : loss.FittingsDp;
                }
                double c = fluid.SpeedOfSound(l.Temperature);
                res.MachWarning[k] = Math.Abs(res.Velocity[k]) > 0.3 * c;
            }
        }
    }
}
