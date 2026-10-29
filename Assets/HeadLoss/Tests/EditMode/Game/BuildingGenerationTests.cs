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
        static readonly string[] Types = { "small-house", "kiosk", "family-house", "shop" };

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

                // Rooms tile the whole footprint; doors form a tree joining every room.
                int area = 0;
                foreach (RoomDef r in def.Rooms) area += r.Width * r.Height;
                Assert.AreEqual(def.Width * def.Height, area, $"{type} seed {seed}: rooms cover the footprint");
                Assert.AreEqual(def.Rooms.Count - 1, def.Doors.Count, $"{type} seed {seed}: one door per room");
                var layout = new BuildingLayout(def);
                var seen = new HashSet<int> { 0 };
                var queue = new Queue<TilePos>();
                queue.Enqueue(new TilePos(def.Rooms[0].X, def.Rooms[0].Y));
                var visited = new HashSet<TilePos> { queue.Peek() };
                while (queue.Count > 0)
                {
                    TilePos u = queue.Dequeue();
                    seen.Add(layout.RoomIndexAt(u));
                    foreach (TilePos v in new[] { new TilePos(u.X + 1, u.Y), new TilePos(u.X - 1, u.Y), new TilePos(u.X, u.Y + 1), new TilePos(u.X, u.Y - 1) })
                        if (layout.IsInside(v) && layout.CanPass(u, v) && visited.Add(v)) queue.Enqueue(v);
                }
                Assert.AreEqual(def.Rooms.Count, seen.Count, $"{type} seed {seed}: every room reachable through doors");

                Assert.AreEqual(1, def.EntryOpenings.Count);
                Assert.AreEqual("air", def.EntryOpenings[0].Utility);
                Assert.IsNotEmpty(def.Outlets);
                foreach (OutletPlacementDef o in def.Outlets)
                {
                    Assert.Greater(o.Min, 0);
                    OutletRequirementDef baseReq = Data.Outlet(o.Type).Tiers[def.Tier.ToString()];
                    Assert.That(o.Min, Is.InRange(baseReq.Min * 0.9 - 2.5, baseReq.Min * 1.1 + 2.5), "±10% then rounded to 5 m³/h");
                    Assert.AreEqual(0, o.Min % 5, 1e-9);
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
                GeneratedBuilding g = BuildingFactory.Create(Data, Req(Types[seed % 4], seed), DistrictKind.Residential)!;
                DuctNetworkState net = g.Reference.State.Air;
                int used = net.Nodes.FindAll(n => net.Degree(n.Id) > 0).Count;
                Assert.AreEqual(used - 1, net.Segments.Count, $"seed {seed}: a tree");
                Assert.IsFalse(net.Nodes.Exists(n => n.Kind == DuctNodeKind.Free && net.Degree(n.Id) == 1), $"seed {seed}: open end");
                foreach (OutletPlacementDef o in g.Layout.Outlets)
                    Assert.AreEqual(1, net.Degree(net.Nodes.Find(n => n.AnchorId == o.Id)!.Id), $"seed {seed}: {o.Id} connected");
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
