using System;
using System.Collections.Generic;
using HeadLoss.Core;
using HeadLoss.Core.Data;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.City;
using HeadLoss.Game.Generation;
using HeadLoss.Game.Save;
using HeadLoss.Game.Session;
using Newtonsoft.Json;
using NUnit.Framework;
using static HeadLoss.Game.Tests.GameTestUtil;

namespace HeadLoss.Game.Tests
{
    public class BuildingGenerationTests
    {
        static readonly string[] Types = { "small-house", "kiosk", "family-house", "shop", "apartment-block", "restaurant" };

        static BuildingRequest Req(string type, ulong seed, string street = "south") => new BuildingRequest("plot-1", type, seed, street);

        static DistrictKind DistrictFor(string type) => Data.BuildingType(type).Districts.ContainsKey("residential") ? DistrictKind.Residential : DistrictKind.Commercial;

        [TestCaseSource(nameof(Types))]
        public void Generate_SameSeed_SameLayout(string type)
        {
            string a = JsonConvert.SerializeObject(BuildingGenerator.Generate(Data, Req(type, 42)));
            string b = JsonConvert.SerializeObject(BuildingGenerator.Generate(Data, Req(type, 42)));
            Assert.AreEqual(a, b);
            Assert.AreNotEqual(a, JsonConvert.SerializeObject(BuildingGenerator.Generate(Data, Req(type, 43))));
        }

        [TestCaseSource(nameof(Types))]
        public void Generate_LayoutIsValidAndEveryRoomReachable(string type)
        {
            BuildingTypeDef t = Data.BuildingType(type);
            BuildingTierDef tier = Data.BuildingTier(t.Tier);
            for (ulong seed = 1; seed <= 60; seed++)
            {
                BuildingLayoutDef? def = BuildingGenerator.Generate(Data, Req(type, seed, seed % 2 == 0 ? "north" : "east"));
                Assert.IsNotNull(def, $"{type} seed {seed}");
                var errors = new List<string>();
                DataValidator.ValidateLayout(type, def!, Data, errors);
                Assert.IsEmpty(errors, $"{type} seed {seed}: {string.Join("; ", errors)}");
                int small = Math.Min(def!.Width, def.Height), large = Math.Max(def.Width, def.Height);
                Assert.That(small, Is.InRange(Math.Min(tier.FootprintMin[0], tier.FootprintMin[1]), Math.Max(tier.FootprintMax[0], tier.FootprintMax[1])));
                Assert.That(large, Is.InRange(Math.Min(tier.FootprintMin[0], tier.FootprintMin[1]), Math.Max(tier.FootprintMax[0], tier.FootprintMax[1])));

                // Every floor has the same footprint, tiled by rooms (L and U shapes from tier 3 leave a cut-out); per
                // floor the doors form a tree joining every room but the shafts.
                Assert.That(def.Floors, Is.InRange(t.FloorsMin > 0 ? t.FloorsMin : tier.FloorsMin, t.FloorsMax > 0 ? t.FloorsMax : tier.FloorsMax));
                var layout = BuildingLayout.From(Data, def);
                int ground = 0;
                for (int f = 0; f < def.Floors; f++)
                {
                    int area = 0, rooms = 0;
                    foreach (RoomDef r in def.Rooms)
                        if (r.Floor == f)
                        {
                            area += r.Width * r.Height;
                            if (r.Type != "shaft") rooms++;
                        }
                    if (f == 0) ground = area;
                    Assert.AreEqual(ground, area, $"{type} seed {seed}: floor {f} has the ground floor's footprint");
                    if (t.Tier < Data.Buildings.Generation.LShapeFromTier) Assert.AreEqual(def.Width * def.Height, area, $"{type} seed {seed}: rooms cover the footprint");
                    else Assert.GreaterOrEqual(area, def.Width * def.Height / 2, $"{type} seed {seed}: the cut-out leaves most of the footprint");
                    Assert.AreEqual(rooms - 1, def.Doors.FindAll(d => d.Floor == f).Count, $"{type} seed {seed}: one door per room on floor {f}");
                    RoomDef first = def.Rooms.Find(r => r.Floor == f && r.Type != "shaft")!;
                    var seen = new HashSet<int>();
                    var queue = new Queue<TilePos>();
                    queue.Enqueue(new TilePos(first.X, first.Y, f));
                    var visited = new HashSet<TilePos> { queue.Peek() };
                    while (queue.Count > 0)
                    {
                        TilePos u = queue.Dequeue();
                        seen.Add(layout.RoomIndexAt(u));
                        foreach (TilePos v in new[] { new TilePos(u.X + 1, u.Y, f), new TilePos(u.X - 1, u.Y, f), new TilePos(u.X, u.Y + 1, f), new TilePos(u.X, u.Y - 1, f) })
                            if (layout.IsInside(v) && (layout.RoomIndexAt(v) == layout.RoomIndexAt(u) || layout.HasDoor(u, v)) && visited.Add(v)) queue.Enqueue(v);
                    }
                    Assert.AreEqual(rooms, seen.Count, $"{type} seed {seed}: every room on floor {f} reachable through doors");
                }

                // Openings: the air intake and the power connection, and a water connection when there are taps.
                bool taps = def.Outlets.Exists(o => BuildingState.OutletFluid(o.Type) == "water");
                Assert.AreEqual(taps ? 3 : 2, def.EntryOpenings.Count);
                Assert.AreEqual(1, def.EntryOpenings.FindAll(o => o.Utility == "air").Count);
                Assert.AreEqual(1, def.EntryOpenings.FindAll(o => o.Utility == "power").Count);
                Assert.AreEqual(taps ? 1 : 0, def.EntryOpenings.FindAll(o => o.Utility == "water").Count);
                Assert.IsTrue(def.EntryOpenings.TrueForAll(o => o.Floor == 0), "openings on the ground floor");
                Assert.IsTrue(def.Equipment.Exists(e => e.EquipmentId == "panel-basic"), "the basic panel is pre-placed");
                Assert.AreEqual(t.Tier >= 2, taps, "taps from tier 2");
                Assert.IsNotEmpty(def.Outlets);
                foreach (OutletPlacementDef o in def.Outlets)
                {
                    Assert.Greater(o.Min, 0);
                    OutletDef od = Data.Outlet(o.Type);
                    OutletRequirementDef baseReq = od.Tiers[def.Tier.ToString()];
                    double step = Data.Buildings.Generation.RoundingSteps[od.Unit];
                    Assert.That(o.Min, Is.InRange(baseReq.Min * 0.9 - step / 2, baseReq.Min * 1.1 + step / 2), "±10% then rounded");
                    Assert.AreEqual(0, Math.Round(o.Min / step, 6) % 1, 1e-6);
                }
                Assert.Contains("noise", def.Codes);
            }
        }

