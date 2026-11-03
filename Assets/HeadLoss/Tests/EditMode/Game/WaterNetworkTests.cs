using System.Collections.Generic;
using HeadLoss.Core;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.Commands;
using HeadLoss.Game.Economy;
using HeadLoss.Game.Session;
using HeadLoss.Sim.Network;
using NUnit.Framework;

namespace HeadLoss.Game.Tests
{
    /// <summary>
    /// Water networks in the two-floor fixture (Phase 4 tasks 1–4): pipes with real wall thicknesses, taps as demand
    /// nodes measured by residual pressure, risers through shafts, the city connection with or without a pump,
    /// regulators and relief valves on the building side, ceiling capacity and floor space.
    /// </summary>
    public class WaterNetworkTests
    {
        const double Bar = 1e5;

        static List<TilePos> RunOn(int floor, params (int x, int y)[] points)
        {
            var list = new List<TilePos>();
            foreach (TilePos t in GameTestUtil.Run(points)) list.Add(t.OnFloor(floor));
            return list;
        }

        static List<TilePos> Riser(int x, int y, int from, int to)
        {
            var list = new List<TilePos>();
            for (int f = from; ; f += System.Math.Sign(to - from))
            {
                list.Add(new TilePos(x, y, f));
                if (f == to) break;
            }
            return list;
        }

        static bool Pipe(BuildingSession s, List<TilePos> path, double d = 22, string material = "pvc") =>
            s.Execute(new PlaceRunCommand(path, d, material, false, "water"));

        /// <summary>Connection → kitchen tap on the ground floor → shaft → riser → both bathroom taps upstairs.</summary>
        static BuildingSession Plumbed(string material = "pvc")
        {
            BuildingSession s = GameTestUtil.Open("water-house");
            Assert.IsTrue(Pipe(s, RunOn(0, (9, 1), (9, 5)), 22, material), "trunk");
            Assert.IsTrue(Pipe(s, RunOn(0, (9, 2), (7, 2), (7, 1)), 15, material), "kitchen branch");
            Assert.IsTrue(Pipe(s, Riser(9, 5, 0, 1), 22, material), "riser");
            Assert.IsTrue(Pipe(s, RunOn(1, (9, 5), (9, 3)), 22, material), "upstairs");
            Assert.IsTrue(Pipe(s, RunOn(1, (9, 4), (7, 4), (7, 1)), 15, material), "bathroom branch");
            s.SolveNow();
            return s;
        }

        [Test]
        public void Materials_InternalDiameterAndRating()
        {
            var data = GameTestUtil.Data;
            Assert.AreEqual(20.2, data.Material("copper").InternalDiameterMm(22), 1e-9);
            Assert.AreEqual(15 - 2 * 1.5, data.Material("pvc").InternalDiameterMm(15), 1e-9);
            Assert.AreEqual(125, data.Material("galvanized-duct").InternalDiameterMm(125), 1e-9);
            // p_rated(D) = p_ref · D_ref / D: PVC is 6 bar at 22 mm, 3.14 bar at 42 mm.
            Assert.AreEqual(6 * Bar * 22 / 42, data.Material("pvc").RatedPressure(42), 1e-6);
            Assert.AreEqual(1500, data.Material("galvanized-duct").RatedPressure(250), 1e-9);
        }

        [Test]
        public void Taps_DeliverResidualPressure_UpperFloorLosesElevation()
        {
            BuildingSession s = Plumbed();
            BuildingEvaluation ev = s.DesignEvaluation;
            OutletStatus t1 = ev.Find("tap-1")!, t2 = ev.Find("tap-2")!, t3 = ev.Find("tap-3")!;
            Assert.IsTrue(t1.Connected && t2.Connected && t3.Connected);
            double supply = s.Supply.PressurePa;
            Assert.Less(t1.Delivery, supply);
            // One floor up costs ρ g × 3 m ≈ 0.29 bar on top of the friction.
            double rhoG3 = 999.5 * 9.80665 * 3;
            Assert.Less(t2.Delivery, t1.Delivery - rhoG3 * 0.95);
            Assert.Greater(t2.Delivery, 1.0 * Bar);
            Assert.AreEqual(OutletZone.Overdelivery, t1.Zone);
            // Mass balance: the city supplies exactly the three taps' design flow.
            FluidSolve w = s.Current!.Water!;
            Assert.AreEqual(3 * 0.10e-3, w.Compiled.SupplyFlow(w.Result), 1e-9);
        }

