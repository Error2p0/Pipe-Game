using System;
using HeadLoss.Core;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.Commands;
using HeadLoss.Game.Session;
using HeadLoss.Game.Thermal;
using HeadLoss.Sim.Rooms;
using NUnit.Framework;
using static HeadLoss.Game.Tests.GameTestUtil;

namespace HeadLoss.Game.Tests
{
    public class ClimateTests
    {
        sealed class CountingStepper : IRoomGridStepper
        {
            public int Steps;
            public double Seconds;
            public void Step(RoomGridLayout layout, RoomGridState state, RoomGridInputs inputs, RoomGridParams p, float dt)
            {
                Steps++;
                Seconds += dt;
            }
        }

        static BuildingSession CountingSession(out CountingStepper stepper)
        {
            BuildingSession s = TestHouseWithFan();
            stepper = new CountingStepper();
            s.Rooms.Stepper = stepper;
            s.SolveNow();
            s.Rooms.Index(s.Current!.Compiled, s.Current.State.Air);
            return s;
        }

        /// <summary>A round of sub-steps must not land in one frame (01-architecture.md: under 8 ms a frame), and no simulated time may go missing.</summary>
        [Test]
        public void BackgroundRound_IsPaidOutOneStepPerFrame_KeepingAllItsTime()
        {
            BuildingSession s = CountingSession(out CountingStepper c);
            NetworkSnapshot n = s.Current!;
            int revision = s.Rooms.Revision;
            int previous = 0;
            for (int frame = 0; frame < 60 * 6; frame++)
            {
                s.Rooms.Tick(1.0 / 60, false, n.Compiled, n.Result);
                Assert.LessOrEqual(c.Steps - previous, 1, "at most one sub-step a frame");
                previous = c.Steps;
            }
            double sub = Data.Balancing.RoomGrid.BackgroundStepFactor / Data.Balancing.RoomGrid.OpenHz;
            Assert.AreEqual(Math.Ceiling(5 / sub), c.Steps, 1, "one round's sub-steps");
            Assert.AreEqual(5.0, c.Seconds, 0.05, "all of the round's time");
            Assert.AreEqual(revision + 1, s.Rooms.Revision, "the summary updates once, at the end of the round");
        }

        [Test]
        public void BackgroundRound_WithALongTick_FinishesInThatTick()
        {
            BuildingSession s = CountingSession(out CountingStepper c);
            NetworkSnapshot n = s.Current!;
            s.Rooms.Tick(5, false, n.Compiled, n.Result);
            Assert.AreEqual(5.0, c.Seconds, 1e-3);
        }

        [Test]
        public void OpeningTheBuilding_FinishesARoundInProgress()
        {
            BuildingSession s = CountingSession(out CountingStepper c);
            NetworkSnapshot n = s.Current!;
            for (int frame = 0; frame < 5 * 60 + 3 && c.Steps < 2; frame++) s.Rooms.Tick(1.0 / 60, false, n.Compiled, n.Result);
            Assert.AreEqual(2, c.Steps);
            s.Rooms.Tick(1.0 / 60, true, n.Compiled, n.Result);
            Assert.GreaterOrEqual(c.Seconds, 5.0 - 1e-3);
        }

        [Test]
        public void SessionPhase_DelaysItsFirstRound()
        {
            BuildingSession s = CountingSession(out CountingStepper c);
            NetworkSnapshot n = s.Current!;
            s.SetPhase(0.5);
            for (int frame = 0; frame < 60 * 7; frame++) s.Rooms.Tick(1.0 / 60, false, n.Compiled, n.Result);
            Assert.AreEqual(0, c.Steps, "half of the 5 s interval later than usual: 7.5 s");
            for (int frame = 0; frame < 60; frame++) s.Rooms.Tick(1.0 / 60, false, n.Compiled, n.Result);
            Assert.Greater(c.Steps, 0);
        }

