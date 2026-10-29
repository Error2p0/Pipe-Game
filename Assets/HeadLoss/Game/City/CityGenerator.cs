using System;
using System.Collections.Generic;
using HeadLoss.Core;
using HeadLoss.Core.Data;
using HeadLoss.Core.Data.Defs;

namespace HeadLoss.Game.City
{
    /// <summary>
    /// Generates a city from its seed (<c>03-city.md</c> §City generation): districts, main and local roads, rail,
    /// parks, protected sites, historic buildings, facilities, plots, the starter area and modifiers. Each step
    /// has its own sub-seed <c>hash(citySeed, "step-name")</c> so changing one step does not reshuffle the others.
    /// The starter house's solvability is settled afterwards by <see cref="StarterGuarantee"/>.
    /// </summary>
    public static class CityGenerator
    {
        static readonly (int dx, int dy)[] Dirs = { (0, 1), (0, -1), (1, 0), (-1, 0) };

        /// <param name="cityIndex">1 for the first city (no modifiers), counting up with each new city.</param>
        public static CityLayout Generate(DataRepository data, ulong citySeed, int cityIndex = 1)
        {
            CityDef def = data.City;
            var c = new CityLayout
            {
                Seed = citySeed,
                Width = def.Width,
                Height = def.Height,
                TileMetres = def.TileMetres,
                EdgeBand = def.EdgeBand,
            };
            int n = c.Width * c.Height;
            c.Tiles = new CityTile[n];
            c.DistrictOf = new int[n];
            c.PlotOf = new int[n];
            for (int i = 0; i < n; i++) c.PlotOf[i] = -1;
            for (int y = 0; y < c.Height; y++)
                for (int x = 0; x < c.Width; x++)
                    if (!c.InCity(x, y)) c.Tiles[c.Index(x, y)] = CityTile.Countryside;

            Rng R(string step) => new Rng(Seeds.Derive(citySeed, step));
            c.Name = Name(def.Names, R("name"));
            int industrialEdge = Districts(c, def.Districts, R("districts"));
            MainRoads(c, def.Roads, R("main-roads"));
            LocalRoads(c, def.Roads, R("local-roads"));
            ConnectRoads(c);
            Rail(c, def.Rail, industrialEdge, R("rail"));
            Areas(c, def.Parks, CityTile.Park, R("parks"), nearRoad: false, oldestOnly: false);
            Areas(c, def.ProtectedSites, CityTile.Protected, R("protected-sites"), nearRoad: false, oldestOnly: false);
            Areas(c, def.Historic, CityTile.Historic, R("historic"), nearRoad: true, oldestOnly: true);
            Facilities(c, def.Facilities, R("facilities"));
            Plots(c, data, R("plots"));
            Modifiers(c, def, cityIndex, R("modifiers"));
            return c;
        }

        static string Name(CityNamesDef names, Rng rng)
        {
            if (names.First.Count == 0 || names.Last.Count == 0) return "City";
            return names.First[rng.NextInt(0, names.First.Count)] + names.Last[rng.NextInt(0, names.Last.Count)];
        }

        // ---------- districts ----------

