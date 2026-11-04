using System.Collections.Generic;
using HeadLoss.Core;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.Codes;
using HeadLoss.Game.Commands;
using HeadLoss.Game.Economy;
using HeadLoss.Game.Failures;
using HeadLoss.Game.Offline;
using HeadLoss.Game.Session;
using NUnit.Framework;
using static HeadLoss.Game.Tests.GameTestUtil;

namespace HeadLoss.Game.Tests
{
    /// <summary>Natural gas in a building: delivery, leaks, the alarm, the timer, safety gear and the certificate (<c>08-failures.md</c>, <c>15-testing.md</c>).</summary>
    public class GasTests
    {
        const string Gas = BuildingState.GasFluid;

        static List<TilePos> RunOn(int floor, params (int x, int y)[] points)
        {
            var list = new List<TilePos>();
            foreach (TilePos t in Run(points)) list.Add(t.OnFloor(floor));
            return list;
        }

        /// <summary>
        /// The gas house piped in copper: the connection up the kitchen wall, a branch to the ground-floor stove behind a part on
        /// (8, 2), and a riser to the upper stove. Everything committed and solved.
        /// </summary>
        static BuildingSession Plumbed(string branchPart = "shutoff-valve", double trunk = 28, string regulator = "gas-regulator", bool commit = true)
        {
            BuildingSession s = Open("gas-house");
            s.Game.Wallet = 1e7;
            s.Record.EverOnline = true;
            Assert.IsTrue(s.Execute(new PlaceRunCommand(RunOn(0, (9, 1), (9, 5)), trunk, "copper", false, Gas)));
            Assert.IsTrue(s.Execute(new PlaceRunCommand(RunOn(0, (9, 2), (7, 2), (7, 1)), 22, "copper", false, Gas)));
            Assert.IsTrue(s.Execute(new PlaceRunCommand(new List<TilePos> { new TilePos(9, 5, 0), new TilePos(9, 5, 1) }, trunk, "copper", false, Gas)));
            Assert.IsTrue(s.Execute(new PlaceRunCommand(RunOn(1, (9, 5), (9, 4), (7, 4), (7, 1)), 22, "copper", false, Gas)));
            if (branchPart.Length > 0) Assert.IsTrue(s.Execute(new PlaceInlineCommand(new TilePos(8, 2), branchPart, false, Gas)));
            if (regulator.Length > 0) Assert.IsTrue(s.Execute(new SetIntakeEquipmentCommand("gas", regulator)));
            // Air to the only vent, so the building can come online.
            Assert.IsTrue(s.Execute(new PlaceRunCommand(RunOn(0, (0, 2), (5, 2), (8, 2), (8, 3)), 160, "galvanized-duct", false, "air")));
            Assert.IsTrue(s.Execute(new SetIntakeEquipmentCommand("air-intake", "small-axial-fan")));
            if (commit)
            {
                Assert.IsTrue(s.Commit(), s.CheckCommit().Reason);
                s.SolveNow();
            }
            return s;
        }

        static OutletStatus Stove(BuildingSession s, string id) => s.CommittedEvaluation.Find(id)!;

        /// <summary>A big leak on the kitchen branch: a quarter of the 22 mm pipe's cross-section.</summary>
        static LeakState BigLeak(BuildingSession s)
        {
            DuctNetworkState gas = s.Committed.Gas;
            DuctSegment branch = gas.Segments.Find(x => x.DiameterMm == 22 && gas.FindNode(x.NodeA)!.Floor == 0)!;
            List<string> edges = BuildingFailures.Edges(Gas, gas, branch);
            LeakState l = s.Failures.AddLeak("burst", edges[0], new TilePos(7, 2), s.Failures.SegmentArea(branch) * s.Data.Balancing.Gas.BurstLeakAreaFraction, 0);
            s.SolveNow();
            return l;
        }

