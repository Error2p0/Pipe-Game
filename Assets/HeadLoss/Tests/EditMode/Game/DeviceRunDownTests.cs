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
    /// Switching a fan or pump (<c>05-simulation.md</c> §2): it slows to a stop or starts up over its own time from
    /// <c>equipment.json</c> rather than jumping, and the flow, pressure, power and money follow it.
    /// </summary>
    public class DeviceRunDownTests
    {
        const double Dt = 0.05;
        const string Intake = "air-intake";
        const string Fan = "small-axial-fan", Pump = "small-booster-pump";
        static readonly string IntakeKey = DamageKeys.Opening(Intake), WaterKey = DamageKeys.Opening("water");

        static double Stop(string device) => Data.EquipmentItem(device).RunDownSeconds;
        static double Start(string device) => Data.EquipmentItem(device).SpinUpSeconds;

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
            Assert.IsTrue(s.Execute(new SetIntakeEquipmentCommand("water", Pump)));
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
        static double Tap(BuildingSession s) => s.CommittedEvaluation.Find("tap-1")!.Delivery;
        static double WaterFlow(BuildingSession s) => s.CommittedSnapshot!.Water!.Compiled.SupplyFlow(s.CommittedSnapshot.Water.Result);

        // ---------- slowing down ----------

        [Test]
        public void FanOff_SlowsToAStop_OverItsOwnTime()
        {
            BuildingSession s = FanHouse();
            double q0 = VentFlow(s), p0 = s.CommittedEvaluation.PowerW;
            Assert.Greater(q0, 0);
            Assert.Greater(p0, 0);

            Assert.IsTrue(s.Execute(new SetEquipmentOnCommand(Intake, false)));
            Assert.IsFalse(s.Committed.EquipmentAt(Intake)!.On, "the switch itself flips at once");
            s.Tick(Dt);
            Assert.Greater(VentFlow(s), q0 * 0.9, "no cut on the first frame");
            Assert.IsFalse(s.SpinningUp(IntakeKey));

            TickFor(s, Stop(Fan) * 0.5);
            double mid = VentFlow(s);
            Assert.Greater(mid, 0, "still moving air halfway through");
            Assert.Less(mid, q0, "and slower than it was");
            Assert.Greater(s.CommittedEvaluation.PowerW, 0, "power follows");
            Assert.Less(s.CommittedEvaluation.PowerW, p0);

            TickFor(s, Stop(Fan) * 0.5 + 0.5);
            s.SolveNow();
            Assert.AreEqual(0, VentFlow(s), 1e-9, "stopped at the end");
            Assert.AreEqual(0, s.CommittedEvaluation.PowerW, 1e-9);
            Assert.AreEqual(0, s.SpinShare(IntakeKey), 1e-12);
        }

        [Test]
        public void FanOff_FlowNeverJumps()
        {
            BuildingSession s = FanHouse();
            double q0 = VentFlow(s), last = q0, largest = 0;
            s.Execute(new SetEquipmentOnCommand(Intake, false));
            for (double t = 0; t < Stop(Fan) + 0.5; t += Dt)
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
        public void InlineFanOff_SlowsDownToo()
        {
            BuildingSession s = Open("test-house");
            s.Execute(new SetIntakeEquipmentCommand(Intake, ""));
            Draw(s, Run((0, 3), (8, 3), (8, 5)));
            var tile = new TilePos(4, 3);
            s.Execute(new PlaceInlineCommand(tile, "inline-duct-fan"));
            s.SolveNow();
            double q0 = s.OutletFlow("vent-2"), stop = Stop("inline-duct-fan");
            Assert.Greater(q0, 0);
            s.Execute(new SetInlineOnCommand(tile, false));
            TickFor(s, stop * 0.5);
            Assert.Greater(s.OutletFlow("vent-2"), 0);
            Assert.Less(s.OutletFlow("vent-2"), q0);
            Assert.Greater(s.SpinShare(DamageKeys.Inline("air", tile)), 0);
            TickFor(s, stop * 0.5 + 0.5);
            s.SolveNow();
            Assert.AreEqual(0, s.OutletFlow("vent-2"), 1e-9);
        }

        [Test]
        public void PumpOff_TapPressureAndFlowFadeAway_WithNoJumpAtTheEnd()
        {
            BuildingSession s = PumpHouse();
            double p0 = Tap(s), last = p0, largest = 0;
            double q0 = WaterFlow(s), lastQ = q0, largestQ = 0;
            Assert.Greater(p0, 0);
            Assert.Greater(q0, 0);
            s.Execute(new SetEquipmentOnCommand("water", false));
            s.Tick(Dt);
            Assert.Greater(Tap(s), p0 * 0.8, "no cut on the first frame");
            Assert.Greater(s.SpinShare(WaterKey), 0);
            double half = -1;
            for (double t = Dt; t < Stop(Pump) + 0.5; t += Dt)
            {
                s.Tick(Dt);
                Assert.IsFalse(s.Settling, $"t = {t}");
                double p = Tap(s);
                largest = System.Math.Max(largest, last - p);
                last = p;
                largestQ = System.Math.Max(largestQ, lastQ - WaterFlow(s));
                lastQ = WaterFlow(s);
                if (half < 0 && t >= Stop(Pump) * 0.5)
                {
                    half = p;
                    Assert.AreEqual(q0 * 0.5, WaterFlow(s), q0 * 0.1, "flow fades with the speed");
                }
            }
            s.SolveNow();
            Assert.Greater(half, 0, "water still arrives halfway through");
            Assert.Less(half, p0);
            Assert.AreEqual(0, Tap(s), 1e-6, "a stopped pump is closed to water");
            Assert.Less(largest, p0 * 0.25, "no single step is a cut");
            Assert.Less(largestQ, q0 * 0.25, "neither for the flow");
            Assert.AreEqual(0, WaterFlow(s), 1e-12);
        }

        // ---------- starting up ----------

        [Test]
        public void FanOn_RampsUp_OverItsOwnTime()
        {
            BuildingSession s = FanHouse();
            double q0 = VentFlow(s), p0 = s.CommittedEvaluation.PowerW;
            s.Execute(new SetEquipmentOnCommand(Intake, false));
            TickFor(s, Stop(Fan) + 0.5);
            Assert.AreEqual(0, VentFlow(s), 1e-9);

            Assert.IsTrue(s.Execute(new SetEquipmentOnCommand(Intake, true)));
            s.Tick(Dt);
            Assert.IsTrue(s.SpinningUp(IntakeKey));
            Assert.Less(VentFlow(s), q0 * 0.2, "no jump on the first frame");

            double last = VentFlow(s), largest = 0, mid = -1, midPower = -1;
            for (double t = Dt; t < Start(Fan) + 0.5; t += Dt)
            {
                s.Tick(Dt);
                Assert.IsFalse(s.Settling, $"t = {t}");
                double q = VentFlow(s);
                Assert.GreaterOrEqual(q, last - q0 * 1e-6, $"flow only rises, t = {t}");
                largest = System.Math.Max(largest, q - last);
                last = q;
                if (mid < 0 && t >= Start(Fan) * 0.5)
                {
                    mid = q;
                    midPower = s.CommittedEvaluation.PowerW;
                }
            }
            Assert.Greater(mid, 0);
            Assert.Less(mid, q0, "not there yet halfway through");
            Assert.Less(midPower, p0, "power follows");
            Assert.Less(largest, q0 * 0.25, "no single step is a jump");
            Assert.AreEqual(q0, VentFlow(s), q0 * 0.01, "full flow at the end");
            Assert.AreEqual(p0, s.CommittedEvaluation.PowerW, p0 * 0.01);
            Assert.AreEqual(0, s.SpinShare(IntakeKey), 1e-12);
        }

        [Test]
        public void PumpOn_TapPressureAndFlowBuildUp()
        {
            BuildingSession s = PumpHouse();
            double p0 = Tap(s), q0 = WaterFlow(s);
            s.Execute(new SetEquipmentOnCommand("water", false));
            TickFor(s, Stop(Pump) + 0.5);
            Assert.AreEqual(0, WaterFlow(s), 1e-12);

            s.Execute(new SetEquipmentOnCommand("water", true));
            double lastP = 0, lastQ = 0, largestP = 0, largestQ = 0;
            for (double t = 0; t < Start(Pump) + 0.5; t += Dt)
            {
                s.Tick(Dt);
                Assert.IsFalse(s.Settling, $"t = {t}");
                largestP = System.Math.Max(largestP, Tap(s) - lastP);
                largestQ = System.Math.Max(largestQ, WaterFlow(s) - lastQ);
                lastP = Tap(s);
                lastQ = WaterFlow(s);
            }
            Assert.AreEqual(p0, Tap(s), p0 * 0.01);
            Assert.AreEqual(q0, WaterFlow(s), q0 * 0.01);
            Assert.Less(largestP, p0 * 0.3, "no single step is a jump");
            Assert.Less(largestQ, q0 * 0.3);
        }

        [Test]
        public void SwitchedBackOn_MidStop_TurnsRoundFromWhereItIs()
        {
            BuildingSession s = FanHouse();
            double q0 = VentFlow(s);
            s.Execute(new SetEquipmentOnCommand(Intake, false));
            TickFor(s, Stop(Fan) * 0.7);
            double slowed = VentFlow(s), share = s.SpinShare(IntakeKey);
            Assert.Less(slowed, q0 * 0.9);

            s.Execute(new SetEquipmentOnCommand(Intake, true));
            s.Tick(Dt);
            Assert.IsTrue(s.SpinningUp(IntakeKey));
            Assert.AreEqual(share + Dt / Start(Fan), s.SpinShare(IntakeKey), 1e-9, "carries on from the speed it had");
            Assert.Less(VentFlow(s), q0 * 0.9, "no jump back to full");

            TickFor(s, Start(Fan));
            Assert.AreEqual(q0, VentFlow(s), q0 * 0.01);
            TickFor(s, Stop(Fan));
            Assert.AreEqual(q0, VentFlow(s), q0 * 0.01, "and it stays running");
        }

        [Test]
        public void SwitchedOff_MidStart_TurnsRoundToo()
        {
            BuildingSession s = FanHouse();
            s.Execute(new SetEquipmentOnCommand(Intake, false));
            TickFor(s, Stop(Fan) + 0.5);
            s.Execute(new SetEquipmentOnCommand(Intake, true));
            TickFor(s, Start(Fan) * 0.5);
            double share = s.SpinShare(IntakeKey);
            Assert.Greater(share, 0.3);
            s.Execute(new SetEquipmentOnCommand(Intake, false));
            s.Tick(Dt);
            Assert.IsFalse(s.SpinningUp(IntakeKey));
            Assert.AreEqual(share - Dt / Stop(Fan), s.SpinShare(IntakeKey), 1e-9);
            TickFor(s, Stop(Fan));
            s.SolveNow();
            Assert.AreEqual(0, VentFlow(s), 1e-9);
        }

        [Test]
        public void UndoingTheSwitch_MidStop_RampsBack()
        {
            BuildingSession s = FanHouse();
            double q0 = VentFlow(s);
            s.Execute(new SetEquipmentOnCommand(Intake, false));
            TickFor(s, Stop(Fan) * 0.5);
            Assert.IsTrue(s.Undo());
            TickFor(s, Start(Fan) + 0.5);
            Assert.AreEqual(q0, VentFlow(s), q0 * 0.01);
        }

        // ---------- what stays steady ----------

        /// <summary>The design previews a new device at full speed ("simulate before you buy"), and a Commit keeps what it showed.</summary>
        [Test]
        public void NewlyPlacedFan_IsAtFullSpeedStraightAway()
        {
            BuildingSession s = Open("test-house");
            Draw(s, Run((0, 3), (3, 3), (3, 5)));
            s.SolveNow();
            s.Tick(Dt);
            Assert.IsTrue(s.Execute(new SetIntakeEquipmentCommand(Intake, Fan)));
            s.Tick(Dt);
            Assert.AreEqual(0, s.SpinShare(IntakeKey), 1e-12);
            double q = VentFlow(s);
            Assert.Greater(q, 0);
            s.Game.Wallet = 1e6;
            s.Record.EverOnline = true;
            Assert.IsTrue(s.Commit(), s.CheckCommit().Reason);
            s.Tick(Dt);
            Assert.AreEqual(0, s.SpinShare(IntakeKey), 1e-12);
            Assert.AreEqual(q, VentFlow(s), q * 0.01);
        }

        [Test]
        public void Loaded_DevicesAreSteady()
        {
            BuildingSession s = TestHouseWithFan();
            Draw(s, Run((0, 3), (3, 3), (3, 5)));
            s.Game.Wallet = 1e6;
            s.Record.EverOnline = true;
            s.Execute(new SetEquipmentOnCommand(Intake, false));
            Assert.IsTrue(s.Commit(), s.CheckCommit().Reason);
            var loadedOff = new BuildingSession(s.Data, s.Layout, s.Record, s.Game);
            loadedOff.Tick(Dt);
            Assert.AreEqual(0, loadedOff.SpinShare(IntakeKey), 1e-12);
            Assert.AreEqual(0, loadedOff.OutletFlow("vent-1"), 1e-12);

            s.Execute(new SetEquipmentOnCommand(Intake, true));
            var loadedOn = new BuildingSession(s.Data, s.Layout, s.Record, s.Game);
            loadedOn.Tick(Dt);
            Assert.AreEqual(0, loadedOn.SpinShare(IntakeKey), 1e-12);
            Assert.Greater(loadedOn.OutletFlow("vent-1"), 0, "running from the first frame");
        }

        [Test]
        public void PreviewsAndOffline_SeeTheSteadyState()
        {
            BuildingSession s = FanHouse();
            double p0 = s.CommittedEvaluation.PowerW;
            s.Execute(new SetEquipmentOnCommand(Intake, false));
            TickFor(s, Stop(Fan) * 0.25);
            Assert.Greater(VentFlow(s), 0, "still slowing");
            Assert.AreEqual(0, s.EvaluateWith(_ => { }).PowerW, 1e-9, "the shop's compare view shows it stopped");
            Assert.AreEqual(0, s.SteadyState(20).PowerW, 1e-9, "so does offline catch-up");

            s.PrepareOffline();
            Assert.AreEqual(0, VentFlow(s), 1e-9, "offline nothing is left slowing");
            Assert.AreEqual(0, s.SpinShare(IntakeKey), 1e-12);
            s.Tick(Dt);
            Assert.AreEqual(0, VentFlow(s), 1e-9, "and it does not start again afterwards");

            s.Execute(new SetEquipmentOnCommand(Intake, true));
            TickFor(s, Start(Fan) * 0.25);
            Assert.AreEqual(p0, s.EvaluateWith(_ => { }).PowerW, p0 * 0.01, "starting up, previews show it running");
            s.PrepareOffline();
            Assert.AreEqual(p0, s.CommittedEvaluation.PowerW, p0 * 0.01);
            Assert.AreEqual(0, s.SpinShare(IntakeKey), 1e-12);
        }

        // ---------- the panel ----------

        [Test]
        public void PanelTrip_ThingsCoastToAStop_IncomeStopsAtOnce_ResetRampsBackUp()
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
                TickFor(s, Stop(Fan) * 0.25);
                Assert.Greater(VentFlow(s), 0, "the rotor is still coasting");
                Assert.Less(VentFlow(s), q0);
                Assert.IsFalse(s.CommittedEvaluation.Online);
                Assert.AreEqual(0, s.CommittedEvaluation.Gross, 1e-9);
                TickFor(s, Stop(Fan));
                s.SolveNow();
                Assert.AreEqual(0, VentFlow(s), 1e-9);

                panel.PanelLimitKW = limit;
                Assert.IsTrue(s.ResetPanel());
                s.Tick(Dt);
                Assert.IsTrue(s.SpinningUp(IntakeKey));
                Assert.Less(VentFlow(s), q0 * 0.2, "a reset ramps up too");
                TickFor(s, Start(Fan) + 0.5);
                Assert.AreEqual(q0, VentFlow(s), q0 * 0.05);
            }
            finally
            {
                panel.PanelLimitKW = limit;
            }
        }

        // ---------- valves stay instant, the crank keeps its own decay ----------

        [Test]
        public void ShutoffValve_ClosesAtOnce()
        {
            BuildingSession s = PumpHouse();
            var tile = new TilePos(8, 2);
            Assert.IsTrue(s.Execute(new PlaceInlineCommand(tile, "shutoff-valve", false, "water")));
            Assert.IsTrue(s.Commit(), s.CheckCommit().Reason);
            s.SolveNow();
            Assert.Greater(Tap(s), 0);
            s.Execute(new SetInlineOnCommand(tile, false, "water"));
            s.Tick(Dt);
            Assert.AreEqual(0, Tap(s), 1e-9, "closed on the next solve");
        }

        [Test]
        public void HandCrank_IsUntouched()
        {
            BuildingSession s = Open("test-house");
            Draw(s, Run((0, 3), (3, 3), (3, 5)));
            s.Crank();
            s.SolveNow();
            Assert.AreEqual(0, s.SpinShare(IntakeKey), 1e-12);
            Assert.Greater(s.OutletFlow("vent-1"), 0);
        }

        // ---------- surges and data ----------

        /// <summary>
        /// A slow stop is a gentle one: 05 §6's surge scales with how fast the flow changes. Needs an inline pump, with pipe
        /// back to the source: at the connection the distance to the reflecting boundary is 0, and so is the surge.
        /// </summary>
        [Test]
        public void InlinePumpSlowingDown_RaisesFarLessSurgeThanASuddenStop()
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
            for (double t = 0; t < Stop("inline-booster-pump") + 0.5; t += Dt)
            {
                slow.Tick(Dt);
                most = System.Math.Max(most, slow.Failures.SurgePa("water"));
            }
            Assert.Less(most, hard * 0.2);
        }

        /// <summary>A branch off upstream of the pump does not depend on it: its tap keeps its full design flow.</summary>
        [Test]
        public void TapNotBehindThePump_IsLeftAlone()
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
            double p0 = Tap(s);
            s.Execute(new SetInlineOnCommand(tile, false, "water"));
            TickFor(s, Stop("inline-booster-pump") * 0.5);
            Assert.AreEqual(p0, Tap(s), p0 * 0.02);
            var water = s.CommittedSnapshot!.Water!;
            Assert.AreEqual(HeadLoss.Game.Networks.NetworkCompiler.DesignFlow(s.Data, s.Layout, s.Layout.FindOutlet("tap-1")!),
                water.Compiled.Graph.Nodes[water.Compiled.OutletNode["tap-1"]].Demand, 1e-12, "it keeps its full design flow");
        }

        [Test]
        public void EveryFanAndPumpHasItsOwnTimes_BiggerMachinesTakeLonger()
        {
            foreach (EquipmentDef e in Data.Equipment)
            {
                if (e.Curve.Count == 0 || e.SpeedControl == "crank") continue;
                Assert.Greater(e.RunDownSeconds, 0, e.Id);
                Assert.Greater(e.SpinUpSeconds, 0, e.Id);
                Assert.Less(e.SpinUpSeconds, e.RunDownSeconds, $"{e.Id}: a motor drives it up faster than it coasts down");
            }
            Assert.Greater(Stop("centrifugal-blower"), Stop(Fan));
            Assert.Greater(Stop("industrial-ahu"), Stop("centrifugal-blower"));
            Assert.Greater(Stop("multistage-pump"), Stop(Pump));
            Assert.Less(Stop(Pump), Stop(Fan), "water brakes a pump");
            Assert.Greater(Data.Balancing.Devices.SpinResolveStep, 0);
            Assert.Less(Data.Balancing.Devices.SpinResolveStep, 1);
        }
    }
}
