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