        [Test]
        public void Generate_EveryOccupiedRoomGetsAVent_ServiceRoomsDoNot()
        {
            for (ulong seed = 1; seed <= 20; seed++)
            {
                BuildingLayoutDef def = BuildingGenerator.Generate(Data, Req("small-house", seed))!;
                var layout = new BuildingLayout(def);
                foreach (RoomDef r in def.Rooms)
                {
                    int vents = def.Outlets.FindAll(o => layout.RoomOf(new TilePos(o.X, o.Y)) == r).Count;
                    if (r.Type == "hall") Assert.AreEqual(0, vents, "hall");
                    else Assert.GreaterOrEqual(vents, 1, r.Type);
                }
            }
        }

        [Test]
        public void Generate_IntakeFavoursTheStreetSide()
        {
            int street = 0;
            for (ulong seed = 1; seed <= 200; seed++)
                if (BuildingGenerator.Generate(Data, Req("small-house", seed, "north"))!.EntryOpenings[0].Facing == "north") street++;
            // 50% forced to the street side plus about a quarter of the rest by chance.
            Assert.That(street, Is.InRange(100, 160));
        }

        [Test]
        public void Generate_NudgesMoveTheIntakeFirst_ThenTheRooms()
        {
            BuildingLayoutDef a = BuildingGenerator.Generate(Data, Req("small-house", 9), 0)!;
            BuildingLayoutDef b = BuildingGenerator.Generate(Data, Req("small-house", 9), 1)!;
            int nudges = Data.Buildings.Generation.OpeningNudges;
            BuildingLayoutDef c = BuildingGenerator.Generate(Data, Req("small-house", 9), nudges)!;
            Assert.AreEqual(JsonConvert.SerializeObject(a.Rooms), JsonConvert.SerializeObject(b.Rooms), "same rooms while the openings move");
            Assert.AreNotEqual(JsonConvert.SerializeObject(a.Rooms), JsonConvert.SerializeObject(c.Rooms), "new rooms afterwards");
        }

