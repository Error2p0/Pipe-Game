using System;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.Commands;
using HeadLoss.Game.Economy;
using HeadLoss.Game.Session;
using NUnit.Framework;
using static HeadLoss.Game.Tests.GameTestUtil;

namespace HeadLoss.Game.Tests
{
    public class PricingTests
    {
        [Test]
        public void PerMetre_ScalesWithDiameterAndMaterial()
        {
            Assert.AreEqual(1.0, Pricing.PerMetre(Data, "galvanized-duct", 100), 1e-12);
            Assert.AreEqual(Math.Pow(1.25, 1.3), Pricing.PerMetre(Data, "galvanized-duct", 125), 1e-12);
            Assert.AreEqual(0.5, Pricing.PerMetre(Data, "flex-duct", 100), 1e-12);
            Assert.AreEqual(1.6, Pricing.PerMetre(Data, "spiral-duct", 100), 1e-12);
        }

        [Test]
        public void Fitting_ScalesAboveReferenceOnly()
        {
            Assert.AreEqual(1.0, Pricing.Fitting(Data, "elbow-90-sharp", 80, true), 1e-12);
            Assert.AreEqual(1.0, Pricing.Fitting(Data, "elbow-90-sharp", 100, true), 1e-12);
            Assert.AreEqual(2.0, Pricing.Fitting(Data, "elbow-90-sharp", 200, true), 1e-12);
            Assert.AreEqual(3.75, Pricing.Fitting(Data, "elbow-90-long-radius", 125, true), 1e-12);
        }

        [Test]
        public void Diff_NewRun_ChargesDuctsAndFittingsInFull()
        {
            BuildingSession s = Open("test-house");
            Draw(s, Run((0, 3), (3, 3), (3, 5)), 100);
            CostSummary c = s.PendingCost;
            Assert.AreEqual(5.0, c.DuctCharge, 1e-9, "5 m of 100 mm galvanized at $1/m");
            Assert.AreEqual(1.0, c.FittingCharge, 1e-9, "one sharp elbow");
            Assert.AreEqual(0.0, c.Refund);
            Assert.AreEqual(6.0, c.Net, 1e-9);
        }

        [Test]
        public void Commit_PaysAndMakesDesignTheCommittedState()
        {
            BuildingSession s = Open("test-house");
            Draw(s, Run((0, 3), (3, 3), (3, 5)), 100);
            double before = s.Game.Wallet;
            int history = s.Commands.History.Count;
            Assert.IsTrue(s.Execute(new CommitDesignCommand()));
            Assert.AreEqual(before - 6.0, s.Game.Wallet, 1e-9);
            Assert.AreEqual(6.0, s.Record.SpentTotal, 1e-9);
            Assert.IsFalse(s.HasPendingDesign);
            Assert.AreEqual(s.State.Fingerprint(), s.Committed.Fingerprint());
            Assert.AreEqual(history, s.Commands.History.Count, "a commit is not an undo step");
        }

        [Test]
        public void Delete_CommittedRun_RefundsHalf()
        {
            BuildingSession s = Open("test-house");
            Draw(s, Run((0, 3), (3, 3), (3, 5)), 100);
            s.Commit();
            s.Execute(new DeleteSegmentCommand(new System.Collections.Generic.List<int>(s.State.Air.Segments.ConvertAll(x => x.Id))));
            Assert.AreEqual(0.0, s.PendingCost.Charge, 1e-9);
            Assert.AreEqual(3.0, s.PendingCost.Refund, 1e-9);
            double before = s.Game.Wallet;
            Assert.IsTrue(s.Commit());
            Assert.AreEqual(before + 3.0, s.Game.Wallet, 1e-9);
            Assert.AreEqual(3.0, s.Record.SpentTotal, 1e-9);
        }

        [Test]
        public void Upgrade_SizeInPlace_CostsOnlyTheDifference()
        {
            BuildingSession s = Open("test-house");
            Draw(s, Run((0, 3), (3, 3)), 100);
            s.Commit();
            s.Execute(new SetSegmentSizeCommand(s.State.Air.Segments[0].Id, 125));
            double expected = 3 * (Pricing.PerMetre(Data, "galvanized-duct", 125) - 1.0);
            Assert.AreEqual(expected, s.PendingCost.Charge, 1e-9);
            Assert.AreEqual(0.0, s.PendingCost.Refund, 1e-9);
        }

