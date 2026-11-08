using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.City;
using HeadLoss.Game.Commands;
using HeadLoss.Game.Economy;
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
    
        // ---------- smart building controller (07) ----------

        /// <summary>The small house as its reference layout with every damper half shut: online, but throttled harder than it needs.</summary>
        static BuildingSession Throttled()
        {
            var ctx = new GameContext(Data, GameContext.NewFixtureGame(Data, "small-house"));
            BuildingSession s = ctx.Building!;
            s.Record.Design = ReferenceLayout.Build(Data, s.Layout);
            s.MarkDirty();
            s.Game.Wallet = 1e6;
            s.Record.EverOnline = true;
            Assert.IsTrue(s.Commit());
            s.SolveNow();
            foreach (DuctNode n in s.Committed.Air.Nodes)
                if (n.Kind == DuctNodeKind.Inline && n.PartId == "damper") n.Opening = System.Math.Max(0.3, n.Opening * 0.7);
            s.Record.Design = s.Committed.Clone();
            s.MarkDirty();
            s.SolveNow();
            return s;
        }

        [Test]
        public void SmartController_NeverWorse_KeepsTheLimits_At40SolvesMost()
        {
            BuildingSession s = Throttled();
            Assume.That(s.CommittedEvaluation.Online, "the throttled house still meets its minimums");
            Assert.IsTrue(s.BuySmartController());
            double before = s.SteadyState(20).Net;
            MetaUpgradeDef def = s.ControllerDef!;
            System.Collections.Generic.List<ControllerChange> changes = SmartController.Run(Data, s.Committed, def, s.CaptureEvaluator(), 1, 1, out int solves);
            Assert.LessOrEqual(solves, def.MaxSolves);
            Assert.IsNotEmpty(changes, "dampers shut harder than needed get opened");
            s.RunControllerNow();
            s.SolveNow();
            BuildingEvaluation after = s.SteadyState(20);
            Assert.GreaterOrEqual(after.Net, before - 1e-9, "a change is kept only when it raises net income");
            Assert.AreEqual(0, after.BelowMinimum);
            Assert.AreEqual(0, after.AboveCodeMax);
            Assert.IsNotEmpty(s.Record.ControllerLog);
            Assert.IsFalse(s.HasPendingDesign, "settings, not a design change");
        }

        [Test]
        public void SmartController_RunsOnAWorkerEvery30Seconds()
        {
            BuildingSession s = Throttled();
            Assume.That(s.CommittedEvaluation.Online);
            Assert.IsTrue(s.BuySmartController());
            int logged = s.Record.ControllerLog.Count;
            Assert.AreEqual(0, logged);
            for (int i = 0; i < 4000 && s.Record.ControllerLog.Count == logged; i++)
            {
                s.Tick(0.05);
                if (i % 50 == 0) System.Threading.Thread.Sleep(1);
            }
            Assert.Greater(s.Record.ControllerLog.Count, logged, "a run started and its changes landed");
            Assert.Greater(s.CommittedEvaluation.PowerW, 0);
        }

        [Test]
        public void SmartController_CostsItsPriceOnce_AndDraws20W()
        {
            BuildingSession s = Throttled();
            double wallet = s.Game.Wallet, power = s.CommittedEvaluation.PowerW;
            Assert.IsTrue(s.BuySmartController());
            Assert.AreEqual(wallet - 3000, s.Game.Wallet, 1e-9);
            Assert.IsFalse(s.BuySmartController(), "once per building");
            s.SolveNow();
            Assert.AreEqual(power + 20, s.CommittedEvaluation.PowerW, 0.5);
        }
}
}
