using System.Collections.Generic;
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
    /// <summary>One positive and one negative test per code (<c>15-testing.md</c> §Game rule tests).</summary>
    public class CodeTests
    {
        static List<TilePos> RunOn(int floor, params (int x, int y)[] points)
        {
            var list = new List<TilePos>();
            foreach (TilePos t in GameTestUtil.Run(points)) list.Add(t.OnFloor(floor));
            return list;
        }

        /// <summary>A copy of a hand-made layout with other codes in force.</summary>
        static BuildingSession WithCodes(string id, params string[] codes)
        {
            var data = GameTestUtil.Data;
            BuildingLayoutDef def = JsonConvert.DeserializeObject<BuildingLayoutDef>(JsonConvert.SerializeObject(data.HandmadeBuilding(id)))!;
            def.Codes = new List<string>(codes);
            var layout = BuildingLayout.From(data, def);
            var game = new GameState { Wallet = 1e6 };
            BuildingRecord r = BuildingRecord.CreateFor(layout);
            game.Buildings.Add(r);
            return new BuildingSession(data, layout, r, game);
        }

        [Test]
        public void Noise_InForce_VentAboveMaxPaysZero_NotInForce_NoMaximum()
        {
            foreach (bool inForce in new[] { true, false })
            {
                BuildingSession s = WithCodes("test-house", inForce ? new[] { "noise" } : new[] { "tap-pressure" });
                Assert.IsTrue(s.Execute(new SetIntakeEquipmentCommand("air-intake", "small-axial-fan")));
                Assert.IsTrue(GameTestUtil.Draw(s, GameTestUtil.Run((0, 3), (3, 3), (3, 5)), 160));
                s.SolveNow();
                OutletStatus? strong = s.DesignEvaluation.Outlets.Find(o => o.Connected);
                Assert.IsNotNull(strong);
                Assert.Greater(strong!.Delivery, strong.Requirement.Ideal);
                if (inForce) Assert.AreEqual(OutletZone.AboveCodeMax, strong.Zone);
                else
                {
                    Assert.AreEqual(0, strong.Requirement.Max, "no code max without the noise code");
                    Assert.AreNotEqual(OutletZone.AboveCodeMax, strong.Zone);
                }
            }
        }

        [Test]
        public void TapPressure_InForce_TapAboveMaxPaysZero_NotInForce_NoMaximum()
        {
            foreach (bool inForce in new[] { true, false })
            {
                BuildingSession s = WithCodes("tap-house", inForce ? new[] { "tap-pressure" } : new[] { "noise" });
                Assert.IsTrue(s.Execute(new SetIntakeEquipmentCommand("water", "small-booster-pump")));
                Assert.IsTrue(s.Execute(new PlaceRunCommand(RunOn(0, (9, 1), (9, 2), (7, 2), (7, 1)), 15, "copper", false, "water")));
                s.SolveNow();
                OutletStatus tap = s.DesignEvaluation.Find("tap-1")!;
                Assert.Greater(tap.Delivery, 4e5);
                Assert.AreEqual(inForce ? OutletZone.AboveCodeMax : OutletZone.Overdelivery, tap.Zone);
            }
        }

        [Test]
        public void NoWaterAboveElectrics_PipeOverTheElectricalRoom_Violates_AroundIt_DoesNot()
        {
            BuildingSession s = WithCodes("tap-house", "no-water-above-electrics");
            // The electrical room is (8–9, 7) on the ground floor.
            Assert.IsTrue(s.Execute(new PlaceRunCommand(RunOn(0, (9, 1), (9, 4)), 22, "pvc", false, "water")));
            Assert.IsEmpty(CodeChecker.Check(s.Data, s.Layout, s.State));
            Assert.IsTrue(s.Execute(new PlaceRunCommand(RunOn(0, (9, 4), (9, 5), (9, 7)), 22, "pvc", false, "water")));
            List<Violation> v = CodeChecker.Check(s.Data, s.Layout, s.State);
            Assert.AreEqual(1, v.Count);
            Assert.AreEqual("no-water-above-electrics", v[0].CodeId);
            Assert.IsTrue(v[0].Tiles.Contains(new TilePos(9, 7)));
            s.SolveNow();
            Assert.AreEqual(1, s.DesignEvaluation.BlockingViolations);
            Assert.IsFalse(s.DesignEvaluation.Online, "a layout violation keeps the building offline");
            // Without the code in force the same pipe is fine.
            BuildingSession t = WithCodes("tap-house", "noise");
            t.Execute(new PlaceRunCommand(RunOn(0, (9, 1), (9, 5), (9, 7)), 22, "pvc", false, "water"));
            Assert.IsEmpty(CodeChecker.Check(t.Data, t.Layout, t.State));
        }

        [Test]
        public void NoWaterAboveElectrics_PipeOverThePanel_Violates()
        {
            BuildingSession s = WithCodes("tap-house", "no-water-above-electrics");
            // The panel sits at the power connection (5, 0); route the kitchen pipe across it.
            Assert.IsTrue(s.Execute(new PlaceRunCommand(RunOn(0, (9, 1), (9, 0), (4, 0)), 22, "pvc", false, "water")));
            List<Violation> v = CodeChecker.Check(s.Data, s.Layout, s.State);
            Assert.AreEqual(1, v.Count);
            StringAssert.Contains("panel", v[0].Reason);
        }

        [Test]
        public void FireDampers_DuctAcrossAFireWall_NeedsADamper()
        {
            var data = GameTestUtil.Data;
            BuildingLayoutDef def = JsonConvert.DeserializeObject<BuildingLayoutDef>(JsonConvert.SerializeObject(data.HandmadeBuilding("water-house")))!;
            def.Tier = 3;
            def.Codes = new List<string> { "fire-dampers" };
            for (int y = 0; y < 5; y++) def.FireWalls.Add(new DoorDef { X = 5, Y = y, Side = "east", Floor = 0 });
            var layout = BuildingLayout.From(data, def);
            var game = new GameState { Wallet = 1e6 };
            BuildingRecord r = BuildingRecord.CreateFor(layout);
            game.Buildings.Add(r);
            var s = new BuildingSession(data, layout, r, game);
            // Living (x < 6) into the kitchen through the fire wall (core-drilled at tier 3).
            Assert.IsTrue(s.Execute(new PlaceRunCommand(RunOn(0, (0, 2), (0, 1), (8, 1)), 125, "galvanized-duct", false)));
            List<Violation> v = CodeChecker.Check(data, layout, s.State);
            Assert.AreEqual(1, v.Count);
            Assert.AreEqual("fire-dampers", v[0].CodeId);
            Assert.IsTrue(s.Execute(new PlaceInlineCommand(new TilePos(6, 1), "fire-damper")));
            Assert.IsEmpty(CodeChecker.Check(data, layout, s.State));
        }
    }
}