        [Test]
        public void SessionsInACity_GetDifferentPhases()
        {
            GameState g = GameContext.NewGame(Data, 3);
            var ctx = new GameContext(Data, g);
            var seen = new System.Collections.Generic.HashSet<double>();
            int count = 0;
            foreach (Game.City.Plot p in ctx.City!.Plots)
            {
                if (p.Tier != 1 || count >= 8) continue;
                BuildingSession s = ctx.Open(p.Id);
                seen.Add(s.Phase);
                count++;
            }
            ctx.Leave();
            Assert.AreEqual(count, seen.Count);
        }

        [Test]
        public void OutsideTemperature_FollowsTheLocalClock_WarmestAtThree()
        {
            var c = new Climate(Data, Array.Empty<string>());
            Assert.AreEqual(21, c.OutsideTemperatureAt(15), 1e-9);
            Assert.AreEqual(9, c.OutsideTemperatureAt(3), 1e-9);
            Assert.AreEqual(15, c.OutsideTemperatureAt(9), 1e-9);
            var cold = new Climate(Data, new[] { "cold-climate" });
            Assert.AreEqual(11, cold.OutsideTemperatureAt(15), 1e-9, "cold climate: −10 °C");
        }

        [Test]
        public void SolarGain_ByFacade_DaytimeOnly()
        {
            var c = new Climate(Data, Array.Empty<string>()) { LocalHour = 13 };
            Assert.AreEqual(300 * 1.0 * 1.2, c.SolarGainW("south", 1.2), 1e-6, "peak at midday between 6 and 20");
            Assert.AreEqual(300 * 0.2 * 1.2, c.SolarGainW("north", 1.2), 1e-6);
            c.LocalHour = 23;
            Assert.AreEqual(0, c.SolarGainW("south", 1.2));
        }

        /// <summary>A building whose vents run: rooms get supply air, ducts take on room temperature, CO₂ stays near outdoor.</summary>
        [Test]
        public void BuildingClimate_FollowsTheNetwork()
        {
            BuildingSession s = TestHouseWithFan();
            Draw(s, Run((0, 3), (5, 3), (5, 5), (3, 5)), 125);
            s.Game.Wallet = 1e6;
            s.Record.EverOnline = true;
            s.Commit();
            s.SolveNow();
            s.IsOpen = true;
            for (int i = 0; i < 50; i++) s.Tick(0.1);
            BuildingClimate rooms = s.Rooms;
            Assert.Greater(rooms.Revision, 0, "the room grid stepped");
            Assert.IsNotNull(rooms.Thermal, "the duct thermal solve ran");
            Assert.Greater(rooms.Rooms[0].AirChangesPerHour, 0.3, "air changes from the vent (one vent into a 216 m³ room)");
            Assert.IsNotEmpty(rooms.SegmentTemperatureK);
            float maxV = 0;
            for (int c = 0; c < rooms.Grid.Cells; c++)
                maxV = Math.Max(maxV, (float)Math.Sqrt(rooms.State.Vx[c] * rooms.State.Vx[c] + rooms.State.Vy[c] * rooms.State.Vy[c]));
            Assert.Greater(maxV, 0.05f, "drafts in the room");
        }

        [Test]
        public void ColdOutsideAir_WarmsAlongTheDuct()
        {
            BuildingSession s = TestHouseWithFan();
            s.Climate.FixedTemperatureC = 0;
            Draw(s, Run((0, 3), (5, 3), (5, 5), (3, 5)), 125);
            s.SolveNow();
            s.IsOpen = true;
            for (int i = 0; i < 30; i++) s.Tick(0.1);
            s.SolveNow();
            for (int i = 0; i < 12; i++) s.Tick(0.1);
            var t = s.Rooms.Thermal!;
            int vent = -1;
            foreach (var kv in s.Rooms.ThermalNetwork!.OutletLink) vent = kv.Value;
            Assert.GreaterOrEqual(vent, 0, "a vent is connected");
            double supplyC = Units.KToC(t.LinkInT[vent]);
            Assert.Greater(supplyC, 0.01, "0 °C air picks up heat from the 20 °C rooms on the way");
            Assert.Less(supplyC, 20);
        }
    }
}
