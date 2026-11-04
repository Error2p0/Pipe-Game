using System.Collections.Generic;
using HeadLoss.Core;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.Commands;
using HeadLoss.Game.Failures;
using HeadLoss.Game.Session;
using NUnit.Framework;
using static HeadLoss.Game.Tests.GameTestUtil;

namespace HeadLoss.Game.Tests
{
    /// <summary>
    /// Switching off a fan or pump (<c>05-simulation.md</c> §2): it slows to a stop over <c>devices.runDownSeconds</c> rather than
    /// cutting, and the flow, pressure, power and money follow it.
    /// </summary>
    public class DeviceRunDownTests
    {
        const double Dt = 0.05;
        const string Intake = "air-intake";
        static double Seconds => Data.Balancing.Devices.RunDownSeconds;

        /// <summary>Test house with a fan and one vent, built and solved.</summary>
        static BuildingSession FanHouse()
        {
            BuildingSession s = TestHouseWithFan();
            DrawAndCommit(s, Run((0, 3), (3, 3), (3, 5)));
            return s;
        }

        /// <summary>Two-floor water house in copper with a booster pump at the connection, built and solved.</summary>
        static BuildingSession PumpHouse()
        {
            BuildingSession s = Open("water-house");
            Assert.IsTrue(s.Execute(new PlaceRunCommand(Run((9, 1), (9, 5)), 22, "copper", false, "water")));
            Assert.IsTrue(s.Execute(new PlaceRunCommand(Run((9, 2), (7, 2), (7, 1)), 15, "copper", false, "water")));
            Assert.IsTrue(s.Execute(new SetIntakeEquipmentCommand("water", "small-booster-pump")));
            s.Game.Wallet = 1e6;
            s.Record.EverOnline = true;
            Assert.IsTrue(s.Commit(), s.CheckCommit().Reason);
            s.SolveNow();
            return s;
        }

        static void TickFor(BuildingSession s, double seconds)
        {
            int n = (int)System.Math.Ceiling(seconds / Dt);
            for (int i = 0; i < n; i++) s.Tick(Dt);
        }

        static double VentFlow(BuildingSession s) => s.OutletFlow("vent-1");

        [Test]
        public void FanOff_SlowsToAStop_OverTheRunDownTime()
        {
            BuildingSession s = FanHouse();
            double q0 = VentFlow(s), p0 = s.CommittedEvaluation.PowerW;
            Assert.Greater(q0, 0);
            Assert.Greater(p0, 0);

            Assert.IsTrue(s.Execute(new SetEquipmentOnCommand(Intake, false)));
            Assert.IsFalse(s.Committed.EquipmentAt(Intake)!.On, "the switch itself flips at once");
            s.Tick(Dt);
            Assert.Greater(VentFlow(s), q0 * 0.9, "no cut on the first frame");

            TickFor(s, Seconds * 0.5);
            double mid = VentFlow(s);
            Assert.Greater(mid, 0, "still moving air halfway through");
            Assert.Less(mid, q0, "and slower than it was");
            Assert.Greater(s.CommittedEvaluation.PowerW, 0, "power follows");
            Assert.Less(s.CommittedEvaluation.PowerW, p0);

            TickFor(s, Seconds * 0.5 + 0.5);
            s.SolveNow();
            Assert.AreEqual(0, VentFlow(s), 1e-9, "stopped at the end");
            Assert.AreEqual(0, s.CommittedEvaluation.PowerW, 1e-9);
            Assert.AreEqual(0, s.RunDownLeft(DamageKeys.Opening(Intake)), 1e-12);
        }

        [Test]
        public void FanOff_FlowNeverJumps()
        {
            BuildingSession s = FanHouse();
            double q0 = VentFlow(s), last = q0, largest = 0;
            s.Execute(new SetEquipmentOnCommand(Intake, false));
            for (double t = 0; t < Seconds + 0.5; t += Dt)
            {
                s.Tick(Dt);
                Assert.IsFalse(s.Settling, $"t = {t}");
                double q = VentFlow(s);
                Assert.LessOrEqual(q, last + q0 * 1e-6, $"flow only falls, t = {t}");
                largest = System.Math.Max(largest, last - q);
                last = q;
            }
            Assert.AreEqual(0, last, 1e-9);
            Assert.Less(largest, q0 * 0.2, "no single step is a cut");
        }

