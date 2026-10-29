using System;
using HeadLoss.Sim.Fluids;
using HeadLoss.Sim.Network;
using HeadLoss.Sim.Rooms;
using HeadLoss.Sim.Thermal;
using NUnit.Framework;

namespace HeadLoss.Sim.Tests
{
    public class ThermalTests
    {
        /// <summary>T9 (15-testing.md): T_in 60 °C, T_amb 20 °C, D 0.02 m, L 20 m, U 10, ṁ 0.05 kg/s, c_p 4180 → 57.666 °C.</summary>
        [Test]
        public void T9_PipeCooling()
        {
            Assert.AreEqual(57.666, PipeThermal.OutletTemperature(60, 20, 10, 0.02, 20, 0.05, 4180), 0.01);
        }

        [Test]
        public void Solve_SeriesPipesCoolTowardAmbient_AndJunctionsMixByMass()
        {
            // Outside (fixed, 0 °C) → pipe → junction ← pipe ← second source (fixed, 40 °C); junction → pipe → sink.
            var g = new NetworkGraph();
            int a = g.AddFixed(100), b = g.AddFixed(100), j = g.AddJunction(), sink = g.AddFixed(0);
            int la = g.AddPipe(a, j, 5, 0.1, 1e-4), lb = g.AddPipe(b, j, 5, 0.1, 1e-4), lc = g.AddPipe(j, sink, 5, 0.1, 1e-4);
            SolveResult r = new NetworkSolver(new SolverSettings()).Solve(g, new ConstantFluid(1.2, 1.8e-5));
            var boundary = new[] { 273.15, 313.15, 0, 293.15 };
            var ambient = new double[] { 293.15, 293.15, 293.15 };
            var noExchange = new double[3];
            var d = new[] { 0.1, 0.1, 0.1 };
            ThermalResult t = PipeThermal.Solve(g, r, boundary, ambient, noExchange, d, 1005);
            Assert.AreEqual(293.15, t.NodeT[j], 1e-6, "equal flows of 0 °C and 40 °C mix to 20 °C");
            Assert.AreEqual(293.15, t.LinkOutT[lc], 1e-6);
            ThermalResult warm = PipeThermal.Solve(g, r, boundary, ambient, new[] { 50.0, 50.0, 50.0 }, d, 1005);
            Assert.Greater(warm.LinkOutT[la], 273.15, "cold air warms in a warm room");
            Assert.Less(warm.LinkOutT[lb], 313.15, "hot air cools");
            Assert.Greater(warm.HeatToAmbient[lb], 0);
            Assert.Less(warm.HeatToAmbient[la], 0);
        }
    }

    public class RoomGridTests
    {
        /// <summary>Two rooms side by side on one floor (each 4 × 4 cells), optionally joined by a door.</summary>
        static RoomGridLayout TwoRooms(bool door)
        {
            var l = new RoomGridLayout { NX = 8, NY = 4, Floors = 1, RoomCount = 2 };
            int n = l.Cells;
            l.Room = new int[n];
            l.OpenE = new bool[n];
            l.OpenN = new bool[n];
            l.Door = new bool[n];
            l.ExteriorArea = new float[n];
            l.Stair = new bool[n];
            l.RoomVolume = new float[] { 16 * 0.25f * 2.7f, 16 * 0.25f * 2.7f };
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 8; x++)
                {
                    int c = l.Index(0, x, y);
                    l.Room[c] = x < 4 ? 0 : 1;
                    l.OpenE[c] = x < 7 && (x != 3 || (door && y == 1));
                    l.OpenN[c] = y < 3;
                    int outside = (x == 0 ? 1 : 0) + (x == 7 ? 1 : 0) + (y == 0 ? 1 : 0) + (y == 3 ? 1 : 0);
                    l.ExteriorArea[c] = outside * 0.5f * 2.7f;
                    if (door && y == 1 && (x == 3 || x == 4)) l.Door[c] = true;
                }
            if (door) l.Doors.Add((0, 1));
            return l;
        }

        static RoomGridParams P => new RoomGridParams();

        [Test]
        public void SealedRoom_NoSources_StaysUniform()
        {
            RoomGridLayout l = TwoRooms(false);
            RoomGridState s = RoomGridState.Initial(l, 20, 420, 0.209f);
            RoomGridInputs inp = RoomGridInputs.For(l);
            inp.OutsideTemperature = 20;
            var step = new RoomGridReference();
            for (int i = 0; i < 100; i++) step.Step(l, s, inp, P, 0.1f);
            foreach (float t in s.T) Assert.AreEqual(20, t, 1e-4);
            foreach (float c in s.Co2) Assert.AreEqual(420, c, 1e-3);
        }

