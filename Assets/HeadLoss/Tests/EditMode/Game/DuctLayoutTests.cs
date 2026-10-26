using System.Collections.Generic;
using System.IO;
using HeadLoss.Core;
using HeadLoss.Core.Data;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.Commands;
using HeadLoss.Game.Networks;
using HeadLoss.Game.Session;
using NUnit.Framework;

namespace HeadLoss.Game.Tests
{
    public class DuctLayoutTests
    {
        static DataRepository? _data;
        static DataRepository Data => _data ??= DataRepository.Load(new FileDataSource(System.IO.Path.Combine(Directory.GetCurrentDirectory(), "Assets", "HeadLoss", "Data")));

        /// <summary>The test house with the Phase 1 small axial fan in place of the starter's hand crank.</summary>
        static BuildingSession TestHouse() => GameTestUtil.TestHouseWithFan();

        /// <summary>Tiles from a to b through the given corners (orthogonal legs).</summary>
        static List<TilePos> Path(params (int x, int y)[] points)
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

        static bool Place(BuildingSession s, List<TilePos> path, double d = 125, bool smooth = false) =>
            s.Execute(new PlaceRunCommand(path, d, "galvanized-duct", smooth));

        [Test]
        public void PlaceRun_LShape_CreatesOneElbowNode()
        {
            BuildingSession s = TestHouse();
            Assert.IsTrue(Place(s, Path((0, 3), (3, 3), (3, 5))));
            Assert.AreEqual(2, s.State.Air.Segments.Count);
            DuctNode corner = s.State.Air.NodeAt(new TilePos(3, 3))!;
            Assert.IsTrue(DuctEditor.IsElbow(s.State.Air, corner));
        }

        [Test]
        public void PlaceRun_FromSegmentMiddle_MakesTee()
        {
            BuildingSession s = TestHouse();
            Place(s, Path((0, 3), (8, 3), (8, 5)));
            Assert.IsTrue(Place(s, Path((5, 3), (5, 1), (7, 1))));
            DuctNode tee = s.State.Air.NodeAt(new TilePos(5, 3))!;
            Assert.AreEqual(3, s.State.Air.Degree(tee.Id));
        }

        [Test]
        public void Validate_Rejections()
        {
            BuildingSession s = TestHouse();
            Place(s, Path((0, 3), (5, 3)));
            int max = Data.Balancing.Drawing.MaxNodeDegree;
            Assert.AreEqual(DrawError.StartNotOnNetwork, DuctEditor.Validate(s.State.Air, s.Layout, Path((6, 6), (6, 4)), max));
            Assert.AreEqual(DrawError.Overlap, DuctEditor.Validate(s.State.Air, s.Layout, Path((3, 3), (5, 3)), max));
            Assert.AreEqual(DrawError.Overlap, DuctEditor.Validate(s.State.Air, s.Layout, Path((5, 3), (5, 5), (2, 5), (2, 1)), max));
            Assert.AreEqual(DrawError.OutsideBuilding, DuctEditor.Validate(s.State.Air, s.Layout, Path((5, 3), (5, 7)), max));
            Assert.AreEqual(DrawError.NodeFull, DuctEditor.Validate(s.State.Air, s.Layout, Path((0, 3), (0, 5)), max), "intake takes one duct");
            Assert.AreEqual(DrawError.None, DuctEditor.Validate(s.State.Air, s.Layout, Path((5, 3), (5, 5), (3, 5)), max));
        }

        [Test]
        public void PlaceRun_StraightExtension_MergesIntoOneSegment()
        {
            BuildingSession s = TestHouse();
            Place(s, Path((0, 3), (3, 3)));
            Place(s, Path((3, 3), (6, 3)));
            Assert.AreEqual(1, s.State.Air.Segments.Count);
            Assert.IsNull(s.State.Air.NodeAt(new TilePos(3, 3)));
        }

