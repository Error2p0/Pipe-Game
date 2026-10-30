using System.Collections.Generic;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.City;
using HeadLoss.Game.Commands;
using HeadLoss.Game.Offline;
using HeadLoss.Game.Save;
using HeadLoss.Game.Session;
using NUnit.Framework;
using static HeadLoss.Game.Tests.GameTestUtil;

namespace HeadLoss.Game.Tests
{
    public class OfflineTests
    {
        /// <summary>A fixture game: the test house with the small fan and a committed duct to two vents, online.</summary>
        static GameContext Earning()
        {
            var ctx = new GameContext(Data, GameContext.NewFixtureGame(Data, "test-house"));
            BuildingSession s = ctx.Building!;
            s.Record.Design = ReferenceLayout.Build(Data, s.Layout);
            s.Record.Design.Equipment[0].EquipmentId = "small-axial-fan";
            s.MarkDirty();
            s.Game.Wallet = 1e4;
            s.Record.EverOnline = true;
            Assert.IsTrue(s.Commit());
            s.SolveNow();
            ctx.Leave();
            return ctx;
        }

        /// <summary>12-save-offline.md: the same scenario in real time and through the catch-up gives the same money within 1%.</summary>
        [Test]
        public void CatchUp_MatchesRealTime_WithinOnePercent()
        {
            GameContext a = Earning(), b = new GameContext(Data, SaveSerializer.Copy(Earning().Game));
            double before = a.Game.Wallet;
            var loop = new GameLoop(a);
            for (int i = 0; i < 6000; i++) loop.Tick(0.1);
            double realTime = a.Game.Wallet - before;

            double beforeB = b.Game.Wallet;
            OfflineReport r = OfflineCatchUp.Run(b, 600, 12);
            double offline = b.Game.Wallet - beforeB;
            Assert.Greater(realTime, 0, "the house earns");
            Assert.AreEqual(realTime, offline, 0.01 * realTime);
            Assert.AreEqual(offline, r.Net, 1e-6);
            Assert.AreEqual(1, r.Buildings.Count);
            Assert.AreEqual(600, r.Buildings[0].OnlineSeconds, 1e-9);
        }

        [Test]
        public void CatchUp_IsCappedAtTheOfflineLimit()
        {
            GameContext ctx = Earning();
            double cap = Data.Balancing.Offline.MaxHours * 3600;
            OfflineReport r = OfflineCatchUp.Run(ctx, cap * 2, 0);
            Assert.IsTrue(r.Capped);
            Assert.AreEqual(cap, r.Seconds, 1e-9);
            Assert.AreEqual(cap * 2, r.AwaySeconds, 1e-9);
        }

        [Test]
        public void HandCrankOnly_EarnsNothingOffline()
        {
            var ctx = new GameContext(Data, GameContext.NewFixtureGame(Data, "test-house"));
            BuildingSession s = ctx.Building!;
            Draw(s, Run((0, 3), (5, 3), (5, 5), (3, 5)), 125);
            s.Game.Wallet = 1e4;
            s.Record.EverOnline = true;
            s.Commit();
            double wallet = ctx.Game.Wallet;
            OfflineReport r = OfflineCatchUp.Run(ctx, 3600, 12);
            Assert.AreEqual(0, r.Gross, 1e-12, "02-economy: offline, crank speed is 0");
            Assert.AreEqual(wallet, ctx.Game.Wallet, 1e-9);
        }

        [Test]
        public void OfflineBuilding_StillPaysElectricity_AndCostsBecomeDebt()
        {
            var ctx = new GameContext(Data, GameContext.NewFixtureGame(Data, "test-house"));
            BuildingSession s = ctx.Building!;
            s.Execute(new SetIntakeEquipmentCommand("air-intake", "small-axial-fan"));
            Draw(s, Run((0, 3), (3, 3)), 125);  // an open end: the fan runs, no vent is reached
            s.Game.Wallet = 1e4;
            s.Commit();
            ctx.Game.Wallet = 1;
            OfflineReport r = OfflineCatchUp.Run(ctx, 3600, 12);
            Assert.AreEqual(0, r.Gross);
            Assert.Greater(r.Electricity, 1, "the fan keeps drawing power");
            Assert.AreEqual(0, ctx.Game.Wallet, 1e-9);
            Assert.AreEqual(r.Electricity - 1, ctx.Game.Debt, 1e-6, "what the wallet cannot pay becomes debt");
        }

        /// <summary>Tier 2 unlocks during the catch-up when two tier-1 buildings are online.</summary>
        [Test]
        public void CatchUp_UnlocksTiers()
        {
            var ctx = new GameContext(Data, GameContext.NewGame(Data, 7));
            int opened = 0;
            foreach (Plot p in ctx.City!.Plots)
            {
                if (p.Tier != 1 || p.Starter || opened == 2) continue;
                BuildingSession s = ctx.Open(p.Id);
                s.Record.Design = ctx.CityBuildings!.Get(p.Id)!.Reference.State.Clone();
                s.Game.Wallet = 1e5;
                s.MarkDirty();
                Assert.IsTrue(s.Commit());
                opened++;
            }
            ctx.Leave();
            OfflineReport r = OfflineCatchUp.Run(ctx, 600, 12);
            CollectionAssert.AreEqual(new[] { 2 }, r.Unlocked);
            Assert.AreEqual(2, ctx.Game.City!.UnlockedTier);
        }

        /// <summary>01-architecture.md budget: 72 h for 100 buildings in under 2 s (scaled to the buildings a city has).</summary>
        [Test]
        public void CatchUp_SeventyTwoHours_WithinBudget()
        {
            var ctx = new GameContext(Data, GameContext.NewGame(Data, 3));
            ctx.Game.City!.UnlockedTier = 2;
            var built = new List<BuildingSession>();
            foreach (Plot p in ctx.City!.Plots)
            {
                if (!ctx.CityBuildings!.Available(p) || p.Starter) continue;
                BuildingSession s = ctx.Open(p.Id);
                s.Record.Design = ctx.CityBuildings.Get(p.Id)!.Reference.State.Clone();
                s.Game.Wallet = 1e7;
                s.MarkDirty();
                s.Commit();
                built.Add(s);
            }
            ctx.Leave();
            Assert.Greater(built.Count, 20);
            OfflineReport r = OfflineCatchUp.Run(ctx, 72 * 3600, 9);
            double budget = 2000.0 * built.Count / 100;
            TestContext.WriteLine($"{built.Count} buildings, 72 h: {r.ComputeMilliseconds:0} ms (budget {budget:0} ms)");
            Assert.Less(r.ComputeMilliseconds, budget);
            Assert.Greater(r.Net, 0);
        }
    }
}
