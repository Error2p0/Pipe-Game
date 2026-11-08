using System.Collections.Generic;
using HeadLoss.Core;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.Codes;
using HeadLoss.Game.Commands;
using HeadLoss.Game.Economy;
using HeadLoss.Game.Session;
using Newtonsoft.Json;
using NUnit.Framework;

namespace HeadLoss.Game.Tests
{
    /// <summary>The indoor air quality code (<c>09-codes.md</c>, Phase 6 task 4): 1 000 ppm room mean CO₂, air outlets pay 0.</summary>
    public class AirQualityTests
    {
        /// <summary>The one-room test house (a 10 × 7 m living room, three vents) with these people and codes.</summary>
        static BuildingSession House(double occupants, params string[] codes) => House(occupants, 125, codes);

        static BuildingSession House(double occupants, double ductMm, params string[] codes)
        {
            var data = GameTestUtil.Data;
            BuildingLayoutDef def = JsonConvert.DeserializeObject<BuildingLayoutDef>(JsonConvert.SerializeObject(data.HandmadeBuilding("test-house")))!;
            def.Codes = new List<string>(codes);
            def.Rooms[0].Occupants = occupants;
            var layout = BuildingLayout.From(data, def);
            var game = new GameState { Wallet = 1e6 };
            BuildingRecord r = BuildingRecord.CreateFor(layout);
            game.Buildings.Add(r);
            var build = new BuildingSession(data, layout, r, game, budgetCap: 1e9);
            Assert.IsTrue(build.Execute(new SetIntakeEquipmentCommand("air-intake", "small-axial-fan")));
            // A trunk past all three vents.
            Assert.IsTrue(GameTestUtil.Draw(build, GameTestUtil.Run((0, 3), (3, 3), (3, 5)), ductMm));
            Assert.IsTrue(GameTestUtil.Draw(build, GameTestUtil.Run((3, 3), (7, 3), (7, 1)), ductMm));
            Assert.IsTrue(GameTestUtil.Draw(build, GameTestUtil.Run((7, 3), (8, 3), (8, 5)), ductMm));
            Assert.IsTrue(build.Commit());
            // Opened again, as when the game loads: the first solve sees what is built.
            var s = new BuildingSession(data, layout, r, game, budgetCap: 1e9);
            s.SolveNow();
            return s;
        }

        static double TotalVentFlow(BuildingSession s)
        {
            double q = 0;
            foreach (OutletStatus o in s.DesignEvaluation.Outlets) if (o.Def.Fluid == "air") q += o.Delivery;
            return q;
        }

        [Test]
        public void SteadyState_IsOutdoorPlusGenerationOverSupply_AndNeedHitsTheLimit()
        {
            var data = GameTestUtil.Data;
            Assert.AreEqual(1000, IndoorAirQuality.MaxPpm(data));
            // 2 people breathe out 0.010 L/s; 1 000 − 420 ppm leaves 580 ppm, so they need 0.01e-3 / 580e-6 = 0.017241 m³/s.
            double need = IndoorAirQuality.RequiredSupply(data, 2);
            Assert.AreEqual(0.017241, need, 1e-5);
            Assert.AreEqual(62.07, Units.M3sToM3h(need), 0.01);
            Assert.AreEqual(1000, IndoorAirQuality.SteadyPpm(data, 2, need), 1e-6);
            Assert.AreEqual(420 + 1e-5 / 0.01 * 1e6, IndoorAirQuality.SteadyPpm(data, 2, 0.01), 1e-6);
            Assert.AreEqual(420, IndoorAirQuality.SteadyPpm(data, 0, 0), "an empty room stays at the outdoor level");
            Assert.IsTrue(double.IsPositiveInfinity(IndoorAirQuality.SteadyPpm(data, 1, 0)));
        }

        [Test]
        public void InForceAtTiers4And5Only()
        {
            CodeDef code = GameTestUtil.Data.FindCode(IndoorAirQuality.CodeId)!;
            Assert.AreEqual("operating", code.Kind);
            CollectionAssert.AreEquivalent(new[] { 4, 5 }, code.Tiers);
            Assert.IsFalse(IndoorAirQuality.InForce(GameTestUtil.Data, House(2).Layout), "the tier 1 test house has no IAQ code");
        }

        /// <summary>People for <paramref name="share"/> × the fresh air the three vents give at the limit.</summary>
        static double Crowd(double share)
        {
            double q = TotalVentFlow(House(0, "tap-pressure"));
            return share * q / IndoorAirQuality.RequiredSupply(GameTestUtil.Data, 1);
        }

        [Test]
        public void RoomAboveTheLimit_ItsAirOutletsPayZero_TheBuildingStaysOnline()
        {
            double gross = House(Crowd(1.5), "tap-pressure").DesignEvaluation.Gross;
            Assert.Greater(gross, 0, "without the code the crowded house earns");

            BuildingSession s = House(Crowd(1.5), IndoorAirQuality.CodeId);
            BuildingEvaluation ev = s.DesignEvaluation;
            Assert.IsTrue(ev.Online, "an operating code: the building stays online");
            Assert.AreEqual(3, ev.AirQualityCut);
            foreach (OutletStatus o in ev.Outlets)
            {
                Assert.AreEqual(OutletZone.AirQuality, o.Zone);
                Assert.AreEqual(0, o.Pay);
            }
            Assert.AreEqual(0, ev.Gross, 1e-12);
            Violation? v = ev.Violations.Find(x => x.CodeId == IndoorAirQuality.CodeId);
            Assert.IsNotNull(v, "the room is on the code list");
            Assert.IsFalse(v!.Blocking);
            Assert.AreEqual(0, ev.BlockingViolations);
            StringAssert.Contains("ppm CO₂ (max 1000)", v.Reason);
            Assert.AreEqual(3, v.Tiles.Count, "the room's three vents are outlined");
            Assert.AreEqual(0, s.CommittedEvaluation.Gross, 1e-12, "what is built pays nothing either");
        }

