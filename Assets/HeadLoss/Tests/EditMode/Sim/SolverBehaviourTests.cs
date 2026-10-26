using System;
using System.Collections.Generic;
using HeadLoss.Core;
using HeadLoss.Sim.Devices;
using HeadLoss.Sim.Fluids;
using HeadLoss.Sim.Hydraulics;
using HeadLoss.Sim.Network;
using NUnit.Framework;

namespace HeadLoss.Sim.Tests
{
    /// <summary>Solver behaviour tests from <c>15-testing.md</c>.</summary>
    public class SolverBehaviourTests
    {
        static readonly ConstantFluid Air = new ConstantFluid(1.204, 1.813e-5);
        static readonly ConstantFluid Water = new ConstantFluid(998.2, 1.002e-3);

        /// <summary>Random duct network: fan from a fixed intake, random tree of ducts plus a few loops, vents to fixed rooms.</summary>
        static NetworkGraph RandomNetwork(ulong seed, int junctions)
        {
            var rng = new Rng(seed);
            var g = new NetworkGraph();
            int intake = g.AddFixed(0);
            var nodes = new List<int> { g.AddJunction() };
            g.AddFan(intake, nodes[0], FanCurve.Quadratic(rng.NextRange(50, 400), Units.M3hToM3s(rng.NextRange(200, 1500)), 6, 0.5), 0.2);
            double[] sizes = { 0.08, 0.1, 0.125, 0.16, 0.2, 0.25 };
            for (int i = 1; i < junctions; i++)
            {
                int parent = nodes[rng.NextInt(0, nodes.Count)];
                int n = g.AddJunction();
                nodes.Add(n);
                g.AddPipe(parent, n, rng.NextRange(0.5, 8), sizes[rng.NextInt(0, sizes.Length)], 0.15e-3, rng.NextRange(0, 3));
            }
            int loops = rng.NextInt(0, 4);
            for (int i = 0; i < loops; i++)
            {
                int a = nodes[rng.NextInt(0, nodes.Count)], b = nodes[rng.NextInt(0, nodes.Count)];
                if (a != b) g.AddPipe(a, b, rng.NextRange(1, 6), sizes[rng.NextInt(0, sizes.Length)], 0.15e-3, rng.NextRange(0, 2));
            }
            int vents = rng.NextInt(1, Math.Max(2, junctions / 2));
            for (int i = 0; i < vents; i++)
            {
                int room = g.AddFixed(0);
                g.AddVent(nodes[rng.NextInt(0, nodes.Count)], room, 2.0, 0.04);
            }
            return g;
        }

        static double Imbalance(NetworkGraph g, SolveResult r)
        {
            var b = new double[g.Nodes.Count];
            for (int k = 0; k < g.Links.Count; k++)
            {
                b[g.Links[k].From] -= r.Flow[k];
                b[g.Links[k].To] += r.Flow[k];
            }
            double max = 0;
            for (int i = 0; i < g.Nodes.Count; i++)
            {
                if (g.Nodes[i].Kind == NodeKind.FixedPressure || !r.NodeActive[i]) continue;
                max = Math.Max(max, Math.Abs(b[i] - (g.Nodes[i].Kind == NodeKind.Demand ? g.Nodes[i].Demand : 0)));
            }
            return max;
        }

        [Test]
        public void Solver_RandomNetworks_ConvergeAndConserveMass()
        {
            var solver = new NetworkSolver(new SolverSettings());
            for (ulong seed = 1; seed <= 200; seed++)
            {
                NetworkGraph g = RandomNetwork(seed, 3 + (int)(seed % 25));
                SolveResult r = solver.Solve(g, Air);
                Assert.IsTrue(r.Converged, $"seed {seed}: not converged (residual {r.Residual:E2}, {r.Iterations} it)");
                Assert.LessOrEqual(r.Iterations, 60, $"seed {seed}");
                Assert.Less(Imbalance(g, r), 1e-9, $"seed {seed}: mass imbalance");
            }
        }

        [Test]
        public void Solver_SameInput_BitIdenticalOutput()
        {
            NetworkGraph g1 = RandomNetwork(777, 40);
            NetworkGraph g2 = RandomNetwork(777, 40);
            SolveResult a = new NetworkSolver(new SolverSettings()).Solve(g1, Air);
            SolveResult b = new NetworkSolver(new SolverSettings()).Solve(g2, Air);
            Assert.AreEqual(a.Iterations, b.Iterations);
            for (int k = 0; k < a.Flow.Length; k++)
                Assert.AreEqual(BitConverter.DoubleToInt64Bits(a.Flow[k]), BitConverter.DoubleToInt64Bits(b.Flow[k]), $"link {k}");
            for (int i = 0; i < a.NodePressure.Length; i++)
                Assert.AreEqual(BitConverter.DoubleToInt64Bits(a.NodePressure[i]), BitConverter.DoubleToInt64Bits(b.NodePressure[i]), $"node {i}");
        }