        /// <summary>Puts a share of gas in both layers of every cell of a room.</summary>
        static void FillRoom(BuildingSession s, string roomId, float share)
        {
            int room = -1;
            for (int r = 0; r < s.Layout.Rooms.Count; r++) if (s.Layout.Rooms[r].Id == roomId) room = r;
            int n = s.Rooms.Grid.Cells;
            for (int c = 0; c < n; c++)
                if (s.Rooms.Grid.Room[c] == room) s.Rooms.State.Gas[c] = s.Rooms.State.Gas[n + c] = share;
            s.Rooms.SeedRoomGas(room, share);
        }

        static void TickFor(BuildingSession s, double seconds, double dt = 1)
        {
            for (double t = 0; t < seconds; t += dt) s.Tick(dt);
        }

        // ---------- delivery ----------

        [Test]
        public void Stove_DeliversTheRegulatedPressure_AndPaysWithTheRiskyCap()
        {
            BuildingSession s = Plumbed();
            Assert.IsTrue(s.CommittedSnapshot!.Gas!.Result.Converged);
            OutletStatus stove = Stove(s, "stove-1");
            Assert.AreEqual(OutletZone.Overdelivery, stove.Zone, $"delivery {stove.Delivery} Pa");
            Assert.AreEqual(2500, stove.Delivery, 60, "25 mbar less a little friction");
            // The risky cap: full multiplier 2.5 at the code maximum, for a 20 / 23 / 28 mbar stove.
            var req = new OutletRequirement(2000, 2300, 2800);
            Assert.AreEqual(2.5, Payout.Multiplier(req, 2800, true, s.Data.Balancing.Economy, out _), 1e-9);
            Assert.AreEqual(1 + 1.5 * System.Math.Sqrt(0.2 / 0.25 * 0.5), Payout.Multiplier(req, 2500, true, s.Data.Balancing.Economy, out _), 1e-9);
        }

        [Test]
        public void Stove_WithoutARegulator_IsAboveItsCodeMaximum()
        {
            BuildingSession s = Plumbed(regulator: "");
            OutletStatus stove = Stove(s, "stove-1");
            Assert.AreEqual(OutletZone.AboveCodeMax, stove.Zone, "LP mains at 67.5 mbar against 28 mbar");
            Assert.AreEqual(0, stove.Multiplier);
        }

        [Test]
        public void Gas_GainsPressureGoingUp_LightGasBuoyancy()
        {
            BuildingSession s = Plumbed(trunk: 54);
            // ρ_air − ρ_gas ≈ 0.52 kg/m³ over one 3 m floor: about +15 Pa upstairs (D7), against a very small friction loss.
            double rise = Stove(s, "stove-2").Delivery - Stove(s, "stove-1").Delivery;
            Assert.AreEqual(15.0, rise, 6.0);
        }

        [Test]
        public void GasReadouts_UseMbarAndCubicMetresPerHour()
        {
            StringAssert.EndsWith("mbar", Units.FormatPressure(2500, Gas));
            StringAssert.EndsWith("m³/h", Units.FormatFlow(0.00015, Gas));
        }

        // ---------- leaks ----------

        [Test]
        public void Leak_FollowsTheOrificeEquation()
        {
            BuildingSession s = Plumbed();
            LeakState l = BigLeak(s);
            FluidSolve gas = s.CommittedSnapshot!.Gas!;
            int link = gas.Compiled.LeakLinks[l.Key];
            double dp = gas.Result.NodePressure[gas.Graph().Links[link].From];
            double rho = gas.Result.Density[link];
            // The orifice coefficient uses the density at room pressure; the link's own is about 1% higher at 12 mbar.
            Assert.AreEqual(0.6 * l.AreaM2 * System.Math.Sqrt(2 * dp / rho), gas.Result.Flow[link], 0.01 * gas.Result.Flow[link]);
            Assert.Greater(gas.Result.Flow[link], 1e-3, "tens of cubic metres an hour leak from a burst");
        }