        /// <summary>Voronoi districts. Industrial leans to one edge, residential to the opposite one. Returns that edge (0 S, 1 N, 2 W, 3 E).</summary>
        static int Districts(CityLayout c, CityDistrictsDef def, Rng rng)
        {
            int count = rng.NextInt(def.SeedPointsMin, def.SeedPointsMax + 1);
            int lo = c.EdgeBand + 2, hiX = c.Width - c.EdgeBand - 2, hiY = c.Height - c.EdgeBand - 2;
            double minGap = Math.Sqrt((double)(hiX - lo) * (hiY - lo) / count) * 0.6;
            for (int attempt = 0; c.Districts.Count < count && attempt < 2000; attempt++)
            {
                int x = rng.NextInt(lo, hiX), y = rng.NextInt(lo, hiY);
                bool far = true;
                foreach (District d in c.Districts)
                    if ((d.SeedX - x) * (d.SeedX - x) + (d.SeedY - y) * (d.SeedY - y) < minGap * minGap) far = false;
                if (attempt > 1500) far = true;
                if (far) c.Districts.Add(new District { Index = c.Districts.Count, SeedX = x, SeedY = y });
            }

            int edge = rng.NextInt(0, 4);
            double T(District d) => edge switch
            {
                0 => (double)d.SeedY / c.Height,
                1 => 1 - (double)d.SeedY / c.Height,
                2 => (double)d.SeedX / c.Width,
                _ => 1 - (double)d.SeedX / c.Width,
            };
            double wr = Weight(def, "residential"), wc = Weight(def, "commercial"), wi = Weight(def, "industrial");
            foreach (District d in c.Districts)
            {
                double t = T(d), b = def.EdgeBias;
                double r = wr * ((1 - b) + b * 2 * t), i = wi * ((1 - b) + b * 2 * (1 - t));
                double u = rng.NextDouble() * (r + wc + i);
                d.Kind = u < r ? DistrictKind.Residential : u < r + wc ? DistrictKind.Commercial : DistrictKind.Industrial;
            }

            // At least one of each: convert the best-suited district of a kind that has more than one.
            foreach (DistrictKind kind in new[] { DistrictKind.Industrial, DistrictKind.Residential, DistrictKind.Commercial })
            {
                if (c.Districts.Exists(d => d.Kind == kind)) continue;
                District? best = null;
                double bestScore = double.MaxValue;
                foreach (District d in c.Districts)
                {
                    if (c.Districts.FindAll(o => o.Kind == d.Kind).Count < 2) continue;
                    double score = kind switch
                    {
                        DistrictKind.Industrial => T(d),
                        DistrictKind.Residential => 1 - T(d),
                        _ => c.DistanceToCentre(d.SeedX, d.SeedY),
                    };
                    if (score < bestScore) { bestScore = score; best = d; }
                }
                if (best != null) best.Kind = kind;
            }

            for (int y = 0; y < c.Height; y++)
                for (int x = 0; x < c.Width; x++)
                {
                    int bestD = -1;
                    long bestDist = long.MaxValue;
                    foreach (District d in c.Districts)
                    {
                        long dd = (long)(d.SeedX - x) * (d.SeedX - x) + (long)(d.SeedY - y) * (d.SeedY - y);
                        if (dd < bestDist) { bestDist = dd; bestD = d.Index; }
                    }
                    c.DistrictOf[c.Index(x, y)] = c.InCity(x, y) ? bestD : -1;
                }

            District? oldest = null;
            foreach (District d in c.Districts)
                if (oldest == null || c.DistanceToCentre(d.SeedX, d.SeedY) < c.DistanceToCentre(oldest.SeedX, oldest.SeedY)) oldest = d;
            if (oldest != null) oldest.Oldest = true;
            return edge;
        }

        static double Weight(CityDistrictsDef def, string kind) => def.Weights.TryGetValue(kind, out double w) ? w : 0;

        // ---------- roads ----------

        static void MarkRoad(CityLayout c, int x, int y, bool main)
        {
            if (!c.InMap(x, y)) return;
            c.Tiles[c.Index(x, y)] |= CityTile.Road | (main ? CityTile.MainRoad : CityTile.None);
        }

