using System.Collections.Generic;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.Commands;
using HeadLoss.Game.Session;
using Newtonsoft.Json;
using NUnit.Framework;
using static HeadLoss.Game.Tests.GameTestUtil;

namespace HeadLoss.Game.Tests
{
    /// <summary>The gas regulator's setpoint: fixed 25 mbar, adjustable 18–30 mbar from tier 4 (<c>07-equipment.md</c> §Gas supply).</summary>
    public class GasRegulatorSetpointTests
    {
        const string Gas = BuildingState.GasFluid;

        static List<TilePos> RunOn(int floor, params (int x, int y)[] points)
        {
            var list = new List<TilePos>();
            foreach (TilePos t in Run(points)) list.Add(t.OnFloor(floor));
            return list;
        }

        /// <summary>The gas house (as in <c>GasTests</c>) at a tier, piped, with the regulator, committed and solved.</summary>
        static BuildingSession Piped(int tier)
        {
            BuildingLayoutDef def = JsonConvert.DeserializeObject<BuildingLayoutDef>(JsonConvert.SerializeObject(Data.HandmadeBuilding("gas-house")))!;
            def.Tier = tier;
            var layout = BuildingLayout.From(Data, def);
            var game = new GameState { Wallet = 1e7 };
            BuildingRecord r = BuildingRecord.CreateFor(layout);
            r.EverOnline = true;
            game.Buildings.Add(r);
            var s = new BuildingSession(Data, layout, r, game, budgetCap: 1e9);
            Assert.IsTrue(s.Execute(new PlaceRunCommand(RunOn(0, (9, 1), (9, 5)), 28, "copper", false, Gas)));
            Assert.IsTrue(s.Execute(new PlaceRunCommand(RunOn(0, (9, 2), (7, 2), (7, 1)), 22, "copper", false, Gas)));
            Assert.IsTrue(s.Execute(new PlaceRunCommand(new List<TilePos> { new TilePos(9, 5, 0), new TilePos(9, 5, 1) }, 28, "copper", false, Gas)));
            Assert.IsTrue(s.Execute(new PlaceRunCommand(RunOn(1, (9, 5), (9, 4), (7, 4), (7, 1)), 22, "copper", false, Gas)));
            Assert.IsTrue(s.Execute(new SetIntakeEquipmentCommand("gas", "gas-regulator")));
            Assert.IsTrue(s.Commit(), s.CheckCommit().Reason);
            s.SolveNow();
            return s;
        }

        static double StoveDelivery(BuildingSession s) => s.CommittedEvaluation.Find("stove-1")!.Delivery;

        [Test]
        public void Tier3_TheSetpointIsFixed()
        {
            BuildingSession s = Piped(3);
            Assert.IsFalse(s.Execute(new SetRegulatorSetpointCommand("gas", 3000)));
            Assert.AreEqual(2500, StoveDelivery(s), 60);
        }

        [Test]
        public void Tier4_TheSetpointMovesTheStovePressure_ClampedAndSnappedTo1Mbar()
        {
            BuildingSession s = Piped(4);
            double at25 = StoveDelivery(s);
            Assert.AreEqual(2500, at25, 60);

            Assert.IsTrue(s.Execute(new SetRegulatorSetpointCommand("gas", 2964)));
            Assert.AreEqual(3000, s.Committed.EquipmentAt("gas")!.SetpointPa, 1e-9, "snapped to 1 mbar, applied to what is built at once");
            Assert.IsFalse(s.HasPendingDesign, "a setting, not a design change");
            s.SolveNow();
            Assert.AreEqual(at25 + 500, StoveDelivery(s), 30);

            Assert.IsTrue(s.Execute(new SetRegulatorSetpointCommand("gas", 900)));
            Assert.AreEqual(1800, s.State.EquipmentAt("gas")!.SetpointPa, 1e-9, "clamped to 18 mbar");
            s.SolveNow();
            Assert.Less(StoveDelivery(s), 1800);

            // Moves in a row merge into one undo step (a slider drag), so undo goes back to the default.
            Assert.IsTrue(s.Undo());
            Assert.AreEqual(0, s.Committed.EquipmentAt("gas")!.SetpointPa, 1e-9);
            s.SolveNow();
            Assert.AreEqual(at25, StoveDelivery(s), 1);
        }
    }
}