        [Test]
        public void Downgrade_SizeInPlace_RefundsHalfTheDifference()
        {
            BuildingSession s = Open("test-house");
            Draw(s, Run((0, 3), (3, 3)), 125);
            s.Commit();
            s.Execute(new SetSegmentSizeCommand(s.State.Air.Segments[0].Id, 100));
            double expected = 0.5 * 3 * (Pricing.PerMetre(Data, "galvanized-duct", 125) - 1.0);
            Assert.AreEqual(0.0, s.PendingCost.Charge, 1e-9);
            Assert.AreEqual(expected, s.PendingCost.Refund, 1e-9);
        }

        [Test]
        public void Upgrade_ElbowToLongRadius_CostsTheDifference()
        {
            BuildingSession s = Open("test-house");
            Draw(s, Run((0, 3), (3, 3), (3, 5)), 100);
            s.Commit();
            s.Execute(new UpgradeAllBendsCommand());
            Assert.AreEqual(2.0, s.PendingCost.Net, 1e-9, "long-radius $3 − sharp $1");
        }

        [Test]
        public void SwapPreplacedCrankForFan_ChargesFanWithoutRefund()
        {
            BuildingSession s = Open("test-house");
            s.Execute(new SetIntakeEquipmentCommand("air-intake", "small-axial-fan"));
            Assert.AreEqual(40.0, s.PendingCost.Charge, 1e-9);
            Assert.AreEqual(0.0, s.PendingCost.Refund, 1e-9, "the pre-placed crank cost nothing");
        }

        [Test]
        public void MoveDuct_IsRemovalPlusPurchase()
        {
            BuildingSession s = Open("test-house");
            Draw(s, Run((0, 3), (3, 3)), 100);
            s.Commit();
            s.Execute(new DeleteSegmentCommand(s.State.Air.Segments[0].Id));
            Draw(s, Run((0, 3), (1, 3), (1, 1)), 100);
            // The first metre is the same tile edge as before; two new metres and a new elbow are bought, two old metres sold.
            Assert.AreEqual(2.0 + 1.0, s.PendingCost.Charge, 1e-9);
            Assert.AreEqual(2 * 0.5, s.PendingCost.Refund, 1e-9);
        }
    }

    public class CommitRuleTests
    {
        [Test]
        public void BudgetCap_BlocksCommitBeforeFirstOnline()
        {
            BuildingSession s = Open("test-house");
            Assert.AreEqual(Data.Balancing.Building.BudgetCapFactor * ReferenceLayout.Cost(Data, s.Layout), s.BudgetCap, 1e-9);
            s.Game.Wallet = 1e6;
            s.Execute(new SetIntakeEquipmentCommand("air-intake", "small-axial-fan"));
            Draw(s, Run((0, 3), (9, 3), (9, 6), (0, 6), (0, 4)), 400);
            CommitCheck check = s.CheckCommit();
            Assert.IsFalse(check.Allowed);
            StringAssert.StartsWith("Over budget cap by $", check.Reason);
            Assert.IsFalse(s.Commit());

            s.Record.EverOnline = true;
            Assert.IsTrue(s.CheckCommit().Allowed, "no cap once the building has been online");
        }

        [Test]
        public void NotEnoughMoney_SaysHowMuchMore()
        {
            BuildingSession s = Open("test-house");
            s.Game.Wallet = 2.0;
            Draw(s, Run((0, 3), (3, 3), (3, 5)), 100);
            CommitCheck check = s.CheckCommit();
            Assert.IsFalse(check.Allowed);
            Assert.AreEqual("Need $4.00 more", check.Reason);
        }

        [Test]
        public void Debt_BlocksPurchasesButAllowsRefunds()
        {
            BuildingSession s = Open("test-house");
            Draw(s, Run((0, 3), (3, 3), (3, 5)), 100);
            s.Commit();
            s.Game.ApplyIncome(-1000);
            Assert.Greater(s.Game.Debt, 0.0);
            Draw(s, Run((3, 4), (5, 4)), 100);
            Assert.IsFalse(s.CheckCommit().Allowed);
            Assert.AreEqual("Purchases wait until the debt is repaid", s.CheckCommit().Reason);
            s.Execute(new DiscardDesignCommand());
            s.Execute(new DeleteSegmentCommand(s.State.Air.Segments[0].Id));
            Assert.IsTrue(s.CheckCommit().Allowed, "selling parts is always allowed");
            double debt = s.Game.Debt;
            s.Commit();
            Assert.Less(s.Game.Debt, debt, "the refund repays debt first");
        }