        [Test]
        public void GasBurst_BecomesALeak_NotABurst()
        {
            BuildingSession s = Plumbed();
            DuctSegment seg = s.Committed.Gas.Segments.Find(x => x.DiameterMm == 28)!;
            s.Failures.BurstSampled(s.CommittedSnapshot!, s.Committed, new OfflineBurstSample { Fluid = Gas, SegmentId = seg.Id, Edge = BuildingFailures.Edges(Gas, s.Committed.Gas, seg)[0] }, 0);
            Assert.AreEqual(0, s.Record.Failures.Bursts.Count);
            Assert.AreEqual(1, s.Record.Failures.Leaks.Count);
            Assert.AreEqual("burst", s.Record.Failures.Leaks[0].Cause);
        }

        [Test]
        public void WeepingGasPipe_LeaksAtFivePercentOfTheBurstArea()
        {
            BuildingSession s = Plumbed();
            DuctSegment seg = s.Committed.Gas.Segments.Find(x => x.DiameterMm == 28)!;
            foreach (string e in BuildingFailures.Edges(Gas, s.Committed.Gas, seg)) s.Record.Failures.Stress[e] = 0.7;
            // Above its rating, so stress keeps moving: run the failure tick with the supply far over pressure.
            s.GasSupply = new WaterSupply(true, 30e5);
            s.Execute(new SetIntakeEquipmentCommand("gas", ""));
            s.Record.Committed = s.Record.Design.Clone();
            s.SolveNow();
            s.Failures.Tick(2, s.CommittedSnapshot, s.Committed, 0);
            LeakState? leak = s.Record.Failures.Leaks.Find(l => l.Cause == "weep");
            Assert.NotNull(leak, "a gas segment at S >= 0.6 weeps");
            GasDef g = s.Data.Balancing.Gas;
            Assert.AreEqual(s.Failures.SegmentArea(seg) * g.BurstLeakAreaFraction * g.WeepLeakFraction, leak!.AreaM2, 1e-12);
        }

        [Test]
        public void FittingLeak_RateIsFlatPerFitting()
        {
            BuildingSession s = Plumbed();
            int fittings = GasSafety.Fittings(s.Committed.Gas).Count;
            Assert.Greater(fittings, 2);
            Assert.AreEqual(0.0005 * fittings / 3600, s.GasSafety.FittingLeakRatePerSecond(fittings), 1e-15);
            LeakState l = s.GasSafety.StartFittingLeak(GasSafety.Fittings(s.Committed.Gas)[0])!;
            Assert.AreEqual("fitting", l.Cause);
            Assert.Less(l.AreaM2, s.Failures.SegmentArea(s.Committed.Gas.Segments[0]) * 0.1);
        }

        [Test]
        public void ClosedShutoffUpstream_StopsTheLeak()
        {
            BuildingSession s = Plumbed();
            LeakState l = BigLeak(s);
            Assert.Greater(s.GasSafety.LeakFlow(l), 0);
            Assert.IsTrue(s.Execute(new SetInlineOnCommand(new TilePos(8, 2), false, Gas)));
            s.SolveNow();
            Assert.AreEqual(0, s.GasSafety.LeakFlow(l), 1e-12);
            Assert.AreEqual(OutletZone.Isolated, Stove(s, "stove-1").Zone);
            Assert.AreNotEqual(OutletZone.Isolated, Stove(s, "stove-2").Zone);
        }

        // ---------- alarm ----------

