using System;
using HeadLoss.Core;
using HeadLoss.Sim.Fluids;
using HeadLoss.Sim.Network;
using NUnit.Framework;

namespace HeadLoss.Sim.Tests
{
    /// <summary>
    /// Control valves and demand nodes (<c>15-testing.md</c> §Solver behaviour: check valve blocks reverse flow,
    /// regulator caps downstream pressure, relief opens above set pressure).
    /// </summary>
    public class ControlValveTests
    {
        static readonly ConstantFluid Water = new ConstantFluid(998.2, 1.002e-3, 4180, 0.6);
        const double Eps = 0.045e-3;
        const double Bar = 1e5;

        static SolveResult Solve(NetworkGraph g)
        {
            SolveResult r = new NetworkSolver(new SolverSettings()).Solve(g, Water);
            Assert.IsTrue(r.Converged, $"not converged (residual {r.Residual}, {r.Iterations} iterations)");
            return r;
        }

        /// <summary>A graph with a leak link, the first place a NaN flow used to throw.</summary>
        static NetworkGraph LeakyLine()
        {
            var g = new NetworkGraph();
            int hi = g.AddFixed(2 * Bar), a = g.AddJunction(), lo = g.AddFixed(0);
            g.AddPipe(hi, a, 10, 0.05, Eps);
            g.AddPipe(a, lo, 10, 0.05, Eps);
            int room = g.AddFixed(0);
            g.AddLeak(a, room, 1e-5, 0.5);
            return g;
        }

        [Test]
        public void NonFiniteProperties_DoNotThrow_TheSolveReportsIt()
        {
            NetworkGraph g = LeakyLine();
            SolveResult r = new NetworkSolver(new SolverSettings()).Solve(g, new ConstantFluid(double.NaN, 1.002e-3, 4180, 0.6));
            Assert.IsFalse(r.Converged);
            Assert.IsTrue(r.NonFinite);
            foreach (double q in r.Flow) Assert.IsFalse(double.IsNaN(q) || double.IsInfinity(q));
            foreach (double p in r.NodePressure) Assert.IsFalse(double.IsNaN(p) || double.IsInfinity(p));
        }

        [Test]
        public void NonFiniteWarmStart_IsIgnored()
        {
            NetworkGraph g = LeakyLine();
            var warm = new double[g.Links.Count];
            for (int i = 0; i < warm.Length; i++) warm[i] = double.NaN;
            SolveResult r = new NetworkSolver(new SolverSettings()).Solve(g, Water, warm, null);
            Assert.IsTrue(r.Converged);
            Assert.IsFalse(r.NonFinite);
            Assert.Greater(r.Flow[0], 0);
        }

        [Test]
        public void CheckValve_ForwardFlow_Passes()
        {
            var g = new NetworkGraph();
            int hi = g.AddFixed(2 * Bar), lo = g.AddFixed(1 * Bar), a = g.AddJunction();
            g.AddPipe(hi, a, 10, 0.02, Eps);
            int check = g.AddCheck(a, lo, 2.0, 0.02);
            SolveResult r = Solve(g);
            Assert.Greater(r.Flow[check], 1e-4);
            Assert.AreEqual(LinkStatus.Open, r.Status[check]);
        }

        [Test]
        public void CheckValve_ReverseFlow_Blocked()
        {
            var g = new NetworkGraph();
            int hi = g.AddFixed(2 * Bar), lo = g.AddFixed(1 * Bar), a = g.AddJunction();
            g.AddPipe(hi, a, 10, 0.02, Eps);
            int check = g.AddCheck(lo, a, 2.0, 0.02);  // points from the low side: flow would run backwards
            int pipe = 0;
            SolveResult r = Solve(g);
            Assert.AreEqual(0, r.Flow[check], 1e-12);
            Assert.AreEqual(0, r.Flow[pipe], 1e-12);
            Assert.AreEqual(LinkStatus.Closed, r.Status[check]);
        }

        [Test]
        public void CheckValve_ParallelPath_OnlyForwardBranchCarriesFlow()
        {
            // Two sources feed one tap; the check valve stops the weaker source from receiving water.
            var g = new NetworkGraph();
            int strong = g.AddFixed(4 * Bar), weak = g.AddFixed(1 * Bar);
            int a = g.AddJunction(), b = g.AddJunction(), tap = g.AddDemand(0.2e-3);
            g.AddPipe(strong, a, 5, 0.02, Eps);
            g.AddPipe(a, tap, 5, 0.02, Eps);
            int w = g.AddPipe(weak, b, 5, 0.02, Eps);
            int check = g.AddCheck(b, a, 2.0, 0.02);
            SolveResult r = Solve(g);
            Assert.AreEqual(LinkStatus.Closed, r.Status[check]);
            Assert.AreEqual(0, r.Flow[w], 1e-12);
            Assert.Greater(r.NodePressure[tap], 3.5 * Bar);
        }