        [Test]
        public void InlineFanOff_RunsDownToo()
        {
            BuildingSession s = Open("test-house");
            s.Execute(new SetIntakeEquipmentCommand(Intake, ""));
            Draw(s, Run((0, 3), (8, 3), (8, 5)));
            var tile = new TilePos(4, 3);
            s.Execute(new PlaceInlineCommand(tile, "inline-duct-fan"));
            s.SolveNow();
            double q0 = s.OutletFlow("vent-2");
            Assert.Greater(q0, 0);
            s.Execute(new SetInlineOnCommand(tile, false));
            TickFor(s, Seconds * 0.5);
            Assert.Greater(s.OutletFlow("vent-2"), 0);
            Assert.Less(s.OutletFlow("vent-2"), q0);
            Assert.Greater(s.RunDownLeft(DamageKeys.Inline("air", tile)), 0);
            TickFor(s, Seconds * 0.5 + 0.5);
            s.SolveNow();
            Assert.AreEqual(0, s.OutletFlow("vent-2"), 1e-9);
        }

        [Test]
        public void PumpOff_TapPressureFallsAway_WithNoJumpAtTheEnd()
        {
            BuildingSession s = PumpHouse();
            double Tap() => s.CommittedEvaluation.Find("tap-1")!.Delivery;
            double Flow() => s.CommittedSnapshot!.Water!.Compiled.SupplyFlow(s.CommittedSnapshot.Water.Result);
            double p0 = Tap(), last = p0, largest = 0;
            double q0 = Flow(), lastQ = q0, largestQ = 0;
            Assert.Greater(p0, 0);
            Assert.Greater(q0, 0);
            s.Execute(new SetEquipmentOnCommand("water", false));
            s.Tick(Dt);
            Assert.Greater(Tap(), p0 * 0.9, "no cut on the first frame");
            double half = -1;
            for (double t = Dt; t < Seconds + 0.5; t += Dt)
            {
                s.Tick(Dt);
                Assert.IsFalse(s.Settling, $"t = {t}");
                double p = Tap();
                largest = System.Math.Max(largest, last - p);
                last = p;
                largestQ = System.Math.Max(largestQ, lastQ - Flow());
                lastQ = Flow();
                if (half < 0 && t >= Seconds * 0.5)
                {
                    half = p;
                    Assert.AreEqual(q0 * 0.5, Flow(), q0 * 0.1, "flow fades with the speed");
                }
            }
            s.SolveNow();
            Assert.Greater(half, 0, "water still arrives halfway through");
            Assert.Less(half, p0);
            Assert.AreEqual(0, Tap(), 1e-6, "a stopped pump is closed to water");
            Assert.Less(largest, p0 * 0.25, "no single step is a cut");
            Assert.Less(largestQ, q0 * 0.25, "neither for the flow");
            Assert.AreEqual(0, Flow(), 1e-12);
        }

        /// <summary>
        /// A slow stop is a gentle one: 05 §6's surge scales with how fast the flow changes. Needs an inline pump, with pipe
        /// back to the source: at the connection the distance to the reflecting boundary is 0, and so is the surge.
        /// </summary>
        [Test]
        public void InlinePumpRunDown_RaisesFarLessSurgeThanASuddenStop()
        {
            var tile = new TilePos(8, 2);
            BuildingSession InlinePumpHouse()
            {
                BuildingSession h = Open("water-house");
                Assert.IsTrue(h.Execute(new PlaceRunCommand(Run((9, 1), (9, 5)), 22, "copper", false, "water")));
                Assert.IsTrue(h.Execute(new PlaceRunCommand(Run((9, 2), (7, 2), (7, 1)), 15, "copper", false, "water")));
                Assert.IsTrue(h.Execute(new PlaceInlineCommand(tile, "inline-booster-pump", false, "water")));
                h.Game.Wallet = 1e6;
                h.Record.EverOnline = true;
                Assert.IsTrue(h.Commit(), h.CheckCommit().Reason);
                h.SolveNow();
                return h;
            }

            BuildingSession sudden = InlinePumpHouse();
            sudden.Execute(new SetInlineOnCommand(tile, false, "water"));
            sudden.SolveNow();
            double hard = sudden.Failures.SurgePa("water");
            Assert.Greater(hard, 0, "a sudden stop still hammers (blast damage takes this path)");

            BuildingSession slow = InlinePumpHouse();
            slow.Execute(new SetInlineOnCommand(tile, false, "water"));
            double most = 0;
            for (double t = 0; t < Seconds + 0.5; t += Dt)
            {
                slow.Tick(Dt);
                most = System.Math.Max(most, slow.Failures.SurgePa("water"));
            }
            Assert.Less(most, hard * 0.2);
        }