        [Test]
        public void NothingPending_CannotCommit()
        {
            BuildingSession s = Open("test-house");
            Assert.IsFalse(s.HasPendingDesign);
            Assert.AreEqual("Nothing to commit", s.CheckCommit().Reason);
        }

        [Test]
        public void SameLayoutReachedDifferently_IsNotPending()
        {
            BuildingSession s = Open("test-house");
            Draw(s, Run((0, 3), (3, 3)), 100);
            s.Commit();
            s.Execute(new DeleteSegmentCommand(s.State.Air.Segments[0].Id));
            Draw(s, Run((0, 3), (3, 3)), 100);
            Assert.IsFalse(s.HasPendingDesign, "ids differ but the layout is the same");
        }
    }

    public class UndoTests
    {
        [Test]
        public void UndoRedo_DrawRun()
        {
            BuildingSession s = Open("test-house");
            string empty = s.State.Fingerprint();
            Draw(s, Run((0, 3), (3, 3), (3, 5)));
            string drawn = s.State.Fingerprint();
            Assert.IsTrue(s.Undo());
            Assert.AreEqual(empty, s.State.Fingerprint());
            Assert.IsTrue(s.Redo());
            Assert.AreEqual(drawn, s.State.Fingerprint());
        }

        [Test]
        public void Undo_UnlimitedDepth()
        {
            BuildingSession s = Open("test-house");
            string empty = s.State.Fingerprint();
            Draw(s, Run((0, 3), (5, 3)));
            for (int x = 1; x < 5; x++) Draw(s, Run((x, 3), (x, 5)));
            for (int i = 0; i < 4; i++) s.Execute(new SetSegmentSizeCommand(s.State.Air.Segments[0].Id, i % 2 == 0 ? 160 : 100));
            while (s.Undo()) { }
            Assert.AreEqual(empty, s.State.Fingerprint());
        }

        [Test]
        public void NewCommand_ClearsRedo()
        {
            BuildingSession s = Open("test-house");
            Draw(s, Run((0, 3), (3, 3)));
            s.Undo();
            Assert.IsTrue(s.Commands.CanRedo);
            Draw(s, Run((0, 3), (2, 3)));
            Assert.IsFalse(s.Commands.CanRedo);
        }

        [Test]
        public void UndoAfterCommit_LeavesCommittedStateAndMakesAPendingChange()
        {
            BuildingSession s = Open("test-house");
            Draw(s, Run((0, 3), (3, 3), (3, 5)), 100);
            s.Commit();
            string committed = s.Committed.Fingerprint();
            s.Undo();
            Assert.AreEqual(committed, s.Committed.Fingerprint(), "money already changed hands");
            Assert.IsTrue(s.HasPendingDesign);
            Assert.AreEqual(3.0, s.PendingCost.Refund, 1e-9, "undoing the run now means selling it");
        }

        [Test]
        public void Discard_IsUndoable()
        {
            BuildingSession s = Open("test-house");
            Draw(s, Run((0, 3), (3, 3)));
            string drawn = s.State.Fingerprint();
            s.Execute(new DiscardDesignCommand());
            Assert.IsFalse(s.HasPendingDesign);
            s.Undo();
            Assert.AreEqual(drawn, s.State.Fingerprint());
        }

        [Test]
        public void Leave_ClearsHistory()
        {
            var ctx = new GameContext(Data, GameContext.NewGame(Data));
            BuildingSession s = ctx.Open("test-house");
            Draw(s, Run((0, 3), (3, 3)));
            ctx.Open("small-house");
            Assert.IsFalse(s.Commands.CanUndo);
        }
    }

    public class CrankTests
    {
        [Test]
        public void Click_AddsSpeedPerClick_CappedAtOne()
        {
            BuildingSession s = Open("test-house");
            s.Crank();
            Assert.AreEqual(Data.Balancing.Crank.SpeedPerClick, s.CrankSpeed, 1e-12);
            for (int i = 0; i < 20; i++) s.Crank();
            Assert.AreEqual(1.0, s.CrankSpeed);
        }

        [Test]
        public void Speed_DecaysWithTimeConstant()
        {
            BuildingSession s = Open("test-house");
            for (int i = 0; i < 20; i++) s.Crank();
            double tau = Data.Balancing.Crank.DecaySeconds;
            for (int i = 0; i < 100; i++) s.Tick(tau / 100);
            Assert.AreEqual(Math.Exp(-1), s.CrankSpeed, 1e-9);
        }