        [Test]
        public void Solver_DenseReference_PcgAndSparseCholesky_Agree()
        {
            for (ulong seed = 10; seed < 30; seed++)
            {
                NetworkGraph g = RandomNetwork(seed, 30);
                SolveResult pcg = new NetworkSolver(new SolverSettings(), new PcgSolver(1e-13, 2000)).Solve(g, Air);
                SolveResult dense = new NetworkSolver(new SolverSettings(), new DenseLdlSolver()).Solve(g, Air);
                SolveResult chol = new NetworkSolver(new SolverSettings(), new SparseCholeskySolver()).Solve(g, Air);
                Assert.IsTrue(pcg.Converged && dense.Converged && chol.Converged, $"seed {seed}");
                for (int k = 0; k < g.Links.Count; k++)
                {
                    Assert.AreEqual(dense.Flow[k], pcg.Flow[k], 1e-9 + Math.Abs(dense.Flow[k]) * 1e-7, $"seed {seed} link {k} (PCG)");
                    Assert.AreEqual(dense.Flow[k], chol.Flow[k], 1e-12 + Math.Abs(dense.Flow[k]) * 1e-9, $"seed {seed} link {k} (sparse Cholesky)");
                }
            }
        }

        [Test]
        public void Solver_WarmStart_ConvergesFasterToSameAnswer()
        {
            NetworkGraph g = RandomNetwork(5, 30);
            var solver = new NetworkSolver(new SolverSettings());
            SolveResult cold = solver.Solve(g, Air);
            SolveResult warm = solver.Solve(g, Air, cold.Flow);
            Assert.LessOrEqual(warm.Iterations, 2);
            for (int k = 0; k < g.Links.Count; k++) Assert.AreEqual(cold.Flow[k], warm.Flow[k], 1e-9);
        }

        [Test]
        public void Solver_ClosedValve_RemovesBranch()
        {
            NetworkGraph Build(bool withSecondBranch, bool closed)
            {
                var g = new NetworkGraph();
                int intake = g.AddFixed(0);
                int a = g.AddJunction();
                g.AddFan(intake, a, FanCurve.Quadratic(100, Units.M3hToM3s(300)), 0.125);
                int b = g.AddJunction();
                g.AddPipe(a, b, 4, 0.125, 0.15e-3, 1.2);
                g.AddVent(b, g.AddFixed(0), 2, 0.04);
                if (withSecondBranch)
                {
                    int c = g.AddJunction();
                    int v = g.AddValve(a, c, 0.3, 0.125);
                    g.Links[v].Closed = closed;
                    int d = g.AddJunction();
                    g.AddPipe(c, d, 3, 0.125, 0.15e-3, 1.2);
                    g.AddVent(d, g.AddFixed(0), 2, 0.04);
                }
                return g;
            }
            var solver = new NetworkSolver(new SolverSettings());
            NetworkGraph single = Build(false, false);
            NetworkGraph closedG = Build(true, true);
            NetworkGraph openG = Build(true, false);
            SolveResult rs = solver.Solve(single, Air);
            SolveResult rc = solver.Solve(closedG, Air);
            SolveResult ro = solver.Solve(openG, Air);
            Assert.IsFalse(rc.LinkActive[3]);
            Assert.AreEqual(0, rc.Flow[3]);
            Assert.AreEqual(0, rc.Flow[4]);
            Assert.AreEqual(rs.Flow[1], rc.Flow[1], 1e-12, "closed branch = branch absent");
            Assert.Greater(ro.Flow[0], rc.Flow[0], "opening the branch raises fan flow");
        }

        [Test]
        public void Solver_FanOff_NoFlow()
        {
            var g = new NetworkGraph();
            int intake = g.AddFixed(0);
            int a = g.AddJunction();
            g.AddFan(intake, a, FanCurve.Quadratic(100, Units.M3hToM3s(300)), 0.125, 0);
            g.AddVent(a, g.AddFixed(0), 2, 0.04);
            SolveResult r = new NetworkSolver(new SolverSettings()).Solve(g, Air);
            Assert.IsTrue(r.Converged);
            Assert.AreEqual(0, r.Flow[0]);
            Assert.AreEqual(0, r.Flow[1], 1e-12);
        }

