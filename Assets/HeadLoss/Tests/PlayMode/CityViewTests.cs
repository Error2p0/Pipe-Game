using System.Collections;
using HeadLoss.Game;
using HeadLoss.Game.City;
using HeadLoss.Unity;
using HeadLoss.Unity.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.TestTools;

namespace HeadLoss.PlayMode.Tests
{
    /// <summary>The Phase 3 city view: new city, select, open, back to the city.</summary>
    public class CityViewTests : InputTestFixture
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

        static IEnumerator StartCity(ulong seed = 7)
        {
            yield return Scenes.LoadMenu();
            var root = Object.FindAnyObjectByType<GameRoot>();
            root.StartGame(GameContext.NewGame(root.Data!, seed), saving: false);
            yield return null;
            yield return null;
        }

        static Vector2 ScreenOf(GameRoot root, Plot p)
        {
            float t = (float)root.Context!.City!.TileMetres;
            return Camera.main.WorldToScreenPoint(new Vector3((float)p.CentreX * t, (float)p.CentreY * t, 0));
        }

        IEnumerator Click(Vector2 screen)
        {
            Move(_mouse.position, screen);
            yield return null;
            Press(_mouse.leftButton);
            yield return null;
            Release(_mouse.leftButton);
            yield return null;
        }

        IEnumerator Tap(KeyControl key)
        {
            Press(key);
            yield return null;
            Release(key);
            yield return null;
        }

        [UnityTest]
        public IEnumerator NewCity_OpensTheCityView()
        {
            yield return StartCity();
            var root = Object.FindAnyObjectByType<GameRoot>();
            Assert.IsTrue(root.InCity);
            Assert.IsNotNull(root.City);
            Assert.IsNull(root.Session, "no building open");
            Assert.IsNotNull(root.Context!.City!.Starter);
        }

        [UnityTest]
        public IEnumerator ClickSelects_DoubleClickOpens_EscBackToTheCity()
        {
            yield return StartCity();
            var root = Object.FindAnyObjectByType<GameRoot>();
            Plot starter = root.Context!.City!.Starter!;
            Vector2 at = ScreenOf(root, starter);
            yield return Click(at);
            Assert.AreEqual(root.Context.City.Plots.IndexOf(starter), root.City!.Selected, "a click selects the plot");
            Assert.IsTrue(root.InCity, "one click only selects");
            yield return Click(at);
            Assert.IsFalse(root.InCity, "the second click opens it");
            Assert.AreEqual(starter.Id, root.Session!.Layout.Id);
            Assert.IsTrue(root.Session.HasCrank);
            yield return Tap(_keyboard.tabKey);
            yield return Tap(_keyboard.escapeKey);
            Assert.IsTrue(root.InCity, "Esc returns to the city");
            Assert.AreEqual(1, root.Context.Sessions.Count, "the opened building keeps its session");
        }

        [UnityTest]
        public IEnumerator EnterOpensTheSelectedPlot_EscClearsTheSelectionFirst()
        {
            yield return StartCity();
            var root = Object.FindAnyObjectByType<GameRoot>();
            Plot starter = root.Context!.City!.Starter!;
            yield return Click(ScreenOf(root, starter));
            yield return Tap(_keyboard.escapeKey);
            Assert.AreEqual(-1, root.City!.Selected);
            Assert.IsTrue(root.InCity, "Esc cleared the selection only");
            yield return Click(ScreenOf(root, starter));
            yield return Tap(_keyboard.enterKey);
            Assert.IsFalse(root.InCity);
        }

