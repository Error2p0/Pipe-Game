using System.Collections.Generic;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.City;
using HeadLoss.Game.Session;
using NUnit.Framework;

namespace HeadLoss.Game.Tests
{
    /// <summary>City mains and cables (<c>03-city.md</c>, <c>15-testing.md</c>: capacity sharing, brownouts furthest first).</summary>
    public class CityUtilityTests
    {
        static GameContext NewCity()
        {
            GameState g = GameContext.NewGame(GameTestUtil.Data, 7);
            g.Wallet = 1e7;
            g.City!.UnlockedTier = 5;
            return new GameContext(GameTestUtil.Data, g);
        }

        /// <summary>An L-shaped path of city tiles (x first, then y).</summary>
        static List<(int x, int y)> L((int x, int y) a, (int x, int y) b)
        {
            var path = new List<(int, int)> { a };
            int x = a.x, y = a.y;
            while (x != b.x) { x += System.Math.Sign(b.x - x); path.Add((x, y)); }
            while (y != b.y) { y += System.Math.Sign(b.y - y); path.Add((x, y)); }
            return path;
        }

        static FacilityPlacement Plant(GameContext c) => c.City!.Facilities.Find(f => f.Utility == "water")!;

        /// <summary>A plot not on the plant's row or column, preferring tier 2 (it has taps).</summary>
        static Plot SomePlot(GameContext c, int tier = 2, int skip = 0) =>
            c.City!.Plots.FindAll(p => p.Tier == tier)[skip];

        [Test]
        public void Route_MustStartAtTheFacilityOrAMain()
        {
            GameContext c = NewCity();
            Plot plot = SomePlot(c);
            FacilityPlacement plant = Plant(c);
            RouteQuote bad = Utilities.Quote(c.Data, c.City!, c.Game.City!.Utilities, "water", 100, 0, L((plot.X - 3, plot.Y), (plot.X, plot.Y)), false);
            if (!(plot.X - 3 >= plant.X && plot.X - 3 < plant.X + plant.Width)) Assert.IsFalse(bad.Valid);
            RouteQuote ok = Utilities.Quote(c.Data, c.City!, c.Game.City!.Utilities, "water", 100, 0, L((plant.X, plant.Y), (plot.X, plot.Y)), false);
            Assert.IsTrue(ok.Valid, ok.Error);
            Assert.AreEqual(plot.Id, ok.Destination);
            Assert.Greater(ok.Cost, 0);
        }

        [Test]
        public void Route_UnderHousesAndProtectedSites_Fines_DrillingIsLegalButDear()
        {
            GameContext c = NewCity();
            CityLayout city = c.City!;
            // Find a straight run that passes under some other plot.
            FacilityPlacement plant = Plant(c);
            Plot target = city.Plots.Find(p => p.Tier <= 2 && (p.X != plant.X))!;
            List<(int x, int y)> path = L((plant.X, plant.Y), (target.X, target.Y));
            RouteQuote plain = Utilities.Quote(c.Data, city, c.Game.City!.Utilities, "water", 100, 0, path, false);
            RouteQuote drilled = Utilities.Quote(c.Data, city, c.Game.City!.Utilities, "water", 100, 0, path, true);
            Assert.IsTrue(plain.Valid && drilled.Valid);
            Assert.AreEqual(0, drilled.FinesPerCycle);
            Assert.AreEqual(0, drilled.CrossingCost);
            Assert.Greater(drilled.Cost, plain.PipeCost);
            foreach (CityViolation v in plain.Violations) Assert.Greater(v.FinePerCycle, 0);
        }

        [Test]
        public void CitySolve_ConnectsTheBuilding_AtFacilityPressureLessFriction()
        {
            GameContext c = NewCity();
            Plot plot = SomePlot(c);
            BuildingSession s = c.Open(plot.Id);
            c.SolveCity();
            Assert.IsFalse(s.Supply.Connected, "no main yet");
            RouteQuote q = c.LayMain("water", 150, 0, L((Plant(c).X, Plant(c).Y), (plot.X, plot.Y)), true);
            Assert.IsTrue(q.Valid, q.Error);
            c.SolveCity();
            Assert.IsTrue(s.Supply.Connected);
            double facility = c.Data.Balancing.City.FacilityPressure["water"];
            Assert.LessOrEqual(s.Supply.PressurePa, facility);
            Assert.Greater(s.Supply.PressurePa, facility * 0.9, "a short main loses little");
        }