        [Test]
        public void Alarm_AtHalfTheLowerLimitAnywhere_OrAFifthAtADetector()
        {
            BuildingSession s = Plumbed();
            s.Data.Balancing.Gas.DetectionSeconds = 0;
            FillRoom(s, "kitchen-0", 0.015f);  // 30% of LFL: below the anywhere threshold, above the detector's
            s.GasSafety.UpdateInjections();
            TickFor(s, 2);
            Assert.IsFalse(s.GasSafety.Evacuated, "no detector: nobody smells 30% of LFL");

            BuildingSession d = Plumbed();
            Assert.IsTrue(d.Execute(new PlaceSafetyCommand("gas-detector", new TilePos(7, 3))));
            Assert.IsTrue(d.Commit(), d.CheckCommit().Reason);
            d.SolveNow();
            FillRoom(d, "kitchen-0", 0.015f);
            TickFor(d, 2);
            Assert.IsTrue(d.GasSafety.Evacuated, "a detector alarms at 20% of LFL");
            s.Data.Balancing.Gas.DetectionSeconds = 30;

            s.Tick(1);
        }

        [Test]
        public void Alarm_EvacuatesTheBuilding_AndStartsTheTimer()
        {
            BuildingSession s = Plumbed();
            Assert.IsTrue(s.CommittedEvaluation.Online);
            double gross = s.CommittedEvaluation.Gross;
            Assert.Greater(gross, 0);
            BigLeak(s);
            s.GasSafety.RaiseAlarm();
            s.SolveNow();
            Assert.IsTrue(s.CommittedEvaluation.Evacuated);
            Assert.IsFalse(s.CommittedEvaluation.Online);
            Assert.AreEqual(0, s.CommittedEvaluation.Gross);
            Assert.IsTrue(s.GasSafety.TimerRunning);
            Assert.AreEqual(600, s.GasSafety.TimerLeft, 1e-9);
        }

        [Test]
        public void LeakFillsTheRoom_LayersAsLightGas_AndRaisesTheAlarm()
        {
            BuildingSession s = Plumbed();
            BigLeak(s);
            s.IsOpen = true;
            double t = 0;
            while (!s.GasSafety.Evacuated && t < 900)
            {
                s.Tick(1);
                t += 1;
            }
            Assert.IsTrue(s.GasSafety.Evacuated, "the kitchen should reach 50% of LFL");
            int room = s.Layout.RoomIndexAt(new TilePos(7, 2));
            RoomGridSums sums = Sums(s, room);
            Assert.Greater(sums.Upper, 3 * sums.Lower, "methane rises: the upper layer holds most of it");
        }

        sealed class RoomGridSums { public double Upper, Lower; }

        static RoomGridSums Sums(BuildingSession s, int room)
        {
            var r = new RoomGridSums();
            int n = s.Rooms.Grid.Cells;
            for (int c = 0; c < n; c++)
            {
                if (s.Rooms.Grid.Room[c] != room) continue;
                r.Lower += s.Rooms.State.Gas[c];
                r.Upper += s.Rooms.State.Gas[n + c];
            }
            return r;
        }

        // ---------- the timer and the explosion check ----------

        [Test]
        public void TimerEnd_ExplodesOnlyInRangeNearAnIgnitionSource()
        {
            // In range next to the stove: it explodes.
            BuildingSession hot = Plumbed();
            double energy = -1;
            hot.GasSafety.Exploded += e => energy = e;
            BigLeak(hot);
            FillRoom(hot, "kitchen-0", 0.08f);
            hot.GasSafety.RaiseAlarm();
            hot.Record.Failures.Gas.TimerLeft = 1.5;
            TickFor(hot, 3);
            Assert.Greater(energy, 0, "8% methane around the stove when the timer ends");

            // Too rich: above the upper limit nothing burns, and the timer starts again.
            BuildingSession rich = Plumbed();
            double richEnergy = -1;
            rich.GasSafety.Exploded += e => richEnergy = e;
            BigLeak(rich);
            FillRoom(rich, "kitchen-0", 0.20f);
            rich.GasSafety.RaiseAlarm();
            rich.Record.Failures.Gas.TimerLeft = 1.5;
            TickFor(rich, 3);
            Assert.AreEqual(-1, richEnergy);
            Assert.Greater(rich.GasSafety.TimerLeft, 500, "the timer restarts; the danger continues until fixed");

            // In range, but in a room with nothing to light it.
            BuildingSession far = Plumbed();
            double farEnergy = -1;
            far.GasSafety.Exploded += e => farEnergy = e;
            BigLeak(far);
            FillRoom(far, "bedroom", 0.08f);
            far.GasSafety.RaiseAlarm();
            far.Record.Failures.Gas.TimerLeft = 1.5;
            TickFor(far, 3);
            Assert.AreEqual(-1, farEnergy);
        }

