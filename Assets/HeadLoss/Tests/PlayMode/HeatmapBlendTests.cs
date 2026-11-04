using System.Collections;
using System.Collections.Generic;
using HeadLoss.Game.Commands;
using HeadLoss.Game.Session;
using HeadLoss.Unity;
using HeadLoss.Unity.View;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HeadLoss.PlayMode.Tests
{
    /// <summary>Heatmap colours run along pipes instead of jumping where they meet (<c>11-ui-visuals-audio.md</c>).</summary>
    public class HeatmapBlendShapeTests
    {
        [Test]
        public void BlendAt_RunsFromTheJointToTheSegmentsOwnValue()
        {
            const float jointA = 0.2f, mid = 0.6f, jointB = 0.9f, l = 6, blend = 1.5f;
            Assert.AreEqual(jointA, DuctRenderer.BlendAt(0, l, blend, jointA, mid, jointB), 1e-5f, "the joint itself");
            Assert.AreEqual(jointB, DuctRenderer.BlendAt(l, l, blend, jointA, mid, jointB), 1e-5f);
            Assert.AreEqual(0.4f, DuctRenderer.BlendAt(0.75f, l, blend, jointA, mid, jointB), 1e-5f, "halfway through the blend");
            Assert.AreEqual(mid, DuctRenderer.BlendAt(1.5f, l, blend, jointA, mid, jointB), 1e-5f, "its own value from there on");
            Assert.AreEqual(mid, DuctRenderer.BlendAt(3, l, blend, jointA, mid, jointB), 1e-5f);
            Assert.AreEqual(mid, DuctRenderer.BlendAt(4.5f, l, blend, jointA, mid, jointB), 1e-5f);

            float last = DuctRenderer.BlendAt(0, l, blend, jointA, mid, jointB), largest = 0;
            for (float x = 0.1f; x <= l; x += 0.1f)
            {
                float t = DuctRenderer.BlendAt(x, l, blend, jointA, mid, jointB);
                largest = Mathf.Max(largest, Mathf.Abs(t - last));
                last = t;
            }
            Assert.Less(largest, 0.05f, "no jump anywhere along it");
        }

        [Test]
        public void BlendAt_OnAShortPiece_MeetsInTheMiddle()
        {
            const float l = 1;
            Assert.AreEqual(0.5f, DuctRenderer.BlendAt(0.5f, l, 0.5f, 0, 0.5f, 1), 1e-5f);
            Assert.AreEqual(0.25f, DuctRenderer.BlendAt(0.25f, l, 0.5f, 0, 0.5f, 1), 1e-5f);
            Assert.AreEqual(0.75f, DuctRenderer.BlendAt(0.75f, l, 0.5f, 0, 0.5f, 1), 1e-5f);
        }
    }

    /// <summary>The same in the running game.</summary>
    public class HeatmapBlendViewTests
    {
        [UnityTearDown]
        public IEnumerator UnloadScene() => Scenes.Unload();

        static int DistinctColours(Mesh mesh)
        {
            var seen = new HashSet<Color>();
            foreach (Color c in mesh.colors) seen.Add(c);
            return seen.Count;
        }

        [UnityTest]
        public IEnumerator TwoSizesOnOneRun_BlendWhereTheyMeet_PlainViewStaysFlat()
        {
            yield return Scenes.LoadMain();
            var root = Object.FindAnyObjectByType<GameRoot>();
            BuildingSession s = root.Session!;
            // The same flow through two sizes: two different velocities, meeting at (4,3).
            Assert.IsTrue(s.Execute(new PlaceRunCommand(Scenes.Path((0, 3), (4, 3)), 125, "galvanized-duct", false)));
            Assert.IsTrue(s.Execute(new PlaceRunCommand(Scenes.Path((4, 3), (8, 3), (8, 5)), 200, "galvanized-duct", false)));
            s.SolveNow();
            yield return null;
            yield return null;
            Mesh Ducts() => root.Ducts!.GetComponent<MeshFilter>().sharedMesh;

            root.Heatmap!.Mode = HeatmapMode.Plain;
            s.MarkDirty();
            yield return null;
            yield return null;
            yield return null;
            yield return null;
            int plain = DistinctColours(Ducts());
            int plainVertices = Ducts().vertexCount;

            root.Heatmap.Mode = HeatmapMode.Velocity;
            s.MarkDirty();
            yield return null;
            yield return null;
            yield return null;
            yield return null;
            Assert.Greater(DistinctColours(Ducts()), plain + 5, "a run of shades instead of one flat colour for each size");
            Assert.Greater(Ducts().vertexCount, plainVertices, "short pieces where the colour changes");
        }
    }
}