        /// <summary>The route tool: drag from the treatment plant along the tiles, release to lay the main and pay for it.</summary>
        [UnityTest]
        public IEnumerator RouteTool_DragFromThePlant_LaysAMain_EscCancelsADrag()
        {
            yield return Scenes.LoadMenu();
            var root = Object.FindAnyObjectByType<GameRoot>();
            GameState g = GameContext.NewGame(root.Data!, 7);
            g.Wallet = 1e6;
            g.City!.UnlockedTier = 2;
            root.StartGame(g, saving: false);
            yield return null;
            yield return null;
            CityLayout city = root.Context!.City!;
            FacilityPlacement plant = city.Facilities.Find(f => f.Utility == "water");
            float t = (float)city.TileMetres;
            Vector3 World(int x, int y) => new Vector3((x + 0.5f) * t, (y + 0.5f) * t, 0);
            int dir = plant.X < city.Width / 2 ? 1 : -1;
            Camera cam = Camera.main;
            cam.orthographicSize = 60;
            cam.transform.position = World(plant.X, plant.Y) + new Vector3(dir * 15, 0, -10);
            yield return null;
            root.City!.Hud.Tool = CityTool.Route;
            Vector2 a = cam.WorldToScreenPoint(World(plant.X, plant.Y));
            Vector2 b = cam.WorldToScreenPoint(World(plant.X + dir * 3, plant.Y));

            // A drag that is cancelled lays nothing.
            Move(_mouse.position, a);
            yield return null;
            Press(_mouse.leftButton);
            yield return null;
            Move(_mouse.position, b);
            yield return null;
            yield return Tap(_keyboard.escapeKey);
            Release(_mouse.leftButton);
            yield return null;
            Assert.IsEmpty(g.City.Utilities.Mains, "Esc cancelled the drag");
            Assert.AreEqual(CityTool.Route, root.City.Hud.Tool, "the tool stays until a second Esc");

            double wallet = g.Wallet;
            Move(_mouse.position, a);
            yield return null;
            Press(_mouse.leftButton);
            yield return null;
            Move(_mouse.position, b);
            yield return null;
            Release(_mouse.leftButton);
            yield return null;
            yield return null;
            Assert.AreEqual(1, g.City.Utilities.Mains.Count, "released: laid");
            Assert.AreEqual(4, g.City.Utilities.Mains[0].Tiles.Count);
            Assert.Less(g.Wallet, wallet, "paid at once");
        }

        [UnityTest]
        public IEnumerator LaterTierPlot_DoesNotOpen()
        {
            yield return StartCity();
            var root = Object.FindAnyObjectByType<GameRoot>();
            Plot high = root.Context!.City!.Plots.Find(p => p.Tier >= 4);
            Camera.main.transform.position = new Vector3((float)high.CentreX * 10, (float)high.CentreY * 10, -10);
            yield return null;
            yield return Click(ScreenOf(root, high));
            yield return Tap(_keyboard.enterKey);
            Assert.IsTrue(root.InCity, "tier 4+ buildings arrive later");
        }

        [UnityTest]
        public IEnumerator Shop_OpensFromTheBuilding_EscClosesItFirst()
        {
            yield return StartCity();
            var root = Object.FindAnyObjectByType<GameRoot>();
            root.OpenBuilding(root.Context!.City!.Starter!.Id);
            yield return Tap(_keyboard.tabKey);
            root.Hud!.ShopRequested = true;
            yield return null;
            Assert.IsNotNull(root.Shop, "the shop is open");
            yield return Tap(_keyboard.escapeKey);
            Assert.IsNull(root.Shop, "Esc closed the shop");
            Assert.IsFalse(root.InCity, "and stayed in the building");
        }

        [UnityTest]
        public IEnumerator Settings_OpenFromTheCity_ApplyLive_EscCloses()
        {
            yield return StartCity();
            var root = Object.FindAnyObjectByType<GameRoot>();
            root.City!.Hud.SettingsRequested = true;
            yield return null;
            Assert.IsNotNull(root.SettingsOpen);
            root.SettingsOpen!.Settings.ReduceMotion = true;
            root.SettingsOpen.Settings.Palette = "magma";
            yield return Tap(_keyboard.escapeKey);
            Assert.IsNull(root.SettingsOpen, "Esc closed the settings");
            Assert.IsTrue(root.InCity, "and stayed in the city");
            Assert.IsTrue(root.Settings.ReduceMotion);
            Assert.AreEqual("magma", root.Heatmap!.Palette, "the palette applied");
        }

        [UnityTest]
        public IEnumerator Offline_ShowsTheReport_EnterClosesIt()
        {
            yield return StartCity();
            var root = Object.FindAnyObjectByType<GameRoot>();
            root.RunCatchUp(2 * 3600, 12);
            Assert.IsNotNull(root.Away, "the While you were away report shows");
            yield return Tap(_keyboard.enterKey);
            Assert.IsNull(root.Away);
            Assert.IsTrue(root.InCity, "Enter only closed the report");
        }
    }
}
