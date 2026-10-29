using System.Collections;
using HeadLoss.Game;
using HeadLoss.Game.City;
using HeadLoss.Unity;
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

        [UnityTest]
        public IEnumerator LaterTierPlot_DoesNotOpen()
        {
            yield return StartCity();
            var root = Object.FindAnyObjectByType<GameRoot>();
            Plot high = root.Context!.City!.Plots.Find(p => p.Tier >= 3);
            Camera.main.transform.position = new Vector3((float)high.CentreX * 10, (float)high.CentreY * 10, -10);
            yield return null;
            yield return Click(ScreenOf(root, high));
            yield return Tap(_keyboard.enterKey);
            Assert.IsTrue(root.InCity, "tier 3+ buildings arrive later");
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
    }
}