        [Test]
        public void Hint_OfAWaterBuilding_CountsFlowNotTapPressure_AndItsRealFloors()
        {
            GameContext c = NewCity();
            foreach (Plot p in c.City!.Plots.FindAll(x => x.Tier == 3).GetRange(0, 3))
            {
                DifficultyHint? h = null;
                c.CityBuildings!.Get(p.Id);
                for (int i = 0; i < 100 && h == null; i++) h = c.CityBuildings.Hint(p.Id);
                Assert.NotNull(h);
                Assert.Less(h!.FlowDemand, 5, "m³/s: flow, not bar");
                Assert.AreEqual(c.CityBuildings.Get(p.Id)!.Layout.Floors, h.Floors);
            }
        }

        [Test]
        public void Route_EndingOnABuilding_IsNotStoppedByTheLinesEndingThere()
        {
            GameContext c = NewCity();
            Plot plot = SomePlot(c, 2, 0), other = SomePlot(c, 2, 1);
            FacilityPlacement plant = Plant(c), sub = c.City!.Facilities.Find(f => f.Utility == "power")!;
            Assert.IsTrue(c.LayMain("water", 150, 0, L((plant.X, plant.Y), (plot.X, plot.Y)), true).Valid);
            Assert.IsTrue(c.LayMain("water", 100, 0, L((plot.X, plot.Y), (other.X, other.Y)), true).Valid);
            // Two water lines already touch the plot's tile; its power cable still connects.
            RouteQuote q = Utilities.Quote(c.Data, c.City, c.Game.City!.Utilities, "power", 0, 50, L((sub.X, sub.Y), (plot.X, plot.Y)), true);
            StringAssert.DoesNotContain("No room", q.Error);
        }

        [Test]
        public void PreviewWater_ShowsTheResultingPressure_WithoutLayingAnything()
        {
            GameContext c = NewCity();
            Plot plot = SomePlot(c);
            var path = L((Plant(c).X, Plant(c).Y), (plot.X, plot.Y));
            RouteQuote q = Utilities.Quote(c.Data, c.City!, c.Game.City!.Utilities, "water", 150, 0, path, true);
            WaterConnection? w = c.PreviewWater(150, path, true, q);
            Assert.NotNull(w);
            Assert.IsTrue(w!.Value.Connected);
            Assert.Less(w.Value.PressurePa, c.Data.Balancing.City.FacilityPressure["water"]);
            Assert.IsEmpty(c.Game.City!.Utilities.Mains, "only a preview");
        }

        [Test]
        public void CitySolve_OverloadedMain_SharesCapacityDownstream()
        {
            GameContext c = NewCity();
            UtilityState u = c.Game.City!.Utilities;
            FacilityPlacement plant = Plant(c);
            Plot a = SomePlot(c, 2, 0), b = SomePlot(c, 2, 1);
            // A thin trunk to a, a branch from its far end to b; each asks for more than the trunk carries.
            var trunk = L((plant.X, plant.Y), (a.X, a.Y));
            Utilities.Lay(u, "water", "water-main", 100, 0, trunk, true, Utilities.Quote(c.Data, c.City!, u, "water", 100, 0, trunk, true));
            var branch = L((a.X, a.Y), (b.X, b.Y));
            Utilities.Lay(u, "water", "water-main", 100, 0, branch, true, Utilities.Quote(c.Data, c.City!, u, "water", 100, 0, branch, true));
            double capacity = Utilities.CapacityM3s(Utilities.FamilyFor(c.Data, "water")!, 100);
            CityNetworkResult r = CityNetwork.Solve(c.Data, c.City!, u, plot => capacity * 0.8, plot => 0);
            Assert.IsTrue(r.Water[a.Id].Connected && r.Water[b.Id].Connected);
            Assert.Less(r.Water[a.Id].Share, 1, "the trunk carries 160% of its capacity: both downstream buildings share it");
            Assert.AreEqual(r.Water[a.Id].Share, r.Water[b.Id].Share, 0.05);
            Assert.Greater(r.Utilisation[1], 1, "utilisation shows the overload");
            CityNetworkResult light = CityNetwork.Solve(c.Data, c.City!, u, plot => capacity * 0.2, plot => 0);
            Assert.AreEqual(1, light.Water[a.Id].Share, 1e-9);
            Assert.AreEqual(1, light.Water[b.Id].Share, 1e-9);
        }