        [Test]
        public void Solver_DisconnectedDuct_IsInactiveAndHarmless()
        {
            var g = new NetworkGraph();
            int intake = g.AddFixed(0);
            int a = g.AddJunction();
            g.AddFan(intake, a, FanCurve.Quadratic(100, Units.M3hToM3s(300)), 0.125);
            g.AddVent(a, g.AddFixed(0), 2, 0.04);
            int x = g.AddJunction(), y = g.AddJunction();
            g.AddPipe(x, y, 3, 0.1, 0.15e-3);
            SolveResult r = new NetworkSolver(new SolverSettings()).Solve(g, Air);
            Assert.IsTrue(r.Converged);
            Assert.IsFalse(r.NodeActive[x]);
            Assert.AreEqual(0, r.Flow[2]);
            Assert.Greater(r.Flow[0], 0);
        }

        [Test]
        public void Solver_DeadEndBranch_CarriesNoFlow()
        {
            var g = new NetworkGraph();
            int intake = g.AddFixed(0);
            int a = g.AddJunction();
            g.AddFan(intake, a, FanCurve.Quadratic(100, Units.M3hToM3s(300)), 0.125);
            int b = g.AddJunction();
            g.AddPipe(a, b, 3, 0.125, 0.15e-3, 1.2);
            g.AddVent(b, g.AddFixed(0), 2, 0.04);
            int dead = g.AddJunction();
            g.AddPipe(a, dead, 2, 0.1, 0.15e-3);
            SolveResult r = new NetworkSolver(new SolverSettings()).Solve(g, Air);
            Assert.IsTrue(r.Converged);
            Assert.AreEqual(0, r.Flow[3], 1e-9);
            Assert.AreEqual(r.NodePressure[a], r.NodePressure[dead], 1e-6);
        }

        static double SolveTwoFans(bool series)
        {
            var g = new NetworkGraph();
            int intake = g.AddFixed(0);
            int a = g.AddJunction();
            int outNode = g.AddFixed(0);
            FanCurve fan = FanCurve.Quadratic(100, Units.M3hToM3s(300), 8);
            if (series)
            {
                int m = g.AddJunction();
                g.AddFan(intake, m, fan, 0.1);
                g.AddFan(m, a, fan, 0.1);
            }
            else
            {
                g.AddFan(intake, a, fan, 0.1);
                g.AddFan(intake, a, fan, 0.1);
            }
            g.AddVent(a, outNode, 60, 0.0314);  // pure quadratic system
            SolveResult r = new NetworkSolver(new SolverSettings()).Solve(g, Air);
            Assert.IsTrue(r.Converged);
            return r.Flow[g.Links.Count - 1];
        }

        [Test]
        public void Solver_SeriesFans_AddPressure()
        {
            // 2·Δp0·(1 − (Q/Qm)²) = R·Q²  ⇒  Q = sqrt(2Δp0 / (R + 2Δp0/Qm²))
            double r = 60 * 1.204 / (2 * 0.0314 * 0.0314);
            double dp0 = 100, qm = Units.M3hToM3s(300);
            double expected = Math.Sqrt(2 * dp0 / (r + 2 * dp0 / (qm * qm)));
            Assert.AreEqual(expected, SolveTwoFans(true), expected * 0.005);
        }

        [Test]
        public void Solver_ParallelFans_AddFlow()
        {
            // Δp0·(1 − (Q/2Qm)²) = R·Q²  ⇒  Q = sqrt(Δp0 / (R + Δp0/(4Qm²)))
            double r = 60 * 1.204 / (2 * 0.0314 * 0.0314);
            double dp0 = 100, qm = Units.M3hToM3s(300);
            double expected = Math.Sqrt(dp0 / (r + dp0 / (4 * qm * qm)));
            Assert.AreEqual(expected, SolveTwoFans(false), expected * 0.005);
        }

        [Test]
        public void Solver_ReverseFlowThroughFan_UsesReverseK()
        {
            // A strong fan pushes air backwards through a weak one in parallel.
            var g = new NetworkGraph();
            int intake = g.AddFixed(0);
            int a = g.AddJunction();
            g.AddFan(intake, a, FanCurve.Quadratic(300, Units.M3hToM3s(700)), 0.2);
            g.AddFan(intake, a, FanCurve.Quadratic(20, Units.M3hToM3s(100)), 0.1);
            g.AddVent(a, g.AddFixed(0), 30, 0.0314);
            SolveResult r = new NetworkSolver(new SolverSettings()).Solve(g, Air);
            Assert.IsTrue(r.Converged);
            Assert.Less(r.Flow[1], 0, "weak fan runs backwards");
            Assert.Less(Imbalance(g, r), 1e-9);
        }

