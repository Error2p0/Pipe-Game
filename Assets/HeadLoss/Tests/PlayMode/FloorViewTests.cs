using System.Collections;
using System.Collections.Generic;
using HeadLoss.Game;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.Commands;
using HeadLoss.Game.Session;
using HeadLoss.Unity;
using HeadLoss.Unity.Interaction;
using HeadLoss.Unity.View;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace HeadLoss.PlayMode.Tests
{
    /// <summary>Between floors: selecting and deleting risers, and the ghosts of the floors above and below.</summary>
    public class FloorViewTests : InputTestFixture
    {
        Mouse _mouse = null!;
        Keyboard _keyboard = null!;

        [UnityTearDown]
        public IEnumerator UnloadScene() => Scenes.Unload();

        public override void Setup()
        {
            base.Setup();
            _mouse = InputSystem.AddDevice<Mouse>();
            _keyboard = InputSystem.AddDevice<Keyboard>();
        }

        static IEnumerator StartWaterHouse()
        {
            yield return Scenes.LoadMenu();
            var root = Object.FindAnyObjectByType<GameRoot>();
            root.StartGame(GameContext.NewFixtureGame(root.Data!, "water-house"), saving: false);
            yield return null;
            yield return null;
            if (root.Hud!.SheetVisible) root.Hud.ToggleSheet();
            yield return null;
        }

        static List<TilePos> Run(int floor, params (int x, int y)[] points)
        {
            var list = new List<TilePos> { new TilePos(points[0].x, points[0].y, floor) };
            for (int i = 1; i < points.Length; i++)
            {
                TilePos cur = list[list.Count - 1];
                var target = new TilePos(points[i].x, points[i].y, floor);
                (int dx, int dy) = cur.DirectionTo(target);
                while (cur != target)
                {
                    cur = new TilePos(cur.X + dx, cur.Y + dy, floor);
                    list.Add(cur);
                }
            }
            return list;
        }

        /// <summary>Connection → shaft on the ground floor, riser, then a pipe upstairs.</summary>
        static void PlumbRiser(BuildingSession s)
        {
            Assert.IsTrue(s.Execute(new PlaceRunCommand(Run(0, (9, 1), (9, 5)), 22, "copper", false, "water")));
            Assert.IsTrue(s.Execute(new PlaceRunCommand(new List<TilePos> { new TilePos(9, 5, 0), new TilePos(9, 5, 1) }, 22, "copper", false, "water")));
            Assert.IsTrue(s.Execute(new PlaceRunCommand(Run(1, (9, 5), (9, 3)), 22, "copper", false, "water")));
        }

        static int Risers(BuildingSession s)
        {
            int n = 0;
            foreach (DuctSegment x in s.State.Water.Segments) if (DuctEditor.IsRiser(s.State.Water, x)) n++;
            return n;
        }

        IEnumerator ClickTile(int x, int y)
        {
            Move(_mouse.position, (Vector2)Camera.main.WorldToScreenPoint(new Vector3(x + 0.5f, y + 0.5f, 0)));
            yield return null;
            Press(_mouse.leftButton);
            yield return null;
            Release(_mouse.leftButton);
            yield return null;
        }

        IEnumerator PressDelete()
        {
            Press(_keyboard.deleteKey);
            yield return null;
            Release(_keyboard.deleteKey);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Riser_CanBeClickedAndDeleted_FromEitherFloor([Values(0, 1)] int floor)
        {
            yield return StartWaterHouse();
            var root = Object.FindAnyObjectByType<GameRoot>();
            BuildingSession s = root.Session!;
            PlumbRiser(s);
            Assert.AreEqual(1, Risers(s));
            root.Interaction!.View.SetFloor(floor);
            yield return null;
            yield return null;

            yield return ClickTile(9, 5);
            Assert.AreEqual(TargetKind.Segment, root.Interaction.Selected.Kind, "the click takes the riser, not the node at its end");
            Assert.IsTrue(DuctEditor.IsRiser(s.State.Water, s.State.Water.FindSegment(root.Interaction.Selected.Id)!));

            yield return PressDelete();
            Assert.AreEqual(0, Risers(s), "gone");
            Assert.AreEqual(2, CountRuns(s), "the pipes on both floors stay");
            Assert.AreEqual(TargetKind.None, root.Interaction.Selected.Kind);

            Assert.IsTrue(s.Undo());
            Assert.AreEqual(1, Risers(s), "and Undo brings it back");
        }

        static int CountRuns(BuildingSession s) => s.State.Water.Segments.Count;

        [UnityTest]
        public IEnumerator BoxSelection_TakesRisersOnTheFloorOnShow()
        {
            yield return StartWaterHouse();
            var root = Object.FindAnyObjectByType<GameRoot>();
            BuildingSession s = root.Session!;
            PlumbRiser(s);
            root.Interaction!.SetFluid("water");
            yield return null;
            yield return null;

            // From an empty corner of the building, across the shaft.
            Vector2 World(float x, float y) => Camera.main.WorldToScreenPoint(new Vector3(x, y, 0));
            Move(_mouse.position, World(7.2f, 4.2f));
            yield return null;
            Press(_mouse.leftButton);
            yield return null;
            Move(_mouse.position, World(9.9f, 6.2f));
            yield return null;
            yield return null;
            Release(_mouse.leftButton);
            yield return null;

            int riserId = s.State.Water.Segments.Find(x => DuctEditor.IsRiser(s.State.Water, x))!.Id;
            Assert.Contains(riserId, new List<int>(root.Interaction.SelectedSegments));
            yield return PressDelete();
            Assert.AreEqual(0, Risers(s));
        }

        [UnityTest]
        public IEnumerator Ghost_ShowsLongRadiusElbows_AtItsOpacity()
        {
            yield return StartWaterHouse();
            var root = Object.FindAnyObjectByType<GameRoot>();
            BuildingSession s = root.Session!;
            Assert.IsTrue(s.Execute(new PlaceRunCommand(Run(0, (9, 1), (9, 3), (7, 3)), 22, "copper", false, "water")));
            root.Interaction!.View.SetFloor(1);
            yield return null;
            yield return null;

            Mesh mesh = root.Ducts!.GetComponent<MeshFilter>().sharedMesh;
            int sharp = mesh.vertexCount;
            Assert.Greater(sharp, 0, "the ground floor's pipe shows as a ghost upstairs");

            DuctNode elbow = s.State.Water.NodeAt(new TilePos(9, 3, 0))!;
            elbow.SmoothFittings = true;
            s.MarkDirty();
            yield return null;
            yield return null;
            yield return null;

            mesh = root.Ducts.GetComponent<MeshFilter>().sharedMesh;
            Assert.Greater(mesh.vertexCount, sharp, "the long-radius elbow is drawn: an arc has more vertices than the cap it replaces");
            float opacity = (float)s.Data.Balancing.Drawing.GhostOpacity;
            foreach (Color c in mesh.colors) Assert.AreEqual(opacity, c.a, 1e-3f, "everything on this floor is ghost");
        }

        static int VerticesAt(Mesh mesh, float alpha)
        {
            int n = 0;
            foreach (Color c in mesh.colors) if (Mathf.Abs(c.a - alpha) < 1e-3f) n++;
            return n;
        }

        [UnityTest]
        public IEnumerator NetworkNotInFocus_IsFaint_ItsPartsToo_AndFocusSwapsIt()
        {
            yield return StartWaterHouse();
            var root = Object.FindAnyObjectByType<GameRoot>();
            BuildingSession s = root.Session!;
            Assert.IsTrue(s.Execute(new PlaceRunCommand(Run(0, (9, 1), (9, 3)), 22, "copper", false, "water")));
            Assert.IsTrue(s.Execute(new PlaceRunCommand(Run(0, (0, 2), (3, 2)), 125, "galvanized-duct", false)));
            Assert.IsTrue(s.Execute(new PlaceInlineCommand(new TilePos(2, 2), "inline-duct-fan")));
            float other = (float)s.Data.Balancing.Drawing.OtherNetworkOpacity;
            Assert.Less(other, 0.5f);
            Mesh Ducts() => root.Ducts!.GetComponent<MeshFilter>().sharedMesh;
            float PartAlpha()
            {
                float most = 0;
                foreach (FanView fan in Object.FindObjectsByType<FanView>())
                    foreach (MeshFilter f in fan.GetComponentsInChildren<MeshFilter>())
                        foreach (Color c in f.sharedMesh.colors) most = Mathf.Max(most, c.a);
                return most;
            }

            // Plain view: pipes in focus are fully solid, so the two states can be told apart.
            root.Heatmap!.Mode = HeatmapMode.Plain;
            root.Interaction!.SetFluid("air");
            yield return null;
            yield return null;
            int waterFaint = VerticesAt(Ducts(), other);
            Assert.Greater(waterFaint, 0, "water is faint while air is in focus");
            Assert.AreEqual(1f, PartAlpha(), 1e-3f, "the inline fan is solid on the network in focus");

            root.Interaction.SetFluid("water");
            yield return null;
            yield return null;
            Assert.Greater(VerticesAt(Ducts(), other), 0, "air is faint while water is in focus");
            Assert.AreNotEqual(waterFaint, VerticesAt(Ducts(), other), "it is the other network that is faint now");
            Assert.AreEqual(other, PartAlpha(), 1e-3f, "and so is its inline fan");
        }
    }
}