        [Test]
        public void Ignition_PanelAndEquipmentAndStoves_NotSparkFree()
        {
            BuildingSession s = Plumbed();
            HashSet<TilePos> tiles = GasSafety.IgnitionTiles(s.Data, s.Layout, s.Committed);
            Assert.IsTrue(tiles.Contains(new TilePos(0, 7)), "the panel");
            Assert.IsTrue(tiles.Contains(new TilePos(0, 2)), "the fan at the intake");
            Assert.IsTrue(tiles.Contains(new TilePos(7, 1)), "the stove");
            Assert.IsTrue(s.Execute(new SetSparkFreeCommand("power", true)));
            Assert.IsTrue(s.Execute(new SetSparkFreeCommand("air-intake", true)));
            Assert.Greater(s.PendingCost.Net, 0, "spark-free costs extra");
            Assert.IsTrue(s.Commit(), s.CheckCommit().Reason);
            tiles = GasSafety.IgnitionTiles(s.Data, s.Layout, s.Committed);
            Assert.IsFalse(tiles.Contains(new TilePos(0, 7)));
            Assert.IsFalse(tiles.Contains(new TilePos(0, 2)));
            Assert.IsTrue(tiles.Contains(new TilePos(7, 1)), "a burner stays a burner");
        }

        [Test]
        public void SparkFree_AddsHalfThePrice_WithAMinimumForThePanel()
        {
            BuildingSession s = Plumbed();
            Assert.IsTrue(s.Execute(new SetSparkFreeCommand("air-intake", true)));
            Assert.AreEqual(40 * 0.5, System.Math.Max(s.PendingCost.Net, 0), 30.01, "small axial fan: +50%, at least the minimum");
            Assert.AreEqual(s.Data.Balancing.Gas.SparkFreeMinPrice, Pricing.SparkFreePrice(s.Data, 0), 1e-9, "the free basic panel still costs the minimum");
            Assert.AreEqual(150, Pricing.SparkFreePrice(s.Data, 300), 1e-9);
        }

        [Test]
        public void AutoShutoff_StopsTheLeak_AndTheTimerNeverStarts()
        {
            BuildingSession s = Plumbed(branchPart: "auto-shutoff-valve");
            LeakState l = BigLeak(s);
            Assert.Greater(s.GasSafety.LeakFlow(l), 0);
            s.GasSafety.RaiseAlarm();
            s.SolveNow();
            Assert.IsTrue(s.GasSafety.Evacuated);
            Assert.IsFalse(s.GasSafety.TimerRunning, "an automatic shutoff stops escalation entirely");
            Assert.AreEqual(0, s.GasSafety.LeakFlow(l), 1e-12);
            Assert.AreNotEqual(OutletZone.Isolated, Stove(s, "stove-2").Zone, "only the leak's branch closes");
        }

        [Test]
        public void AutoShutoff_ClosesOnPowerLoss()
        {
            BuildingSession s = Plumbed(branchPart: "auto-shutoff-valve");
            Assert.AreNotEqual(OutletZone.Isolated, Stove(s, "stove-1").Zone);
            s.PowerSpeed = 0;
            s.SolveNow();
            Assert.AreEqual(OutletZone.Isolated, Stove(s, "stove-1").Zone);
        }

