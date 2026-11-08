using System.Collections.Generic;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.Commands;
using HeadLoss.Game.Economy;
using HeadLoss.Game.Save;
using HeadLoss.Game.Session;
using Newtonsoft.Json;
using NUnit.Framework;
using static HeadLoss.Game.Tests.GameTestUtil;

namespace HeadLoss.Game.Tests
{
    /// <summary>The wall cavity (<c>04-buildings.md</c> §Layers, D170): small pipes along walls, outside the ceiling's capacity, tier 4.</summary>
    public class WallCavityTests
    {
        /// <summary>The tap house (kitchen x 6–9, y 0–4; water connection at (9, 1) on the east wall) at a tier.</summary>
        static BuildingSession House(int tier)
        {
            BuildingLayoutDef def = JsonConvert.DeserializeObject<BuildingLayoutDef>(JsonConvert.SerializeObject(Data.HandmadeBuilding("tap-house")))!;
            def.Tier = tier;
            var layout = BuildingLayout.From(Data, def);
            var game = new GameState { Wallet = 1e7 };
            BuildingRecord r = BuildingRecord.CreateFor(layout);
            r.EverOnline = true;
            game.Buildings.Add(r);
            return new BuildingSession(Data, layout, r, game, budgetCap: 1e9);
        }

        static List<TilePos> AlongSouthWall => Run((9, 1), (9, 0), (7, 0));

        [Test]
        public void Rules_SmallPipesAlongWalls_FromTier4()
        {
            BuildingSession s = House(4);
            Assert.AreEqual(DrawError.TooBigForCavity, s.ValidateRun("water", AlongSouthWall, 28, "copper", false, true), "22 mm at most");
            Assert.AreEqual(DrawError.NotAlongWall, s.ValidateRun("water", Run((9, 1), (9, 2), (7, 2)), 15, "copper", false, true), "(8, 2) is mid-room");
            Assert.AreEqual(DrawError.None, s.ValidateRun("water", AlongSouthWall, 15, "copper", false, true));
            Assert.AreEqual(DrawError.CavityLocked, House(3).ValidateRun("water", AlongSouthWall, 15, "copper", false, true));
            Assert.IsTrue(DuctEditor.AlongWall(s.Layout, new TilePos(9, 3)), "the building's edge is a wall");
            Assert.IsTrue(DuctEditor.AlongWall(s.Layout, new TilePos(6, 2)), "so is the wall to the next room");
            Assert.IsFalse(DuctEditor.AlongWall(s.Layout, new TilePos(8, 2)));
        }

        [Test]
        public void UsesNoCeilingCapacity_AndHoldsOnePipeATile()
        {
            BuildingSession s = House(4);
            Assert.IsTrue(s.Execute(new PlaceRunCommand(AlongSouthWall, 15, "copper", false, "water", true)));
            var usage = new Dictionary<TilePos, int>();
            DuctEditor.AddUsage(s.State.Water, s.CapacityUnits, usage);
            foreach (TilePos t in AlongSouthWall) Assert.IsFalse(usage.ContainsKey(t), $"{t}: the cavity is not the ceiling");
            foreach (DuctSegment seg in s.State.Water.Segments) Assert.IsTrue(seg.Cavity);

            // Another network's cavity pipe through the same wall tiles: the cavity there is taken.
            DuctNetworkState other = s.State.Net("hot-water");
            Assert.AreEqual(DrawError.CavityFull, DuctEditor.ValidateCavity(s.State, other, s.Layout, Run((8, 0), (6, 0))));
            Assert.AreEqual(DrawError.None, DuctEditor.ValidateCavity(s.State, other, s.Layout, Run((6, 1), (6, 3))));
        }

        [Test]
        public void FeedsATap_JoinedToACeilingPipe()
        {
            BuildingSession s = House(4);
            Assert.IsTrue(s.Execute(new SetIntakeEquipmentCommand("water", "small-booster-pump")));
            Assert.IsTrue(s.Execute(new PlaceRunCommand(AlongSouthWall, 15, "copper", false, "water", true)));
            Assert.IsTrue(s.Execute(new PlaceRunCommand(Run((7, 0), (7, 1)), 15, "copper", false, "water")), "a ceiling pipe on from the cavity's end");
            Assert.IsTrue(s.Commit(), s.CheckCommit().Reason);
            s.SolveNow();
            OutletStatus tap = s.CommittedEvaluation.Find("tap-1")!;
            Assert.IsTrue(tap.Connected);
            Assert.Greater(tap.Delivery, 1e5, "the tap gets water through the wall");
            Assert.IsTrue(s.Committed.Water.Segments.Exists(x => x.Cavity) && s.Committed.Water.Segments.Exists(x => !x.Cavity));
        }

        [Test]
        public void SurvivesASaveAndTellsTheLayersApart()
        {
            BuildingSession s = House(4);
            Assert.IsTrue(s.Execute(new PlaceRunCommand(AlongSouthWall, 15, "copper", false, "water", true)));
            string cavity = s.State.Fingerprint();
            var g = new GameState();
            g.Buildings.Add(s.Record);
            GameState copy = SaveSerializer.Copy(g);
            Assert.IsTrue(copy.Buildings[0].Design.Water.Segments.TrueForAll(x => x.Cavity));
            foreach (DuctSegment seg in s.State.Water.Segments) seg.Cavity = false;
            Assert.AreNotEqual(cavity, s.State.Fingerprint(), "a cavity pipe is not the same build as a ceiling pipe");
        }
    }
}