        /// <summary>A second way to the supply: stopping the inline pump on one path must not fade a tap the other path still feeds.</summary>
        [Test]
        public void InlinePumpWithABypass_LeavesTheFedTapAlone()
        {
            var tile = new TilePos(9, 3);
            BuildingSession s = Open("water-house");
            Assert.IsTrue(s.Execute(new PlaceRunCommand(Run((9, 1), (9, 5)), 22, "copper", false, "water")));
            Assert.IsTrue(s.Execute(new PlaceRunCommand(Run((9, 2), (7, 2), (7, 1)), 15, "copper", false, "water")));
            Assert.IsTrue(s.Execute(new PlaceInlineCommand(tile, "inline-booster-pump", false, "water")));
            s.Game.Wallet = 1e6;
            s.Record.EverOnline = true;
            Assert.IsTrue(s.Commit(), s.CheckCommit().Reason);
            s.SolveNow();
            // The kitchen tap branches off upstream of the pump, so the pump is not on its path at all.
            double p0 = s.CommittedEvaluation.Find("tap-1")!.Delivery;
            s.Execute(new SetInlineOnCommand(tile, false, "water"));
            TickFor(s, Seconds * 0.5);
            Assert.AreEqual(p0, s.CommittedEvaluation.Find("tap-1")!.Delivery, p0 * 0.02);
            var water = s.CommittedSnapshot!.Water!;
            Assert.AreEqual(water.Compiled.Graph.Nodes[water.Compiled.OutletNode["tap-1"]].Demand,
                NetworkDemandAtFullSpeed(s, "tap-1"), 1e-12, "it keeps its full design flow");
        }

        static double NetworkDemandAtFullSpeed(BuildingSession s, string outlet) =>
            HeadLoss.Game.Networks.NetworkCompiler.DesignFlow(s.Data, s.Layout, s.Layout.FindOutlet(outlet)!);

        [Test]
        public void SwitchedBackOn_DuringTheRunDown_RunsAgainAtOnce()
        {
            BuildingSession s = FanHouse();
            double q0 = VentFlow(s);
            s.Execute(new SetEquipmentOnCommand(Intake, false));
            TickFor(s, Seconds * 0.7);
            Assert.Less(VentFlow(s), q0 * 0.9);
            s.Execute(new SetEquipmentOnCommand(Intake, true));
            s.Tick(Dt);
            Assert.AreEqual(q0, VentFlow(s), q0 * 0.01);
            Assert.AreEqual(0, s.RunDownLeft(DamageKeys.Opening(Intake)), 1e-12);
            TickFor(s, Seconds);
            Assert.AreEqual(q0, VentFlow(s), q0 * 0.01, "and it stays running");
        }

        [Test]
        public void UndoingTheSwitch_DuringTheRunDown_RunsAgain()
        {
            BuildingSession s = FanHouse();
            double q0 = VentFlow(s);
            s.Execute(new SetEquipmentOnCommand(Intake, false));
            TickFor(s, Seconds * 0.5);
            Assert.IsTrue(s.Undo());
            s.Tick(Dt);
            Assert.AreEqual(q0, VentFlow(s), q0 * 0.01);
        }

