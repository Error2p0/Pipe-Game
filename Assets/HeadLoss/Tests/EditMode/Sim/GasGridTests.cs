using System;
using HeadLoss.Sim.Fluids;
using HeadLoss.Sim.Network;
using HeadLoss.Sim.Rooms;
using NUnit.Framework;

namespace HeadLoss.Sim.Tests
{
    /// <summary>Gas in the room grid (<c>05-simulation.md</c> §3): sources, layers, buoyancy, floors, and gas buoyancy in the network solver.</summary>
    public class GasGridTests
    {
        /// <summary>One room per floor, sealed, with a stair link at cell (0, 0).</summary>
        static RoomGridLayout Tower(int floors, int side = 4)
        {
            var l = new RoomGridLayout { NX = side, NY = side, Floors = floors, RoomCount = floors };
            int n = l.Cells;
            l.Room = new int[n];
            l.OpenE = new bool[n];
            l.OpenN = new bool[n];
            l.Door = new bool[n];
            l.ExteriorArea = new float[n];
            l.Stair = new bool[n];
            l.RoomVolume = new float[floors];
            for (int f = 0; f < floors; f++)
            {
                l.RoomVolume[f] = side * side * 0.25f * 2.7f;
                for (int y = 0; y < side; y++)
                    for (int x = 0; x < side; x++)
                    {
                        int c = l.Index(f, x, y);
                        l.Room[c] = f;
                        l.OpenE[c] = x + 1 < side;
                        l.OpenN[c] = y + 1 < side;
                    }
                l.Stair[l.Index(f, 0, 0)] = true;
            }
            return l;
        }

        static double Total(RoomGridLayout l, RoomGridState s, RoomGridParams p)
        {
            int n = l.Cells;
            double sum = 0;
            for (int c = 0; c < n; c++) sum += s.Gas[c] * p.LowerHeight + s.Gas[n + c] * p.UpperHeight;
            return sum * p.CellSize * p.CellSize;
        }

        [Test]
        public void Leak_AddsTheVolumeItInjects()
        {
            RoomGridLayout l = Tower(1);
            var p = new RoomGridParams();
            RoomGridInputs inp = RoomGridInputs.For(l);
            RoomGridState s = RoomGridState.Initial(l, 20, 420, 0.209f);
            inp.GasActive = true;
            inp.GasUpper[l.Index(0, 1, 1)] = 1e-4f;
            var step = new RoomGridReference();
            for (int i = 0; i < 100; i++) step.Step(l, s, inp, p, 0.1f);
            Assert.AreEqual(1e-4 * 10, Total(l, s, p), 1e-5, "10 s at 0.1 L/s");
        }

        [Test]
        public void LightGas_RisesToTheUpperLayer()
        {
            RoomGridLayout l = Tower(1);
            var p = new RoomGridParams();
            RoomGridInputs inp = RoomGridInputs.For(l);
            inp.GasActive = true;
            RoomGridState s = RoomGridState.Initial(l, 20, 420, 0.209f);
            int n = l.Cells;
            for (int c = 0; c < n; c++) s.Gas[c] = 0.02f;
            double before = Total(l, s, p);
            var step = new RoomGridReference();
            for (int i = 0; i < 300; i++) step.Step(l, s, inp, p, 0.1f);
            double lower = 0, upper = 0;
            for (int c = 0; c < n; c++) { lower += s.Gas[c]; upper += s.Gas[n + c]; }
            Assert.Greater(upper, 5 * lower, "methane collects high");
            Assert.AreEqual(before, Total(l, s, p), before * 1e-3, "and none is lost");
        }

        [Test]
        public void HeavyGas_SinksToTheLowerLayer()
        {
            RoomGridLayout l = Tower(1);
            var p = new RoomGridParams { GasMolarMass = 0.044097f };  // propane
            RoomGridInputs inp = RoomGridInputs.For(l);
            inp.GasActive = true;
            RoomGridState s = RoomGridState.Initial(l, 20, 420, 0.209f);
            int n = l.Cells;
            for (int c = 0; c < n; c++) s.Gas[n + c] = 0.02f;
            var step = new RoomGridReference();
            for (int i = 0; i < 300; i++) step.Step(l, s, inp, p, 0.1f);
            double lower = 0, upper = 0;
            for (int c = 0; c < n; c++) { lower += s.Gas[c]; upper += s.Gas[n + c]; }
            Assert.Greater(lower, upper);
        }

