using System.Collections.Generic;
using System.Text;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.City;
using NUnit.Framework;

namespace HeadLoss.Game.Tests
{
    public class CityGenerationTests
    {
        const int Seeds = 40;

        static CityLayout Make(ulong seed, int index = 1) => CityGenerator.Generate(GameTestUtil.Data, seed, index);

        static string Fingerprint(CityLayout c)
        {
            var sb = new StringBuilder(c.Name);
            foreach (CityTile t in c.Tiles) sb.Append((char)('A' + (int)t % 26)).Append((int)t);
            foreach (Plot p in c.Plots) sb.Append($"|{p.Id},{p.X},{p.Y},{p.Width},{p.Height},{p.Tier},{p.BuildingType},{p.BuildingSeed},{p.StreetSide}");
            foreach (FacilityPlacement f in c.Facilities) sb.Append($"|{f.Id},{f.X},{f.Y}");
            return sb.ToString();
        }

        [Test]
        public void City_SameSeed_SameCity()
        {
            Assert.AreEqual(Fingerprint(Make(1234)), Fingerprint(Make(1234)));
        }

        [Test]
        public void City_DifferentSeed_DifferentCity()
        {
            Assert.AreNotEqual(Fingerprint(Make(1)), Fingerprint(Make(2)));
        }

        [Test]
        public void City_EveryDistrictKind_AtLeastOnce()
        {
            for (ulong s = 1; s <= Seeds; s++)
            {
                CityLayout c = Make(s);
                CityDistrictsDef def = GameTestUtil.Data.City.Districts;
                Assert.That(c.Districts.Count, Is.InRange(def.SeedPointsMin, def.SeedPointsMax), $"seed {s}");
                foreach (DistrictKind k in new[] { DistrictKind.Residential, DistrictKind.Commercial, DistrictKind.Industrial })
                    Assert.IsTrue(c.Districts.Exists(d => d.Kind == k), $"seed {s}: no {k} district");
                Assert.AreEqual(1, c.Districts.FindAll(d => d.Oldest).Count, $"seed {s}");
            }
        }

        [Test]
        public void City_Facilities_OneEachAndASubstationPerDistrict()
        {
            for (ulong s = 1; s <= Seeds; s++)
            {
                CityLayout c = Make(s);
                foreach (string id in new[] { "water-treatment-plant", "gas-receiving-station", "district-heating-plant", "industrial-gas-depot" })
                    Assert.AreEqual(1, c.Facilities.FindAll(f => f.TypeId == id).Count, $"seed {s}: {id}");
                FacilityPlacement depot = c.Facilities.Find(f => f.TypeId == "industrial-gas-depot");
                Assert.AreEqual(DistrictKind.Industrial, c.Districts[depot.District].Kind, $"seed {s}");
                Assert.AreEqual(c.Districts.Count, c.Facilities.FindAll(f => f.TypeId == "electrical-substation").Count, $"seed {s}");
            }
        }

        [Test]
        public void City_Plots_TouchRoadsAndFillTarget()
        {
            for (ulong s = 1; s <= Seeds; s++)
            {
                CityLayout c = Make(s);
                int target = GameTestUtil.Data.City.Plots.Target;
                Assert.That(c.Plots.Count, Is.InRange(target * 8 / 10, target), $"seed {s}");
                foreach (Plot p in c.Plots)
                {
                    bool road = false;
                    for (int x = p.X - 1; x <= p.X + p.Width; x++)
                        for (int y = p.Y - 1; y <= p.Y + p.Height; y++)
                        {
                            bool inside = x >= p.X && x < p.X + p.Width && y >= p.Y && y < p.Y + p.Height;
                            bool corner = (x == p.X - 1 || x == p.X + p.Width) && (y == p.Y - 1 || y == p.Y + p.Height);
                            if (inside)
                            {
                                Assert.IsTrue(c.InCity(x, y), $"seed {s}: {p.Id} in the countryside");
                                Assert.AreEqual(CityTile.Plot, c.At(x, y), $"seed {s}: {p.Id} overlaps {c.At(x, y)}");
                                Assert.AreSame(p, c.PlotAt(x, y));
                            }
                            else if (!corner && c.Has(x, y, CityTile.Road)) road = true;
                        }
                    Assert.IsTrue(road, $"seed {s}: {p.Id} does not touch a road");
                    List<int> size = GameTestUtil.Data.City.Plots.TierSizes[p.Tier - 1];
                    Assert.IsTrue((p.Width == size[0] && p.Height == size[1]) || (p.Width == size[1] && p.Height == size[0]), $"seed {s}: {p.Id} size");
                    Assert.IsNotEmpty(p.BuildingType, $"seed {s}: {p.Id} has no building type");
                    Assert.AreEqual(p.Tier, GameTestUtil.Data.BuildingType(p.BuildingType).Tier);
                }
            }
        }