        [Test]
        public void AutoShutoff_NotOnTheLeaksPath_DoesNotHelp()
        {
            BuildingSession s = Plumbed(branchPart: "auto-shutoff-valve");
            // A leak on the upper floor: the valve on the ground-floor branch is not upstream of it.
            LeakState up = s.Failures.AddLeak("damage", "", new TilePos(7, 3, 1), 1e-5, 0);
            s.SolveNow();
            s.GasSafety.RaiseAlarm();
            s.SolveNow();
            Assert.IsTrue(s.GasSafety.TimerRunning);
            Assert.Greater(s.GasSafety.LeakFlow(up), 0);
        }

        [Test]
        public void AllClear_NeedsTheRepairAndClearAir()
        {
            BuildingSession s = Plumbed();
            LeakState l = BigLeak(s);
            s.GasSafety.RaiseAlarm();
            s.SolveNow();
            // Clean air alone is not enough while the leak is not repaired.
            s.Execute(new SetInlineOnCommand(new TilePos(8, 2), false, Gas));
            s.SolveNow();
            System.Array.Clear(s.Rooms.State.Gas, 0, s.Rooms.State.Gas.Length);
            TickFor(s, 2);
            Assert.IsTrue(s.GasSafety.Evacuated);
            // Repaired, but gas is still there.
            Assert.IsTrue(s.RepairLeak(l.Key, true));
            FillRoom(s, "kitchen-0", 0.02f);
            TickFor(s, s.Data.Balancing.Failure.RepairEmergencySeconds + 2);
            Assert.AreEqual(0, s.Record.Failures.Leaks.Count);
            bool stillEvacuated = s.GasSafety.Evacuated;
            // Clear air: below 10% of LFL everywhere ends it.
            System.Array.Clear(s.Rooms.State.Gas, 0, s.Rooms.State.Gas.Length);
            TickFor(s, 2);
            Assert.IsFalse(s.GasSafety.Evacuated);
            Assert.IsTrue(stillEvacuated || s.Rooms.GasPresent == false);
        }

        [Test]
        public void Ventilation_ClearsGasFasterThanAClosedRoom()
        {
            // The same gas in the same kitchen, with and without the vent running.
            BuildingSession vented = Plumbed();
            BuildingSession still = Plumbed();
            still.Execute(new SetEquipmentOnCommand("air-intake", false));
            still.SolveNow();
            foreach (BuildingSession s in new[] { vented, still })
            {
                FillRoom(s, "kitchen-0", 0.01f);
                TickFor(s, 300);
            }
            int room = vented.Layout.RoomIndexAt(new TilePos(7, 2));
            Assert.Less(vented.Rooms.Rooms[room].GasMean, still.Rooms.Rooms[room].GasMean * 0.9);
        }

        [Test]
        public void PassiveVent_ClearsGas_AndCountsAsAirChanges()
        {
            BuildingSession s = Plumbed();
            s.Execute(new SetEquipmentOnCommand("air-intake", false));
            Assert.IsFalse(new PlaceSafetyCommand("passive-vent-opening", new TilePos(7, 2)).Execute(s), "an interior tile has no outside wall");
            Assert.IsTrue(s.Execute(new PlaceSafetyCommand("passive-vent-opening", new TilePos(9, 3))));
            Assert.IsTrue(s.Commit(), s.CheckCommit().Reason);
            s.SolveNow();
            double ach = CodeChecker.AirChanges(s.Data, s.Layout, s.Committed, s.CommittedSnapshot!, s.Layout.RoomIndexAt(new TilePos(9, 3)));
            Assert.AreEqual(30.0 / (4 * 5 * 2.7), ach, 1e-6, "30 m³/h at wind factor 1");

            BuildingSession closed = Plumbed();
            closed.Execute(new SetEquipmentOnCommand("air-intake", false));
            closed.SolveNow();
            foreach (BuildingSession x in new[] { s, closed })
            {
                FillRoom(x, "kitchen-0", 0.01f);
                TickFor(x, 300);
            }
            int room = s.Layout.RoomIndexAt(new TilePos(7, 2));
            Assert.Less(s.Rooms.Rooms[room].GasMean, closed.Rooms.Rooms[room].GasMean);
        }