        [Test]
        public void DeleteSegment_RemovesOrphanNodesAndKeepsAnchors()
        {
            BuildingSession s = TestHouse();
            Place(s, Path((0, 3), (3, 3), (3, 5)));
            int anchors = s.Layout.Outlets.Count + 1;
            foreach (DuctSegment seg in new List<DuctSegment>(s.State.Air.Segments))
                s.Execute(new DeleteSegmentCommand(seg.Id));
            Assert.AreEqual(0, s.State.Air.Segments.Count);
            Assert.AreEqual(anchors, s.State.Air.Nodes.Count);
        }

        [Test]
        public void Compiler_ElbowK_SharpAndLongRadius()
        {
            BuildingSession s = TestHouse();
            Place(s, Path((0, 3), (2, 3), (2, 5), (3, 5)));
            CompiledNetwork c = NetworkCompiler.Compile(s.Layout, s.State, Data, 293.15);
            var elbows = c.Fittings.FindAll(f => f.Kind == FittingKind.Elbow);
            Assert.AreEqual(2, elbows.Count);
            foreach (CompiledFitting f in elbows) Assert.AreEqual(1.2, f.K);
            Assert.AreEqual(1, c.Fittings.FindAll(f => f.Kind == FittingKind.Entry).Count);

            s.Execute(new UpgradeAllBendsCommand());
            c = NetworkCompiler.Compile(s.Layout, s.State, Data, 293.15);
            foreach (CompiledFitting f in c.Fittings.FindAll(f => f.Kind == FittingKind.Elbow)) Assert.AreEqual(0.3, f.K);
        }

        [Test]
        public void Compiler_Tee_RunAndBranchByFlowDirection()
        {
            BuildingSession s = TestHouse();
            Place(s, Path((0, 3), (8, 3), (8, 5)));
            Place(s, Path((5, 3), (5, 1), (7, 1)));
            CompiledNetwork c = NetworkCompiler.Compile(s.Layout, s.State, Data, 293.15);
            DuctNode tee = s.State.Air.NodeAt(new TilePos(5, 3))!;
            var atTee = c.Fittings.FindAll(f => f.NodeId == tee.Id);
            Assert.AreEqual(2, atTee.Count);
            Assert.AreEqual(1, atTee.FindAll(f => f.Kind == FittingKind.TeeRun && f.K == 0.4).Count);
            Assert.AreEqual(1, atTee.FindAll(f => f.Kind == FittingKind.TeeBranch && f.K == 1.0).Count);
        }

        [Test]
        public void Compiler_Reducer_ContractionOnSmallerSegment()
        {
            BuildingSession s = TestHouse();
            Place(s, Path((0, 3), (4, 3)), 160);
            Place(s, Path((4, 3), (8, 3), (8, 5)), 100);
            CompiledNetwork c = NetworkCompiler.Compile(s.Layout, s.State, Data, 293.15);
            CompiledFitting red = c.Fittings.Find(f => f.Kind == FittingKind.Reducer)!;
            Assert.AreEqual(0.2, red.K);
            DuctSegment small = s.State.Air.Segments.Find(x => x.DiameterMm == 100 && c.SegmentLink[x.Id] == red.LinkIndex)!;
            Assert.IsNotNull(small);
        }

        [Test]
        public void Session_FanToOneVent_DeliversFlowAndConserves()
        {
            BuildingSession s = TestHouse();
            Place(s, Path((0, 3), (3, 3), (3, 5)));
            s.SolveNow();
            Assert.IsTrue(s.Current!.Result.Converged);
            double q = s.OutletFlow("vent-1");
            Assert.Greater(Units.M3sToM3h(q), 50);
            Assert.AreEqual(s.Current.Result.Flow[s.Current.Compiled.FanLink["air-intake"]], q, 1e-9);
            Assert.AreEqual(0, s.OutletFlow("vent-2"));
        }

