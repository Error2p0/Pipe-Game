using System.Collections;
using System.Collections.Generic;
using HeadLoss.Game;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.Commands;
using HeadLoss.Game.Session;
using HeadLoss.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.TestTools;

namespace HeadLoss.PlayMode.Tests
{
    /// <summary>Phase 2 flows driven through the Input System: commit, crank, part tool, undo/redo, leaving.</summary>
    public class Phase2FlowTests : InputTestFixture
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

        IEnumerator Tap(KeyControl key)
        {
            Press(key);
            yield return null;
            Release(key);
            yield return null;
        }

        /// <summary>
        /// Modifier first, then the key a frame later (shortcuts need the modifier down first). One key change per
        /// frame: queued test events on one device are built from its last processed state, so two in one frame
        /// would leave the first key held.
        /// </summary>
        IEnumerator Chord(KeyControl modifier, KeyControl key)
        {
            Press(modifier);
            yield return null;
            Press(key);
            yield return null;
            Release(key);
            yield return null;
            Release(modifier);
            yield return null;
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

        /// <summary>Puts the auto-router's reference ducts into the design (equipment unchanged).</summary>
        static void DrawReference(BuildingSession s)
        {
            s.Record.Design.Air = ReferenceLayout.Build(s.Data, s.Layout).Air.Clone();
            s.MarkDirty();
        }

        [UnityTest]
        public IEnumerator Enter_CommitsTheDesign_AndChargesTheWallet()
        {
            yield return Scenes.LoadMain(crank: true);
            var root = Object.FindAnyObjectByType<GameRoot>();
            BuildingSession s = root.Session!;
            DrawReference(s);
            Assert.IsTrue(s.HasPendingDesign);
            double cost = s.PendingCost.Net, wallet = s.Game.Wallet;
            Assert.LessOrEqual(cost, wallet, "the starter reference fits the starting wallet");
            yield return Tap(_keyboard.enterKey);
            Assert.IsFalse(s.HasPendingDesign);
            Assert.AreEqual(wallet - cost, s.Game.Wallet, 0.05, "wallet charged (plus a few frames of income)");
        }

        [UnityTest]
        public IEnumerator HoldC_CranksTheFan_AndBringsTheStarterOnline()
        {
            yield return Scenes.LoadMain(crank: true);
            var root = Object.FindAnyObjectByType<GameRoot>();
            BuildingSession s = root.Session!;
            DrawReference(s);
            s.Execute(new CommitDesignCommand());
            Press(_keyboard.cKey);
            float t0 = Time.realtimeSinceStartup;
            // Holding = 5 clicks/s against a 4 s decay: full speed after about 2 s.
            while (Time.realtimeSinceStartup - t0 < 3f) yield return null;
            Assert.Greater(s.CrankSpeed, 0.9);
            s.SolveNow();
            Assert.IsTrue(s.CommittedEvaluation.Online);
            double wallet = s.Game.Wallet;
            t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 0.5f) yield return null;
            Assert.Greater(s.Game.Wallet, wallet, "earning while cranking");
            Release(_keyboard.cKey);
            yield return null;
        }

        [UnityTest]
        public IEnumerator PartTool_ClickOnDuct_PlacesDamper()
        {
            yield return Scenes.LoadMain();
            var root = Object.FindAnyObjectByType<GameRoot>();
            BuildingSession s = root.Session!;
            s.Execute(new PlaceRunCommand(Scenes.Path((0, 3), (8, 3), (8, 5)), 125, "galvanized-duct", false));
            yield return null;
            root.Interaction!.SetPlaceTool("damper");
            yield return ClickTile(4, 3);
            DuctNode? n = s.State.Air.NodeAt(new TilePos(4, 3));
            Assert.IsNotNull(n);
            Assert.AreEqual(DuctNodeKind.Inline, n!.Kind);
            Assert.AreEqual("damper", n.PartId);
            yield return Tap(_keyboard.escapeKey);
            Assert.AreEqual("", root.Interaction.PlaceTool, "Esc puts the tool away");
        }

        [UnityTest]
        public IEnumerator BoxSelect_ShowsTheBoxAndSelectsLiveWhileDragging()
        {
            yield return Scenes.LoadMain();
            var root = Object.FindAnyObjectByType<GameRoot>();
            BuildingSession s = root.Session!;
            s.Execute(new PlaceRunCommand(Scenes.Path((0, 3), (8, 3), (8, 5)), 125, "galvanized-duct", false));
            yield return null;
            Camera cam = Camera.main;
            Move(_mouse.position, (Vector2)cam.WorldToScreenPoint(new Vector3(1.2f, 0.4f, 0)));
            yield return null;
            Press(_mouse.leftButton);
            yield return null;
            Move(_mouse.position, (Vector2)cam.WorldToScreenPoint(new Vector3(9.6f, 6.6f, 0)));
            yield return null;
            yield return null;
            Assert.IsTrue(root.Interaction!.IsBoxSelecting);
            Assert.IsTrue(root.View.Box.HasValue, "the box is drawn while dragging");
            Assert.AreEqual(2, root.Interaction.SelectedSegments.Count, "ducts inside are selected before release");
            Release(_mouse.leftButton);
            yield return null;
            Assert.IsFalse(root.View.Box.HasValue);
            Assert.AreEqual(2, root.Interaction.SelectedSegments.Count);
        }

        [UnityTest]
        public IEnumerator CtrlZ_CtrlY_UndoAndRedo()
        {
            yield return Scenes.LoadMain();
            var root = Object.FindAnyObjectByType<GameRoot>();
            BuildingSession s = root.Session!;
            s.Execute(new PlaceRunCommand(Scenes.Path((0, 3), (3, 3), (3, 5)), 125, "galvanized-duct", false));
            Assert.AreEqual(2, s.State.Air.Segments.Count);
            int history = s.Commands.History.Count;
            yield return Chord(_keyboard.leftCtrlKey, _keyboard.zKey);
            Assert.AreEqual(0, s.State.Air.Segments.Count);
            Assert.AreEqual(history - 1, s.Commands.History.Count, "exactly one undo step");
            Assert.IsTrue(s.Commands.CanRedo, "redo available after undo");
            yield return Chord(_keyboard.leftCtrlKey, _keyboard.yKey);
            Assert.AreEqual(2, s.State.Air.Segments.Count, $"canRedo={s.Commands.CanRedo} history={s.Commands.History.Count}");
        }

        [UnityTest]
        public IEnumerator Esc_WithNothingSelected_GoesToTheBuildingList_AndOpensAnother()
        {
            yield return Scenes.LoadMain();
            var root = Object.FindAnyObjectByType<GameRoot>();
            yield return Tap(_keyboard.escapeKey);
            Assert.IsTrue(root.InBuildingList);
            Assert.IsNull(root.Session);
            root.OpenBuilding("small-house");
            yield return null;
            Assert.AreEqual("small-house", root.Session!.Layout.Id);
            Assert.IsNotNull(root.Hud);
        }

        [UnityTest]
        public IEnumerator FirstOpen_ShowsTheRequirementSheet_TabClosesIt()
        {
            yield return Scenes.LoadMenu();
            var root = Object.FindAnyObjectByType<GameRoot>();
            root.StartGame(GameContext.NewGame(root.Data!), saving: false);
            yield return null;
            Assert.IsTrue(root.Hud!.SheetVisible);
            Assert.IsTrue(root.Session!.Record.SheetSeen);
            yield return Tap(_keyboard.tabKey);
            Assert.IsFalse(root.Hud.SheetVisible);
        }
    }
}