        /// <summary>2–3 perturbed straight lines crossing the city edge to edge: first horizontal, then vertical, then either.</summary>
        static void MainRoads(CityLayout c, CityRoadsDef def, Rng rng)
        {
            int count = rng.NextInt(def.MainMin, def.MainMax + 1);
            var used = new List<(bool horizontal, int pos)>();
            for (int k = 0; k < count; k++)
            {
                bool horizontal = k == 0 || (k > 1 ? rng.Chance(0.5) : false);
                int size = horizontal ? c.Height : c.Width;
                int pos = 0;
                for (int attempt = 0; attempt < 50; attempt++)
                {
                    pos = rng.NextInt((int)(size * 0.28), (int)(size * 0.72));
                    bool clear = true;
                    foreach (var u in used) if (u.horizontal == horizontal && Math.Abs(u.pos - pos) < size / 5) clear = false;
                    if (clear) break;
                }
                used.Add((horizontal, pos));
                int offset = 0;
                int length = horizontal ? c.Width : c.Height;
                for (int s = 0; s < length; s++)
                {
                    int before = offset;
                    if (rng.Chance(def.MainStepChance))
                        offset = Math.Max(-def.MainWander, Math.Min(def.MainWander, offset + (rng.Chance(0.5) ? 1 : -1)));
                    // A sideways step keeps the road connected: mark both rows at this column.
                    if (horizontal)
                    {
                        MarkRoad(c, s, pos + before, true);
                        MarkRoad(c, s, pos + offset, true);
                    }
                    else
                    {
                        MarkRoad(c, pos + before, s, true);
                        MarkRoad(c, pos + offset, s, true);
                    }
                }
            }
        }

        /// <summary>A jittered grid of local roads per district (spacing 4–6 tiles), inside the city only.</summary>
        static void LocalRoads(CityLayout c, CityRoadsDef def, Rng rng)
        {
            foreach (District d in c.Districts)
            {
                int spacing = rng.NextInt(def.LocalSpacingMin, def.LocalSpacingMax + 1);
                int phaseX = rng.NextInt(0, spacing), phaseY = rng.NextInt(0, spacing);
                for (int line = c.EdgeBand + phaseX; line < c.Width - c.EdgeBand; line += spacing)
                {
                    int x = line + rng.NextInt(-def.LocalJitter, def.LocalJitter + 1);
                    for (int y = c.EdgeBand; y < c.Height - c.EdgeBand; y++)
                        if (c.InCity(x, y) && c.DistrictAt(x, y) == d.Index) MarkRoad(c, x, y, false);
                }
                for (int line = c.EdgeBand + phaseY; line < c.Height - c.EdgeBand; line += spacing)
                {
                    int y = line + rng.NextInt(-def.LocalJitter, def.LocalJitter + 1);
                    for (int x = c.EdgeBand; x < c.Width - c.EdgeBand; x++)
                        if (c.InCity(x, y) && c.DistrictAt(x, y) == d.Index) MarkRoad(c, x, y, false);
                }
            }
        }

        /// <summary>Every road piece joins the main roads: unreached pieces get the shortest connector through open land.</summary>
        static void ConnectRoads(CityLayout c)
        {
            while (true)
            {
                bool[] reached = new bool[c.Tiles.Length];
                var queue = new Queue<int>();
                for (int i = 0; i < c.Tiles.Length; i++)
                    if ((c.Tiles[i] & CityTile.MainRoad) != 0) { reached[i] = true; queue.Enqueue(i); }
                Flood(c, reached, queue, CityTile.Road);
                int orphan = -1;
                for (int i = 0; i < c.Tiles.Length && orphan < 0; i++)
                    if ((c.Tiles[i] & CityTile.Road) != 0 && !reached[i]) orphan = i;
                if (orphan < 0) return;

                // Breadth-first from the orphan piece over any tile until a reached road; mark the way back.
                bool[] piece = new bool[c.Tiles.Length];
                var q = new Queue<int>();
                piece[orphan] = true;
                q.Enqueue(orphan);
                Flood(c, piece, q, CityTile.Road);
                var prev = new int[c.Tiles.Length];
                for (int i = 0; i < prev.Length; i++) prev[i] = -2;
                var bfs = new Queue<int>();
                for (int i = 0; i < piece.Length; i++)
                    if (piece[i]) { prev[i] = -1; bfs.Enqueue(i); }
                int hit = -1;
                while (bfs.Count > 0 && hit < 0)
                {
                    int u = bfs.Dequeue();
                    int ux = u % c.Width, uy = u / c.Width;
                    foreach ((int dx, int dy) in Dirs)
                    {
                        int vx = ux + dx, vy = uy + dy;
                        if (!c.InMap(vx, vy)) continue;
                        int v = c.Index(vx, vy);
                        if (prev[v] != -2) continue;
                        prev[v] = u;
                        if (reached[v]) { hit = v; break; }
                        bfs.Enqueue(v);
                    }
                }
                if (hit < 0) return;
                for (int t = prev[hit]; t >= 0 && !piece[t]; t = prev[t]) MarkRoad(c, t % c.Width, t / c.Width, false);
            }
        }