        [Test]
        public void City_Roads_AllConnectedToMainRoads()
        {
            for (ulong s = 1; s <= Seeds; s++)
            {
                CityLayout c = Make(s);
                var seen = new bool[c.Tiles.Length];
                var queue = new Queue<int>();
                for (int i = 0; i < c.Tiles.Length; i++)
                    if ((c.Tiles[i] & CityTile.MainRoad) != 0) { seen[i] = true; queue.Enqueue(i); }
                Assert.Greater(queue.Count, 0);
                while (queue.Count > 0)
                {
                    int u = queue.Dequeue();
                    int x = u % c.Width, y = u / c.Width;
                    foreach ((int dx, int dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        if (!c.Has(x + dx, y + dy, CityTile.Road)) continue;
                        int v = c.Index(x + dx, y + dy);
                        if (seen[v]) continue;
                        seen[v] = true;
                        queue.Enqueue(v);
                    }
                }
                for (int i = 0; i < c.Tiles.Length; i++)
                    if ((c.Tiles[i] & CityTile.Road) != 0) Assert.IsTrue(seen[i], $"seed {s}: road tile {i % c.Width},{i / c.Width} not connected");
            }
        }

        [Test]
        public void City_StarterArea_ThreeTierOnePlotsNearTheCentre()
        {
            CityStarterDef def = GameTestUtil.Data.City.Starter;
            for (ulong s = 1; s <= Seeds; s++)
            {
                CityLayout c = Make(s);
                int near = 0;
                foreach (Plot p in c.Plots)
                    if (p.Tier == 1 && c.DistanceToCentre(p.CentreX, p.CentreY) <= def.Radius + 1) near++;
                Assert.GreaterOrEqual(near, def.Count, $"seed {s}");
                Assert.AreEqual(1, c.Plots.FindAll(p => p.Starter).Count, $"seed {s}");
                Assert.AreEqual(def.BuildingType, c.Starter!.BuildingType);
            }
        }

        [Test]
        public void City_ParksAndHistoric_InTheirDistricts()
        {
            for (ulong s = 1; s <= Seeds; s++)
            {
                CityLayout c = Make(s);
                int parks = 0, historic = 0;
                for (int y = 0; y < c.Height; y++)
                    for (int x = 0; x < c.Width; x++)
                    {
                        if (c.Has(x, y, CityTile.Park))
                        {
                            parks++;
                            Assert.AreNotEqual(DistrictKind.Industrial, c.Districts[c.DistrictAt(x, y)].Kind, $"seed {s}: park in industrial");
                        }
                        if (c.Has(x, y, CityTile.Historic))
                        {
                            historic++;
                            Assert.IsTrue(c.Districts[c.DistrictAt(x, y)].Oldest, $"seed {s}: historic building outside the oldest district");
                        }
                    }
                Assert.GreaterOrEqual(parks, 2 * 4, $"seed {s}: parks");
                Assert.GreaterOrEqual(historic, 1, $"seed {s}: historic");
            }
        }

        [Test]
        public void City_Rail_ZeroOrOneLineAboutSixtyPercent()
        {
            int with = 0;
            for (ulong s = 1; s <= 100; s++)
            {
                CityLayout c = Make(s);
                int rail = 0;
                foreach (CityTile t in c.Tiles) if ((t & CityTile.Rail) != 0) rail++;
                Assert.IsTrue(rail == 0 || rail == c.Width, $"seed {s}: {rail} rail tiles");
                if (rail > 0) with++;
            }
            Assert.That(with, Is.InRange(45, 75));
        }

        [Test]
        public void City_Modifiers_NoneInTheFirstCity()
        {
            for (ulong s = 1; s <= 20; s++)
            {
                Assert.AreEqual(0, Make(s, 1).Modifiers.Count);
                Assert.LessOrEqual(Make(s, 2).Modifiers.Count, GameTestUtil.Data.City.MaxModifiers);
            }
        }
    }
}
