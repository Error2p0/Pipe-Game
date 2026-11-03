using System.Collections.Generic;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.City;
using HeadLoss.Game.Commands;
using HeadLoss.Game.Failures;
using HeadLoss.Game.Offline;
using HeadLoss.Game.Save;
using HeadLoss.Game.Session;
using NUnit.Framework;
using static HeadLoss.Game.Tests.GameTestUtil;

namespace HeadLoss.Game.Tests
{
    /// <summary>The offline event timeline (<c>12-save-offline.md</c> §Offline catch-up): stress, sampled bursts, crews and city costs.</summary>
    public class OfflineFailureTests
    {
        static List<TilePos> RunOn(int floor, params (int x, int y)[] points)
        {
            var list = new List<TilePos>();
            foreach (TilePos t in Run(points)) list.Add(t.OnFloor(floor));
            return list;
        }

        /// <summary>The tap house plumbed in PVC with a booster pump, so the 35 mm trunk runs above its rating.</summary>
        static GameContext Overpressured()
        {
            var ctx = new GameContext(Data, GameContext.NewFixtureGame(Data, "tap-house"));
            BuildingSession s = ctx.Building!;
            s.Game.Wallet = 1e7;
            s.Record.EverOnline = true;
            Assert.IsTrue(s.Execute(new PlaceRunCommand(RunOn(0, (9, 1), (9, 5)), 35, "pvc", false, "water")));
            Assert.IsTrue(s.Execute(new PlaceRunCommand(RunOn(0, (9, 2), (7, 2), (7, 1)), 15, "pvc", false, "water")));
            Assert.IsTrue(s.Execute(new PlaceRunCommand(new List<TilePos> { new TilePos(9, 5, 0), new TilePos(9, 5, 1) }, 35, "pvc", false, "water")));
            Assert.IsTrue(s.Execute(new PlaceRunCommand(RunOn(1, (9, 5), (9, 3)), 22, "pvc", false, "water")));
            Assert.IsTrue(s.Execute(new PlaceRunCommand(RunOn(1, (9, 4), (7, 4), (7, 1)), 15, "pvc", false, "water")));
            Assert.IsTrue(s.Execute(new SetIntakeEquipmentCommand("water", "small-booster-pump")));
            // Regulators keep the taps within their code maximum: only the trunk sees the pump's pressure.
            Assert.IsTrue(s.Execute(new PlaceInlineCommand(new TilePos(8, 2), "pressure-reducing-valve", false, "water")));
            Assert.IsTrue(s.Execute(new PlaceInlineCommand(new TilePos(8, 4, 1), "pressure-reducing-valve", false, "water")));
            Assert.IsTrue(s.Commit(), s.CheckCommit().Reason);
            s.SolveNow();
            ctx.Leave();
            return ctx;
        }

        static DuctSegment Trunk(BuildingSession s) => s.Committed.Water.Segments.Find(x => x.DiameterMm == 35)!;

        static void SetStress(BuildingSession s, DuctSegment seg, double stress)
        {
            foreach (string e in BuildingFailures.Edges("water", s.Committed.Water, seg)) s.Record.Failures.Stress[e] = stress;
        }

        [Test]
        public void NextBurst_MedianMatchesTheForecast()
        {
            BuildingSession s = Overpressured().Sessions[0];
            NetworkSnapshot snap = s.CommittedSnapshot!;
            BuildingFailures f = s.Failures;
            DuctSegment trunk = Trunk(s);
            double rate = f.StressRate(f.RatingRatio(snap.Water!, s.Committed.Water, trunk, 0));
            Assert.Greater(rate, 0);
            // Only one segment has to be above rating for the median to be that segment's: take the worst one on its own.
            var times = new List<double>();
            for (int i = 0; i < 4001; i++)
            {
                OfflineBurstSample? b = f.SampleNextBurst(snap, s.Committed, trunk.Id);
                Assert.NotNull(b);
                times.Add(b!.Seconds);
            }
            times.Sort();
            double forecast = f.TimeToHalfChance(0, rate);
            Assert.AreEqual(forecast, times[2000], forecast * 0.05, "bursts happen roughly when the forecast says");
        }

        [Test]
        public void CatchUp_BuildsStress_AtTheInGameRate()
        {
            GameContext a = Overpressured(), b = new GameContext(Data, SaveSerializer.Copy(a.Game));
            BuildingSession sa = a.Sessions[0], sb = b.Sessions[0];
            b.Sessions[0].SolveNow();
            var loop = new GameLoop(a);
            for (int i = 0; i < 600; i++) loop.Tick(1);
            OfflineCatchUp.Run(b, 600, 12);
            Assume.That(sa.Record.Failures.Bursts.Count + sb.Record.Failures.Bursts.Count, Is.EqualTo(0), "no burst in ten minutes on these seeds");
            double inGame = sa.Failures.StressOf("water", Trunk(sa).Id, sa.Committed), offline = sb.Failures.StressOf("water", Trunk(sb).Id, sb.Committed);
            Assert.Greater(inGame, 0);
            Assert.AreEqual(inGame, offline, inGame * 0.02);
        }