        [Test]
        public void Water_NotConnectedToCity_TapsGetNothing()
        {
            BuildingSession s = Plumbed();
            s.Supply = WaterSupply.None;
            s.MarkDirty();
            s.SolveNow();
            Assert.AreEqual(0, s.DesignEvaluation.Find("tap-1")!.Delivery);
            Assert.IsFalse(s.DesignEvaluation.Online);
        }

        [Test]
        public void Water_UnconnectedTap_KeepsBuildingOffline()
        {
            BuildingSession s = GameTestUtil.Open("water-house");
            Assert.IsTrue(Pipe(s, RunOn(0, (9, 1), (9, 2), (7, 2), (7, 1)), 15));
            s.SolveNow();
            Assert.AreEqual(OutletZone.NotConnected, s.DesignEvaluation.Find("tap-2")!.Zone);
            Assert.Greater(s.DesignEvaluation.Find("tap-1")!.Delivery, 1 * Bar);
        }

        [Test]
        public void Riser_OnlyInShaft()
        {
            BuildingSession s = GameTestUtil.Open("water-house");
            Assert.AreEqual(DrawError.NotInShaft, s.ValidateRun("water", Riser(5, 2, 0, 1), 22, "pvc", false));
            Assert.IsTrue(Pipe(s, RunOn(0, (9, 1), (9, 5))));
            Assert.AreEqual(DrawError.None, s.ValidateRun("water", Riser(9, 5, 0, 1), 22, "pvc", false));
        }

        [Test]
        public void Riser_JoinsCollinearRunsAndPricesFloorHeight()
        {
            BuildingSession s = GameTestUtil.Open("water-house");
            Assert.IsTrue(Pipe(s, RunOn(0, (9, 1), (9, 5))));
            double before = s.PendingCost.Charge;
            Assert.IsTrue(Pipe(s, Riser(9, 5, 0, 1)));
            DuctNetworkState w = s.State.Water;
            DuctSegment riser = w.Segments.Find(x => DuctEditor.IsRiser(w, x))!;
            Assert.IsNotNull(riser);
            // A bend from horizontal into the riser is an elbow node, not a merge.
            Assert.AreEqual(2, w.Segments.Count);
            double perMetre = Pricing.SegmentPerMetre(s.Data, riser);
            double expected = perMetre * s.Data.Balancing.Building.FloorHeight + Pricing.Fitting(s.Data, "elbow-90-sharp", 22, false);
            Assert.AreEqual(expected, s.PendingCost.Charge - before, 1e-9);
        }

        [Test]
        public void Water_WrongMaterialAndWrongPart_Refused()
        {
            BuildingSession s = GameTestUtil.Open("water-house");
            Assert.AreEqual(DrawError.WrongMaterial, s.ValidateRun("water", RunOn(0, (9, 1), (9, 3)), 125, "galvanized-duct", false));
            Assert.AreEqual(DrawError.WrongMaterial, s.ValidateRun("air", RunOn(0, (0, 2), (2, 2)), 22, "pvc", false));
            Assert.IsTrue(Pipe(s, RunOn(0, (9, 1), (9, 4))));
            var damper = new PlaceInlineCommand(new TilePos(9, 3), "damper", false, "water");
            Assert.IsFalse(s.Execute(damper));
            Assert.AreEqual(DrawError.WrongNetwork, damper.LastError);
            Assert.IsTrue(s.Execute(new PlaceInlineCommand(new TilePos(9, 3), "shutoff-valve", false, "water")));
        }

