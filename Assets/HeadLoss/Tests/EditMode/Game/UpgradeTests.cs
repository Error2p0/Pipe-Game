using System;
using System.Collections.Generic;
using HeadLoss.Core;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.City;
using HeadLoss.Game.Commands;
using HeadLoss.Game.Economy;
using HeadLoss.Game.Generation;
using HeadLoss.Game.Networks;
using HeadLoss.Game.Progression;
using HeadLoss.Game.Session;
using HeadLoss.Sim.Network;
using NUnit.Framework;
using static HeadLoss.Game.Tests.GameTestUtil;

namespace HeadLoss.Game.Tests
{
    /// <summary>Phase 3 network upgrades and equipment: sealing, materials, smooth tees, variable-speed drives.</summary>
    public class UpgradeTests
    {
        /// <summary>Test house with the small fan, a trunk to vent-2 and a branch to vent-1.</summary>
        static BuildingSession TwoBranches()
        {
            BuildingSession s = TestHouseWithFan();
            Draw(s, Run((0, 3), (8, 3), (8, 5)));
            Draw(s, Run((3, 3), (3, 5)));
            s.SolveNow();
            return s;
        }

        static double VentTotal(BuildingSession s)
        {
            double q = 0;
            foreach (var kv in s.Current!.Compiled.OutletLink) q += s.Current.Result.Flow[kv.Value];
            return q;
        }

        static double LeakTotal(BuildingSession s)
        {
            double q = 0;
            foreach (var kv in s.Current!.Compiled.SegmentLeaks) q += s.Current.Result.Flow[kv.Value.a] + s.Current.Result.Flow[kv.Value.b];
            return q;
        }

        [Test]
        public void Leakage_UnsealedDuctsLoseAir_AndMassIsConserved()
        {
            BuildingSession s = TwoBranches();
            double fan = s.Current!.Result.Flow[s.Current.Compiled.FanLink["air-intake"]];
            double leaks = LeakTotal(s), vents = VentTotal(s);
            Assert.Greater(leaks, 0, "unsealed ducts leak");
            Assert.Less(leaks, 0.2 * fan, "a few percent, not most of the air");
            Assert.AreEqual(fan, leaks + vents, 1e-9, "fan flow = vents + leaks (no open ends here)");
        }

        [Test]
        public void Sealing_CutsLeakageAndRaisesDelivery_CostsPerMetre()
        {
            BuildingSession s = TwoBranches();
            s.Game.Wallet = 1e6;
            s.Record.EverOnline = true;
            s.Commit();
            double leaks = LeakTotal(s), vents = VentTotal(s);
            var ids = new List<int>();
            foreach (DuctSegment seg in s.State.Air.Segments) ids.Add(seg.Id);
            int metres = 0;
            foreach (DuctSegment seg in s.State.Air.Segments) metres += s.State.Air.FindNode(seg.NodeA)!.Pos.ManhattanTo(s.State.Air.FindNode(seg.NodeB)!.Pos);
            Assert.IsTrue(s.Execute(new SetSegmentSpecCommand(ids, sealing: "sealed")));
            s.SolveNow();
            Assert.Less(LeakTotal(s), leaks * 0.5, "sealed class C_L is a quarter of unsealed");
            Assert.Greater(VentTotal(s), vents);
            Assert.AreEqual(0.3 * metres, s.PendingCost.Net, 1e-6, "+$0.30 per metre");
        }

        /// <summary>Q = C · Δp^n through a single leak between two fixed pressures.</summary>
        [Test]
        public void LeakLink_MatchesThePowerLaw()
        {
            var g = new NetworkGraph();
            int a = g.AddFixed(50), j = g.AddJunction(), b = g.AddFixed(0);
            g.AddPipe(a, j, 1, 0.5, 1e-5);
            int leak = g.AddLeak(j, b, 1e-4, 0.65);
            SolveResult r = new NetworkSolver(new SolverSettings()).Solve(g, new HeadLoss.Sim.Fluids.ConstantFluid(1.204, 1.813e-5));
            Assert.IsTrue(r.Converged);
            double dp = r.NodePressure[j];
            Assert.AreEqual(1e-4 * Math.Pow(dp, 0.65), r.Flow[leak], 1e-9);
            Assert.AreEqual(50, dp, 0.5, "a wide pipe loses almost nothing");
        }

