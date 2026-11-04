using System.Collections;
using System.Collections.Generic;
using HeadLoss.Game;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.City;
using HeadLoss.Game.Commands;
using HeadLoss.Game.Failures;
using HeadLoss.Game.Generation;
using HeadLoss.Game.Progression;
using HeadLoss.Game.Session;
using HeadLoss.Unity;
using HeadLoss.Unity.View;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HeadLoss.PlayMode.Tests
{
    /// <summary>The Phase 5 screens: gas in the building view, the alarm, explosions and ruins, the prestige screen, the away report.</summary>
    public class Phase5Tests
    {
        const string Gas = BuildingState.GasFluid;

        [UnityTearDown]
        public IEnumerator UnloadScene() => Scenes.Unload();

        static IEnumerator StartFixture()
        {
            yield return Scenes.LoadMenu();
            var root = Object.FindAnyObjectByType<GameRoot>();
            root.StartGame(GameContext.NewFixtureGame(root.Data!, "gas-house"), saving: false);
            yield return null;
            yield return null;
        }

        static List<TilePos> Run(int floor, params (int x, int y)[] points)
        {
            var path = new List<TilePos> { new TilePos(points[0].x, points[0].y, floor) };
            for (int i = 1; i < points.Length; i++)
            {
                TilePos cur = path[path.Count - 1];
                var target = new TilePos(points[i].x, points[i].y, floor);
                (int dx, int dy) = cur.DirectionTo(target);
                while (cur != target)
                {
                    cur = new TilePos(cur.X + dx, cur.Y + dy, floor);
                    path.Add(cur);
                }
            }
            return path;
        }

        static void PipeIt(BuildingSession s)
        {
            s.Game.Wallet = 1e7;
            s.Record.EverOnline = true;
            Assert.IsTrue(s.Execute(new PlaceRunCommand(Run(0, (9, 1), (9, 2), (7, 2), (7, 1)), 22, "copper", false, Gas)));
            Assert.IsTrue(s.Execute(new SetIntakeEquipmentCommand("gas", "gas-regulator")));
            Assert.IsTrue(s.Execute(new PlaceInlineCommand(new TilePos(8, 2), "auto-shutoff-valve", false, Gas)));
            Assert.IsTrue(s.Execute(new PlaceSafetyCommand("gas-detector", new TilePos(7, 3))));
            Assert.IsTrue(s.Commit(), s.CheckCommit().Reason);
            s.SolveNow();
        }

        [UnityTest]
        public IEnumerator GasHouse_HasAGasTab_DrawsAndCommits_ConcentrationColoursTheRooms()
        {
            yield return StartFixture();
            var root = Object.FindAnyObjectByType<GameRoot>();
            BuildingSession s = root.Session!;
            Assert.IsTrue(s.HasGas);
            CollectionAssert.Contains(root.Interaction!.View.Fluids, Gas);
            root.Interaction.SetFluid(Gas);
            yield return null;
            Assert.AreEqual(Gas, root.Heatmap!.Fluid);
            PipeIt(s);
            yield return null;
            Assert.Greater(root.View.Gas.Geometry.Segments.Count, 0, "the gas pipes are drawn");
            root.Heatmap.SetMode(HeatmapMode.Concentration);
            Assert.IsTrue(root.Heatmap.RoomsColoured, "concentration colours the rooms with gas in focus");
            root.Heatmap.SetSpecies(RoomSpecies.Co2);
            Assert.AreEqual("ppm", root.Heatmap.UnitNow);
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator Leak_ShowsInTheHud_AlarmEvacuates_NoErrors()
        {
            yield return StartFixture();
            var root = Object.FindAnyObjectByType<GameRoot>();
            BuildingSession s = root.Session!;
            PipeIt(s);
            s.Failures.AddLeak("burst", "", new TilePos(7, 2), 2e-5, 0);
            s.SolveNow();
            yield return null;
            s.GasSafety.RaiseAlarm();
            s.SolveNow();
            yield return null;
            yield return null;
            Assert.IsTrue(s.CommittedEvaluation.Evacuated);
            // Leak at (7, 2) is behind the automatic shutoff at (8, 2): it closed.
            Assert.IsFalse(s.GasSafety.TimerRunning);
            Assert.IsTrue(root.Ducts != null);
            Assert.AreEqual(1, root.View.LeakTiles.Count);
        }

        static IEnumerator StartBuiltCity()
        {
            yield return Scenes.LoadMenu();
            var root = Object.FindAnyObjectByType<GameRoot>();
            GameState g = GameContext.NewGame(root.Data!, 11);
            g.Wallet = 1e9;
            g.City!.UnlockedTier = 3;
            root.StartGame(g, saving: false);
            yield return null;
            GameContext ctx = root.Context!;
            Plot plot = ctx.City!.Plots.Find(p => p.Tier == 3)!;
            root.OpenBuilding(plot.Id);
            BuildingSession s = root.Session!;
            GeneratedBuilding made = ctx.CityBuildings!.Get(plot.Id)!;
            s.Record.Design = made.Reference.State.Clone();
            s.MarkDirty();
            s.Record.EverOnline = true;
            Assert.IsTrue(s.Commit(), s.CheckCommit().Reason);
            if (root.Hud!.SheetVisible) root.Hud.ToggleSheet();
            yield return null;
        }

        [UnityTest]
        public IEnumerator Explosion_InTheOpenBuilding_GoesBackToTheCity_WithARuin()
        {
            yield return StartBuiltCity();
            var root = Object.FindAnyObjectByType<GameRoot>();
            GameContext ctx = root.Context!;
            string id = root.Session!.Record.BuildingId;
            ctx.Explode(root.Session, 1e7);
            yield return null;
            yield return null;
            Assert.IsTrue(root.InCity, "the ruin closes the building view");
            Assert.IsTrue(ctx.IsRuin(id));
            Assert.IsFalse(ctx.CanOpen(id));
            yield return null;
        }

        [UnityTest]
        public IEnumerator PrestigeScreen_Opens_Buys_AndFoundsANewCity()
        {
            yield return Scenes.LoadMenu();
            var root = Object.FindAnyObjectByType<GameRoot>();
            GameState g = GameContext.NewGame(root.Data!, 7);
            g.Reputation = 10;
            root.StartGame(g, saving: false);
            yield return null;
            root.City!.Hud.PrestigeRequested = true;
            yield return null;
            yield return null;
            Assert.IsNotNull(root.Prestige, "the New city button opens the screen");
            Assert.IsTrue(PrestigeEffects.Buy(root.Data!, PrestigeEffects.PayRates, root.Context!.Game));

            // Meet the milestone by hand, then found a new city from a typed seed.
            double share = root.Data!.Balancing.Prestige.ShareOnline;
            root.Data.Balancing.Prestige.ShareOnline = 0;
            try
            {
                root.Context.Game.LifetimeIncome = root.Context.Milestone().IncomeTarget + 1;
                root.FoundNewCity("12345");
                yield return null;
                yield return null;
                Assert.AreEqual(2, root.Context!.Game.City!.Index);
                Assert.AreEqual(12345UL, root.Context.Game.City.Seed);
                Assert.AreEqual(1, PrestigeEffects.Level(root.Context.Game, PrestigeEffects.PayRates), "bonuses stay");
                Assert.Greater(root.Context.Game.Reputation, 6, "the reward is added");
                Assert.IsNull(root.Prestige);
                Assert.IsTrue(root.InCity);
            }
            finally
            {
                root.Data.Balancing.Prestige.ShareOnline = share;
            }
        }

        [UnityTest]
        public IEnumerator AwayReport_PutsThePausedTimerFirst()
        {
            yield return StartBuiltCity();
            var root = Object.FindAnyObjectByType<GameRoot>();
            BuildingSession s = root.Session!;
            s.Failures.AddLeak("burst", "", s.Layout.FindOutlet("stove-1") is { } o ? BuildingLayout.PosOf(o) : new TilePos(1, 1), 2e-5, 0);
            s.GasSafety.RaiseAlarm();
            root.Context!.Leave();
            root.ShowCityView();
            yield return null;
            double before = s.GasSafety.TimerLeft;
            root.RunCatchUp(3600, 12);
            yield return null;
            Assert.IsNotNull(root.Away);
            Assert.AreEqual(before, s.GasSafety.TimerLeft, 1e-9, "the timer waited");
            yield return null;
        }
    }
}
