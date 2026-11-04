using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.City;
using HeadLoss.Game.Commands;
using HeadLoss.Game.Progression;
using HeadLoss.Game.Session;
using NUnit.Framework;
using static HeadLoss.Game.Tests.GameTestUtil;

namespace HeadLoss.Game.Tests
{
    /// <summary>The milestone, Reputation, the tree and founding a new city (<c>03-city.md</c> §Prestige).</summary>
    public class PrestigeTests
    {
        static GameContext NewCity(ulong seed = 21)
        {
            GameState g = GameContext.NewGame(Data, seed);
            return new GameContext(Data, g);
        }

        [Test]
        public void Reward_IsTheFloorOfKTimesLogIncome_AtLeastOne()
        {
            Assert.AreEqual(26, Milestone.Reward(Data, 5e8), "floor(3 · 8.699)");
            Assert.AreEqual(3, Milestone.Reward(Data, 10));
            Assert.AreEqual(1, Milestone.Reward(Data, 1));
            Assert.AreEqual(1, Milestone.Reward(Data, 0));
        }

        [Test]
        public void IncomeTarget_GrowsTenfoldPerCity()
        {
            double first = Data.Balancing.Prestige.IncomeTargetFirst;
            Assert.AreEqual(first, Milestone.TargetFor(Data, 1), 1e-6);
            Assert.AreEqual(first * 100, Milestone.TargetFor(Data, 3), 1e-3);
        }

        [Test]
        public void Milestone_NeedsBothTheShareAndTheIncome()
        {
            GameContext c = NewCity();
            Milestone m = c.Milestone();
            Assert.Greater(m.Plots, 0);
            Assert.IsFalse(m.Met);
            c.Game.LifetimeIncome = m.IncomeTarget * 2;
            Assert.IsTrue(c.Milestone().IncomeMet);
            Assert.IsFalse(c.Milestone().Met, "income alone is not enough");
            Assert.IsNull(c.FoundNewCity(99));
        }

        [Test]
        public void Milestone_CountsOnlyPlotsThatHaveBuildings_RuinsNeverOnline()
        {
            GameContext c = NewCity();
            int expected = c.City!.Plots.FindAll(p => Data.BuildingTier(p.Tier).Available).Count;
            Assert.AreEqual(expected, c.Milestone().Plots);
            Plot someone = c.City.Plots.Find(p => p.Tier == 1 && !p.Starter)!;
            BuildingSession s = c.Open(someone.Id);
            c.Explode(s, 1);
            Milestone m = c.Milestone();
            Assert.AreEqual(expected, m.Plots, "a ruin still counts as a plot");
            Assert.AreEqual(1, m.Ruins);
            Assert.AreEqual(0, m.Online);
        }

        [Test]
        public void Tree_CostsFollowLevels_AndPermitsNeedThreeOtherNodes()
        {
            GameContext c = NewCity();
            GameState g = c.Game;
            g.Reputation = 100;
            Assert.IsTrue(PrestigeEffects.Buy(Data, PrestigeEffects.Materials, g));
            Assert.AreEqual(94, g.Reputation, "materials level 1 costs 6");
            Assert.IsTrue(PrestigeEffects.Buy(Data, PrestigeEffects.Materials, g));
            Assert.AreEqual(82, g.Reputation, "level 2 costs 12");
            Assert.IsFalse(PrestigeEffects.Buy(Data, PrestigeEffects.Materials, g), "maximum level");
            Assert.IsFalse(PrestigeEffects.Buy(Data, PrestigeEffects.ReconstructionPermits, g), "needs 3 other nodes");
            Assert.IsTrue(PrestigeEffects.Buy(Data, PrestigeEffects.PayRates, g));
            Assert.IsTrue(PrestigeEffects.Buy(Data, PrestigeEffects.RapidResponse, g));
            Assert.IsTrue(PrestigeEffects.Buy(Data, PrestigeEffects.ReconstructionPermits, g));
            g.Reputation = 0;
            Assert.IsFalse(PrestigeEffects.Buy(Data, PrestigeEffects.PayRates, g), "not enough Reputation");
        }

        [Test]
        public void Effects_ElectricityPayRepairsAndMaterials()
        {
            GameContext c = NewCity();
            GameState g = c.Game;
            g.Prestige[PrestigeEffects.ElectricityContracts] = 3;
            g.Prestige[PrestigeEffects.PayRates] = 5;
            g.Prestige[PrestigeEffects.RapidResponse] = 2;
            g.Prestige[PrestigeEffects.Materials] = 1;
            Assert.AreEqual(0.7, PrestigeEffects.ElectricityFactor(Data, g), 1e-12);
            Assert.AreEqual(1.5, PrestigeEffects.PayFactor(Data, g), 1e-12);
            Assert.AreEqual(0.6, PrestigeEffects.RepairTimeFactor(Data, g), 1e-12);
            Assert.IsTrue(Tech.ItemUnlocked(Data, g, "copper"), "materials 1 grants copper at tier 1");
            Assert.IsFalse(Tech.ItemUnlocked(Data, g, "steel"));
        }