        [Test]
        public void FlexDuct_IsRougher_SoLessAirReachesTheVents()
        {
            BuildingSession s = TwoBranches();
            double before = VentTotal(s);
            var ids = new List<int>();
            foreach (DuctSegment seg in s.State.Air.Segments) ids.Add(seg.Id);
            Assert.IsTrue(s.Execute(new SetSegmentSpecCommand(ids, materialId: "flex-duct")));
            s.SolveNow();
            Assert.Less(VentTotal(s), before);
            Assert.Less(Pricing.Value(Data, s.Layout, s.State), Pricing.Value(Data, s.Layout, s.Committed) + 100, "flex costs half per metre");
        }

        [Test]
        public void SmoothTee_UsesTeeSmoothK_AndPrice()
        {
            BuildingSession s = TwoBranches();
            DuctNode tee = s.State.Air.NodeAt(new TilePos(3, 3))!;
            Assert.IsTrue(DuctEditor.IsTee(s.State.Air, tee));
            double sharp = Pricing.Value(Data, s.Layout, s.State);
            Assert.IsTrue(s.Execute(new SetElbowCommand(tee.Id, true)));
            s.SolveNow();
            var ks = new List<double>();
            foreach (CompiledFitting f in s.Current!.Compiled.Fittings)
                if (f.NodeId == tee.Id && (f.Kind == FittingKind.TeeRun || f.Kind == FittingKind.TeeBranch)) ks.Add(f.K);
            CollectionAssert.AreEquivalent(new[] { 0.2, 0.6 }, ks);
            Assert.AreEqual(sharp + 5 * 1.25 - 2 * 1.25, Pricing.Value(Data, s.Layout, s.State), 1e-6, "smooth $5 vs sharp $2, × D/D_ref 125/100");
        }

        [Test]
        public void Vsd_AddsASpeedSlider_CostsThreePercentEfficiency_AndOnlyItsOwnPrice()
        {
            BuildingSession s = TwoBranches();
            s.Game.Wallet = 1e6;
            s.Record.EverOnline = true;
            s.Commit();
            s.SolveNow();
            double power = s.CommittedEvaluation.PowerW;
            Assert.IsFalse(s.Execute(new SetEquipmentSpeedCommand("air-intake", 0.5)), "no speed control without a drive");
            Assert.IsTrue(s.Execute(new SetVsdCommand("air-intake", true)));
            Assert.AreEqual(250, s.PendingCost.Net, 1e-9, "only the drive is charged");
            s.SolveNow();
            Assert.AreEqual(power / 0.97, s.DesignEvaluation.PowerW, power * 1e-6, "same speed, 3% less efficient");
            Assert.IsTrue(s.Execute(new SetEquipmentSpeedCommand("air-intake", 0.5)));
            Assert.AreEqual(1.0, s.Committed.EquipmentAt("air-intake")!.Speed, "the built fan has no drive yet");
            s.Commit();
            s.SolveNow();
            Assert.Less(s.CommittedEvaluation.PowerW, power * 0.25, "half speed: roughly an eighth of the power");
            Assert.IsTrue(s.Execute(new SetEquipmentSpeedCommand("air-intake", 0.70)));
            Assert.AreEqual(0.70, s.Committed.EquipmentAt("air-intake")!.Speed, 1e-9, "live on the built fan once the drive is built (D25)");
            s.Execute(new SetEquipmentSpeedCommand("air-intake", 0.80));
            s.Undo();
            Assert.AreEqual(0.5, s.State.EquipmentAt("air-intake")!.Speed, 1e-9, "one slider drag is one undo step");
        }

