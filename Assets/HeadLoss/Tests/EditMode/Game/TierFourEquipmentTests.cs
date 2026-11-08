using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.City;
using HeadLoss.Game.Commands;
using HeadLoss.Game.Generation;
using HeadLoss.Game.Session;
using NUnit.Framework;
using static HeadLoss.Game.Tests.GameTestUtil;

namespace HeadLoss.Game.Tests
{
    /// <summary>Tier 4 equipment (<c>07-equipment.md</c>, Phase 6 task 3).</summary>
    public class TierFourEquipmentTests
    {
        /// <summary>A generated tier 4 layout (the tier opened for the call), as a session with everything unlocked.</summary>
        static BuildingSession Generated(string type, ulong seed)
        {
            BuildingTierDef tier = Data.BuildingTier(4);
            bool was = tier.Available;
            tier.Available = true;
            try
            {
                BuildingLayoutDef def = BuildingGenerator.Generate(Data, new BuildingRequest("plot-1", type, seed, "south"), 0)!;
                Assert.IsNotNull(def);
                var layout = BuildingLayout.From(Data, def);
                var game = new GameState { Wallet = 1e8 };
                BuildingRecord r = BuildingRecord.CreateFor(layout);
                game.Buildings.Add(r);
                return new BuildingSession(Data, layout, r, game, budgetCap: 1e9);
            }
            finally { tier.Available = was; }
        }

        [Test]
        public void TransformerRoom_FitsAtEveryWarehousePowerConnection_250kW()
        {
            EquipmentDef t = Data.EquipmentItem("transformer-room");
            Assert.AreEqual(250, t.PanelLimitKW);
            Assert.AreEqual(3, t.Footprint.Width);
            Assert.AreEqual(3, t.Footprint.Height);
            for (ulong seed = 1; seed <= 10; seed++)
            {
                BuildingSession s = Generated("warehouse", seed);
                Assert.IsTrue(s.Execute(new SetIntakeEquipmentCommand(FloorPlanGenerator.PowerId, "transformer-room")), $"seed {seed}: the generator keeps 3 × 3 m at the power connection");
                Assert.IsTrue(s.Commit(), s.CheckCommit().Reason);
                Assert.AreEqual(250, s.PanelLimitKW, 1e-9);
            }
        }

        [Test]
        public void TransformerRoom_IndustrialBuildingsOnly()
        {
            BuildingSession office = Generated("office-building", 1);
            Assert.IsFalse(office.Execute(new SetIntakeEquipmentCommand(FloorPlanGenerator.PowerId, "transformer-room")));
            Assert.IsTrue(office.Execute(new SetIntakeEquipmentCommand(FloorPlanGenerator.PowerId, "panel-large")));
            CollectionAssert.AreEquivalent(new[] { "warehouse", "factory", "lab-building" }, Data.EquipmentItem("transformer-room").BuildingTypes);
        }
    }
}