        // --- Loop network vs an independent Hardy Cross solution ---

        struct TestPipe { public int From, To; public double L, D; }

        [Test]
        public void Solver_ThreeLoopNetwork_MatchesHardyCross()
        {
            // 2 × 4 grid of nodes (row r, column c → index r*4+c), 10 pipes, 3 loops.
            var pipes = new List<TestPipe>
            {
                new TestPipe { From = 0, To = 1, L = 300, D = 0.25 },  // 0 top row
                new TestPipe { From = 1, To = 2, L = 250, D = 0.2 },   // 1
                new TestPipe { From = 2, To = 3, L = 200, D = 0.15 },  // 2
                new TestPipe { From = 0, To = 4, L = 150, D = 0.25 },  // 3 vertical c=0
                new TestPipe { From = 4, To = 5, L = 300, D = 0.25 },  // 4 bottom row
                new TestPipe { From = 5, To = 6, L = 250, D = 0.2 },   // 5
                new TestPipe { From = 6, To = 7, L = 200, D = 0.2 },   // 6
                new TestPipe { From = 1, To = 5, L = 150, D = 0.15 },  // 7 vertical c=1
                new TestPipe { From = 2, To = 6, L = 150, D = 0.15 },  // 8 vertical c=2
                new TestPipe { From = 3, To = 7, L = 150, D = 0.1 },   // 9 vertical c=3
            };
            double[] demand = { -0.1, 0, 0.02, 0, 0, 0.03, 0, 0.05 };
            const double eps = 0.045e-3;

            // GGA: node 7 fixed (absorbs 0.05 m³/s), the rest demand nodes.
            var g = new NetworkGraph();
            for (int i = 0; i < 8; i++)
            {
                if (i == 7) g.AddFixed(0);
                else g.AddDemand(demand[i]);
            }
            foreach (TestPipe p in pipes) g.AddPipe(p.From, p.To, p.L, p.D, eps);
            SolveResult r = new NetworkSolver(new SolverSettings()).Solve(g, Water);
            Assert.IsTrue(r.Converged);

            // Hardy Cross, initial flows on the spanning tree (top row + c=0 vertical + bottom row).
            var q = new double[] { 0.02, 0.02, 0, 0.08, 0.08, 0.05, 0.05, 0, 0, 0 };
            var loops = new[]
            {
                new[] { (0, 1), (7, 1), (4, -1), (3, -1) },
                new[] { (1, 1), (8, 1), (5, -1), (7, -1) },
                new[] { (2, 1), (9, 1), (6, -1), (8, -1) },
            };
            var fm = new FrictionModel(2300, 4000);
            for (int it = 0; it < 500; it++)
            {
                double maxCorrection = 0;
                foreach (var loop in loops)
                {
                    double num = 0, den = 0;
                    foreach ((int k, int sign) in loop)
                    {
                        TestPipe p = pipes[k];
                        double area = Math.PI * p.D * p.D / 4;
                        double qk = q[k];
                        double h = 1e-9 + Math.Abs(qk) * 1e-6;
                        double dp = PipeLoss.Evaluate(qk, p.L, p.D, area, eps, 0, 998.2, 1.002e-3, fm).TotalDp;
                        double dpp = PipeLoss.Evaluate(qk + h, p.L, p.D, area, eps, 0, 998.2, 1.002e-3, fm).TotalDp;
                        double dpm = PipeLoss.Evaluate(qk - h, p.L, p.D, area, eps, 0, 998.2, 1.002e-3, fm).TotalDp;
                        num += sign * dp;
                        den += (dpp - dpm) / (2 * h);
                    }
                    double dq = -num / den;
                    foreach ((int k, int sign) in loop) q[k] += sign * dq;
                    maxCorrection = Math.Max(maxCorrection, Math.Abs(dq));
                }
                if (maxCorrection < 1e-13) break;
            }
            for (int k = 0; k < pipes.Count; k++)
                Assert.AreEqual(q[k], r.Flow[k], 1e-8 + Math.Abs(q[k]) * 1e-5, $"pipe {k}");
        }

