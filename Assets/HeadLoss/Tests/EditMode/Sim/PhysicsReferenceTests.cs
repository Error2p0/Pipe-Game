using System;
using System.IO;
using HeadLoss.Core;
using HeadLoss.Core.Data;
using HeadLoss.Sim.Devices;
using HeadLoss.Sim.Fluids;
using HeadLoss.Sim.Hydraulics;
using HeadLoss.Sim.Network;
using NUnit.Framework;

namespace HeadLoss.Sim.Tests
{
    /// <summary>Physics reference cases from <c>15-testing.md</c>. Tolerances are relative unless stated.</summary>
    public class PhysicsReferenceTests
    {
        static readonly FrictionModel Fm = new FrictionModel(2300, 4000);
        const double G = 9.80665;

        static void Rel(double expected, double actual, double tol, string what = "")
        {
            Assert.That(Math.Abs(actual - expected), Is.LessThanOrEqualTo(Math.Abs(expected) * tol),
                $"{what}: expected {expected}, got {actual} (rel {Math.Abs(actual - expected) / Math.Abs(expected):E3})");
        }

        // --- T1: Swamee-Jain vs Colebrook ---

        [TestCase(1e5, 1e-4, 0.018514, 0.018452)]
        [TestCase(1e6, 1e-3, 0.019943, 0.020029)]
        public void T1_SwameeJain_WithinOnePercentOfColebrook(double re, double rr, double colebrook, double sj)
        {
            double c = FrictionModel.Colebrook(re, rr);
            double s = FrictionModel.SwameeJain(re, rr);
            Rel(colebrook, c, 1e-4, "Colebrook");
            Rel(sj, s, 1e-4, "Swamee-Jain");
            Rel(c, s, 0.01, "SJ vs Colebrook");
            Rel(s, Fm.Factor(re, rr), 1e-12, "model uses SJ");
        }

        // --- T2: laminar and transition ---

        [Test]
        public void T2_Laminar_Re1000_Is0064()
        {
            Assert.AreEqual(0.064, Fm.Factor(1000, 1e-3), 1e-9);
        }

        [Test]
        public void T2b_Transition_Re3000_LinearBetween2300And4000()
        {
            double rr = 1e-3;
            double f0 = 64.0 / 2300;
            double f1 = FrictionModel.SwameeJain(4000, rr);
            double expected = f0 + (3000 - 2300) / 1700.0 * (f1 - f0);
            Assert.AreEqual(expected, Fm.Factor(3000, rr));
        }

        [TestCase(1500.0)]
        [TestCase(3000.0)]
        [TestCase(5e4)]
        [TestCase(2e6)]
        public void Friction_ReTimesDerivative_MatchesFiniteDifference(double re)
        {
            double rr = 9e-4;
            double h = re * 1e-6;
            double fd = (Fm.Factor(re + h, rr) - Fm.Factor(re - h, rr)) / (2 * h) * re;
            Assert.AreEqual(fd, Fm.ReTimesDerivative(re, rr), Math.Abs(fd) * 1e-5 + 1e-12);
        }

        // --- T3: water pipe friction ---

        [Test]
        public void T3_WaterPipeFriction()
        {
            double d = 0.05, area = Math.PI * d * d / 4;
            PipeLossResult r = PipeLoss.Evaluate(0.002, 100, d, area, 0.045e-3, 0, 998.2, 1.002e-3, Fm);
            Rel(1.0186, r.Velocity, 0.005, "v");
            Rel(50736, r.Reynolds, 0.005, "Re");
            Rel(0.023838, r.FrictionFactor, 0.005, "f");
            Rel(24688, r.TotalDp, 0.005, "Δp");
        }

        // --- T4: air duct with fittings ---

        [TestCase(1.2, 46.960, TestName = "T4_AirDuct_SharpElbows")]
        [TestCase(0.3, 33.405, TestName = "T4b_AirDuct_LongRadiusElbows")]
        public void T4_AirDuctWithFittings(double elbowK, double expectedTotal)
        {
            double d = 0.1, area = Math.PI * d * d / 4;
            double q = Units.M3hToM3s(100);
            double sumK = 2 * elbowK + 1.0;
            PipeLossResult r = PipeLoss.Evaluate(q, 10, d, area, 0.15e-3, sumK, 1.204, 1.813e-5, Fm);
            Rel(3.5368, r.Velocity, 0.005, "v");
            Rel(23487, r.Reynolds, 0.005, "Re");
            Rel(0.028361, r.FrictionFactor, 0.005, "f");
            Rel(7.5303, 0.5 * 1.204 * r.Velocity * r.Velocity, 0.005, "q");
            Rel(21.357, r.FrictionDp, 0.005, "Δp friction");
            Rel(expectedTotal, r.TotalDp, 0.005, "Δp total");
        }

