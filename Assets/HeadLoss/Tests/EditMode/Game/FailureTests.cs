using System.Collections.Generic;
using HeadLoss.Core;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.Commands;
using HeadLoss.Game.Economy;
using HeadLoss.Game.Failures;
using HeadLoss.Game.Session;
using NUnit.Framework;

namespace HeadLoss.Game.Tests
{
    /// <summary>The burst model, water hammer, repairs and shutoff isolation (<c>08-failures.md</c>, <c>15-testing.md</c> T8, T12, T13).</summary>
    public class FailureTests
    {
        const double Bar = 1e5;

        static List<TilePos> RunOn(int floor, params (int x, int y)[] points)
        {
            var list = new List<TilePos>();
            foreach (TilePos t in GameTestUtil.Run(points)) list.Add(t.OnFloor(floor));
            return list;
        }

        /// <summary>The tap house (the water house without vents) plumbed and committed, the kitchen tap behind a shutoff.</summary>
        static BuildingSession Built(string material = "copper", bool shutoff = true, double trunk = 22)
        {
            BuildingSession s = GameTestUtil.Open("tap-house");
            Assert.IsTrue(s.Execute(new PlaceRunCommand(RunOn(0, (9, 1), (9, 5)), trunk, material, false, "water")));
            Assert.IsTrue(s.Execute(new PlaceRunCommand(RunOn(0, (9, 2), (7, 2), (7, 1)), 15, material, false, "water")));
            Assert.IsTrue(s.Execute(new PlaceRunCommand(new List<TilePos> { new TilePos(9, 5, 0), new TilePos(9, 5, 1) }, trunk, material, false, "water")));
            Assert.IsTrue(s.Execute(new PlaceRunCommand(RunOn(1, (9, 5), (9, 3)), 22, material, false, "water")));
            Assert.IsTrue(s.Execute(new PlaceRunCommand(RunOn(1, (9, 4), (7, 4), (7, 1)), 15, material, false, "water")));
            if (shutoff) Assert.IsTrue(s.Execute(new PlaceInlineCommand(new TilePos(8, 2), "shutoff-valve", false, "water")));
            s.Game.Wallet = 1e6;
            s.Record.EverOnline = true;
            Assert.IsTrue(s.Commit(), s.CheckCommit().Reason);
            s.SolveNow();
            return s;
        }

        [Test]
        public void T8_Joukowsky()
        {
            Assert.AreEqual(1.7968e6, Hammer.Joukowsky(998.2, 1200, 1.5), 1.7968e6 * 0.001);
            // A slow closure scales by (2L/a)/t: 20 m of copper, closed over 1 s.
            Assert.AreEqual(Hammer.Joukowsky(998.2, 1200, 1.5) * (2 * 20 / 1200.0) / 1.0, Hammer.Surge(998.2, 1200, 1.5, 20, 1.0), 1e-6);
            Assert.AreEqual(Hammer.Joukowsky(998.2, 1200, 1.5), Hammer.Surge(998.2, 1200, 1.5, 20, 0.01), 1e-6);
        }

        [Test]
        public void T12_Weibull_StressOneAfterAnHourAtTwiceTheRating_ChanceOneMinusOneOverE()
        {
            BuildingFailures f = GameTestUtil.Open("water-house").Failures;
            Assert.AreEqual(1.0, f.StressRate(2) * 3600, 1e-9);
            // Monte Carlo over 20 000 seeded trials, one-minute ticks.
            int bursts = 0;
            var rng = new Rng(12345);
            for (int trial = 0; trial < 20000; trial++)
            {
                double s = 0;
                for (int t = 0; t < 60; t++)
                {
                    double next = s + f.StressRate(2) * 60;
                    if (rng.NextDouble() < f.BurstChance(s, next)) { bursts++; break; }
                    s = next;
                }
            }
            Assert.AreEqual(1 - System.Math.Exp(-1), bursts / 20000.0, 0.01);
        }

        [Test]
        public void T13_Weibull_AtOnePointTwoTimesTheRating()
        {
            BuildingFailures f = GameTestUtil.Open("water-house").Failures;
            Assert.AreEqual(40249, 1 / f.StressRate(1.2), 40249 * 0.005);
            Assert.AreEqual(0, f.StressRate(1.0), 1e-15, "no stress at or below the rating");
        }

        [Test]
        public void Forecast_TimeToHalfChance()
        {
            BuildingFailures f = GameTestUtil.Open("water-house").Failures;
            // From S = 0 at 1/3600 per second: S50 = ln2^(1/3) ≈ 0.885 → about 3,187 s.
            Assert.AreEqual(System.Math.Pow(System.Math.Log(2), 1 / 3.0) * 3600, f.TimeToHalfChance(0, 1 / 3600.0), 1e-6);
            Assert.IsTrue(double.IsPositiveInfinity(f.TimeToHalfChance(0.3, 0)));
        }