        [Test]
        public void Pump_RaisesPressure_AboveCodeMaxPaysZero_RegulatorFixesIt()
        {
            BuildingSession s = Plumbed("copper");
            double before = s.DesignEvaluation.Find("tap-1")!.Delivery;
            Assert.IsTrue(s.Execute(new SetIntakeEquipmentCommand("water", "small-booster-pump")));
            s.SolveNow();
            OutletStatus t1 = s.DesignEvaluation.Find("tap-1")!;
            Assert.Greater(t1.Delivery, before + 1 * Bar);
            Assert.AreEqual(OutletZone.AboveCodeMax, t1.Zone);
            Assert.Greater(s.DesignEvaluation.PowerW, 0);

            // A regulator on the trunk to the upper floor set to 2.5 bar brings those taps back under the code maximum.
            Assert.IsTrue(s.Execute(new PlaceInlineCommand(new TilePos(9, 3), "pressure-reducing-valve", false, "water")));
            Assert.IsTrue(s.Execute(new SetValveSettingCommand(new TilePos(9, 3), 2.5 * Bar)));
            s.SolveNow();
            FluidSolve w = s.Current!.Water!;
            int prv = w.Compiled.InlineLink[s.State.Water.NodeAt(new TilePos(9, 3))!.Id];
            Assert.AreEqual(LinkStatus.Active, w.Result.Status[prv]);
            foreach (string tap in new[] { "tap-2", "tap-3" })
            {
                OutletStatus t = s.DesignEvaluation.Find(tap)!;
                Assert.Less(t.Delivery, 2.5 * Bar);
                Assert.AreNotEqual(OutletZone.AboveCodeMax, t.Zone);
            }
        }

        [Test]
        public void ReliefValve_VentsAboveSetPressure()
        {
            BuildingSession s = Plumbed("copper");
            s.Execute(new SetIntakeEquipmentCommand("water", "small-booster-pump"));
            Assert.IsTrue(s.Execute(new PlaceInlineCommand(new TilePos(9, 3), "relief-valve", false, "water")));
            Assert.IsTrue(s.Execute(new SetValveSettingCommand(new TilePos(9, 3), 4 * Bar)));
            s.SolveNow();
            FluidSolve w = s.Current!.Water!;
            int node = s.State.Water.NodeAt(new TilePos(9, 3))!.Id;
            int relief = w.Compiled.ReliefLink[node];
            Assert.AreEqual(LinkStatus.Open, w.Result.Status[relief]);
            Assert.Greater(w.Result.Flow[relief], 0);
            Assert.Less(w.Result.NodePressure[w.Compiled.NodeIndex[node]], 4.6 * Bar);
        }

        [Test]
        public void ShutoffValve_Closed_StopsTheBranch()
        {
            BuildingSession s = Plumbed();
            Assert.IsTrue(s.Execute(new PlaceInlineCommand(new TilePos(8, 2), "shutoff-valve", false, "water")));
            Assert.IsTrue(s.Execute(new SetInlineOnCommand(new TilePos(8, 2), false, "water")));
            s.SolveNow();
            Assert.AreEqual(0, s.DesignEvaluation.Find("tap-1")!.Delivery);
            Assert.Greater(s.DesignEvaluation.Find("tap-3")!.Delivery, 1 * Bar);
        }

        [Test]
        public void CeilingCapacity_SharedBetweenNetworks()
        {
            BuildingSession s = GameTestUtil.Open("water-house");
            // A 250 mm duct (3 units) and a 22 mm pipe (1 unit) share tiles: 4 of 4.
            Assert.IsTrue(s.Execute(new PlaceRunCommand(RunOn(0, (0, 2), (1, 2), (1, 3), (5, 3)), 250, "galvanized-duct", false)));
            Assert.AreEqual(DrawError.None, s.ValidateRun("water", RunOn(0, (9, 1), (9, 3), (2, 3)), 22, "pvc", false));
            // A 54 mm pipe (2 units) does not fit beside the 250 mm duct.
            Assert.AreEqual(DrawError.NoCeilingSpace, s.ValidateRun("water", RunOn(0, (9, 1), (9, 3), (2, 3)), 54, "pvc", false));
            // Crossing it on one tile costs the same there: 3 + 2 > 4.
            Assert.AreEqual(DrawError.NoCeilingSpace, s.ValidateRun("water", RunOn(0, (9, 1), (9, 2), (4, 2), (4, 4)), 54, "pvc", false));
            Assert.AreEqual(DrawError.None, s.ValidateRun("water", RunOn(0, (9, 1), (9, 2), (4, 2), (4, 4)), 22, "pvc", false));
        }

