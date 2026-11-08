using System.Diagnostics;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.City;
using HeadLoss.Game.Failures;
using HeadLoss.Game.Generation;
using HeadLoss.Game.Session;
using Newtonsoft.Json;
using NUnit.Framework;
using static HeadLoss.Game.Tests.GameTestUtil;

namespace HeadLoss.Game.Tests
{
    /// <summary>Slow ageing (<c>08-failures.md</c>, D134, D166): stress below rating from tier 4, none below.</summary>
    public class AgeingTests
    {
        static BuildingFailures FailuresOfTier(int tier)
        {
            BuildingLayoutDef def = JsonConvert.DeserializeObject<BuildingLayoutDef>(JsonConvert.SerializeObject(Data.HandmadeBuilding("tap-house")))!;
            def.Tier = tier;
            var layout = BuildingLayout.From(Data, def);
            return new BuildingFailures(Data, layout, BuildingRecord.CreateFor(layout), 1);
        }

        [Test]
        public void BelowRating_AgesFromTier4Only()
        {
            FailureDef f = Data.Balancing.Failure;
            Assert.AreEqual(4, f.AgeingFromTier);
            Assert.Greater(f.AgeingRate, 0);
            for (int tier = 1; tier <= 5; tier++)
            {
                BuildingFailures bf = FailuresOfTier(tier);
                Assert.AreEqual(tier >= 4 ? f.AgeingRate : 0, bf.StressRate(0.5), 1e-18, $"tier {tier}, below rating");
                Assert.AreEqual(f.StressRate * System.Math.Pow(1, f.StressExponent) + (tier >= 4 ? f.AgeingRate : 0), bf.StressRate(2), 1e-12, $"tier {tier}, twice the rating");
            }
        }

        [Test]
        public void Rate_IsSlow_OneSegmentIn40BurstsInAThousandHours()
        {
            // D166: S = k_age · t; the cumulative burst chance is 1 − exp(−(S/η)^β).
            FailureDef f = Data.Balancing.Failure;
            double s = f.AgeingRate * 1000 * 3600;
            Assert.AreEqual(0.3, s, 0.01);
            double p = 1 - System.Math.Exp(-System.Math.Pow(s / f.WeibullEta, f.WeibullBeta));
            Assert.AreEqual(0.027, p, 0.003);
            Assert.Greater(f.WeepThreshold / f.AgeingRate / 3600, 1900, "weeping (and the leak sensors' alert) comes after about 2 000 hours");
        }

        [Test]
        [Timeout(600000)]
        public void Offline_ATier4BuildingSamplesThreeDaysQuickly()
        {
            // Every segment of a tier 4 building now builds stress, so the catch-up draws a burst for each at every stop.
            BuildingTierDef tier = Data.BuildingTier(4);
            bool was = tier.Available;
            tier.Available = true;
            try
            {
                GeneratedBuilding g = BuildingFactory.Create(Data, new BuildingRequest("plot-1", "office-building", 3, "south"), DistrictKind.Commercial)!;
                Assert.IsTrue(g.Reference.Solvable, g.Reference.Reason);
                BuildingRecord r = BuildingRecord.CreateFor(g.Layout);
                r.Committed = g.Reference.State.Clone();
                r.Design = r.Committed.Clone();
                var s = new BuildingSession(Data, g.Layout, r, new GameState { Wallet = 1e6 }, g.BudgetCap, null, g.Reference);
                s.SolveNow();
                NetworkSnapshot snap = s.CommittedSnapshot!;
                int segments = 0;
                foreach (DuctNetworkState n in r.Committed.Networks) segments += n.Segments.Count;
                var sw = Stopwatch.StartNew();
                for (int hour = 0; hour < 72; hour++)
                {
                    Assert.IsNotNull(s.Failures.SampleNextBurst(snap, r.Committed), "every segment ages, so a burst is always ahead");
                    s.Failures.AdvanceStress(snap, r.Committed, 3600);
                }
                sw.Stop();
                // 01's budget: 72 h × 100 buildings under 2 s, so 20 ms a building; a tier 4 building is among the largest.
                Assert.Less(sw.ElapsedMilliseconds, 60, $"{segments} segments, 72 stops: {sw.ElapsedMilliseconds} ms");
                double expected = Data.Balancing.Failure.AgeingRate * 72 * 3600;
                foreach (double v in r.Failures.Stress.Values) Assert.GreaterOrEqual(v, expected - 1e-9);
            }
            finally { tier.Available = was; }
        }
    }
}