        [Test]
        public void T3T4_WithTableProperties_WithinLooserTolerance()
        {
            // 15-testing.md: repeat with table properties and a 1% looser tolerance. Only air has a table in Phase 1.
            DataRepository data = DataRepository.Load(new FileDataSource(SimTestData.DataRoot));
            var air = new TableFluid(data.Fluid("air"), data.FluidTables["air"], data.Balancing.Physics.GasConstant);
            double rho = air.Density(101325, 293.15), mu = air.Viscosity(101325, 293.15);
            double d = 0.1, area = Math.PI * d * d / 4;
            PipeLossResult r = PipeLoss.Evaluate(Units.M3hToM3s(100), 10, d, area, 0.15e-3, 3.4, rho, mu, Fm);
            Rel(46.960, r.TotalDp, 0.015, "Δp total (table air)");
        }

        // --- T5: fan operating point, sharp vs long-radius (the Phase 1 gate case) ---

        static SolveResult SolveFanDuct(double sumK, double speed = 1)
        {
            var g = new NetworkGraph();
            int inlet = g.AddFixed(0);
            int mid = g.AddJunction();
            int outlet = g.AddFixed(0);
            FanCurve fan = FanCurve.Quadratic(100, Units.M3hToM3s(300));
            g.AddFan(inlet, mid, fan, 0.1, speed);
            g.AddPipe(mid, outlet, 10, 0.1, 0.15e-3, sumK);
            return new NetworkSolver(new SolverSettings()).Solve(g, new ConstantFluid(1.204, 1.813e-5));
        }

        [Test]
        public void T5_FanOperatingPoint_SharpElbows()
        {
            SolveResult r = SolveFanDuct(3.4);
            Assert.IsTrue(r.Converged);
            Rel(132.26, Units.M3sToM3h(r.Flow[1]), 0.005, "Q");
            Rel(80.56, r.DpDevice[0], 0.005, "Δp");
        }

        [Test]
        public void T5b_FanOperatingPoint_LongRadiusElbows_About15PercentMoreFlow()
        {
            SolveResult sharp = SolveFanDuct(3.4);
            SolveResult smooth = SolveFanDuct(1.6);
            Assert.IsTrue(smooth.Converged);
            Rel(152.11, Units.M3sToM3h(smooth.Flow[1]), 0.005, "Q");
            Rel(74.29, smooth.DpDevice[0], 0.005, "Δp");
            Rel(1.15, smooth.Flow[1] / sharp.Flow[1], 0.01, "flow ratio");
        }

        // --- T6: parallel pipes split ---

        [Test]
        public void T6_ParallelPipesSplit()
        {
            var g = new NetworkGraph();
            int a = g.AddDemand(-0.005);
            int b = g.AddFixed(0);
            g.AddPipe(a, b, 50, 0.05, 0.045e-3);
            g.AddPipe(a, b, 30, 0.04, 0.045e-3);
            SolveResult r = new NetworkSolver(new SolverSettings()).Solve(g, new ConstantFluid(998.2, 1.002e-3));
            Assert.IsTrue(r.Converged);
            Rel(2.9010, Units.M3sToLps(r.Flow[0]), 0.005, "Q1");
            Rel(2.0990, Units.M3sToLps(r.Flow[1]), 0.005, "Q2");
            Rel(24761, r.NodePressure[a], 0.005, "Δp");
        }

        // --- T7: hydrostatic ---

        [Test]
        public void T7_Hydrostatic_10mRise()
        {
            Rel(0.97890, Units.PaToBar(Hydrostatics.Pressure(998.2, G, 10)), 0.001, "formula");

            // Same through the solver's elevation term: a no-flow riser.
            var g = new NetworkGraph();
            int bottom = g.AddFixed(Units.BarToPa(2), 0);
            int top = g.AddDemand(0, 10);
            g.AddPipe(bottom, top, 10, 0.022, 0.0015e-3);
            SolveResult r = new NetworkSolver(new SolverSettings()).Solve(g, new ConstantFluid(998.2, 1.002e-3));
            Rel(Units.BarToPa(2) - 97890, r.NodePressure[top], 0.001, "riser top pressure");
        }