        [Test]
        public void CeilingCapacity_UpsizeBlockedWhenFull()
        {
            BuildingSession s = GameTestUtil.Open("water-house");
            Assert.IsTrue(s.Execute(new PlaceRunCommand(RunOn(0, (0, 2), (1, 2), (1, 3), (5, 3)), 250, "galvanized-duct", false)));
            Assert.IsTrue(Pipe(s, RunOn(0, (9, 1), (9, 3), (2, 3))));
            DuctNetworkState w = s.State.Water;
            int pipe = w.Segments.Find(x => w.FindNode(x.NodeA)!.Pos == new TilePos(2, 3) || w.FindNode(x.NodeB)!.Pos == new TilePos(2, 3))!.Id;
            var up = new SetSegmentSizeCommand(pipe, 54, "water");
            Assert.IsFalse(s.Execute(up));
            Assert.AreEqual(DrawError.NoCeilingSpace, up.LastError);
            Assert.IsTrue(s.State.Water.Segments.TrueForAll(x => x.DiameterMm == 22));
        }

        [Test]
        public void CoreDrilling_Tier2CrossesInteriorWalls_AtACost()
        {
            BuildingSession s = GameTestUtil.Open("water-house");
            // Living (x < 6) to kitchen (x ≥ 6) without a door: core-drilled in a tier 2 building.
            Assert.IsTrue(s.Execute(new PlaceRunCommand(RunOn(0, (0, 2), (0, 1), (7, 1)), 125, "galvanized-duct", false)));
            double drill = s.Data.Balancing.Building.CoreDrillPrice;
            double ducts = 8 * Pricing.PerMetre(s.Data, "galvanized-duct", 125) + Pricing.Fitting(s.Data, "elbow-90-sharp", 125, true);
            Assert.AreEqual(ducts + drill, s.PendingCost.Charge, 1e-9);
            // The tier 1 small house still cannot cross walls.
            BuildingSession t1 = GameTestUtil.Open("small-house");
            Assert.AreEqual(DrawError.CrossesWall, DuctEditor.Validate(t1.State.Air, t1.Layout, GameTestUtil.Run((5, 2), (6, 2)), 4));
        }

        [Test]
        public void FloorSpace_LargeEquipmentNeedsRoomAtTheOpening()
        {
            BuildingSession s = GameTestUtil.Open("water-house");
            // The multistage pump (2×1) fits beside the water connection; it takes two kitchen floor tiles.
            Assert.AreEqual(DrawError.None, EquipmentFits.FloorSpace(s, "water", s.Data.EquipmentItem("multistage-pump"), out List<TilePos> tiles));
            Assert.AreEqual(2, tiles.Count);
            Assert.IsFalse(tiles.Contains(new TilePos(7, 1)), "the tap's tile stays free");
            // A pump does not go at the air intake; a fan does not go at the water connection.
            Assert.IsFalse(s.Execute(new SetIntakeEquipmentCommand("air-intake", "small-booster-pump")));
            Assert.IsFalse(s.Execute(new SetIntakeEquipmentCommand("water", "small-axial-fan")));
        }

        [Test]
        public void Fingerprint_IncludesWaterAndFloors()
        {
            BuildingSession s = Plumbed();
            string a = s.State.Fingerprint();
            BuildingState copy = s.State.Clone();
            Assert.AreEqual(a, copy.Fingerprint());
            copy.Water.Segments[0].DiameterMm = 28;
            Assert.AreNotEqual(a, copy.Fingerprint());
            Assert.IsTrue(a.Contains("water:sf0,1"), "riser recorded with its floors");
        }
    }
}
