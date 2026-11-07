using System.Collections.Generic;
using HeadLoss.Core;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.Commands;
using HeadLoss.Game.Session;
using HeadLoss.Game.Thermal;
using NUnit.Framework;

namespace HeadLoss.Game.Tests
{
    /// <summary>Phase 6 foundations: the thermal solve on piped liquid networks (<c>05-simulation.md</c> §4).</summary>
    public class PipeThermalTests
    {
        static List<TilePos> RunOn(int floor, params (int x, int y)[] points)
        {
            var list = new List<TilePos>();
            foreach (TilePos t in GameTestUtil.Run(points)) list.Add(t.OnFloor(floor));
            return list;
        }

        /// <summary>The water house plumbed on the ground floor: connection → trunk → kitchen tap.</summary>
        static BuildingSession Plumbed(string insulation = "")
        {
            BuildingSession s = GameTestUtil.Open("water-house");
            Assert.IsTrue(s.Execute(new PlaceRunCommand(RunOn(0, (9, 1), (9, 5)), 22, "copper", false, "water")));
            Assert.IsTrue(s.Execute(new PlaceRunCommand(RunOn(0, (9, 2), (7, 2), (7, 1)), 15, "copper", false, "water")));
            foreach (DuctSegment seg in s.State.Water.Segments) seg.Insulation = insulation;
            s.MarkDirty();
            s.SolveNow();
            return s;
        }

        static double KitchenTapK(BuildingSession s)
        {
            FluidSolve w = s.Current!.Water!;
            DuctNode tap = s.State.Water.Nodes.Find(n => n.Kind == DuctNodeKind.Outlet && w.Compiled.OutletNode.ContainsKey(n.AnchorId))!;
            return w.Temperatures.NodeK[tap.Id];
        }

        [Test]
        public void ColdWater_WarmsTowardTheRoomAlongItsPipes()
        {
            BuildingSession s = Plumbed();
            FluidSolve w = s.Current!.Water!;
            Assert.IsNotNull(w.Thermal, "water networks get a thermal solve");
            double mains = Units.CToK(GameTestUtil.Data.Balancing.Water.MainsTemperatureC);
            double tap = KitchenTapK(s);
            Assert.Greater(tap, mains, "water from the 12 °C main warms in a 20 °C building");
            Assert.Less(tap, Units.CToK(20));
            Assert.IsNull(s.Current.Air.Thermal, "air keeps its own thermal solve in the room climate");
        }

        [Test]
        public void Insulation_SlowsTheWarming()
        {
            double mains = Units.CToK(GameTestUtil.Data.Balancing.Water.MainsTemperatureC);
            double bare = KitchenTapK(Plumbed()) - mains, insulated = KitchenTapK(Plumbed("insulation-25")) - mains;
            Assert.Greater(bare, 0);
            Assert.Less(insulated, bare * 0.5, "25 mm insulation (U 0.8) against bare copper (U 10)");
        }

        [Test]
        public void NextSolve_CompilesWithTheTemperaturesItFound()
        {
            BuildingSession s = Plumbed();
            FluidSolve first = s.Current!.Water!;
            DuctSegment seg = s.State.Water.Segments[s.State.Water.Segments.Count - 1];
            double t = first.Temperatures.SegmentK[seg.Id];
            s.MarkDirty();
            s.SolveNow();
            FluidSolve second = s.Current!.Water!;
            Assert.AreEqual(t, second.Compiled.Graph.Links[second.Compiled.SegmentLink[seg.Id]].Temperature, 1e-9,
                "the flow solve uses the water's temperature (viscosity falls as water warms)");
        }

        [Test]
        public void ColdPipes_TakeHeatOutOfTheRooms()
        {
            BuildingSession s = Plumbed();
            var heat = new List<HeatInjection>();
            NetworkThermal.AddPipeHeat(s.Current!.Water!, s.State.Water, heat);
            Assert.IsNotEmpty(heat);
            double total = 0;
            foreach (HeatInjection h in heat)
            {
                Assert.IsTrue(h.Upper, "ceiling pipes exchange with the upper layer");
                total += h.W;
            }
            FluidSolve w = s.Current.Water!;
            double expected = 0;
            foreach (DuctSegment seg in s.State.Water.Segments) expected += w.Thermal!.HeatToAmbient[w.Compiled.SegmentLink[seg.Id]];
            Assert.Less(total, 0, "cold water absorbs heat from the air");
            Assert.AreEqual(expected, total, 1e-9);
        }
    }
}