        [Test]
        public void LightGas_ClimbsTheStairs_ToTheFloorAbove()
        {
            RoomGridLayout l = Tower(2);
            var p = new RoomGridParams();
            RoomGridInputs inp = RoomGridInputs.For(l);
            inp.GasActive = true;
            RoomGridState s = RoomGridState.Initial(l, 20, 420, 0.209f);
            int n = l.Cells;
            for (int y = 0; y < l.NY; y++)
                for (int x = 0; x < l.NX; x++) s.Gas[n + l.Index(0, x, y)] = 0.05f;
            var step = new RoomGridReference();
            for (int i = 0; i < 600; i++) step.Step(l, s, inp, p, 0.1f);
            double upstairs = 0;
            for (int y = 0; y < l.NY; y++)
                for (int x = 0; x < l.NX; x++) upstairs += s.Gas[n + l.Index(1, x, y)];
            Assert.Greater(upstairs, 0.01);
        }

        [Test]
        public void SupplyAir_DilutesGas()
        {
            RoomGridLayout l = Tower(1);
            var p = new RoomGridParams();
            RoomGridInputs inp = RoomGridInputs.For(l);
            inp.GasActive = true;
            RoomGridState s = RoomGridState.Initial(l, 20, 420, 0.209f);
            int n = l.Cells;
            for (int c = 0; c < n; c++) s.Gas[n + c] = 0.05f;
            double before = Total(l, s, p);
            inp.SupplyFlow[l.Index(0, 1, 1)] = 0.02f;
            inp.SupplyTemperature[l.Index(0, 1, 1)] = 20;
            var step = new RoomGridReference();
            for (int i = 0; i < 1000; i++) step.Step(l, s, inp, p, 0.1f);
            Assert.Less(Total(l, s, p), before * 0.95);
        }

        [Test]
        public void LowVent_FeedsTheLowerLayer()
        {
            RoomGridLayout l = Tower(1);
            var p = new RoomGridParams();
            RoomGridInputs inp = RoomGridInputs.For(l);
            inp.GasActive = true;
            RoomGridState s = RoomGridState.Initial(l, 20, 420, 0.209f);
            int c0 = l.Index(0, 1, 1);
            s.Gas[c0] = 0.05f;
            RoomGridState control = s.Clone();
            inp.SupplyLower[c0] = 0.02f;
            inp.SupplyTemperature[c0] = 20;
            new RoomGridReference().Step(l, s, inp, p, 0.01f);
            new RoomGridReference().Step(l, control, RoomGridInputsWithGas(l), p, 0.01f);
            Assert.Less(s.Gas[c0], control.Gas[c0], "fresh air into the lower layer dilutes it");
        }

        static RoomGridInputs RoomGridInputsWithGas(RoomGridLayout l)
        {
            RoomGridInputs i = RoomGridInputs.For(l);
            i.GasActive = true;
            return i;
        }

        [Test]
        public void NoGas_LeavesTheFieldUntouched()
        {
            RoomGridLayout l = Tower(1);
            var p = new RoomGridParams();
            RoomGridInputs inp = RoomGridInputs.For(l);
            RoomGridState s = RoomGridState.Initial(l, 20, 420, 0.209f);
            s.Gas[0] = 0.5f;  // Not stepped while inactive.
            new RoomGridReference().Step(l, s, inp, p, 0.1f);
            Assert.AreEqual(0.5f, s.Gas[0]);
        }

        [Test]
        public void Density_LightGasLowersIt()
        {
            var p = new RoomGridParams();
            Assert.Less(RoomGridMath.Density(p, 20, 420, 0.05f), RoomGridMath.Density(p, 20, 420));
        }

        [Test]
        public void Solver_GasBuoyancy_UsesTheDensityDifference_D7()
        {
            // A column of gas open at the top, with a closed regulator-free feed at the bottom: static pressure rises with height.
            var g = new NetworkGraph { AmbientDensity = 1.2 };
            int bottom = g.AddFixed(2500, 0), top = g.AddJunction(10);
            g.AddPipe(bottom, top, 10, 0.05, 1e-5);
            SolveResult r = new NetworkSolver(new SolverSettings()).Solve(g, new ConstantFluid(0.67, 1.1e-5));
            Assert.AreEqual(2500 + (1.2 - 0.67) * 9.80665 * 10, r.NodePressure[top], 0.5);
        }

        [Test]
        public void Solver_Liquids_KeepTheFullHydrostaticTerm()
        {
            var g = new NetworkGraph();
            int bottom = g.AddFixed(200000, 0), top = g.AddJunction(10);
            g.AddPipe(bottom, top, 10, 0.05, 1e-5);
            SolveResult r = new NetworkSolver(new SolverSettings()).Solve(g, new ConstantFluid(998.2, 1e-3));
            Assert.AreEqual(200000 - 998.2 * 9.80665 * 10, r.NodePressure[top], 1);
        }
    }
}