        [Test]
        public void Solver_LaminarNetwork_Converges()
        {
            var g = new NetworkGraph();
            int a = g.AddDemand(-2e-6);
            int b = g.AddJunction();
            int c = g.AddFixed(0);
            g.AddPipe(a, b, 5, 0.01, 0.0015e-3);
            g.AddPipe(b, c, 5, 0.01, 0.0015e-3);
            SolveResult r = new NetworkSolver(new SolverSettings()).Solve(g, Water);
            Assert.IsTrue(r.Converged);
            Assert.Less(r.Reynolds[0], 2300);
            // Hagen-Poiseuille: Δp = 128 μ L Q / (π D⁴) per pipe.
            double expected = 2 * 128 * 1.002e-3 * 5 * 2e-6 / (Math.PI * Math.Pow(0.01, 4));
            Assert.AreEqual(expected, r.NodePressure[a], expected * 1e-6);
        }

        [Test]
        public void Solver_CompressibleAir_DensityFollowsPressure()
        {
            // An ideal-gas fluid: link densities differ slightly with local pressure.
            var g = new NetworkGraph();
            int intake = g.AddFixed(0);
            int a = g.AddJunction();
            g.AddFan(intake, a, FanCurve.Quadratic(900, Units.M3hToM3s(1800)), 0.315);
            int b = g.AddJunction();
            g.AddPipe(a, b, 20, 0.2, 0.15e-3, 2);
            g.AddVent(b, g.AddFixed(0), 2, 0.04);
            var fluid = new IdealGasTestFluid();
            SolveResult r = new NetworkSolver(new SolverSettings()).Solve(g, fluid);
            Assert.IsTrue(r.Converged);
            double pMean = 0.5 * (r.NodePressure[a] + r.NodePressure[b]) + 101325;
            Assert.AreEqual(IdealGas.Density(pMean, 0.028965, 293.15, 8.314462618), r.Density[1], 1e-6);
            Assert.Greater(r.Density[1], 1.2041);
        }

        sealed class IdealGasTestFluid : IFluidProperties
        {
            public double Density(double p, double t) => IdealGas.Density(p, 0.028965, t, 8.314462618);
            public double Viscosity(double p, double t) => 1.813e-5;
            public double SpecificHeat(double p, double t) => 1005;
            public double Conductivity(double p, double t) => 0.0257;
            public double SpeedOfSound(double t) => IdealGas.SpeedOfSound(1.4, 0.028965, t, 8.314462618);
            public bool IsCompressible => true;
        }
    }

    public class FanCurveTests
    {
        [Test]
        public void MonotoneCubic_ReproducesQuadraticOnInteriorSegments()
        {
            FanCurve fan = FanCurve.Quadratic(100, 1.0, 6);
            for (double q = 0.2; q <= 0.8; q += 0.01)
            {
                double p = fan.PressureRise(q, 1, 1.2, 0.01, out double d);
                Assert.AreEqual(100 * (1 - q * q), p, 1e-9, $"Q {q}");
                Assert.AreEqual(-200 * q, d, 1e-7, $"dQ {q}");
            }
        }

        [Test]
        public void MonotoneCubic_NoOvershoot()
        {
            var mc = new MonotoneCubic(new double[] { 0, 1, 2, 3, 4 }, new double[] { 0, 0, 1, 1, 1 });
            for (double x = 0; x <= 4; x += 0.01)
            {
                double y = mc.Evaluate(x);
                Assert.That(y, Is.InRange(-1e-12, 1 + 1e-12), $"x {x}");
            }
        }

        [Test]
        public void FanCurve_BeyondQmax_ExtendsLinearly()
        {
            FanCurve fan = FanCurve.Quadratic(100, 1.0, 6);
            double slope = (0 - 100 * (1 - 0.64)) / 0.2;
            Assert.AreEqual(slope * 0.1, fan.PressureRise(1.1, 1, 1.2, 0.01, out double d), 1e-9);
            Assert.AreEqual(slope, d, 1e-9);
        }

        [Test]
        public void FanCurve_SpeedScaling_AffinityLaws()
        {
            FanCurve fan = FanCurve.Quadratic(100, 1.0, 6);
            double full = fan.PressureRise(0.5, 1, 1.2, 0.01, out _);
            double half = fan.PressureRise(0.25, 0.5, 1.2, 0.01, out _);
            Assert.AreEqual(full * 0.25, half, 1e-9);
        }

        [Test]
        public void FanCurve_ReverseFlow_ShutoffPlusReverseLoss()
        {
            FanCurve fan = FanCurve.Quadratic(100, 1.0, 6, 0, 5);
            double area = 0.01;
            double q = -0.05;
            double expected = 100 + 5 * 1.2 / (2 * area * area) * q * q;
            Assert.AreEqual(expected, fan.PressureRise(q, 1, 1.2, area, out _), 1e-9);
        }
    }
}