        // ---------- offline ----------

        static GameContext Fixture(out BuildingSession s, string part = "shutoff-valve")
        {
            var ctx = new GameContext(Data, GameContext.NewFixtureGame(Data, "gas-house"));
            s = ctx.Building!;
            s.Game.Wallet = 1e7;
            s.Record.EverOnline = true;
            Assert.IsTrue(s.Execute(new PlaceRunCommand(RunOn(0, (9, 1), (9, 5)), 28, "copper", false, Gas)));
            Assert.IsTrue(s.Execute(new PlaceRunCommand(RunOn(0, (9, 2), (7, 2), (7, 1)), 22, "copper", false, Gas)));
            Assert.IsTrue(s.Execute(new PlaceRunCommand(new List<TilePos> { new TilePos(9, 5, 0), new TilePos(9, 5, 1) }, 28, "copper", false, Gas)));
            Assert.IsTrue(s.Execute(new PlaceRunCommand(RunOn(1, (9, 5), (9, 4), (7, 4), (7, 1)), 22, "copper", false, Gas)));
            Assert.IsTrue(s.Execute(new PlaceInlineCommand(new TilePos(8, 2), part, false, Gas)));
            Assert.IsTrue(s.Execute(new SetIntakeEquipmentCommand("gas", "gas-regulator")));
            Assert.IsTrue(s.Commit(), s.CheckCommit().Reason);
            s.SolveNow();
            ctx.Leave();
            return ctx;
        }

        [Test]
        public void Offline_TheTimerPauses_AndTheReportListsTheBuildingFirst()
        {
            GameContext ctx = Fixture(out BuildingSession s);
            BigLeak(s);
            s.GasSafety.RaiseAlarm();
            s.Record.Failures.Gas.TimerLeft = 321;
            OfflineReport r = OfflineCatchUp.Run(ctx, 6 * 3600, 12);
            Assert.AreEqual(321, s.GasSafety.TimerLeft, 1e-9, "every catastrophe timer pauses offline");
            Assert.AreEqual(1, r.PausedTimers.Count);
            Assert.AreEqual(321, r.PausedTimers[0].SecondsLeft, 1e-9);
            Assert.AreEqual(0, r.Gross, 1e-9, "an evacuated building pays nothing");
            Assert.Greater(ctx.FindSession("gas-house")!.Rooms.Rooms.Length, 0);
        }

        [Test]
        public void Offline_ALeakReachesTheAlarm_ButNeverTheExplosion()
        {
            GameContext ctx = Fixture(out BuildingSession s);
            BigLeak(s);
            OfflineReport r = OfflineCatchUp.Run(ctx, 6 * 3600, 12);
            Assert.AreEqual(1, r.Alarms);
            Assert.IsTrue(s.GasSafety.Evacuated);
            Assert.IsTrue(s.GasSafety.TimerRunning);
            Assert.AreEqual(s.Data.Balancing.Failure.ExplosionTimerSeconds, s.GasSafety.TimerLeft, 1e-9);
            Assert.AreEqual(0, s.Game.Statistics.Explosions);
            Assert.IsTrue(s.Rooms.GasPresent, "on return the full grid takes over with the gas in it");
        }

        [Test]
        public void Offline_AutoShutoff_StopsEscalationEntirely()
        {
            GameContext ctx = Fixture(out BuildingSession s, "auto-shutoff-valve");
            BigLeak(s);
            OfflineReport r = OfflineCatchUp.Run(ctx, 6 * 3600, 12);
            Assert.IsTrue(s.GasSafety.Evacuated, "the alarm stage still evacuates");
            Assert.IsFalse(s.GasSafety.TimerRunning);
            Assert.AreEqual(0, r.PausedTimers.Count);
        }

