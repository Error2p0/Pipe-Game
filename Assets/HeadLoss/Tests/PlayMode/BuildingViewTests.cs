using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using HeadLoss.Core;
using HeadLoss.Game;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.Commands;
using HeadLoss.Game.Session;
using HeadLoss.Sim.Devices;
using HeadLoss.Sim.Fluids;
using HeadLoss.Sim.Network;
using HeadLoss.Unity;
using HeadLoss.Unity.View;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace HeadLoss.PlayMode.Tests
{
    static class Scenes
    {
        /// <summary>Loads Main (it opens on the main menu).</summary>
        public static IEnumerator LoadMenu()
        {
            SceneManager.LoadScene("Main");
            yield return null;
            yield return null;
        }

        /// <summary>
        /// Loads Main and starts a new game without saving (tests never touch the player's save), with the
        /// requirement sheets marked as seen and, unless <paramref name="crank"/>, the Phase 1 small axial fan in the
        /// test house instead of the starter's hand crank.
        /// </summary>
        public static IEnumerator LoadMain(bool crank = false)
        {
            yield return LoadMenu();
            var root = Object.FindAnyObjectByType<GameRoot>();
            GameState g = GameContext.NewFixtureGame(root.Data!, "test-house", "small-house");
            foreach (BuildingRecord b in g.Buildings) b.SheetSeen = true;
            root.StartGame(g, saving: false);
            if (!crank) root.Session!.Execute(new SetIntakeEquipmentCommand("air-intake", "small-axial-fan"));
            root.Session!.SolveNow();
            yield return null;
            yield return null;
        }

        /// <summary>Unloads Main so its input actions are disposed before the next test.</summary>
        public static IEnumerator Unload()
        {
            Scene empty = SceneManager.CreateScene("Empty" + Time.frameCount);
            SceneManager.SetActiveScene(empty);
            Scene main = SceneManager.GetSceneByName("Main");
            if (main.isLoaded) yield return SceneManager.UnloadSceneAsync(main);
        }

        public static List<TilePos> Path(params (int x, int y)[] points)
        {
            var path = new List<TilePos> { new TilePos(points[0].x, points[0].y) };
            for (int i = 1; i < points.Length; i++)
            {
                TilePos cur = path[path.Count - 1];
                var target = new TilePos(points[i].x, points[i].y);
                (int dx, int dy) = cur.DirectionTo(target);
                while (cur != target)
                {
                    cur = new TilePos(cur.X + dx, cur.Y + dy);
                    path.Add(cur);
                }
            }
            return path;
        }
    }

    public class BuildingViewTests
    {
        [UnityTearDown]
        public IEnumerator UnloadScene() => Scenes.Unload();

        [UnityTest]
        public IEnumerator Main_OpensOnTheMenu()
        {
            yield return Scenes.LoadMenu();
            var root = Object.FindAnyObjectByType<GameRoot>();
            Assert.IsTrue(root.InMenu);
            Assert.IsNull(root.Session);
        }

        [UnityTest]
        public IEnumerator Main_Boots_SolvesTestHouseWithoutErrors()
        {
            yield return Scenes.LoadMain();
            var root = Object.FindAnyObjectByType<GameRoot>();
            Assert.IsNotNull(root);
            Assert.IsNotNull(root.Session);
            Assert.IsNotNull(root.Session!.Current);
            Assert.IsTrue(root.Session.Current!.Result.Converged);
            Assert.IsNotNull(Camera.main);
        }

        /// <summary>
        /// Phase 1 gate (14-roadmap.md): two sharp bends feeding a vent; switching both to long-radius raises
        /// that vent's flow visibly and the velocity heatmap changes colour within one second.
        /// </summary>
        [UnityTest]
        public IEnumerator Gate_UpgradeBends_RaisesFlowAndRecoloursWithinOneSecond()
        {
            yield return Scenes.LoadMain();
            var root = Object.FindAnyObjectByType<GameRoot>();
            BuildingSession s = root.Session!;
            Assert.IsTrue(s.Execute(new PlaceRunCommand(Scenes.Path((0, 3), (2, 3), (2, 5), (3, 5)), 125, "galvanized-duct", false)));
            int rev = s.SolveRevision;
            while (root.RenderedSolveRevision <= rev) yield return null;

            DuctSegment last = s.State.Air.Segments.Find(x => s.State.Air.FindNode(x.NodeB)!.Kind == DuctNodeKind.Outlet || s.State.Air.FindNode(x.NodeA)!.Kind == DuctNodeKind.Outlet)!;
            double flowBefore = s.OutletFlow("vent-1");
            Color before = root.Ducts!.SegmentColour(root.View, last.Id);

            float t0 = Time.realtimeSinceStartup;
            rev = s.SolveRevision;
            Assert.IsTrue(s.Execute(new UpgradeAllBendsCommand()));
            while (root.RenderedSolveRevision <= rev && Time.realtimeSinceStartup - t0 < 2f) yield return null;
            float elapsed = Time.realtimeSinceStartup - t0;

            double flowAfter = s.OutletFlow("vent-1");
            Color after = root.Ducts.SegmentColour(root.View, last.Id);
            float colourDelta = Mathf.Abs(after.r - before.r) + Mathf.Abs(after.g - before.g) + Mathf.Abs(after.b - before.b);
            UnityEngine.Debug.Log($"Gate: {Units.M3sToM3h(flowBefore):F1} → {Units.M3sToM3h(flowAfter):F1} m³/h (+{(flowAfter / flowBefore - 1) * 100:F1}%), colour Δ {colourDelta:F3}, {elapsed * 1000:F0} ms");
            Assert.Less(elapsed, 1f, "heatmap must update within one second");
            Assert.Greater(flowAfter / flowBefore, 1.08, "visible flow gain");
            Assert.Greater(colourDelta, 0.08f, "visible colour change");
        }
    }

    /// <summary>Drawing tool driven through the Input System (isolated test devices, no OS focus needed).</summary>
    public class DrawingToolTests : InputTestFixture
    {
        Mouse _mouse = null!;
        Keyboard _keyboard = null!;

        /// <summary>Runs before the fixture restores the input system, so the game's actions are disposed first.</summary>
        [UnityTearDown]
        public IEnumerator UnloadScene() => Scenes.Unload();

        public override void Setup()
        {
            base.Setup();
            _mouse = InputSystem.AddDevice<Mouse>();
            _keyboard = InputSystem.AddDevice<Keyboard>();
        }

        static Vector2 Screen(Camera cam, int x, int y) => cam.WorldToScreenPoint(new Vector3(x + 0.5f, y + 0.5f, 0));

        IEnumerator Drag(Camera cam, List<TilePos> path)
        {
            Move(_mouse.position, Screen(cam, path[0].X, path[0].Y));
            yield return null;
            Press(_mouse.leftButton);
            yield return null;
            for (int i = 1; i < path.Count; i++)
            {
                Move(_mouse.position, Screen(cam, path[i].X, path[i].Y));
                yield return null;
            }
            Release(_mouse.leftButton);
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator Draw_TwoBendRunFromFanToVent_PlacesElbowsAndFeedsVent()
        {
            yield return Scenes.LoadMain();
            var root = Object.FindAnyObjectByType<GameRoot>();
            BuildingSession s = root.Session!;
            yield return Drag(Camera.main, Scenes.Path((0, 3), (2, 3), (2, 5), (3, 5)));

            Assert.AreEqual(3, s.State.Air.Segments.Count, "three straight runs");
            Assert.IsTrue(DuctEditor.IsElbow(s.State.Air, s.State.Air.NodeAt(new TilePos(2, 3))!));
            Assert.IsTrue(DuctEditor.IsElbow(s.State.Air, s.State.Air.NodeAt(new TilePos(2, 5))!));
            Assert.Greater(Units.M3sToM3h(s.OutletFlow("vent-1")), 30);
        }

        [UnityTest]
        public IEnumerator Draw_FromEmptySpace_DoesNothing_AndClickSelectsDuct()
        {
            yield return Scenes.LoadMain();
            var root = Object.FindAnyObjectByType<GameRoot>();
            BuildingSession s = root.Session!;
            yield return Drag(Camera.main, Scenes.Path((6, 6), (6, 4)));
            Assert.AreEqual(0, s.State.Air.Segments.Count);

            yield return Drag(Camera.main, Scenes.Path((0, 3), (4, 3), (4, 5), (8, 5)));
            Assert.Greater(s.OutletFlow("vent-2"), 0);

            Move(_mouse.position, Screen(Camera.main, 2, 3));
            yield return null;
            Press(_mouse.leftButton);
            yield return null;
            Release(_mouse.leftButton);
            yield return null;
            Assert.AreEqual(1, root.Interaction!.SelectedSegments.Count);

            Press(_keyboard.deleteKey);
            yield return null;
            Release(_keyboard.deleteKey);
            yield return null;
            Assert.AreEqual(2, s.State.Air.Segments.Count, "deleted the selected duct");
        }

        [UnityTest]
        public IEnumerator Hotkeys_SwitchHeatmapMode()
        {
            yield return Scenes.LoadMain();
            var root = Object.FindAnyObjectByType<GameRoot>();
            Assert.AreEqual(HeatmapMode.Velocity, root.Heatmap!.Mode);
            Press(_keyboard.digit0Key);
            yield return null;
            Release(_keyboard.digit0Key);
            yield return null;
            Assert.AreEqual(HeatmapMode.Plain, root.Heatmap.Mode);
            Press(_keyboard.digit3Key);
            yield return null;
            Assert.AreEqual(HeatmapMode.Plain, root.Heatmap.Mode, "unbuilt modes stay greyed out");
            Release(_keyboard.digit3Key);
            Press(_keyboard.digit1Key);
            yield return null;
            Release(_keyboard.digit1Key);
            yield return null;
            Assert.AreEqual(HeatmapMode.Velocity, root.Heatmap.Mode);
        }
    }

    public class PerformanceTests
    {
        /// <summary>01-architecture.md: building network solve (2,000 segments) under 5 ms.</summary>
        [Test]
        public void NetworkSolve_2000Segments_Under5ms()
        {
            var rng = new Rng(2024);
            var g = new NetworkGraph();
            int intake = g.AddFixed(0);
            var nodes = new List<int> { g.AddJunction() };
            g.AddFan(intake, nodes[0], FanCurve.Quadratic(1500, Units.M3hToM3s(20000), 6, 0.75), 1.0);
            double[] sizes = { 0.125, 0.16, 0.2, 0.25, 0.315, 0.4 };
            for (int i = 1; i <= 2000; i++)
            {
                int parent = nodes[rng.NextInt(System.Math.Max(0, nodes.Count - 40), nodes.Count)];
                int n = g.AddJunction();
                nodes.Add(n);
                g.AddPipe(parent, n, rng.NextRange(1, 6), sizes[rng.NextInt(0, sizes.Length)], 0.15e-3, rng.NextRange(0, 1.5));
                if (i % 12 == 0) g.AddVent(n, g.AddFixed(0), 2.0, 0.04);
            }
            var air = new ConstantFluid(1.204, 1.813e-5);
            new NetworkSolver(new SolverSettings()).Solve(g, air);  // JIT warm-up
            var fresh = new NetworkSolver(new SolverSettings());
            var first = Stopwatch.StartNew();
            SolveResult warm = fresh.Solve(g, air);
            double withAnalysis = first.Elapsed.TotalMilliseconds;
            Assert.IsTrue(warm.Converged);
            // Re-solves of an unchanged topology (drift, setting changes) reuse the ordering; still cold flows.
            NetworkSolver solver = fresh;
            var times = new List<double>();
            for (int i = 0; i < 7; i++)
            {
                var sw = Stopwatch.StartNew();
                SolveResult r = solver.Solve(g, air);
                times.Add(sw.Elapsed.TotalMilliseconds);
                Assert.IsTrue(r.Converged);
            }
            times.Sort();
            UnityEngine.Debug.Log($"2000-segment solve: median {times[3]:F2} ms (first solve incl. ordering {withAnalysis:F2} ms), {warm.Iterations} iterations");
            Assert.Less(times[3], 5.0);
        }
    }
}