        [Test]
        public void Stress_BuildsAboveRatingOnly_AndBurstsWhenTheHazardSaysSo()
        {
            BuildingSession s = Built("pvc", shutoff: false, trunk: 35);
            // Mains pressure stays under the 35 mm PVC trunk's rating (3.8 bar): no stress.
            for (int i = 0; i < 10; i++) s.Tick(1);
            Assert.IsEmpty(s.Record.Failures.Stress, "no stress below the rating");
            // A booster pump pushes the trunk above its rating: stress builds, visibly, before any burst.
            s.Execute(new SetIntakeEquipmentCommand("water", "small-booster-pump"));
            Assert.IsTrue(s.Commit());
            s.SolveNow();
            FluidSolve w = s.CommittedSnapshot!.Water!;
            DuctSegment trunk = s.Committed.Water.Segments.Find(x => x.DiameterMm == 35)!;
            double x = s.Failures.RatingRatio(w, s.Committed.Water, trunk, 0);
            Assert.Greater(x, 1, "the pump pushes the trunk above its rating");
            for (int i = 0; i < 60; i++) s.Tick(1);
            Assert.Greater(s.Failures.StressOf("water", trunk.Id, s.Committed), 0);
            // Run it to a burst (deterministic stream): at most a few hours of game time.
            int ticks = 0;
            while (s.Record.Failures.Bursts.Count == 0 && ticks < 200000) { s.Tick(10); ticks++; }
            Assert.AreEqual(1, s.Record.Failures.Bursts.Count);
            s.SolveNow();
            Assert.IsTrue(s.CommittedEvaluation.Burst);
            Assert.AreEqual(0, s.CommittedEvaluation.Gross, "in game a burst cuts the income to 0");
        }

        [Test]
        public void Burst_Offline_CutsTo25Percent()
        {
            BuildingSession s = Built();
            double gross = s.CommittedEvaluation.Gross;
            Assert.Greater(gross, 0);
            DuctSegment trunk = s.Committed.Water.Segments.Find(x => x.DiameterMm == 22)!;
            s.Record.Failures.Bursts.Add(new BurstState { Fluid = "water", Edge = BuildingFailures.Edges("water", s.Committed.Water, trunk)[0] });
            s.OfflineRules = true;
            s.MarkDirty();
            s.SolveNow();
            Assert.IsTrue(s.CommittedEvaluation.Burst);
            Assert.AreEqual(s.CommittedEvaluation.GrossBeforeBurst * 0.25, s.CommittedEvaluation.Gross, 1e-9);
        }

        [Test]
        public void Shutoff_IsolatesABurstBranch_AndItsTapsAreExempt()
        {
            BuildingSession s = Built();
            // The kitchen branch bursts beyond its shutoff.
            DuctSegment branch = s.Committed.Water.Segments.Find(x => x.DiameterMm == 15 && s.Committed.Water.FindNode(x.NodeA)!.Floor == 0
                && (s.Committed.Water.FindNode(x.NodeA)!.Pos == new TilePos(7, 1) || s.Committed.Water.FindNode(x.NodeB)!.Pos == new TilePos(7, 1)))!;
            s.Record.Failures.Bursts.Add(new BurstState { Fluid = "water", Edge = BuildingFailures.Edges("water", s.Committed.Water, branch)[0] });
            s.MarkDirty();
            s.SolveNow();
            Assert.AreEqual(0, s.CommittedEvaluation.Gross, "the burst cuts everything while it is open");
            Assert.IsTrue(s.Execute(new SetInlineOnCommand(new TilePos(8, 2), false, "water")));
            s.SolveNow();
            BuildingEvaluation ev = s.CommittedEvaluation;
            Assert.IsFalse(ev.Burst, "a closed shutoff isolates the burst");
            Assert.AreEqual(OutletZone.Isolated, ev.Find("tap-1")!.Zone);
            Assert.IsTrue(ev.Online, "the rest of the building stays online");
            Assert.Greater(ev.Gross, 0);
            Assert.AreEqual(0, ev.Find("tap-1")!.Pay);
        }