        [Test]
        public void Offline_ContractCrewRepairsTheLeak_AndTheBuildingReopens()
        {
            GameContext ctx = Fixture(out BuildingSession s);
            s.Record.Failures.MaintenanceContract = true;
            BigLeak(s);
            OfflineReport r = OfflineCatchUp.Run(ctx, 24 * 3600, 12);
            Assert.AreEqual(0, s.Record.Failures.Leaks.FindAll(l => l.Cause == "burst").Count);
            Assert.AreEqual(1, r.RepairsDone + 0 * r.Leaks, 1.0);
        }

        [Test]
        public void WellMixed_ApproachesTheBalance_AndTimeToReachInverts()
        {
            double c = GasSafety.WellMixed(0, 0.01, 0.09, 30, 1e6);
            Assert.AreEqual(0.1, c, 1e-9, "c∞ = leak / (leak + fresh)");
            double t = GasSafety.TimeToReach(0, 0.05, 0.01, 0.09, 30);
            Assert.AreEqual(0.05, GasSafety.WellMixed(0, 0.01, 0.09, 30, t), 1e-9);
            Assert.IsTrue(double.IsPositiveInfinity(GasSafety.TimeToReach(0, 0.2, 0.01, 0.09, 30)), "ventilation holds it below the threshold");
        }

        // ---------- certificate ----------

        [Test]
        public void Certificate_ComesWithTheFirstGasPipe_AndExpiryHalvesPay()
        {
            BuildingSession s = Plumbed();
            GasCertificateState c = s.Record.Failures.Certificate;
            Assert.IsTrue(c.Issued);
            Assert.AreEqual(48 * 3600, c.SecondsLeft, 1e-6);
            double gross = s.CommittedEvaluation.Gross;
            s.GasSafety.TickCertificate(48 * 3600 + 1);
            Assert.IsTrue(s.GasSafety.CertificateExpired);
            s.SolveNow();
            Assert.AreEqual(gross * 0.5, s.CommittedEvaluation.Gross, gross * 0.01);
            Assert.IsTrue(s.CommittedEvaluation.Online);
        }

        [Test]
        public void Certificate_RenewalPasses_OrFailsOnALeak()
        {
            BuildingSession s = Plumbed();
            GasCertificateState c = s.Record.Failures.Certificate;
            s.GasSafety.TickCertificate(47 * 3600);
            double wallet = s.Game.Wallet;
            Assert.IsTrue(s.GasSafety.RenewCertificate());
            Assert.AreEqual(wallet - 100 * 25, s.Game.Wallet, 1e-6, "$100 × the tier factor");
            s.GasSafety.TickCertificate(600);
            Assert.AreEqual(0, c.LastFailure.Length);
            Assert.AreEqual(48 * 3600, c.SecondsLeft, 1e-6);

            BigLeak(s);
            s.GasSafety.TickCertificate(47 * 3600);
            Assert.IsTrue(s.GasSafety.RenewCertificate());
            s.GasSafety.TickCertificate(600);
            StringAssert.Contains("leak", c.LastFailure);
            Assert.Less(c.SecondsLeft, 2 * 3600);
        }

        [Test]
        public void Certificate_CountsWhileAway_AndTheReportSaysSo()
        {
            GameContext ctx = Fixture(out BuildingSession s);
            OfflineReport r = OfflineCatchUp.Run(ctx, 50 * 3600, 12);
            Assert.IsTrue(s.GasSafety.CertificateExpired);
            Assert.AreEqual(1, r.CertificatesExpired);
        }
    }

    static class GasTestExtensions
    {
        public static HeadLoss.Sim.Network.NetworkGraph Graph(this FluidSolve s) => s.Compiled.Graph;
    }
}