        [Test]
        public void EcFan_HasBuiltInSpeedControl()
        {
            BuildingSession s = Open("small-house");
            Assert.IsTrue(s.Execute(new SetIntakeEquipmentCommand("air-intake", "ec-fan")));
            Assert.IsTrue(s.Execute(new SetEquipmentSpeedCommand("air-intake", 0.3)));
            Assert.IsFalse(s.Execute(new SetVsdCommand("air-intake", true)), "no drive on a variable fan");
            Assert.AreEqual(0.3, s.State.EquipmentAt("air-intake")!.Speed, 1e-9);
        }

        [Test]
        public void SpeedStep_ClampsToTwentyPercentAndRoundsToOnePercent()
        {
            Assert.AreEqual(0.2, SetEquipmentSpeedCommand.SpeedStep(0.05), 1e-12);
            Assert.AreEqual(1.0, SetEquipmentSpeedCommand.SpeedStep(1.4), 1e-12);
            Assert.AreEqual(0.57, SetEquipmentSpeedCommand.SpeedStep(0.5712), 1e-12);
        }
    }

    public class UnlockTests
    {
        static (GameContext ctx, List<BuildingSession> t1) City()
        {
            var ctx = new GameContext(Data, GameContext.NewGame(Data, 7));
            var list = new List<BuildingSession>();
            foreach (Plot p in ctx.City!.Plots)
            {
                if (p.Tier != 1 || p.Starter || list.Count == 2) continue;
                BuildingSession s = ctx.Open(p.Id);
                s.Record.Design = ctx.CityBuildings!.Get(p.Id)!.Reference.State.Clone();
                s.MarkDirty();
                list.Add(s);
            }
            return (ctx, list);
        }

        [Test]
        public void TierTwoItems_AreLockedAtTheStart()
        {
            (GameContext ctx, List<BuildingSession> t1) = City();
            BuildingSession s = t1[0];
            Assert.AreEqual(1, Tech.UnlockedTier(ctx.Game));
            Assert.IsFalse(s.Execute(new SetVsdCommand(BuildingGenerator.AirIntakeId, true)), "VSD is tier 2");
            Assert.IsFalse(s.Execute(new UpgradeAllBendsCommand()), "long-radius elbows are tier 2");
            Assert.IsFalse(s.Execute(new SetIntakeEquipmentCommand(BuildingGenerator.AirIntakeId, "centrifugal-blower")), "tier 3");
            var ids = new List<int>();
            foreach (DuctSegment seg in s.State.Air.Segments) ids.Add(seg.Id);
            Assert.IsFalse(s.Execute(new SetSegmentSizeCommand(ids, 200)), "200 mm is tier 2 (D44)");
            Assert.IsFalse(s.Execute(new SetSegmentSpecCommand(ids, sealing: "sealed")), "sealing is tier 2");
            Assert.IsTrue(s.Execute(new SetSegmentSpecCommand(ids, materialId: "flex-duct")), "flex duct is tier 1");
            StringAssert.Contains("0 of 2", Tech.Condition(Data, ctx, 2));
        }

        [Test]
        public void TwoTierOneBuildingsOnline_UnlockTierTwo_AndItStays()
        {
            (GameContext ctx, List<BuildingSession> t1) = City();
            foreach (BuildingSession s in t1)
            {
                s.Game.Wallet = 1e6;
                Assert.IsTrue(s.Commit(), s.CheckCommit().Reason);
                s.SolveNow();
                Assert.IsTrue(s.CommittedEvaluation.Online, s.Layout.Id);
            }
            int unlocked = 0;
            ctx.TierUnlocked += t => unlocked = t;
            ctx.UpdateUnlocks();
            Assert.AreEqual(2, ctx.Game.City!.UnlockedTier);
            Assert.AreEqual(2, unlocked);
            Assert.IsTrue(t1[0].Execute(new SetVsdCommand(BuildingGenerator.AirIntakeId, true)));

            // Unlocks stay even when the buildings go offline.
            foreach (BuildingSession s in t1) { s.Record.Committed.Equipment.Clear(); s.MarkDirty(); s.SolveNow(); }
            ctx.UpdateUnlocks();
            Assert.AreEqual(2, ctx.Game.City.UnlockedTier);
            Assert.AreEqual("Unlocked", Tech.Condition(Data, ctx, 2));
        }
    }
}