        [Test]
        public void Repair_Standard_RemovesTheBurstAndResetsStress_EmergencyJumpsTheQueue()
        {
            BuildingSession s = Built();
            DuctNetworkState net = s.Committed.Water;
            DuctSegment a = net.Segments.Find(x => x.DiameterMm == 22)!, b = net.Segments.Find(x => x.DiameterMm == 15)!;
            string ea = BuildingFailures.Edges("water", net, a)[0], eb = BuildingFailures.Edges("water", net, b)[0];
            foreach (string e in BuildingFailures.Edges("water", net, a)) s.Record.Failures.Stress[e] = 0.9;
            s.Record.Failures.Bursts.Add(new BurstState { Fluid = "water", Edge = ea });
            s.Record.Failures.Bursts.Add(new BurstState { Fluid = "water", Edge = eb });
            double paid = 0;
            Assert.IsTrue(s.Failures.StartRepair("water", ea, false, "", 0, net, a, p => { paid += p; return true; }));
            Assert.IsTrue(s.Failures.StartRepair("water", eb, true, "", 0, net, b, p => { paid += p; return true; }));
            Assert.IsTrue(s.Record.Failures.Repairs[0].Emergency, "the emergency crew goes first");
            double tier = s.Data.Balancing.Economy.TierFactor[1];
            Assert.Greater(paid, 0);
            s.Failures.TickRepairs(s.Data.Balancing.Failure.RepairEmergencySeconds + 1, s.Record);
            Assert.IsFalse(s.Record.Failures.Bursts.Exists(x => x.Edge == eb));
            Assert.IsTrue(s.Record.Failures.Bursts.Exists(x => x.Edge == ea), "the standard repair is still waiting");
            s.Failures.TickRepairs(s.Data.Balancing.Failure.RepairStandardSeconds + 1, s.Record);
            Assert.IsEmpty(s.Record.Failures.Bursts);
            Assert.AreEqual(0, s.Failures.StressOf("water", a.Id, s.Committed), "a repaired segment starts again at 0");
            Assert.GreaterOrEqual(tier, 1);
        }

        [Test]
        public void Repair_WithUpgrade_FitsTheNewMaterialInBothStates()
        {
            BuildingSession s = Built("pvc", shutoff: false);
            DuctNetworkState net = s.Committed.Water;
            DuctSegment a = net.Segments.Find(x => x.DiameterMm == 22)!;
            string e = BuildingFailures.Edges("water", net, a)[0];
            s.Record.Failures.Bursts.Add(new BurstState { Fluid = "water", Edge = e });
            Assert.IsTrue(s.Failures.StartRepair("water", e, true, "copper", 0, net, a, _ => true));
            s.Failures.TickRepairs(1e6, s.Record);
            Assert.IsTrue(s.Committed.Water.Segments.Exists(x => x.MaterialId == "copper"));
            Assert.IsTrue(s.State.Water.Segments.Exists(x => x.MaterialId == "copper"));
        }

        [Test]
        public void Hammer_ClosingAShutoff_RaisesASurge_APressureTankDampsIt()
        {
            BuildingSession s = Built();
            Assert.AreEqual(0, s.Failures.SurgePa("water"));
            Assert.IsTrue(s.Execute(new SetInlineOnCommand(new TilePos(8, 2), false, "water")));
            s.SolveNow();
            double plain = s.Failures.SurgePa("water");
            Assert.Greater(plain, 0, "closing a valve on flowing water raises a surge");

            BuildingSession t = Built();
            Assert.IsTrue(t.Execute(new PlaceInlineCommand(new TilePos(9, 4), "pressure-tank", false, "water")));
            Assert.IsTrue(t.Commit());
            t.SolveNow();
            Assert.IsTrue(t.Execute(new SetInlineOnCommand(new TilePos(8, 2), false, "water")));
            t.SolveNow();
            Assert.AreEqual(plain * t.Data.Balancing.Failure.AccumulatorSurgeFactor, t.Failures.SurgePa("water"), plain * 0.05);
            // Surges fade after their duration.
            t.Tick(t.Data.Balancing.Failure.HammerDurationSeconds + 0.1);
            Assert.AreEqual(0, t.Failures.SurgePa("water"));
        }

        [Test]
        public void Commit_ReplacingAPipe_ResetsItsStress()
        {
            BuildingSession s = Built();
            DuctNetworkState net = s.Committed.Water;
            DuctSegment a = net.Segments.Find(x => x.DiameterMm == 22)!;
            foreach (string e in BuildingFailures.Edges("water", net, a)) s.Record.Failures.Stress[e] = 0.5;
            DuctSegment other = net.Segments.Find(x => x.DiameterMm == 15)!;
            foreach (string e in BuildingFailures.Edges("water", net, other)) s.Record.Failures.Stress[e] = 0.4;
            int id = s.State.Water.Segments.Find(x => x.DiameterMm == 22)!.Id;
            Assert.IsTrue(s.Execute(new SetSegmentSizeCommand(id, 28, "water")));
            Assert.IsTrue(s.Commit());
            DuctSegment replaced = s.Committed.Water.Segments.Find(x => x.DiameterMm == 28)!;
            Assert.AreEqual(0, s.Failures.StressOf("water", replaced.Id, s.Committed));
            DuctSegment kept = s.Committed.Water.Segments.Find(x => x.DiameterMm == 15)!;
            Assert.AreEqual(0.4, s.Failures.StressOf("water", kept.Id, s.Committed), 1e-9, "other pipes keep their stress");
        }
    }
}