        [Test]
        public void ColdSupply_CoolsTheRoom_AndAchMatchesTheFlow()
        {
            RoomGridLayout l = TwoRooms(true);
            RoomGridState s = RoomGridState.Initial(l, 20, 420, 0.209f);
            RoomGridInputs inp = RoomGridInputs.For(l);
            inp.OutsideTemperature = 20;
            int vent = l.Index(0, 1, 2);
            inp.SupplyFlow[vent] = 0.01f;
            inp.SupplyTemperature[vent] = 10;
            inp.Jets.Add(new VentJet { Cell = vent, DirX = 1, FaceVelocity = 0.25f });
            var step = new RoomGridReference();
            for (int i = 0; i < 300; i++) step.Step(l, s, inp, P, 0.1f);
            RoomSummary[] rooms = RoomGridMath.Summaries(l, s, inp, P);
            Assert.Less(rooms[0].MeanTemperature, 19.98f, "slowly: the room has 5× the air's heat capacity");
            Assert.AreEqual(3600 * 0.01 / l.RoomVolume[0], rooms[0].AirChangesPerHour, 1e-3);
            Assert.Greater(Math.Abs(s.Vx[l.Index(0, 2, 2)]), Math.Abs(s.Vx[l.Index(0, 2, 0)]), "the jet makes a draft in front of the vent");
        }

        [Test]
        public void Walls_BlockAir_DoorsLetItThrough()
        {
            foreach (bool door in new[] { false, true })
            {
                RoomGridLayout l = TwoRooms(door);
                RoomGridState s = RoomGridState.Initial(l, 20, 420, 0.209f);
                RoomGridInputs inp = RoomGridInputs.For(l);
                inp.OutsideTemperature = 20;
                for (int y = 0; y < 4; y++) inp.Co2Lower[l.Index(0, 0, y)] = 2e-5f;
                var step = new RoomGridReference();
                for (int i = 0; i < 600; i++) step.Step(l, s, inp, P, 0.1f);
                float far = s.Co2[l.Index(0, 6, 1)];
                if (door) Assert.Greater(far, 421, "CO₂ spreads through the door");
                else Assert.AreEqual(420, far, 1e-3, "nothing crosses a wall");
                // No velocity through the wall between the rooms except at the door.
                Assert.IsFalse(l.OpenE[l.Index(0, 3, 0)]);
            }
        }

        [Test]
        public void NetSources_BalanceToZero_OutflowLeavesThroughTheFacade()
        {
            RoomGridLayout l = TwoRooms(true);
            RoomGridInputs inp = RoomGridInputs.For(l);
            inp.SupplyFlow[l.Index(0, 1, 1)] = 0.02f;
            var net = new float[l.Cells];
            RoomGridMath.NetSources(l, inp, net);
            float sum = 0, room1 = 0;
            for (int c = 0; c < l.Cells; c++)
            {
                sum += net[c];
                if (l.Room[c] == 1) room1 += net[c];
            }
            Assert.AreEqual(0, sum, 1e-6, "what comes in goes out");
            Assert.Less(room1, 0, "some air leaves through the second room, via the door");
        }

        [Test]
        public void Occupants_RaiseCo2_AndWarmLowerAirRisesToTheUpperLayer()
        {
            RoomGridLayout l = TwoRooms(false);
            RoomGridState s = RoomGridState.Initial(l, 20, 420, 0.209f);
            RoomGridInputs inp = RoomGridInputs.For(l);
            inp.OutsideTemperature = 20;
            for (int c = 0; c < l.Cells; c++)
                if (l.Room[c] == 0)
                {
                    inp.Co2Lower[c] = 2 * 0.005e-3f / 16;
                    inp.HeatLower[c] = 2 * 70f / 16;
                }
            var step = new RoomGridReference();
            for (int i = 0; i < 600; i++) step.Step(l, s, inp, P, 0.1f);
            int cell = l.Index(0, 1, 1), n = l.Cells;
            Assert.Greater(s.Co2[cell], 425);
            Assert.Greater(s.T[cell], 20);
            Assert.Greater(s.T[n + cell], 20.0f, "heat reaches the upper layer by overturning");
            Assert.AreEqual(420, s.Co2[l.Index(0, 6, 1)], 1e-3, "the other room has nobody in it");
        }
    }
}