        [Test]
        public void Power_Brownout_CutsTheFurthestBuildingFirst()
        {
            GameContext c = NewCity();
            UtilityState u = c.Game.City!.Utilities;
            FacilityPlacement sub = c.City!.Facilities.Find(f => f.Utility == "power")!;
            Plot near = c.City.Plots.Find(p => System.Math.Abs(p.X - sub.X) + System.Math.Abs(p.Y - sub.Y) > 2)!;
            Plot far = c.City.Plots.Find(p => p != near && System.Math.Abs(p.X - near.X) + System.Math.Abs(p.Y - near.Y) > 3)!;
            var trunk = L((sub.X, sub.Y), (near.X, near.Y));
            Utilities.Lay(u, "power", "power-cable", 0, 50, trunk, true, Utilities.Quote(c.Data, c.City, u, "power", 0, 50, trunk, true));
            var branch = L((near.X, near.Y), (far.X, far.Y));
            Utilities.Lay(u, "power", "power-cable", 0, 50, branch, true, Utilities.Quote(c.Data, c.City, u, "power", 0, 50, branch, true));
            CityNetworkResult r = CityNetwork.Solve(c.Data, c.City, u, _ => 0, _ => 40);
            Assert.AreEqual(40, r.Power[near.Id].AllocatedKW, 1e-9, "the nearer building keeps its power");
            Assert.AreEqual(10, r.Power[far.Id].AllocatedKW, 1e-9, "the furthest is cut to what is left");
            Assert.AreEqual(50, r.Power[far.Id].ConnectionKW, 1e-9);
        }

        [Test]
        public void Connection_BelowTheDraw_IsABrownoutNotATrip()
        {
            BuildingSession s = GameTestUtil.TestHouseWithFan();
            GameTestUtil.DrawAndCommit(s, GameTestUtil.Run((0, 3), (3, 3), (3, 5)));
            for (int i = 0; i < 5; i++) s.Tick(0.5);
            Assert.IsFalse(s.Record.PanelTripped);
            // A 1 W limit: the small fan's draw is over it.
            s.Power = new PowerConnection(true, 0.0005, 0.0005);
            for (int i = 0; i < 10; i++) s.Tick(0.5);
            Assert.IsFalse(s.Record.PanelTripped, "a connection limit is a brownout, not a trip");
            Assert.Less(s.PowerSpeed, 1, "the fan slows in the brownout");
        }

        [Test]
        public void Fines_AreChargedEveryCycle_InsuranceHalvesThem()
        {
            GameContext c = NewCity();
            CityLayout city = c.City!;
            FacilityPlacement plant = Plant(c);
            // A route across other plots to collect under-house fines.
            Plot target = city.Plots.FindAll(p => p.Tier == 1)[5];
            var path = L((plant.X, plant.Y), (target.X, target.Y));
            RouteQuote q = c.LayMain("water", 100, 0, path, false);
            Assert.IsTrue(q.Valid, q.Error);
            c.SolveCity();
            double fines = c.FinesPerCycle;
            Assume.That(fines, Is.GreaterThan(0), "this route passes under something");
            double before = c.Game.Wallet;
            GameLoop.CityCosts(c, c.Data.Balancing.Economy.IncomeCycleSeconds);
            Assert.AreEqual(fines, before - c.Game.Wallet, 1e-6);
            Assert.IsTrue(c.SetInsurance(true));
            c.SolveCity();
            Assert.AreEqual(fines * 0.5, c.FinesPerCycle, 1e-6);
        }

        [Test]
        public void RemoveMain_RefundsHalf_AndItsBranchesGoToo()
        {
            GameContext c = NewCity();
            FacilityPlacement plant = Plant(c);
            Plot a = SomePlot(c, 2, 0), b = SomePlot(c, 2, 1);
            RouteQuote q1 = c.LayMain("water", 100, 0, L((plant.X, plant.Y), (a.X, a.Y)), true);
            RouteQuote q2 = c.LayMain("water", 100, 0, L((a.X, a.Y), (b.X, b.Y)), true);
            Assert.IsTrue(q1.Valid && q2.Valid);
            double wallet = c.Game.Wallet;
            int trunk = c.Game.City!.Utilities.Mains[0].Id;
            Assert.IsTrue(c.RemoveMain(trunk));
            Assert.IsEmpty(c.Game.City.Utilities.Mains, "the branch lost its supply and went too");
            Assert.AreEqual(wallet + (q1.Cost + q2.Cost) * 0.5, c.Game.Wallet, 1e-6);
        }
    }
}