        // --- T10: affinity laws ---

        [Test]
        public void T10_AffinityLaws_HalfSpeed()
        {
            SolveResult full = SolvePureQuadratic(1.0);
            SolveResult half = SolvePureQuadratic(0.5);
            Rel(0.5, half.Flow[0] / full.Flow[0], 0.005, "Q");
            Rel(0.25, half.DpDevice[0] / full.DpDevice[0], 0.005, "Δp");
            Rel(0.125, half.Power[0] / full.Power[0], 0.005, "P");
        }

        static SolveResult SolvePureQuadratic(double speed)
        {
            var g = new NetworkGraph();
            int inlet = g.AddFixed(0);
            int mid = g.AddJunction();
            int outlet = g.AddFixed(0);
            g.AddFan(inlet, mid, FanCurve.Quadratic(300, Units.M3hToM3s(700), 6, 0.45), 0.2, speed);
            g.AddVent(mid, outlet, 40, 0.0314);
            SolveResult r = new NetworkSolver(new SolverSettings()).Solve(g, new ConstantFluid(1.204, 1.813e-5));
            Assert.IsTrue(r.Converged);
            return r;
        }

        // --- T11: ideal gas density ---

        [TestCase(0.028965, 1.2041, TestName = "T11_IdealGasDensity_Air")]
        [TestCase(0.016043, 0.6669, TestName = "T11_IdealGasDensity_Methane")]
        public void T11_IdealGasDensity(double molarMass, double expected)
        {
            Rel(expected, IdealGas.Density(101325, molarMass, 293.15, 8.314462618), 0.005);
        }

        /// <summary>06-fluids.md: the ideal gas law agrees with the CoolProp table within 1% for air and methane in the game's range.</summary>
        /// <remarks>Methane up to 2 bar absolute (building-side gas); at MP-main pressure and −20 °C real-gas effects reach 1.1% (decision D21).</remarks>
        [TestCase("air", 0.028965, 300000.0)]
        [TestCase("natural-gas", 0.016043, 200000.0)]
        public void Table_AgreesWithIdealGas_WithinOnePercent(string fluidId, double molarMass, double maxPressure)
        {
            DataRepository data = DataRepository.Load(new FileDataSource(SimTestData.DataRoot));
            var fluid = new TableFluid(data.Fluid(fluidId), data.FluidTables[fluidId], data.Balancing.Physics.GasConstant);
            StringAssert.StartsWith("CoolProp", data.FluidTables[fluidId].Source);
            for (double tC = -20; tC <= 100; tC += 7)
            {
                foreach (double pAbs in new[] { 95000.0, 101325.0, 110000.0, maxPressure })
                {
                    double ideal = IdealGas.Density(pAbs, molarMass, tC + 273.15, 8.314462618);
                    Rel(ideal, fluid.TableDensity(pAbs, tC + 273.15), 0.01, $"{fluidId} ρ at {tC} °C, {pAbs} Pa");
                }
            }
        }

        [Test]
        public void Tables_MatchTextbookValuesAt20C()
        {
            DataRepository data = DataRepository.Load(new FileDataSource(SimTestData.DataRoot));
            double g = data.Balancing.Physics.GasConstant;
            var air = new TableFluid(data.Fluid("air"), data.FluidTables["air"], g);
            var water = new TableFluid(data.Fluid("water"), data.FluidTables["water"], g);
            Rel(1.204, air.TableDensity(101325, 293.15), 0.005, "air ρ");
            Rel(1.813e-5, air.Viscosity(101325, 293.15), 0.01, "air μ");
            Rel(998.2, water.Density(101325, 293.15), 0.001, "water ρ (T3)");
            Rel(1.002e-3, water.Viscosity(101325, 293.15), 0.005, "water μ (T3)");
        }
    }

    static class SimTestData
    {
        public static string DataRoot => Path.Combine(Directory.GetCurrentDirectory(), "Assets", "HeadLoss", "Data");
    }
}