        /// <summary>15-testing: every generated tier-1–3 building passes the solvability check (200 seeds per tier).</summary>
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void Solvability_EveryGeneratedBuildingOfTheTierPasses(int tier)
        {
            var failures = new List<string>();
            int lowered = 0;
            foreach (BuildingTypeDef t in Data.Buildings.Types)
            {
                if (t.Tier != tier || t.Districts.Count == 0) continue;
                for (ulong seed = 1; seed <= 100; seed++)
                {
                    GeneratedBuilding? g = BuildingFactory.Create(Data, Req(t.Id, seed), DistrictFor(t.Id));
                    Assert.IsNotNull(g);
                    if (g!.TierLowered) lowered++;
                    if (!g.Reference.Solvable) failures.Add($"{t.Id} {seed}: {g.Reference.Reason}");
                }
            }
            Assert.IsEmpty(failures, string.Join("\n", failures));
            Assert.LessOrEqual(lowered, 4, "few buildings drop a tier");
        }

        [Test]
        public void AutoRouter_ReferenceIsATreeWithNoOpenEnds()
        {
            for (ulong seed = 1; seed <= 30; seed++)
            {
                GeneratedBuilding g = BuildingFactory.Create(Data, Req(Types[seed % (ulong)Types.Length], seed), DistrictKind.Residential)!;
                foreach (DuctNetworkState net in g.Reference.State.Networks)
                {
                    int used = net.Nodes.FindAll(n => net.Degree(n.Id) > 0).Count;
                    if (used == 0) continue;
                    Assert.AreEqual(used - 1, net.Segments.Count, $"seed {seed}: {net.FluidId} is a tree");
                    Assert.IsFalse(net.Nodes.Exists(n => n.Kind == DuctNodeKind.Free && net.Degree(n.Id) == 1), $"seed {seed}: {net.FluidId} open end");
                }
                foreach (OutletPlacementDef o in g.Layout.Outlets)
                {
                    DuctNetworkState net = g.Reference.State.Net(BuildingState.OutletFluid(o.Type));
                    Assert.AreEqual(1, net.Degree(net.Nodes.Find(n => n.AnchorId == o.Id)!.Id), $"seed {seed}: {o.Id} connected");
                }
                Assert.Greater(g.Reference.Cost, 0);
                Assert.AreEqual(g.BudgetCap, Data.Balancing.Building.BudgetCapFactor * g.Reference.Cost, 1e-9);
            }
        }

        [Test]
        public void AutoRouter_UsesOnlyTierSizes_AndLongRadiusElbowsFromTierTwo()
        {
            for (ulong seed = 1; seed <= 20; seed++)
            {
                foreach (string type in Types)
                {
                    GeneratedBuilding g = BuildingFactory.Create(Data, Req(type, seed), DistrictFor(type))!;
                    List<double> sizes = AutoRouter.SizesFor(Data, g.Layout.Tier, "galvanized-duct");
                    DuctNetworkState net = g.Reference.State.Air;
                    foreach (DuctSegment s in net.Segments) Assert.Contains(s.DiameterMm, sizes);
                    foreach (DuctNode n in net.Nodes)
                        if (DuctEditor.IsElbow(net, n)) Assert.AreEqual(g.Layout.Tier >= 2, n.SmoothFittings, $"{type} {seed}");
                }
            }
        }

        [Test]
        public void AutoRouter_HandmadeHouses_ReferenceSolvable()
        {
            foreach (string id in new[] { "test-house", "small-house" })
            {
                BuildingLayout l = GameContext.LayoutFor(Data, id);
                ReferenceResult r = AutoRouter.Build(Data, l);
                Assert.IsTrue(r.Solvable, $"{id}: {r.Reason}");
            }
        }

        [Test]
        public void StarterGuarantee_TwoRoomsTwoVents_SolvableAtSeventyPercentCrank()
        {
            for (ulong citySeed = 1; citySeed <= 25; citySeed++)
            {
                CityLayout city = StarterGuarantee.NewCity(Data, citySeed, 1, out bool ok);
                Assert.IsTrue(ok, $"city {citySeed}");
                Plot p = city.Starter!;
                GeneratedBuilding g = BuildingFactory.Create(Data, p, city.Districts[p.District].Kind)!;
                Assert.AreEqual(2, g.Layout.Rooms.Count);
                Assert.AreEqual(2, g.Layout.Outlets.Count);
                Assert.AreEqual(0.7, g.Reference.CrankSpeed, 1e-9);
                Assert.AreEqual("hand-crank-fan", g.Reference.EquipmentId);
                Assert.IsTrue(g.Reference.Solvable, $"city {citySeed}: {g.Reference.Reason}");
                EntryOpeningDef intake = g.Layout.EntryOpenings[0];
                foreach (OutletPlacementDef o in g.Layout.Outlets)
                    Assert.LessOrEqual(Math.Abs(o.X - intake.X) + Math.Abs(o.Y - intake.Y), 6);
            }
        }
    }
}
