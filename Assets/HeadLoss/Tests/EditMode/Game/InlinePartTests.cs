using HeadLoss.Core;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.Commands;
using HeadLoss.Game.Networks;
using HeadLoss.Game.Session;
using HeadLoss.Sim.Network;
using NUnit.Framework;
using static HeadLoss.Game.Tests.GameTestUtil;

namespace HeadLoss.Game.Tests
{
    public class InlinePartTests
    {
        /// <summary>Test house with the fan and a straight trunk to vent-2 plus a branch to vent-1.</summary>
        static BuildingSession TwoBranches()
        {
            BuildingSession s = TestHouseWithFan();
            Draw(s, Run((0, 3), (8, 3), (8, 5)));
            Draw(s, Run((3, 3), (3, 5)));
            return s;
        }

        [Test]
        public void PlaceDamper_OnStraightDuct_SplitsRunIntoInlineNode()
        {
            BuildingSession s = TwoBranches();
            int before = s.State.Air.Segments.Count;
            Assert.IsTrue(s.Execute(new PlaceInlineCommand(new TilePos(3, 4), "damper")));
            DuctNode n = s.State.Air.NodeAt(new TilePos(3, 4))!;
            Assert.AreEqual(DuctNodeKind.Inline, n.Kind);
            Assert.AreEqual(2, s.State.Air.Degree(n.Id));
            Assert.AreEqual(before + 1, s.State.Air.Segments.Count);
            Assert.AreEqual((0, 1), (n.DirX, n.DirY), "points away from the intake");
        }

        [Test]
        public void PlaceInline_OnAFitting_IsRejected()
        {
            BuildingSession s = TwoBranches();
            var cmd = new PlaceInlineCommand(new TilePos(3, 3), "damper");
            Assert.IsFalse(s.Execute(cmd), "the tee tile");
            Assert.AreEqual(DrawError.NotOnStraightDuct, cmd.LastError);
            Assert.IsFalse(s.Execute(new PlaceInlineCommand(new TilePos(5, 6), "damper")), "no duct");
        }

        [Test]
        public void Damper_K_FollowsOpeningSquared()
        {
            BuildingSession s = TwoBranches();
            s.Execute(new PlaceInlineCommand(new TilePos(3, 4), "damper"));
            s.Execute(new SetDamperOpeningCommand(new TilePos(3, 4), 0.5));
            DuctNode n = s.State.Air.NodeAt(new TilePos(3, 4))!;
            CompiledNetwork c = NetworkCompiler.Compile(s.Layout, s.State, Data, 293.15);
            NetworkLink l = c.Graph.Links[c.InlineLink[n.Id]];
            Assert.AreEqual(LinkKind.Valve, l.Kind);
            Assert.AreEqual(0.3, l.SumK, 1e-12);
            Assert.AreEqual(0.5, l.Opening, 1e-12);
            Assert.AreEqual(Units.MmToM(125), l.Diameter, 1e-12);
        }

        [Test]
        public void Damper_ClampedToMinimumOpening()
        {
            BuildingSession s = TwoBranches();
            s.Execute(new PlaceInlineCommand(new TilePos(3, 4), "damper"));
            s.Execute(new SetDamperOpeningCommand(new TilePos(3, 4), 0.0));
            Assert.AreEqual(Data.Fitting("damper").MinOpening, s.State.Air.NodeAt(new TilePos(3, 4))!.Opening, 1e-12);
        }

        [Test]
        public void Damper_ClosingOneBranch_SendsAirToTheOther()
        {
            BuildingSession s = TwoBranches();
            s.Execute(new PlaceInlineCommand(new TilePos(3, 4), "damper"));
            s.SolveNow();
            double v1 = s.OutletFlow("vent-1"), v2 = s.OutletFlow("vent-2");
            s.Execute(new SetDamperOpeningCommand(new TilePos(3, 4), 0.2));
            s.SolveNow();
            Assert.Less(s.OutletFlow("vent-1"), v1 * 0.8);
            Assert.Greater(s.OutletFlow("vent-2"), v2 * 1.05, "balancing: the other branch gets more");
        }

        [Test]
        public void DamperSlider_ConsecutiveMovesMergeIntoOneUndo()
        {
            BuildingSession s = TwoBranches();
            s.Execute(new PlaceInlineCommand(new TilePos(3, 4), "damper"));
            int history = s.Commands.History.Count;
            for (int i = 9; i >= 3; i--) s.Execute(new SetDamperOpeningCommand(new TilePos(3, 4), i / 10.0));
            Assert.AreEqual(history + 1, s.Commands.History.Count);
            s.Undo();
            Assert.AreEqual(1.0, s.State.Air.NodeAt(new TilePos(3, 4))!.Opening, 1e-12);
        }

        [Test]
        public void Damper_OnBuiltPart_IsFreeButNeedsCommit()
        {
            BuildingSession s = TwoBranches();
            s.Execute(new PlaceInlineCommand(new TilePos(3, 4), "damper"));
            s.Game.Wallet = 1e6;
            s.Record.EverOnline = true;
            Assert.IsTrue(s.Commit());
            s.Execute(new SetDamperOpeningCommand(new TilePos(3, 4), 0.4));
            Assert.AreEqual(1.0, s.Committed.Air.NodeAt(new TilePos(3, 4))!.Opening, 1e-12, "the built damper waits for Commit");
            Assert.IsTrue(s.HasPendingDesign);
            Assert.AreEqual(0.0, s.PendingCost.Net, 1e-12, "moving a damper costs nothing");
            Assert.IsTrue(s.Commit());
            Assert.AreEqual(0.4, s.Committed.Air.NodeAt(new TilePos(3, 4))!.Opening, 1e-12);
        }