        [Test]
        public void CatchUp_BurstCutsIncomeToAQuarter_AndAContractCrewFixesIt()
        {
            GameContext ctx = Overpressured();
            BuildingSession s = ctx.Sessions[0];
            s.SolveNow();
            double wholeGross = s.SteadyState(20).Gross;
            Assert.Greater(wholeGross, 0);
            // Far along the hazard curve: the trunk bursts in the first moments away.
            SetStress(s, Trunk(s), 4);
            s.Record.Failures.MaintenanceContract = true;
            s.Record.Failures.PreferEmergency = false;
            double standard = s.Failures.RepairSeconds(false);
            double wallet = s.Game.Wallet;
            OfflineReport r = OfflineCatchUp.Run(ctx, standard * 2, 12);
            Assert.GreaterOrEqual(r.Bursts, 1);
            Assert.GreaterOrEqual(r.RepairsDone, 1);
            Assert.Greater(r.RepairCosts, 0, "the crew's bill is paid");
            // Burst for the repair time at 25%, whole again after it (the repaired pipe is back at zero stress).
            double expected = wholeGross * (standard * 0.25 + standard);
            Assert.AreEqual(expected, r.Gross, expected * 0.1);
            Assert.Less(s.Failures.StressOf("water", Trunk(s).Id, s.Committed), 1, "stress reset by the repair");
            Assert.AreEqual(wallet + r.Net - r.RepairCosts, s.Game.Wallet, 1e-3);
        }

        [Test]
        public void CatchUp_BurstWithoutAContract_StaysUntilTheReturn()
        {
            GameContext ctx = Overpressured();
            BuildingSession s = ctx.Sessions[0];
            s.SolveNow();
            double wholeGross = s.SteadyState(20).Gross;
            Assert.Greater(wholeGross, 0);
            SetStress(s, Trunk(s), 4);
            OfflineReport r = OfflineCatchUp.Run(ctx, 3600, 12);
            Assert.GreaterOrEqual(r.Bursts, 1);
            Assert.AreEqual(0, r.RepairsDone);
            Assert.AreEqual(wholeGross * 0.25 * 3600, r.Gross, wholeGross * 0.25 * 3600 * 0.1);
            Assert.IsNotEmpty(s.Record.Failures.Bursts);
        }

        [Test]
        public void CatchUp_TrippedPanel_PausesIncome()
        {
            GameContext ctx = Overpressured();
            BuildingSession s = ctx.Sessions[0];
            s.Record.PanelTripped = true;
            OfflineReport r = OfflineCatchUp.Run(ctx, 3600, 12);
            Assert.AreEqual(0, r.Gross, 1e-9);
            Assert.IsEmpty(r.Buildings.FindAll(b => b.OnlineSeconds > 0));
        }

        [Test]
        public void CatchUp_ChargesCityFines_AndPremiums()
        {
            GameState g = GameContext.NewGame(Data, 7);
            g.Wallet = 1e7;
            g.City!.UnlockedTier = 5;
            var ctx = new GameContext(Data, g);
            FacilityPlacement plant = ctx.City!.Facilities.Find(f => f.Utility == "water")!;
            Plot target = ctx.City.Plots.FindAll(p => p.Tier == 1)[5];
            var path = new List<(int, int)> { (plant.X, plant.Y) };
            int x = plant.X, y = plant.Y;
            while (x != target.X) { x += System.Math.Sign(target.X - x); path.Add((x, y)); }
            while (y != target.Y) { y += System.Math.Sign(target.Y - y); path.Add((x, y)); }
            Assert.IsTrue(ctx.LayMain("water", 100, 0, path, false).Valid);
            ctx.SolveCity();
            Assume.That(ctx.FinesPerCycle, Is.GreaterThan(0));
            double cycle = Data.Balancing.Economy.IncomeCycleSeconds;
            OfflineReport r = OfflineCatchUp.Run(ctx, 3600, 12);
            Assert.AreEqual(ctx.FinesPerCycle / cycle * 3600, r.Fines, 1e-6);
            Assert.AreEqual(r.Gross - r.Electricity - r.Fines - r.Premiums, r.Net, 1e-6);
        }

        /// <summary>A panel over its limit for longer than the delay stops everything and takes the building offline, until a reset.</summary>
        [Test]
        public void Panel_TripsAfterTheDelay_GoesOffline_AndWaitsForAReset()
        {
            BuildingSession s = TestHouseWithFan();
            DrawAndCommit(s, Run((0, 3), (3, 3), (3, 5)));
            s.Record.Committed.Equipment.RemoveAll(e => e.OpeningId == "power");
            s.Record.Committed.Equipment.Add(new EquipmentState { EquipmentId = "panel-basic", OpeningId = "power" });
            for (int i = 0; i < 4; i++) s.Tick(0.5);
            double draw = s.RequestedPowerW;
            Assert.Greater(draw, 0);
            Core.Data.Defs.EquipmentDef panel = Data.EquipmentItem("panel-basic");
            double limit = panel.PanelLimitKW;
            try
            {
                panel.PanelLimitKW = draw / 2000; // half of what it draws, in kW
                s.Tick(0.5);
                s.Tick(0.5);
                Assert.IsFalse(s.Record.PanelTripped, "not before the delay");
                for (int i = 0; i < 4; i++) s.Tick(0.5);
                Assert.IsTrue(s.Record.PanelTripped);
                s.SolveNow();
                Assert.AreEqual(0, s.PowerSpeed, 1e-9);
                Assert.IsFalse(s.CommittedEvaluation.Online, "everything stops and the building goes offline");
                Assert.AreEqual(0, s.CommittedEvaluation.Gross, 1e-9);
                for (int i = 0; i < 20; i++) s.Tick(0.5);
                Assert.IsTrue(s.Record.PanelTripped, "it does not reset by itself");
                panel.PanelLimitKW = limit;
                Assert.IsTrue(s.ResetPanel());
                s.SolveNow();
                Assert.AreEqual(1, s.PowerSpeed, 1e-9);
            }
            finally
            {
                panel.PanelLimitKW = limit;
            }
        }
    }
}
