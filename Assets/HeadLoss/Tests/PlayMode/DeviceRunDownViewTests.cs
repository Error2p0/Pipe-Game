using System.Collections;
using System.Collections.Generic;
using HeadLoss.Game.Commands;
using HeadLoss.Game.Session;
using HeadLoss.Unity;
using HeadLoss.Unity.Interaction;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace HeadLoss.PlayMode.Tests
{
    /// <summary>A switched-off fan keeps showing its numbers while it slows down (<c>05-simulation.md</c> §2).</summary>
    public class DeviceRunDownViewTests
    {
        [UnityTearDown]
        public IEnumerator UnloadScene() => Scenes.Unload();

        static string Readout()
        {
            var lines = new List<string>();
            foreach (UIDocument doc in Object.FindObjectsByType<UIDocument>())
                doc.rootVisualElement?.Query<Label>(className: "hl-readout-line").ForEach(l => lines.Add(l.text));
            return string.Join("\n", lines);
        }

        [UnityTest]
        public IEnumerator SwitchedOffFan_ReadoutFollowsItDown_ThenSaysSwitchedOff()
        {
            yield return Scenes.LoadMain();
            var root = Object.FindAnyObjectByType<GameRoot>();
            BuildingSession s = root.Session!;
            Assert.IsTrue(s.Execute(new PlaceRunCommand(Scenes.Path((0, 3), (3, 3), (3, 5)), 125, "galvanized-duct", false)));
            s.Game.Wallet = 1e6;
            s.Record.EverOnline = true;
            Assert.IsTrue(s.Commit(), s.CheckCommit().Reason);
            root.Interaction!.Select(new Target { Kind = TargetKind.Intake, Anchor = "air-intake", Fluid = "air" });
            yield return null;
            yield return null;
            double before = s.OutletFlow("vent-1");
            Assert.Greater(before, 0);

            Assert.IsTrue(s.Execute(new SetEquipmentOnCommand("air-intake", false)));
            float t0 = Time.realtimeSinceStartup;
            double seconds = s.Data.Balancing.Devices.RunDownSeconds;
            while (Time.realtimeSinceStartup - t0 < seconds * 0.4) yield return null;
            string midway = Readout();
            StringAssert.Contains("Slowing to a stop", midway);
            StringAssert.Contains("Flow", midway, "the numbers stay up while it slows");
            Assert.Greater(s.OutletFlow("vent-1"), 0);
            Assert.Less(s.OutletFlow("vent-1"), before);

            while (s.OutletFlow("vent-1") > 0 && Time.realtimeSinceStartup - t0 < seconds * 3) yield return null;
            yield return null;
            yield return null;
            Assert.AreEqual(0, s.OutletFlow("vent-1"), 1e-9);
            StringAssert.Contains("Switched off", Readout());
            StringAssert.DoesNotContain("Slowing to a stop", Readout());
        }
    }
}