        /// <summary>Spreads <paramref name="seen"/> over 4-connected tiles carrying <paramref name="flag"/>.</summary>
        static void Flood(CityLayout c, bool[] seen, Queue<int> queue, CityTile flag)
        {
            while (queue.Count > 0)
            {
                int u = queue.Dequeue();
                int ux = u % c.Width, uy = u / c.Width;
                foreach ((int dx, int dy) in Dirs)
                {
                    int vx = ux + dx, vy = uy + dy;
                    if (!c.InMap(vx, vy)) continue;
                    int v = c.Index(vx, vy);
                    if (seen[v] || (c.Tiles[v] & flag) == 0) continue;
                    seen[v] = true;
                    queue.Enqueue(v);
                }
            }
        }

        // ---------- rail ----------

        /// <summary>0–1 rail line edge to edge, parallel to the industrial edge along the line with the most industrial tiles.</summary>
        static void Rail(CityLayout c, CityRailDef def, int industrialEdge, Rng rng)
        {
            if (!rng.Chance(def.Chance)) return;
            bool horizontal = industrialEdge <= 1;
            int size = horizontal ? c.Height : c.Width, length = horizontal ? c.Width : c.Height;
            int best = -1, bestScore = -1;
            for (int pos = c.EdgeBand + 2; pos < size - c.EdgeBand - 2; pos++)
            {
                int industrial = 0, roads = 0;
                for (int s = 0; s < length; s++)
                {
                    int x = horizontal ? s : pos, y = horizontal ? pos : s;
                    int d = c.DistrictAt(x, y);
                    if (d >= 0 && c.Districts[d].Kind == DistrictKind.Industrial) industrial++;
                    if (c.Has(x, y, CityTile.Road)) roads++;
                }
                if (roads > length / 4) continue;
                int score = industrial * 8 + rng.NextInt(0, 8);
                if (score > bestScore) { bestScore = score; best = pos; }
            }
            if (best < 0) return;
            for (int s = 0; s < length; s++)
            {
                int x = horizontal ? s : best, y = horizontal ? best : s;
                c.Tiles[c.Index(x, y)] |= CityTile.Rail;
            }
        }

        // ---------- parks, protected sites, historic buildings ----------

        const CityTile Blocked = CityTile.Countryside | CityTile.Road | CityTile.Rail | CityTile.Park | CityTile.Protected
            | CityTile.Historic | CityTile.Facility | CityTile.Plot;

        static bool Free(CityLayout c, int x, int y) => c.InCity(x, y) && (c.At(x, y) & Blocked) == 0;

        static bool TouchesRoad(CityLayout c, int x0, int y0, int w, int h)
        {
            for (int x = x0; x < x0 + w; x++)
                if (c.Has(x, y0 - 1, CityTile.Road) || c.Has(x, y0 + h, CityTile.Road)) return true;
            for (int y = y0; y < y0 + h; y++)
                if (c.Has(x0 - 1, y, CityTile.Road) || c.Has(x0 + w, y, CityTile.Road)) return true;
            return false;
        }

        static bool RectFree(CityLayout c, int x0, int y0, int w, int h, int district)
        {
            for (int x = x0; x < x0 + w; x++)
                for (int y = y0; y < y0 + h; y++)
                    if (!Free(c, x, y) || (district >= 0 && c.DistrictAt(x, y) != district)) return false;
            return true;
        }