        [Test]
        public void Session_FanOff_NoFlow()
        {
            BuildingSession s = TestHouse();
            Place(s, Path((0, 3), (3, 3), (3, 5)));
            s.Execute(new SetEquipmentOnCommand("air-intake", false));
            s.SolveNow();
            Assert.AreEqual(0, s.OutletFlow("vent-1"), 1e-12);
        }

        [Test]
        public void Session_FanOff_WithSeveralBranches_ConvergesToZeroFlow()
        {
            BuildingSession s = TestHouse();
            Place(s, Path((0, 3), (5, 3), (5, 5), (8, 5)));
            Place(s, Path((3, 3), (3, 5)));
            Place(s, Path((5, 3), (5, 1), (7, 1)));
            s.SolveNow();
            Assert.Greater(s.OutletFlow("vent-2"), 0);
            s.Execute(new SetEquipmentOnCommand("air-intake", false));
            s.SolveNow();
            Assert.IsFalse(s.Settling, "fan off must converge");
            foreach (double q in s.Current!.Result.Flow) Assert.AreEqual(0, q, 1e-9);
        }

        [Test]
        public void Session_FanOff_ThenDrawMore_StillSolves_ThenFanOnDelivers()
        {
            BuildingSession s = TestHouse();
            Place(s, Path((0, 3), (5, 3), (5, 5), (8, 5)));
            s.Execute(new SetEquipmentOnCommand("air-intake", false));
            s.SolveNow();
            Place(s, Path((3, 3), (3, 5)));
            s.SolveNow();
            Assert.IsFalse(s.Settling);
            Assert.IsTrue(s.Current!.Compiled.OutletLink.ContainsKey("vent-1"), "new duct is in the shown solve");
            s.Execute(new SetEquipmentOnCommand("air-intake", true));
            s.SolveNow();
            Assert.Greater(s.OutletFlow("vent-1"), 0);
        }

        [Test]
        public void Cross_DrawingStraightAcrossADuct_MakesFourWayJunction()
        {
            BuildingSession s = TestHouse();
            Place(s, Path((0, 3), (8, 3), (8, 5)));
            Assert.IsTrue(Place(s, Path((7, 1), (7, 2), (6, 2), (6, 5), (3, 5))), "run crossing the trunk at (6,3)");
            DuctNode cross = s.State.Air.NodeAt(new TilePos(6, 3))!;
            Assert.AreEqual(4, s.State.Air.Degree(cross.Id));
            CompiledNetwork c = NetworkCompiler.Compile(s.Layout, s.State, Data, 293.15);
            var atCross = c.Fittings.FindAll(f => f.NodeId == cross.Id);
            Assert.AreEqual(3, atCross.Count);
            Assert.IsTrue(atCross.TrueForAll(f => f.FittingId == "cross-sharp"));
            Assert.AreEqual(1, atCross.FindAll(f => f.Kind == FittingKind.TeeRun).Count);
            s.SolveNow();
            Assert.IsFalse(s.Settling);
            Assert.Greater(s.OutletFlow("vent-1"), 0);
            Assert.Greater(s.OutletFlow("vent-2"), 0);
            Assert.Greater(s.OutletFlow("vent-3"), 0);
        }

        [Test]
        public void Cross_FromExistingTee_FourthDuct()
        {
            BuildingSession s = TestHouse();
            Place(s, Path((0, 3), (8, 3), (8, 5)));
            Place(s, Path((5, 3), (5, 1), (7, 1)));
            Assert.IsTrue(Place(s, Path((5, 3), (5, 5), (3, 5))));
            Assert.AreEqual(4, s.State.Air.Degree(s.State.Air.NodeAt(new TilePos(5, 3))!.Id));
        }

        [Test]
        public void Cross_DrawingAlongADuct_IsStillAnOverlap()
        {
            BuildingSession s = TestHouse();
            Place(s, Path((0, 3), (8, 3), (8, 5)));
            Assert.AreEqual(DrawError.Overlap, DuctEditor.Validate(s.State.Air, s.Layout, Path((7, 1), (4, 1), (4, 3), (6, 3), (6, 5)), 4));
        }