        [Test]
        public void Effects_ReachTheEvaluation()
        {
            BuildingSession plain = Open("small-house");
            Build(plain);
            double gross = plain.CommittedEvaluation.Gross, electricity = plain.CommittedEvaluation.Electricity;
            Assert.Greater(gross, 0);

            BuildingSession bonus = Open("small-house");
            bonus.Game.Prestige[PrestigeEffects.PayRates] = 2;
            bonus.Game.Prestige[PrestigeEffects.ElectricityContracts] = 1;
            Build(bonus);
            Assert.AreEqual(gross * 1.2, bonus.CommittedEvaluation.Gross, gross * 1e-6);
            Assert.AreEqual(electricity * 0.9, bonus.CommittedEvaluation.Electricity, electricity * 1e-6);
        }

        static void Build(BuildingSession s)
        {
            var r = Generation.AutoRouter.Build(s.Data, s.Layout);
            Assert.IsTrue(r.Solvable, r.Reason);
            s.Record.Design = r.State.Clone();
            s.MarkDirty();
            s.Game.Wallet = 1e6;
            s.Record.EverOnline = true;
            Assert.IsTrue(s.Commit(), s.CheckCommit().Reason);
            s.SolveNow();
        }

        [Test]
        public void StarterKit_TheFirstFanInHandIsFree()
        {
            GameState g = GameContext.NewGame(Data, 5);
            g.City!.StarterItems.Add("small-axial-fan");
            var ctx = new GameContext(Data, g);
            BuildingSession s = ctx.Open(ctx.City!.Starter!.Id);
            Assert.IsTrue(s.Execute(new SetIntakeEquipmentCommand("air-intake", "small-axial-fan")));
            Assert.AreEqual(s.PendingCost.Net - 40, s.NetCommitCost(), 1e-9);
            s.Game.Wallet = 1e4;
            Assert.IsTrue(s.Commit(), s.CheckCommit().Reason);
            Assert.AreEqual(0, g.City.StarterItems.Count, "used up");
        }

        [Test]
        public void FoundNewCity_ResetsTheCity_KeepsReputationAndBonuses()
        {
            GameContext c = NewCity();
            GameState g = c.Game;
            g.Reputation = 2;
            g.Prestige[PrestigeEffects.StarterKit] = 1;
            g.Wallet = 12345;
            g.LifetimeIncome = 1e9;
            g.Statistics.Explosions = 3;
            // Meet the share by hand: every counted plot online would need a full city, so test with a tiny target instead.
            double share = Data.Balancing.Prestige.ShareOnline;
            Data.Balancing.Prestige.ShareOnline = 0;
            try
            {
                Milestone m = c.Milestone();
                Assert.IsTrue(m.Met);
                GameState? next = c.FoundNewCity(777);
                Assert.NotNull(next);
                Assert.AreEqual(Data.Balancing.Economy.StartingWallet, next!.Wallet, 1e-9);
                Assert.AreEqual(0, next.LifetimeIncome, 1e-9);
                Assert.AreEqual(0, next.Buildings.Count);
                Assert.AreEqual(2 + m.ReputationReward, next.Reputation);
                Assert.AreEqual(1, next.Prestige[PrestigeEffects.StarterKit]);
                Assert.AreEqual(2, next.City!.Index);
                Assert.AreEqual(777UL, next.City.Seed);
                Assert.AreEqual(1, next.City.UnlockedTier);
                Assert.AreEqual(0, next.City.Utilities.Mains.Count);
                Assert.AreEqual(0, next.City.Ruins.Count);
                CollectionAssert.Contains(next.City.StarterItems, "small-axial-fan");
                Assert.AreEqual(3, next.Statistics.Explosions);
                Assert.AreEqual(1, next.Statistics.CitiesFounded);
                Assert.AreEqual(1e9, next.Statistics.IncomeAllCities, 1e-3);
            }
            finally
            {
                Data.Balancing.Prestige.ShareOnline = share;
            }
        }

        [Test]
        public void Modifiers_LaterCities_ApplyToClimateAndCeiling()
        {
            // Find a second-city seed that rolled each modifier.
            bool cold = false, old = false;
            for (ulong seed = 1; seed < 200 && !(cold && old); seed++)
            {
                GameState g = GameContext.NewGame(Data, seed);
                g.City!.Index = 2;
                var ctx = new GameContext(Data, g);
                if (!cold && ctx.City!.Modifiers.Contains("cold-climate"))
                {
                    cold = true;
                    Assert.AreEqual(Data.Balancing.Climate.MeanTemperatureC - 10 + Data.Balancing.Climate.DailyAmplitudeK, ctx.Climate.OutsideTemperatureAt(Data.Balancing.Climate.WarmestHour), 1e-9);
                }
                if (!old && ctx.City!.Modifiers.Contains("old-buildings"))
                {
                    old = true;
                    Assert.AreEqual(1.2, ctx.Climate.WallUOverride, 1e-9);
                    Plot p = ctx.City.Plots.Find(x => x.Tier == 2)!;
                    BuildingSession s = ctx.Open(p.Id);
                    Assert.AreEqual(3, s.CeilingCapacity);
                    Assert.AreEqual(3, s.Layout.Def.CeilingCapacity);
                }
            }
            Assert.IsTrue(cold && old, "both modifiers appear within 200 seeds");
        }

        [Test]
        public void DemoCap_StopsUnlocksAtItsTier()
        {
            GameState g = GameContext.NewGame(Data, 3);
            var ctx = new GameContext(Data, g) { Demo = true };
            g.City!.UnlockedTier = Data.Balancing.Demo.MaxTier;
            int before = g.City.UnlockedTier;
            Tech.Update(ctx, _ => true);
            Assert.AreEqual(before, g.City.UnlockedTier);
        }
    }
}