        [Test]
        public void RoomBelowTheLimit_PaysAsUsual()
        {
            BuildingSession s = House(Crowd(0.8), IndoorAirQuality.CodeId);
            BuildingEvaluation ev = s.DesignEvaluation;
            Assert.AreEqual(0, ev.AirQualityCut);
            Assert.IsNull(ev.Violations.Find(x => x.CodeId == IndoorAirQuality.CodeId));
            Assert.Greater(ev.Gross, 0);
        }

        [Test]
        public void NotInForce_ACrowdedRoomCutsNothing()
        {
            BuildingSession s = House(Crowd(3), "tap-pressure");
            Assert.AreEqual(0, s.DesignEvaluation.AirQualityCut);
            Assert.Greater(s.DesignEvaluation.Gross, 0);
        }

        [Test]
        public void MoreAirInTheDesign_PaysAgainAsSoonAsItIsCommitted()
        {
            // Narrow ducts starve a crowd that wider ones serve.
            double narrow = TotalVentFlow(House(0, 100, "tap-pressure")), wide = TotalVentFlow(House(0, 160, "tap-pressure"));
            Assert.Less(narrow, 0.85 * wide);
            double crowd = 0.92 * wide / IndoorAirQuality.RequiredSupply(GameTestUtil.Data, 1);
            BuildingSession s = House(crowd, 100, IndoorAirQuality.CodeId);
            Assert.AreEqual(3, s.CommittedEvaluation.AirQualityCut);
            var ids = new List<int>();
            foreach (DuctSegment seg in s.State.Air.Segments) ids.Add(seg.Id);
            Assert.IsTrue(s.Execute(new SetSegmentSizeCommand(ids, 160)));
            s.SolveNow();
            Assert.AreEqual(0, s.DesignEvaluation.AirQualityCut, "the design preview already pays");
            Assert.AreEqual(3, s.CommittedEvaluation.AirQualityCut, "what is built still does not");
            Assert.IsTrue(s.Commit());
            s.SolveNow();
            Assert.AreEqual(0, s.CommittedEvaluation.AirQualityCut, "no waiting for the air to change");
            Assert.Greater(s.CommittedEvaluation.Gross, 0);
        }

        [Test]
        public void LoadingABuilding_SeedsRoomsAtTheirSteadyState()
        {
            double q = TotalVentFlow(House(0, "tap-pressure"));
            BuildingSession s = House(4, IndoorAirQuality.CodeId);
            double expected = IndoorAirQuality.SteadyPpm(s.Data, 4, q);
            Assert.AreEqual(expected, s.Rooms.Rooms[0].Co2Ppm, expected * 1e-4, "the first solve starts the room where the vents hold it");
        }

        [Test]
        public void Offline_SteadyStateAppliesTheCode()
        {
            BuildingEvaluation away = House(Crowd(1.5), IndoorAirQuality.CodeId).SteadyState(20);
            Assert.AreEqual(3, away.AirQualityCut, "offline the steady state is above the limit");
            Assert.AreEqual(0, away.Gross, 1e-12);
            Assert.IsTrue(away.Online);

            BuildingEvaluation fine = House(Crowd(0.5), IndoorAirQuality.CodeId).SteadyState(20);
            Assert.AreEqual(0, fine.AirQualityCut);
            Assert.Greater(fine.Gross, 0);
        }

        [Test]
        public void RoomGrid_SettlesNearTheWellMixedSteadyState()
        {
            // People's CO₂ mixes with the air instead of settling in the lower layer (D157): from outdoor air the room grid
            // comes to rest within 20% of the well-mixed excess (it reads a little low: fresh air replaces air at the vents).
            double q = TotalVentFlow(House(0, "tap-pressure"));
            BuildingSession s = House(6, IndoorAirQuality.CodeId);
            double expected = IndoorAirQuality.SteadyPpm(s.Data, 6, q);
            NetworkSnapshot snap = s.CommittedSnapshot!;
            s.Rooms.SeedCo2(new[] { 420.0 });
            s.Rooms.Index(snap.Compiled, snap.State.Air);
            s.Rooms.Step(6 * 3600, 4, snap.Compiled, snap.Result);
            double first = s.Rooms.Rooms[0].Co2Ppm;
            s.Rooms.Step(3600, 4, snap.Compiled, snap.Result);
            Assert.AreEqual(first, s.Rooms.Rooms[0].Co2Ppm, 2, "settled");
            Assert.AreEqual(expected - 420, first - 420, 0.2 * (expected - 420), $"well mixed {expected:0} ppm, grid {first:0} ppm");
            int n = s.Rooms.Grid.Cells;
            double lower = 0, upper = 0;
            for (int c = 0; c < n; c++) if (s.Rooms.Grid.Room[c] == 0) { lower += s.Rooms.State.Co2[c]; upper += s.Rooms.State.Co2[n + c]; }
            Assert.AreEqual(1, lower / upper, 0.05, "no CO₂ pool in the lower layer");
        }
    }
}