        static void Areas(CityLayout c, CityAreaRuleDef rule, CityTile flag, Rng rng, bool nearRoad, bool oldestOnly)
        {
            int count = rng.NextInt(rule.Min, rule.Max + 1);
            int placed = 0;
            for (int attempt = 0; placed < count && attempt < 4000; attempt++)
            {
                int w = rng.NextInt(rule.SizeMin, rule.SizeMax + 1), h = rng.NextInt(rule.SizeMin, rule.SizeMax + 1);
                int x0 = rng.NextInt(c.EdgeBand, c.Width - c.EdgeBand - w + 1), y0 = rng.NextInt(c.EdgeBand, c.Height - c.EdgeBand - h + 1);
                int d = c.DistrictAt(x0, y0);
                if (d < 0) continue;
                if (oldestOnly && !c.Districts[d].Oldest) continue;
                if (rule.Districts.Count > 0 && !rule.Districts.Contains(District.KindId(c.Districts[d].Kind))) continue;
                if (!RectFree(c, x0, y0, w, h, d)) continue;
                if (nearRoad && !TouchesRoad(c, x0, y0, w, h)) continue;
                for (int x = x0; x < x0 + w; x++)
                    for (int y = y0; y < y0 + h; y++)
                        c.Tiles[c.Index(x, y)] |= flag;
                placed++;
            }
        }

        // ---------- facilities ----------

        /// <summary>
        /// One of each city-wide facility (in its district type, if any) and a substation per district, each on the
        /// city-edge side of its district, touching a road.
        /// </summary>
        static void Facilities(CityLayout c, List<FacilityTypeDef> types, Rng rng)
        {
            foreach (FacilityTypeDef t in types)
            {
                var districts = new List<District>();
                foreach (District d in c.Districts)
                    if (t.District.Length == 0 || District.KindId(d.Kind) == t.District) districts.Add(d);
                if (t.Count == "per-district")
                {
                    foreach (District d in c.Districts) PlaceFacility(c, t, d, rng, c.Facilities.FindAll(f => f.TypeId == t.Id).Count + 1);
                    continue;
                }
                // Shuffle, then try districts until one fits.
                for (int i = districts.Count - 1; i > 0; i--)
                {
                    int j = rng.NextInt(0, i + 1);
                    (districts[i], districts[j]) = (districts[j], districts[i]);
                }
                foreach (District d in districts)
                    if (PlaceFacility(c, t, d, rng, 1)) break;
            }
        }