        [Test]
        public void Regulator_CapsDownstreamPressure()
        {
            var g = new NetworkGraph();
            int src = g.AddFixed(6 * Bar), a = g.AddJunction(), b = g.AddJunction(), tap = g.AddDemand(0.3e-3);
            g.AddPipe(src, a, 10, 0.022, Eps);
            int prv = g.AddRegulator(a, b, 2.0, 0.022, 3 * Bar);
            int down = g.AddPipe(b, tap, 10, 0.022, Eps);
            SolveResult r = Solve(g);
            Assert.AreEqual(LinkStatus.Active, r.Status[prv]);
            Assert.AreEqual(3 * Bar, r.NodePressure[b], 1.0);
            Assert.AreEqual(0.3e-3, r.Flow[prv], 1e-9);
            Assert.AreEqual(3 * Bar - r.DpFriction[down] - r.DpFittings[down], r.NodePressure[tap], 1.0);
            Assert.Less(r.MaxImbalance, 1e-9);
        }

        [Test]
        public void Regulator_UpstreamBelowSetpoint_OpensFully()
        {
            var g = new NetworkGraph();
            int src = g.AddFixed(2 * Bar), a = g.AddJunction(), b = g.AddJunction(), tap = g.AddDemand(0.3e-3);
            g.AddPipe(src, a, 10, 0.022, Eps);
            int prv = g.AddRegulator(a, b, 2.0, 0.022, 3 * Bar);
            g.AddPipe(b, tap, 10, 0.022, Eps);
            SolveResult r = Solve(g);
            Assert.AreEqual(LinkStatus.Open, r.Status[prv]);
            Assert.Less(r.NodePressure[b], 2 * Bar);
            double v = 0.3e-3 / (Math.PI * 0.022 * 0.022 / 4);
            Assert.AreEqual(2.0 * 998.2 * v * v / 2, r.NodePressure[a] - r.NodePressure[b], 0.01);
        }

        [Test]
        public void Regulator_ReversePressure_Closes()
        {
            var g = new NetworkGraph();
            int up = g.AddFixed(1 * Bar), down = g.AddFixed(4 * Bar), a = g.AddJunction(), b = g.AddJunction();
            g.AddPipe(up, a, 5, 0.022, Eps);
            int prv = g.AddRegulator(a, b, 2.0, 0.022, 3 * Bar);
            g.AddPipe(b, down, 5, 0.022, Eps);
            SolveResult r = Solve(g);
            Assert.AreEqual(LinkStatus.Closed, r.Status[prv]);
            Assert.AreEqual(0, r.Flow[prv], 1e-12);
        }

        [Test]
        public void Relief_AboveSetPressure_OpensAndHoldsNearSetting()
        {
            var g = new NetworkGraph();
            int src = g.AddFixed(8 * Bar), n = g.AddJunction(), drain = g.AddFixed(0);
            g.AddPipe(src, n, 20, 0.015, Eps);
            int relief = g.AddRelief(n, drain, 2.0, 0.015, 5 * Bar);
            SolveResult r = Solve(g);
            Assert.AreEqual(LinkStatus.Open, r.Status[relief]);
            Assert.Greater(r.Flow[relief], 0);
            Assert.Greater(r.NodePressure[n], 5 * Bar);
            Assert.Less(r.NodePressure[n], 8 * Bar);
            double area = Math.PI * 0.015 * 0.015 / 4, q = r.Flow[relief];
            Assert.AreEqual(5 * Bar + 2.0 * 998.2 / (2 * area * area) * q * q, r.NodePressure[n], 1.0);
        }

        [Test]
        public void Relief_BelowSetPressure_StaysClosed()
        {
            var g = new NetworkGraph();
            int src = g.AddFixed(3 * Bar), n = g.AddJunction(), tap = g.AddDemand(0.1e-3), drain = g.AddFixed(0);
            g.AddPipe(src, n, 10, 0.015, Eps);
            g.AddPipe(n, tap, 2, 0.015, Eps);
            int relief = g.AddRelief(n, drain, 2.0, 0.015, 5 * Bar);
            SolveResult r = Solve(g);
            Assert.AreEqual(LinkStatus.Closed, r.Status[relief]);
            Assert.AreEqual(0, r.Flow[relief], 1e-12);
        }

        [Test]
        public void DemandNode_DeliveryIsResidualPressure_WithElevation()
        {
            // City at 4 bar, a tap one floor up (3 m) drawing 0.1 L/s through 12 m of 15 mm pipe.
            var g = new NetworkGraph();
            int city = g.AddFixed(4 * Bar, 0.3), riserTop = g.AddJunction(3.3), tap = g.AddDemand(0.1e-3, 3.3);
            int riser = g.AddPipe(city, riserTop, 3, 0.0136, 0.0015e-3);
            int run = g.AddPipe(riserTop, tap, 9, 0.0136, 0.0015e-3);
            SolveResult r = Solve(g);
            double friction = r.DpFriction[riser] + r.DpFriction[run];
            Assert.AreEqual(4 * Bar - 998.2 * 9.80665 * 3 - friction, r.NodePressure[tap], 1.0);
            Assert.AreEqual(0.1e-3, r.Flow[run], 1e-12);
        }
    }
}
