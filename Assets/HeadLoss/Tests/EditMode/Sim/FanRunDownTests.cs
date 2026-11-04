using HeadLoss.Core;
using HeadLoss.Sim.Devices;
using HeadLoss.Sim.Fluids;
using HeadLoss.Sim.Network;
using NUnit.Framework;

namespace HeadLoss.Sim.Tests
{
    /// <summary>
    /// A device running down (<c>05-simulation.md</c> §2): speed s·f and an extra loss
    /// Δp₀/Q_max²·(1/f² − 1)·Q|Q|, so it closes smoothly into the closed valve a stopped device is.
    /// </summary>
    public class FanRunDownTests
    {
        static readonly FanCurve Curve = FanCurve.Quadratic(100, Units.M3hToM3s(300));

        static SolveResult Solve(double left, double drivePa = 0)
        {
            var g = new NetworkGraph();
            int inlet = g.AddFixed(drivePa);
            int mid = g.AddJunction();
            int outlet = g.AddFixed(0);
            int fan = g.AddFan(inlet, mid, Curve, 0.1, left);
            g.Links[fan].Opening = left;
            g.AddPipe(mid, outlet, 10, 0.1, 0.15e-3, 3.4);
            return new NetworkSolver(new SolverSettings()).Solve(g, new ConstantFluid(1.204, 1.813e-5));
        }

        [Test]
        public void FullyRunning_HasNoExtraLoss()
        {
            Assert.AreEqual(0, Curve.RunDownResistance(1), 1e-12);
            SolveResult r = Solve(1);
            Assert.AreEqual(0, r.DpFittings[0], 1e-12);
            Assert.AreEqual(132.26, Units.M3sToM3h(r.Flow[1]), 132.26 * 0.005, "the T5 operating point");
        }

        [Test]
        public void ExtraLoss_FollowsTheFormula_AndBalancesTheSystem()
        {
            const double f = 0.5;
            SolveResult r = Solve(f);
            Assert.IsTrue(r.Converged);
            double q = r.Flow[0];
            double expected = Curve.ShutoffPressure / (Curve.MaxFlow * Curve.MaxFlow) * (1 / (f * f) - 1) * q * q;
            Assert.AreEqual(expected, r.DpFittings[0], expected * 1e-9);
            double system = r.DpFriction[1] + r.DpFittings[1];
            Assert.AreEqual(r.DpDevice[0] - r.DpFittings[0], system, system * 1e-3, "device rise less the extra loss drives the duct");
        }

        [Test]
        public void FlowFallsSteadily_OnTheWayToStopped()
        {
            double before = Solve(1).Flow[1];
            double full = before;
            for (double f = 0.9; f > 0.01; f -= 0.02)
            {
                SolveResult r = Solve(f);
                Assert.IsTrue(r.Converged, $"f = {f}");
                Assert.Less(r.Flow[1], before, $"f = {f}");
                before = r.Flow[1];
            }
            Assert.Less(before, full * 0.01, "almost nothing left before it closes");
        }

        [Test]
        public void ResistanceSetByTheCompiler_ReplacesTheCurvesOwn()
        {
            var g = new NetworkGraph();
            int inlet = g.AddFixed(0);
            int mid = g.AddJunction();
            int outlet = g.AddFixed(0);
            int fan = g.AddFan(inlet, mid, Curve, 0.1, 0.5);
            g.Links[fan].Opening = 0.5;
            g.Links[fan].RunDownLoss = 2e5;
            g.AddPipe(mid, outlet, 10, 0.1, 0.15e-3, 3.4);
            SolveResult r = new NetworkSolver(new SolverSettings()).Solve(g, new ConstantFluid(1.204, 1.813e-5));
            Assert.IsTrue(r.Converged);
            Assert.AreEqual(2e5 * r.Flow[0] * r.Flow[0], r.DpFittings[0], 1e-6);
        }

        /// <summary>A pump between the mains and the building: with no loss, a nearly stopped pump would pass the mains flow right up to the end.</summary>
        [Test]
        public void DrivenFromUpstream_FlowStillFadesOut()
        {
            const double mains = 300;
            double full = Solve(1, mains).Flow[1];
            Assert.Greater(full, 0);
            SolveResult nearlyStopped = Solve(0.02, mains);
            Assert.IsTrue(nearlyStopped.Converged);
            Assert.Less(nearlyStopped.Flow[1], full * 0.05);
            double half = Solve(0.5, mains).Flow[1];
            Assert.Less(half, full);
            Assert.Greater(half, nearlyStopped.Flow[1]);
        }
    }
}