        static bool PlaceFacility(CityLayout c, FacilityTypeDef t, District d, Rng rng, int number)
        {
            var candidates = new List<(double score, int x, int y, int w, int h)>();
            for (int rot = 0; rot < (t.Width == t.Height ? 1 : 2); rot++)
            {
                int w = rot == 0 ? t.Width : t.Height, h = rot == 0 ? t.Height : t.Width;
                for (int y = c.EdgeBand; y <= c.Height - c.EdgeBand - h; y++)
                    for (int x = c.EdgeBand; x <= c.Width - c.EdgeBand - w; x++)
                    {
                        if (!RectFree(c, x, y, w, h, d.Index) || !TouchesRoad(c, x, y, w, h)) continue;
                        candidates.Add((-c.DistanceToCentre(x + w * 0.5, y + h * 0.5), x, y, w, h));
                    }
            }
            if (candidates.Count == 0) return false;
            candidates.Sort((a, b) => a.score != b.score ? a.score.CompareTo(b.score) : a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
            var pick = candidates[rng.NextInt(0, Math.Min(5, candidates.Count))];
            for (int x = pick.x; x < pick.x + pick.w; x++)
                for (int y = pick.y; y < pick.y + pick.h; y++)
                    c.Tiles[c.Index(x, y)] |= CityTile.Facility;
            c.Facilities.Add(new FacilityPlacement
            {
                Id = t.Count == "per-district" ? $"{t.Id}-{number}" : t.Id,
                TypeId = t.Id,
                Utility = t.Utility,
                X = pick.x,
                Y = pick.y,
                Width = pick.w,
                Height = pick.h,
                District = d.Index,
            });
            return true;
        }

        // ---------- plots ----------

        /// <summary>
        /// Plots fill the blocks from the centre outward until the target count: each candidate is a free tile beside
        /// a road; its tier comes from the district's mix and its size from the tier. The starter area is placed
        /// first: <c>starter.count</c> tier-1 plots nearest the centre (the nearest is the starter house).
        /// </summary>
        static void Plots(CityLayout c, DataRepository data, Rng rng)
        {
            CityPlotsDef def = data.City.Plots;
            var frontage = new List<(double key, int x, int y)>();
            for (int y = c.EdgeBand; y < c.Height - c.EdgeBand; y++)
                for (int x = c.EdgeBand; x < c.Width - c.EdgeBand; x++)
                    if (Free(c, x, y) && TouchesRoad(c, x, y, 1, 1))
                        frontage.Add((c.DistanceToCentre(x + 0.5, y + 0.5), x, y));
            frontage.Sort((a, b) => a.key != b.key ? a.key.CompareTo(b.key) : a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));

            // Starter area: the nearest frontage tiles, one plot each, spaced a tile apart where possible.
            CityStarterDef starter = data.City.Starter;
            foreach (var f in frontage)
            {
                if (c.Plots.Count >= starter.Count || f.key > starter.Radius) break;
                if (!Free(c, f.x, f.y) || NextToPlot(c, f.x, f.y)) continue;
                bool first = c.Plots.Count == 0;
                AddPlot(c, data, f.x, f.y, 1, 1, 1, first ? starter.BuildingType : RollType(data, 1, c.Districts[c.DistrictAt(f.x, f.y)].Kind, rng), first);
            }
            foreach (var f in frontage)
            {
                if (c.Plots.Count >= starter.Count || f.key > starter.Radius) break;
                if (!Free(c, f.x, f.y)) continue;
                AddPlot(c, data, f.x, f.y, 1, 1, 1, RollType(data, 1, c.Districts[c.DistrictAt(f.x, f.y)].Kind, rng), false);
            }

            // The rest, nearest first with some spread.
            var order = new List<(double key, int x, int y)>();
            foreach (var f in frontage) order.Add((f.key * (1 + def.OrderJitter * rng.NextDouble()), f.x, f.y));
            order.Sort((a, b) => a.key != b.key ? a.key.CompareTo(b.key) : a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
            foreach (var f in order)
            {
                if (c.Plots.Count >= def.Target) break;
                if (!Free(c, f.x, f.y)) continue;
                if (rng.Chance(def.SkipChance)) continue;
                District d = c.Districts[c.DistrictAt(f.x, f.y)];
                int tier = RollTier(def, d.Kind, rng);
                if (tier == 0) continue;
                List<int> size = def.TierSizes[tier - 1];
                bool rotate = rng.Chance(0.5);
                int w = rotate ? size[1] : size[0], h = rotate ? size[0] : size[1];
                if (!FitRect(c, f.x, f.y, w, h, d.Index, rng, out int x0, out int y0)) continue;
                AddPlot(c, data, x0, y0, w, h, tier, RollType(data, tier, d.Kind, rng), false);
            }
        }

        static bool NextToPlot(CityLayout c, int x, int y)
        {
            foreach ((int dx, int dy) in Dirs) if (c.Has(x + dx, y + dy, CityTile.Plot)) return true;
            return false;
        }

        /// <summary>A free w×h rectangle covering (x, y), inside one district.</summary>
        static bool FitRect(CityLayout c, int x, int y, int w, int h, int district, Rng rng, out int x0, out int y0)
        {
            int start = rng.NextInt(0, w * h);
            for (int k = 0; k < w * h; k++)
            {
                int o = (start + k) % (w * h);
                x0 = x - o % w;
                y0 = y - o / w;
                if (RectFree(c, x0, y0, w, h, district)) return true;
            }
            x0 = y0 = 0;
            return false;
        }

        static int RollTier(CityPlotsDef def, DistrictKind kind, Rng rng)
        {
            if (!def.TierMix.TryGetValue(District.KindId(kind), out List<double>? mix)) return 0;
            double total = 0;
            foreach (double w in mix) total += w;
            if (total <= 0) return 0;
            double u = rng.NextDouble() * total;
            for (int i = 0; i < mix.Count; i++)
            {
                if (u < mix[i]) return i + 1;
                u -= mix[i];
            }
            return mix.Count;
        }

        /// <summary>A building type of the tier, weighted by the district; falls back to any rollable type of the tier.</summary>
        public static string RollType(DataRepository data, int tier, DistrictKind kind, Rng rng)
        {
            string k = District.KindId(kind);
            var types = new List<(BuildingTypeDef t, double w)>();
            double total = 0;
            foreach (BuildingTypeDef t in data.Buildings.Types)
            {
                if (t.Tier != tier || !t.Districts.TryGetValue(k, out double w) || w <= 0) continue;
                types.Add((t, w));
                total += w;
            }
            if (types.Count == 0)
                foreach (BuildingTypeDef t in data.Buildings.Types)
                {
                    if (t.Tier != tier || t.Districts.Count == 0) continue;
                    double w = 0;
                    foreach (double v in t.Districts.Values) w = Math.Max(w, v);
                    types.Add((t, w));
                    total += w;
                }
            if (types.Count == 0) return "";
            double u = rng.NextDouble() * total;
            foreach ((BuildingTypeDef t, double w) in types)
            {
                if (u < w) return t.Id;
                u -= w;
            }
            return types[types.Count - 1].t.Id;
        }

        static void AddPlot(CityLayout c, DataRepository data, int x0, int y0, int w, int h, int tier, string type, bool starter)
        {
            var p = new Plot
            {
                Id = $"plot-{c.Plots.Count + 1}",
                X = x0,
                Y = y0,
                Width = w,
                Height = h,
                Tier = tier,
                BuildingType = type,
                District = c.DistrictAt(x0, y0),
                Starter = starter,
            };
            p.BuildingSeed = Seeds.Derive(c.Seed, p.Id);
            p.StreetSide = StreetSide(c, x0, y0, w, h);
            int index = c.Plots.Count;
            c.Plots.Add(p);
            for (int x = x0; x < x0 + w; x++)
                for (int y = y0; y < y0 + h; y++)
                {
                    c.Tiles[c.Index(x, y)] |= CityTile.Plot;
                    c.PlotOf[c.Index(x, y)] = index;
                }
        }

        /// <summary>The plot side with the most road tiles along it (ties: south, north, west, east).</summary>
        static string StreetSide(CityLayout c, int x0, int y0, int w, int h)
        {
            int s = 0, n = 0, west = 0, e = 0;
            for (int x = x0; x < x0 + w; x++)
            {
                if (c.Has(x, y0 - 1, CityTile.Road)) s++;
                if (c.Has(x, y0 + h, CityTile.Road)) n++;
            }
            for (int y = y0; y < y0 + h; y++)
            {
                if (c.Has(x0 - 1, y, CityTile.Road)) west++;
                if (c.Has(x0 + w, y, CityTile.Road)) e++;
            }
            int best = Math.Max(Math.Max(s, n), Math.Max(west, e));
            return best == s ? "south" : best == n ? "north" : best == west ? "west" : "east";
        }

        // ---------- modifiers ----------

        static void Modifiers(CityLayout c, CityDef def, int cityIndex, Rng rng)
        {
            if (cityIndex <= 1 || def.Modifiers.Count == 0) return;
            int count = rng.NextInt(0, Math.Min(def.MaxModifiers, def.Modifiers.Count) + 1);
            var pool = new List<CityModifierDef>(def.Modifiers);
            for (int i = 0; i < count; i++)
            {
                int j = rng.NextInt(0, pool.Count);
                c.Modifiers.Add(pool[j].Id);
                pool.RemoveAt(j);
            }
        }
    }
}