        [Test]
        public void FanOnOff_OnBuiltFan_AppliesToCommittedState()
        {
            BuildingSession s = TwoBranches();
            s.Game.Wallet = 1e6;
            s.Record.EverOnline = true;
            s.Commit();
            s.Execute(new SetEquipmentOnCommand("air-intake", false));
            Assert.IsFalse(s.Committed.EquipmentAt("air-intake")!.On);
            Assert.IsFalse(s.HasPendingDesign);
        }

        [Test]
        public void DeletingADuctBesideAPart_RemovesThePart()
        {
            BuildingSession s = TwoBranches();
            s.Execute(new PlaceInlineCommand(new TilePos(3, 4), "damper"));
            DuctNode n = s.State.Air.NodeAt(new TilePos(3, 4))!;
            DuctSegment upper = s.State.Air.Segments.Find(x => x.NodeA == n.Id && s.State.Air.FindNode(x.NodeB)!.Y == 5
                || x.NodeB == n.Id && s.State.Air.FindNode(x.NodeA)!.Y == 5)!;
            s.Execute(new DeleteSegmentCommand(upper.Id));
            DuctNode? left = s.State.Air.NodeAt(new TilePos(3, 4));
            Assert.IsNotNull(left, "the lower duct keeps its end");
            Assert.AreEqual(DuctNodeKind.Free, left!.Kind);
            Assert.AreEqual("", left.PartId);
        }

        [Test]
        public void RemoveInline_JoinsTheDuctAgain()
        {
            BuildingSession s = TwoBranches();
            int before = s.State.Air.Segments.Count;
            s.Execute(new PlaceInlineCommand(new TilePos(3, 4), "damper"));
            int id = s.State.Air.NodeAt(new TilePos(3, 4))!.Id;
            Assert.IsTrue(s.Execute(new RemoveInlineCommand(id)));
            Assert.AreEqual(before, s.State.Air.Segments.Count);
            Assert.IsNull(s.State.Air.NodeAt(new TilePos(3, 4)));
        }

        [Test]
        public void InlinePart_BlocksDrawingThroughOrFromIt()
        {
            BuildingSession s = TwoBranches();
            s.Execute(new PlaceInlineCommand(new TilePos(5, 3), "damper"));
            int max = Data.Balancing.Drawing.MaxNodeDegree;
            Assert.AreEqual(DrawError.Overlap, DuctEditor.Validate(s.State.Air, s.Layout, Run((6, 3), (6, 5), (5, 5), (5, 1)), max), "straight across the part");
            Assert.AreEqual(DrawError.NodeFull, DuctEditor.Validate(s.State.Air, s.Layout, Run((5, 3), (5, 5)), max));
        }

        [Test]
        public void InlineFan_AloneWithOpenIntake_DrawsAirThroughTheLouvre()
        {
            BuildingSession s = Open("test-house");
            s.Execute(new SetIntakeEquipmentCommand("air-intake", ""));
            Draw(s, Run((0, 3), (8, 3), (8, 5)));
            s.SolveNow();
            Assert.AreEqual(0.0, s.OutletFlow("vent-2"), 1e-9, "nothing drives the air");
            s.Execute(new PlaceInlineCommand(new TilePos(4, 3), "inline-duct-fan"));
            s.SolveNow();
            Assert.Greater(Units.M3sToM3h(s.OutletFlow("vent-2")), 100.0);
            Assert.Greater(s.DesignEvaluation.PowerW, 0.0, "inline fans draw electricity");
        }

        [Test]
        public void InlineFan_Flipped_BlowsBackwards()
        {
            BuildingSession s = Open("test-house");
            s.Execute(new SetIntakeEquipmentCommand("air-intake", ""));
            Draw(s, Run((0, 3), (8, 3), (8, 5)));
            s.Execute(new PlaceInlineCommand(new TilePos(4, 3), "inline-duct-fan"));
            int id = s.State.Air.NodeAt(new TilePos(4, 3))!.Id;
            s.Execute(new FlipInlineCommand(id));
            s.SolveNow();
            Assert.Less(s.OutletFlow("vent-2"), 0.0, "air is pulled in through the vent and out of the louvre");
            Assert.AreEqual(0.0, s.DesignEvaluation.Find("vent-2")!.Delivery, "reverse flow delivers nothing");
        }

        [Test]
        public void InlineFanOff_ActsAsAClosedValve()
        {
            BuildingSession s = TestHouseWithFan();
            Draw(s, Run((0, 3), (8, 3), (8, 5)));
            s.Execute(new PlaceInlineCommand(new TilePos(4, 3), "inline-duct-fan"));
            s.Execute(new SetInlineOnCommand(new TilePos(4, 3), false));
            s.SolveNow();
            Assert.AreEqual(0.0, s.OutletFlow("vent-2"), 1e-9);
        }

        [Test]
        public void Pricing_InlineParts()
        {
            BuildingSession s = TwoBranches();
            s.Game.Wallet = 1e6;
            s.Record.EverOnline = true;
            s.Commit();
            s.Execute(new PlaceInlineCommand(new TilePos(3, 4), "damper"));
            Assert.AreEqual(8.0 * 1.25, s.PendingCost.Net, 1e-9, "damper $8 × D/D_ref at 125 mm");
            s.Execute(new PlaceInlineCommand(new TilePos(5, 3), "inline-duct-fan"));
            Assert.AreEqual(8.0 * 1.25 + 300, s.PendingCost.Net, 1e-9);
        }
    }
}