        [Test]
        public void OpenEnd_DanglingDuct_DischargesAllFanFlowIntoRoom()
        {
            BuildingSession s = TestHouse();
            Place(s, Path((0, 3), (4, 3), (4, 1)));
            s.SolveNow();
            DuctNode end = s.State.Air.NodeAt(new TilePos(4, 1))!;
            Assert.IsTrue(s.Current!.Compiled.OpenEndLink.TryGetValue(end.Id, out int link));
            double fan = s.Current.Result.Flow[s.Current.Compiled.FanLink["air-intake"]];
            Assert.Greater(Units.M3sToM3h(fan), 50);
            Assert.AreEqual(fan, s.Current.Result.Flow[link], 1e-9);
        }

        [Test]
        public void OpenEnd_SplitsFlowWithVent()
        {
            BuildingSession s = TestHouse();
            Place(s, Path((0, 3), (3, 3), (3, 5)));
            s.SolveNow();
            double ventAlone = s.OutletFlow("vent-1");
            Place(s, Path((2, 3), (2, 1)));
            s.SolveNow();
            Assert.Less(s.OutletFlow("vent-1"), ventAlone, "air leaking from an open branch starves the vent");
        }

        [Test]
        public void OpenEnd_FanWithNoDuct_BlowsIntoRoom()
        {
            BuildingSession s = TestHouse();
            s.SolveNow();
            Assert.AreEqual(1, s.Current!.Compiled.OpenEndLink.Count);
            Assert.Greater(s.Current.Result.Flow[s.Current.Compiled.FanLink["air-intake"]], 0);
        }

        [Test]
        public void Preview_DoesNotChangeState()
        {
            BuildingSession s = TestHouse();
            DrawPreview p = s.Preview(Path((0, 3), (3, 3), (3, 5)), 125, "galvanized-duct", false);
            Assert.AreEqual(DrawError.None, p.Error);
            Assert.Greater(p.Snapshot!.Result.Flow[p.Snapshot.Compiled.OutletLink["vent-1"]], 0);
            Assert.AreEqual(0, s.State.Air.Segments.Count);
        }

        /// <summary>
        /// Phase 1 gate (14-roadmap.md): a duct run with two sharp 90° bends feeding a vent; switching both to
        /// long-radius raises that vent's flow by a visible amount.
        /// </summary>
        [Test]
        public void Gate_TwoSharpBendsToLongRadius_RaisesVentFlowVisibly()
        {
            BuildingSession s = TestHouse();
            Place(s, Path((0, 3), (2, 3), (2, 5), (3, 5)), 100);
            s.SolveNow();
            double sharp = s.OutletFlow("vent-1");
            s.Execute(new UpgradeAllBendsCommand());
            s.SolveNow();
            double smooth = s.OutletFlow("vent-1");
            TestContext.WriteLine($"sharp {Units.M3sToM3h(sharp):F1} m³/h → long-radius {Units.M3sToM3h(smooth):F1} m³/h (+{(smooth / sharp - 1) * 100:F1}%)");
            Assert.Greater(smooth / sharp, 1.08);
        }

        [Test]
        public void Solve_TestHouseThreeVents_Deterministic()
        {
            BuildingSession a = TestHouse(), b = TestHouse();
            foreach (BuildingSession s in new[] { a, b })
            {
                Place(s, Path((0, 3), (5, 3), (5, 5), (8, 5)));
                Place(s, Path((3, 3), (3, 5)));
                Place(s, Path((5, 3), (5, 1), (7, 1)), 100);
                s.SolveNow();
            }
            Assert.AreEqual(a.Current!.Result.Flow, b.Current!.Result.Flow);
            Assert.Greater(a.OutletFlow("vent-1"), 0);
            Assert.Greater(a.OutletFlow("vent-2"), 0);
            Assert.Greater(a.OutletFlow("vent-3"), 0);
        }
    }
}