        [Test]
        public void AlreadyOff_WhenLoaded_DoesNotRunDown()
        {
            BuildingSession s = TestHouseWithFan();
            Draw(s, Run((0, 3), (3, 3), (3, 5)));
            s.Game.Wallet = 1e6;
            s.Record.EverOnline = true;
            s.Execute(new SetEquipmentOnCommand(Intake, false));
            Assert.IsTrue(s.Commit(), s.CheckCommit().Reason);
            var loaded = new BuildingSession(s.Data, s.Layout, s.Record, s.Game);
            loaded.Tick(Dt);
            Assert.AreEqual(0, loaded.RunDownLeft(DamageKeys.Opening(Intake)), 1e-12);
            Assert.AreEqual(0, loaded.OutletFlow("vent-1"), 1e-12);
        }

        [Test]
        public void PricesPreviewsAndOfflineUseTheStoppedState()
        {
            BuildingSession s = FanHouse();
            s.Execute(new SetEquipmentOnCommand(Intake, false));
            TickFor(s, Seconds * 0.25);
            Assert.Greater(VentFlow(s), 0, "still running down");

            Assert.AreEqual(0, s.EvaluateWith(_ => { }).PowerW, 1e-9, "the shop's compare view is a steady state");
            Assert.AreEqual(0, s.SteadyState(20).PowerW, 1e-9, "so is offline catch-up");

            s.PrepareOffline();
            Assert.AreEqual(0, VentFlow(s), 1e-9, "offline nothing is left running down");
            Assert.AreEqual(0, s.RunDownLeft(DamageKeys.Opening(Intake)), 1e-12);
            s.Tick(Dt);
            Assert.AreEqual(0, VentFlow(s), 1e-9, "and it does not start again afterwards");
        }

        [Test]
        public void PanelTrip_ThingsCoastToAStop_IncomeStopsAtOnce_ResetRunsAgain()
        {
            BuildingSession s = FanHouse();
            s.Record.Committed.Equipment.RemoveAll(e => e.OpeningId == "power");
            s.Record.Committed.Equipment.Add(new EquipmentState { EquipmentId = "panel-basic", OpeningId = "power" });
            TickFor(s, 2);
            double q0 = VentFlow(s), draw = s.RequestedPowerW;
            Assert.Greater(q0, 0);
            EquipmentDef panel = Data.EquipmentItem("panel-basic");
            double limit = panel.PanelLimitKW;
            try
            {
                panel.PanelLimitKW = draw / 2000;
                for (int i = 0; i < 1000 && !s.Record.PanelTripped; i++) s.Tick(Dt);
                Assert.IsTrue(s.Record.PanelTripped);
                Assert.AreEqual(0, s.PowerSpeed, 1e-9, "the power is gone at once");
                TickFor(s, Seconds * 0.25);
                Assert.Greater(VentFlow(s), 0, "the rotor is still coasting");
                Assert.Less(VentFlow(s), q0);
                Assert.IsFalse(s.CommittedEvaluation.Online);
                Assert.AreEqual(0, s.CommittedEvaluation.Gross, 1e-9);
                TickFor(s, Seconds);
                s.SolveNow();
                Assert.AreEqual(0, VentFlow(s), 1e-9);

                panel.PanelLimitKW = limit;
                Assert.IsTrue(s.ResetPanel());
                s.Tick(Dt);
                Assert.AreEqual(q0, VentFlow(s), q0 * 0.05);
            }
            finally
            {
                panel.PanelLimitKW = limit;
            }
        }

        [Test]
        public void HandCrank_IsUntouched()
        {
            // It slows by itself through its own decay (02), and has no switch.
            BuildingSession s = Open("test-house");
            Draw(s, Run((0, 3), (3, 3), (3, 5)));
            s.Crank();
            s.SolveNow();
            Assert.AreEqual(0, s.RunDownLeft(DamageKeys.Opening(Intake)), 1e-12);
            Assert.Greater(s.OutletFlow("vent-1"), 0);
        }

        [Test]
        public void DataHasTheRunDownNumbers()
        {
            DevicesDef d = Data.Balancing.Devices;
            Assert.Greater(d.RunDownSeconds, 0);
            Assert.Greater(d.RunDownResolveStep, 0);
            Assert.Less(d.RunDownResolveStep, 1);
        }
    }
}
