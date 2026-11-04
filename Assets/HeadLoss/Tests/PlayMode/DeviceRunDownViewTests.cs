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
    /// <summary>A fan keeps showing its numbers while it slows down or starts up (<c>05-simulation.md</c> §2).</summary>
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

        static IEnumerator WaitSeconds(double seconds)
        {
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < seconds) yield return null;
        }

        [UnityTest]
        public IEnumerator Fan_ReadoutFollowsItDown_ThenBackUp()
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
            string fan = s.Committed.EquipmentAt("air-intake")!.EquipmentId;
            double stop = s.Data.EquipmentItem(fan).RunDownSeconds, start = s.Data.EquipmentItem(fan).SpinUpSeconds;

            Assert.IsTrue(s.Execute(new SetEquipmentOnCommand("air-intake", false)));
            yield return WaitSeconds(stop * 0.4);
            string midway = Readout();
            StringAssert.Contains("Slowing to a stop", midway);
            StringAssert.Contains("Flow", midway, "the numbers stay up while it slows");
            Assert.Greater(s.OutletFlow("vent-1"), 0);
            Assert.Less(s.OutletFlow("vent-1"), before);

            float t0 = Time.realtimeSinceStartup;
            while (s.OutletFlow("vent-1") > 0 && Time.realtimeSinceStartup - t0 < stop * 3) yield return null;
            yield return null;
            yield return null;
            Assert.AreEqual(0, s.OutletFlow("vent-1"), 1e-9);
            StringAssert.Contains("Switched off", Readout());
            StringAssert.DoesNotContain("Slowing to a stop", Readout());

            Assert.IsTrue(s.Execute(new SetEquipmentOnCommand("air-intake", true)));
            yield return WaitSeconds(start * 0.4);
            string starting = Readout();
            StringAssert.Contains("Starting up", starting);
            StringAssert.Contains("Flow", starting);
            Assert.Less(s.OutletFlow("vent-1"), before);

            t0 = Time.realtimeSinceStartup;
            while (s.SpinShare(HeadLoss.Game.Failures.DamageKeys.Opening("air-intake")) > 0 && Time.realtimeSinceStartup - t0 < start * 3) yield return null;
            yield return null;
            yield return null;
            StringAssert.DoesNotContain("Starting up", Readout());
            Assert.AreEqual(before, s.OutletFlow("vent-1"), before * 0.01);
        }
    }
}