        [Test]
        public void Hold_ReachesFullSpeed()
        {
            BuildingSession s = Open("test-house");
            s.CrankHeld = true;
            for (int i = 0; i < 300; i++) s.Tick(0.02);
            Assert.AreEqual(1.0, s.CrankSpeed, 1e-9);
        }

        [Test]
        public void NoCrank_ClickDoesNothing()
        {
            BuildingSession s = Open("small-house");
            s.Crank();
            Assert.AreEqual(0.0, s.CrankSpeed);
        }

        [Test]
        public void Cranking_StarterReference_BringsItOnline()
        {
            BuildingSession s = Open("test-house");
            s.Record.Design = ReferenceLayout.Build(Data, s.Layout);
            s.Record.Committed = s.Record.Design.Clone();
            s.MarkDirty();
            s.SolveNow();
            Assert.IsFalse(s.CommittedEvaluation.Online, "a crank at rest moves no air");
            s.CrankHeld = true;
            for (int i = 0; i < 300; i++) s.Tick(0.02);
            s.SolveNow();  // collect a solve that may still be on a worker thread
            Assert.IsTrue(s.CommittedEvaluation.Online);
            Assert.AreEqual(0.0, s.CommittedEvaluation.Electricity, "the crank uses no electricity");
            Assert.Greater(s.CommittedEvaluation.Gross, 0.0);
        }
    }

    public class ReferenceLayoutTests
    {
        static readonly string[] Buildings = { "test-house", "small-house" };

        /// <summary>The solvability check: the reference meets every minimum and no code maximum with the reference equipment.</summary>
        [TestCaseSource(nameof(Buildings))]
        public void Reference_MeetsEveryMinimumWithReferenceEquipment(string id)
        {
            BuildingSession s = Open(id);
            s.Record.Design = ReferenceLayout.Build(Data, s.Layout);
            s.Record.Committed = s.Record.Design.Clone();
            s.MarkDirty();
            if (s.Layout.ReferenceCrankSpeed > 0)
            {
                while (s.CrankSpeed < s.Layout.ReferenceCrankSpeed) s.Crank();
                // Back down to exactly the reference speed (clicks come in steps).
                while (s.CrankSpeed > s.Layout.ReferenceCrankSpeed + 1e-6) s.Tick(0.001);
            }
            s.SolveNow();
            BuildingEvaluation ev = s.CommittedEvaluation;
            Assert.IsTrue(ev.Online, $"{id}: every outlet meets its minimum");
            Assert.AreEqual(0, ev.AboveCodeMax, $"{id}: no outlet above its code maximum");
        }

        [TestCaseSource(nameof(Buildings))]
        public void Reference_FitsInsideTheBudgetCap(string id)
        {
            BuildingLayout layout = GameContext.LayoutFor(Data, id);
            Assert.Less(ReferenceLayout.Cost(Data, layout), ReferenceLayout.BudgetCap(Data, layout));
        }

        [Test]
        public void SmallHouse_DoorsLetDuctsThroughWalls()
        {
            BuildingLayout l = GameContext.LayoutFor(Data, "small-house");
            Assert.IsTrue(l.CanPass(new TilePos(2, 3), new TilePos(2, 4)), "living → hall door");
            Assert.IsFalse(l.CanPass(new TilePos(3, 3), new TilePos(3, 4)), "wall");
            Assert.IsTrue(l.CanPass(new TilePos(5, 1), new TilePos(6, 1)), "living → kitchen door");
            Assert.IsTrue(l.CanPass(new TilePos(1, 1), new TilePos(1, 2)), "inside a room");
        }

        [Test]
        public void SmallHouse_DrawingThroughAWall_IsRejected()
        {
            BuildingSession s = Open("small-house");
            Assert.AreEqual(DrawError.CrossesWall, DuctEditor.Validate(s.State.Air, s.Layout, Run((3, 0), (3, 5)), 4));
        }

        [Test]
        public void NewGame_StartsInTheStarterWithTheStartingWallet()
        {
            GameState g = GameContext.NewGame(Data);
            Assert.AreEqual(Data.Balancing.Economy.StartingWallet, g.Wallet);
            Assert.AreEqual(Data.Game.StarterBuildings.Count, g.Buildings.Count);
            Assert.AreEqual("test-house", g.CurrentBuildingId);
            var ctx = new GameContext(Data, g);
            Assert.IsNotNull(ctx.Building);
            Assert.IsTrue(ctx.Building!.HasCrank);
        }
    }
}
