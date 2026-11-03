using System;
using System.Collections.Generic;
using HeadLoss.Core;
using HeadLoss.Core.Data;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Buildings;

namespace HeadLoss.Game.Generation
{
    /// <summary>What the generator needs from a plot (<c>03-city.md</c>: plots store type, tier and seed).</summary>
    public readonly struct BuildingRequest
    {
        public readonly string Id;
        public readonly string BuildingType;
        public readonly ulong Seed;
        /// <summary>Plot side facing the road: north, south, east or west.</summary>
        public readonly string StreetSide;

        public BuildingRequest(string id, string buildingType, ulong seed, string streetSide)
        {
            Id = id;
            BuildingType = buildingType;
            Seed = seed;
            StreetSide = streetSide;
        }
    }

    /// <summary>
    /// Generates a single-floor building layout from its seed (<c>04-buildings.md</c> §Procedural generation, steps
    /// 1 and 3–9): footprint, rooms by BSP from the type's room program, one door per room into a hall or a
    /// neighbour (a tree, so every room is reachable), windows on exterior walls, outlets by room type with seeded
    /// requirements, the air intake on the facade (street-side bias), occupants, machine loads and the codes in
    /// force. Multi-floor buildings, shafts, columns and fire walls arrive with tier 3 (Phase 4).
    /// </summary>
    public static class BuildingGenerator
    {
        /// <summary>Internal retries when a room split or intake cannot be made.</summary>
        const int MaxInternalAttempts = 40;
        public const string AirIntakeId = "air-intake";

        /// <summary>
        /// Generates the layout. <paramref name="attempt"/> is the solvability nudge: attempts below
        /// <c>generation.openingNudges</c> keep the rooms and move the intake, later ones regenerate the rooms.
        /// Returns null when the type cannot be laid out (no program).
        /// </summary>
        public static BuildingLayoutDef? Generate(DataRepository data, BuildingRequest req, int attempt = 0)
        {
            BuildingTypeDef type = data.BuildingType(req.BuildingType);
            if (type.Program.Count == 0) return null;
            if (type.Tier >= 2) return FloorPlanGenerator.Generate(data, req, attempt);
            BuildingGenerationDef gen = data.Buildings.Generation;
            int roomVariant = attempt < gen.OpeningNudges ? 0 : attempt - gen.OpeningNudges + 1;
            for (int k = 0; k < MaxInternalAttempts; k++)
            {
                ulong roomSeed = Seeds.Derive(Seeds.Derive(req.Seed, "rooms"), (ulong)(roomVariant * MaxInternalAttempts + k));
                var rooms = new Rng(roomSeed);
                var outletCounts = new List<int>();
                BuildingLayoutDef? b = Rooms(data, type, req, rooms, outletCounts);
                if (b == null) continue;
                var intakeRng = new Rng(Seeds.Derive(Seeds.Derive(roomSeed, "intake"), (ulong)attempt));
                if (!Finish(data, type, b, rooms, intakeRng, outletCounts)) continue;
                // Phase 4 additions draw from their own streams, so tier 1 rooms, outlets and intakes stay as they were.
                if (FloorPlanGenerator.PlaceOpening(data, type, b, new Rng(Seeds.Derive(roomSeed, "power")), "power", FloorPlanGenerator.PowerId,
                        new List<string> { "panel-basic" }))
                    b.Equipment.Add(new EquipmentPlacementDef { EquipmentId = "panel-basic", OpeningId = FloorPlanGenerator.PowerId, On = true });
                AddFurniture(data, b, new Rng(Seeds.Derive(req.Seed, "furniture")));
                return b;
            }
            return null;
        }

        // ---------- footprint and rooms ----------

        /// <summary>A room to place by the BSP: its type, area weight and outlet override.</summary>
        internal sealed class SplitSlot
        {
            public readonly RoomTypeDef Type;
            public readonly double Weight;
            public readonly int Outlets;

            public SplitSlot(RoomTypeDef type, double weight, int outlets)
            {
                Type = type;
                Weight = weight;
                Outlets = outlets;
            }
        }

        /// <param name="outletCounts">Filled with each room's outlet override (0 = the room type's rules), parallel to the rooms.</param>
        static BuildingLayoutDef? Rooms(DataRepository data, BuildingTypeDef type, BuildingRequest req, Rng rng, List<int> outletCounts)
        {
            BuildingTierDef tier = data.BuildingTier(type.Tier);
            List<int> min = type.FootprintMin.Count == 2 ? type.FootprintMin : tier.FootprintMin;
            List<int> max = type.FootprintMax.Count == 2 ? type.FootprintMax : tier.FootprintMax;
            int w = rng.NextInt(min[0], max[0] + 1), h = rng.NextInt(min[1], max[1] + 1);
            if (rng.Chance(0.5)) (w, h) = (h, w);

            var slots = new List<SplitSlot>();
            foreach (ProgramRoomDef p in type.Program)
            {
                int n = rng.NextInt(p.CountMin, p.CountMax + 1);
                for (int i = 0; i < n; i++) slots.Add(new SplitSlot(data.RoomType(p.Room), p.AreaWeight, p.Outlets));
            }
            for (int i = slots.Count - 1; i > 0; i--)
            {
                int j = rng.NextInt(0, i + 1);
                (slots[i], slots[j]) = (slots[j], slots[i]);
            }
            var b = new BuildingLayoutDef
            {
                Id = req.Id,
                DisplayName = type.DisplayName,
                Tier = type.Tier,
                BuildingType = type.Id,
                StreetSide = req.StreetSide,
                Width = w,
                Height = h,
                ReferenceCrankSpeed = type.ReferenceCrankSpeed,
            };
            if (!Split(0, 0, w, h, slots, b.Rooms, outletCounts, data.Buildings.Generation.MinRoomSide)) return null;
            var counts = new Dictionary<string, int>();
            foreach (RoomDef r in b.Rooms)
            {
                counts[r.Type] = counts.TryGetValue(r.Type, out int c) ? c + 1 : 1;
                r.Id = $"{r.Type}-{counts[r.Type]}";
            }
            return b;
        }

        /// <summary>
        /// Binary space partition: the rooms are divided into two groups whose area weights are closest to half
        /// each, the rectangle is cut across its longer side in that ratio, and both halves recurse.
        /// </summary>
        internal static bool Split(int x, int y, int w, int h, List<SplitSlot> rooms, List<RoomDef> output, List<int> outlets, int minSide)
        {
            if (w < minSide || h < minSide) return false;
            if (rooms.Count == 1)
            {
                bool hub = rooms[0].Type.Hub;
                if (!hub && Math.Max(w, h) > 3 * Math.Min(w, h)) return false;
                output.Add(new RoomDef { Type = rooms[0].Type.Id, X = x, Y = y, Width = w, Height = h });
                outlets.Add(rooms[0].Outlets);
                return true;
            }
            if (w * h < rooms.Count * minSide * minSide) return false;
            double total = 0;
            foreach (SplitSlot s in rooms) total += s.Weight;
            var ks = new List<(double score, int k)>();
            double prefix = 0;
            for (int k = 1; k < rooms.Count; k++)
            {
                prefix += rooms[k - 1].Weight;
                ks.Add((Math.Abs(prefix - total / 2), k));
            }
            ks.Sort((a, b) => a.score != b.score ? a.score.CompareTo(b.score) : a.k.CompareTo(b.k));
            bool[] axes = w >= h ? new[] { true, false } : new[] { false, true };
            foreach (bool alongX in axes)
                foreach ((double _, int k) in ks)
                {
                    double share = 0;
                    for (int i = 0; i < k; i++) share += rooms[i].Weight;
                    int len = alongX ? w : h;
                    int cut = (int)Math.Round(len * share / total);
                    cut = Math.Max(minSide, Math.Min(len - minSide, cut));
                    if (cut < minSide || len - cut < minSide) continue;
                    int mark = output.Count;
                    List<SplitSlot> a = rooms.GetRange(0, k), b = rooms.GetRange(k, rooms.Count - k);
                    bool ok = alongX
                        ? Split(x, y, cut, h, a, output, outlets, minSide) && Split(x + cut, y, w - cut, h, b, output, outlets, minSide)
                        : Split(x, y, w, cut, a, output, outlets, minSide) && Split(x, y + cut, w, h - cut, b, output, outlets, minSide);
                    if (ok) return true;
                    output.RemoveRange(mark, output.Count - mark);
                    outlets.RemoveRange(mark, outlets.Count - mark);
                }
            return false;
        }

        // ---------- doors, outlets, intake, windows, loads ----------

        static readonly (string side, int dx, int dy)[] Sides = { ("north", 0, 1), ("south", 0, -1), ("east", 1, 0), ("west", -1, 0) };

        static int RoomAt(BuildingLayoutDef b, int x, int y)
        {
            for (int i = 0; i < b.Rooms.Count; i++)
            {
                RoomDef r = b.Rooms[i];
                if (x >= r.X && y >= r.Y && x < r.X + r.Width && y < r.Y + r.Height) return i;
            }
            return -1;
        }

        static bool Finish(DataRepository data, BuildingTypeDef type, BuildingLayoutDef b, Rng rng, Rng intakeRng, List<int> outletCounts)
        {
            Doors(data, b, rng);
            Outlets(data, b, rng, outletCounts);
            if (b.Outlets.Count == 0) return false;
            if (!Intake(data, type, b, intakeRng)) return false;
            Windows(data, b);
            foreach (RoomDef r in b.Rooms)
            {
                RoomTypeDef t = data.RoomType(r.Type);
                r.Occupants = t.Occupants + t.OccupantsPerM2 * r.Width * r.Height;
                r.MachinesW = t.MachinesW;
            }
            foreach (CodeDef c in data.Codes)
                if (c.Tiers.Contains(type.Tier) || type.Codes.Contains(c.Id)) b.Codes.Add(c.Id);
            foreach (string e in type.InitialEquipment)
                b.Equipment.Add(new EquipmentPlacementDef { EquipmentId = e, OpeningId = AirIntakeId, On = true });
            return true;
        }

        /// <summary>One door per room, as a tree grown from the main hub: rooms open into a hall-like room when they can.</summary>
        static void Doors(DataRepository data, BuildingLayoutDef b, Rng rng)
        {
            int n = b.Rooms.Count;
            var edges = new Dictionary<(int, int), List<(int x, int y, string side)>>();
            for (int x = 0; x < b.Width; x++)
                for (int y = 0; y < b.Height; y++)
                {
                    int a = RoomAt(b, x, y);
                    foreach ((string side, int dx, int dy) in new[] { ("east", 1, 0), ("north", 0, 1) })
                    {
                        int o = RoomAt(b, x + dx, y + dy);
                        if (o < 0 || o == a) continue;
                        var key = a < o ? (a, o) : (o, a);
                        if (!edges.TryGetValue(key, out var list)) edges[key] = list = new List<(int, int, string)>();
                        list.Add((x, y, side));
                    }
                }
            bool Hub(int i) => data.RoomType(b.Rooms[i].Type).Hub;
            int root = 0;
            for (int i = 0; i < n; i++)
            {
                int ai = b.Rooms[i].Width * b.Rooms[i].Height, ar = b.Rooms[root].Width * b.Rooms[root].Height;
                if ((Hub(i) && !Hub(root)) || (Hub(i) == Hub(root) && ai > ar)) root = i;
            }
            var connected = new HashSet<int> { root };
            var keys = new List<(int, int)>(edges.Keys);
            keys.Sort();
            while (connected.Count < n)
            {
                var best = new List<(int, int)>();
                int bestScore = int.MaxValue;
                foreach ((int a, int o) in keys)
                {
                    if (connected.Contains(a) == connected.Contains(o)) continue;
                    int inside = connected.Contains(a) ? a : o;
                    int score = Hub(inside) ? 0 : 1;
                    if (score < bestScore) { bestScore = score; best.Clear(); }
                    if (score == bestScore) best.Add((a, o));
                }
                if (best.Count == 0) break;
                var pick = best[rng.NextInt(0, best.Count)];
                var spots = edges[pick];
                var spot = spots[rng.NextInt(0, spots.Count)];
                b.Doors.Add(new DoorDef { X = spot.x, Y = spot.y, Side = spot.side });
                connected.Add(pick.Item1);
                connected.Add(pick.Item2);
            }
        }

        /// <summary>
        /// Outlets by room type: max(min, ⌈area / areaPerOutlet⌉) per rule, spread along the room's long axis, with
        /// requirements from <c>outlets.json</c> varied ±10% and rounded to readable steps.
        /// </summary>
        static void Outlets(DataRepository data, BuildingLayoutDef b, Rng rng, List<int> outletCounts)
        {
            BuildingGenerationDef gen = data.Buildings.Generation;
            for (int ri = 0; ri < b.Rooms.Count; ri++)
            {
                RoomDef r = b.Rooms[ri];
                RoomTypeDef t = data.RoomType(r.Type);
                foreach (RoomOutletRuleDef rule in t.Outlets)
                {
                    OutletDef outlet = data.Outlet(rule.Type);
                    if (rule.FromTier > b.Tier || !gen.PhaseFluids.Contains(outlet.Fluid)) continue;
                    if (!outlet.Tiers.TryGetValue(b.Tier.ToString(), out OutletRequirementDef? req)) continue;
                    int area = r.Width * r.Height;
                    int count = rule.AreaPerOutlet > 0 ? Math.Max(rule.Min, (int)Math.Ceiling(area / rule.AreaPerOutlet)) : rule.Min;
                    if (outletCounts[ri] > 0) count = outletCounts[ri];
                    count = Math.Min(count, Math.Max(1, area / 4));
                    bool alongX = r.Width >= r.Height;
                    int len = alongX ? r.Width : r.Height;
                    for (int i = 0; i < count; i++)
                    {
                        int along = (int)Math.Floor((i + 0.5) * len / count);
                        int across = (alongX ? r.Height : r.Width) / 2;
                        int x = r.X + (alongX ? along : across), y = r.Y + (alongX ? across : along);
                        if (!FreeTileNear(b, r, ref x, ref y)) continue;
                        string facing = count == 1
                            ? (alongX ? (rng.Chance(0.5) ? "east" : "west") : (rng.Chance(0.5) ? "north" : "south"))
                            : alongX ? (along < len / 2.0 ? "east" : "west") : (along < len / 2.0 ? "north" : "south");
                        double f = 1 + gen.RequirementVariation * (2 * rng.NextDouble() - 1);
                        double step = gen.RoundingSteps.TryGetValue(outlet.Unit, out double s) ? s : 1;
                        double min = Round(req.Min * f, step), ideal = Math.Max(min + step, Round(req.Ideal * f, step));
                        double max = req.Max > 0 ? Math.Max(ideal + step, Round(req.Max * f, step)) : 0;
                        b.Outlets.Add(new OutletPlacementDef
                        {
                            Id = $"vent-{b.Outlets.Count + 1}",
                            Type = rule.Type,
                            X = x,
                            Y = y,
                            Min = min,
                            Ideal = ideal,
                            Max = max,
                            Facing = facing,
                        });
                    }
                }
            }
        }

        /// <summary>True when the tile is on either side of a door (a duct must pass through it).</summary>
        static bool IsDoorTile(BuildingLayoutDef b, int x, int y)
        {
            foreach (DoorDef d in b.Doors)
            {
                if (d.X == x && d.Y == y) return true;
                int nx = d.X + (d.Side == "east" ? 1 : d.Side == "west" ? -1 : 0), ny = d.Y + (d.Side == "north" ? 1 : d.Side == "south" ? -1 : 0);
                if (nx == x && ny == y) return true;
            }
            return false;
        }

        /// <summary>Moves (x, y) to the nearest tile of the room that is neither a door tile nor taken by another outlet.</summary>
        static bool FreeTileNear(BuildingLayoutDef b, RoomDef r, ref int x, ref int y)
        {
            int bx = x, by = y, best = int.MaxValue;
            for (int tx = r.X; tx < r.X + r.Width; tx++)
                for (int ty = r.Y; ty < r.Y + r.Height; ty++)
                {
                    if (IsDoorTile(b, tx, ty) || b.Outlets.Exists(o => o.X == tx && o.Y == ty)) continue;
                    int d = Math.Abs(tx - bx) + Math.Abs(ty - by);
                    if (d < best) { best = d; x = tx; y = ty; }
                }
            return best != int.MaxValue;
        }

        static double Round(double v, double step) => Math.Round(Math.Round(v / step, MidpointRounding.AwayFromZero) * step, 6);

        /// <summary>The air intake: a facade tile of a room (not protected, not an outlet), on the street side with <c>streetSideChance</c>.</summary>
        static bool Intake(DataRepository data, BuildingTypeDef type, BuildingLayoutDef b, Rng rng)
        {
            var all = new List<(int x, int y, string side)>();
            for (int x = 0; x < b.Width; x++)
                for (int y = 0; y < b.Height; y++)
                {
                    int r = RoomAt(b, x, y);
                    if (r < 0 || data.RoomType(b.Rooms[r].Type).Protected) continue;
                    if (b.Outlets.Exists(o => o.X == x && o.Y == y) || IsDoorTile(b, x, y)) continue;
                    foreach ((string side, int dx, int dy) in Sides)
                    {
                        if (RoomAt(b, x + dx, y + dy) >= 0 || x + dx >= 0 && y + dy >= 0 && x + dx < b.Width && y + dy < b.Height) continue;
                        if (!LeadInFits(data, b, r, x, y, -dx, -dy)) continue;
                        if (type.MaxIntakeDistance > 0)
                        {
                            int far = 0;
                            foreach (OutletPlacementDef o in b.Outlets) far = Math.Max(far, Math.Abs(o.X - x) + Math.Abs(o.Y - y));
                            if (far > type.MaxIntakeDistance) continue;
                        }
                        all.Add((x, y, side));
                    }
                }
            if (all.Count == 0) return false;
            List<(int x, int y, string side)> pool = all;
            if (rng.Chance(data.Buildings.Generation.StreetSideChance))
            {
                List<(int x, int y, string side)> street = all.FindAll(c => c.side == b.StreetSide);
                if (street.Count > 0) pool = street;
            }
            var pick = pool[rng.NextInt(0, pool.Count)];
            b.EntryOpenings.Add(new EntryOpeningDef { Id = AirIntakeId, Utility = "air", X = pick.x, Y = pick.y, Facing = pick.side });
            return true;
        }

        /// <summary>The auto-router's straight lead-in from the intake: tiles inward stay in the same room and hold no outlet.</summary>
        static bool LeadInFits(DataRepository data, BuildingLayoutDef b, int room, int x, int y, int ix, int iy)
        {
            for (int k = 1; k <= data.Balancing.AutoRouter.LeadInTiles; k++)
            {
                int tx = x + ix * k, ty = y + iy * k;
                if (RoomAt(b, tx, ty) != room || IsDoorTile(b, tx, ty) || b.Outlets.Exists(o => o.X == tx && o.Y == ty)) return false;
            }
            return true;
        }

        /// <summary>Furniture per room type: kind and footprint (width, height) of each piece.</summary>
        static (string kind, int w, int h)[] FurnitureFor(RoomDef r)
        {
            int area = r.Width * r.Height;
            int many(int per) => Math.Max(1, area / per);
            var list = new List<(string, int, int)>();
            switch (r.Type)
            {
                case "bedroom": list.Add(("bed", 2, 2)); break;
                case "living": list.Add(("sofa", 2, 1)); break;
                case "kitchen": list.Add(("counter", 1, 2)); break;
                case "dining": for (int i = 0; i < many(10); i++) list.Add(("table", 1, 1)); break;
                case "office": for (int i = 0; i < many(10); i++) list.Add(("desk", 1, 1)); break;
                case "shop-floor": for (int i = 0; i < many(14); i++) list.Add(("shelf", 1, 2)); break;
                case "storage": list.Add(("shelf", 1, 1)); list.Add(("shelf", 1, 1)); break;
                case "workshop": list.Add(("bench", 2, 1)); break;
            }
            return list.ToArray();
        }

        /// <summary>
        /// Furniture (<c>04-buildings.md</c> §Layers): decoration against the walls that blocks equipment, not pipes. Never on
        /// door tiles, outlets, columns or within two tiles of an entry opening (so the reference equipment always fits).
        /// </summary>
        public static void AddFurniture(DataRepository data, BuildingLayoutDef b, Rng rng)
        {
            double chance = data.Buildings.Generation.FurnitureChance;
            var taken = new HashSet<(int, int, int)>();
            foreach (DoorDef d in b.Doors)
            {
                taken.Add((d.X, d.Y, d.Floor));
                int nx = d.X + (d.Side == "east" ? 1 : d.Side == "west" ? -1 : 0), ny = d.Y + (d.Side == "north" ? 1 : d.Side == "south" ? -1 : 0);
                taken.Add((nx, ny, d.Floor));
            }
            foreach (OutletPlacementDef o in b.Outlets) taken.Add((o.X, o.Y, o.Floor));
            foreach (EntryOpeningDef o in b.EntryOpenings)
                for (int dx = -2; dx <= 2; dx++)
                    for (int dy = -2; dy <= 2; dy++) taken.Add((o.X + dx, o.Y + dy, o.Floor));
            EntryOpeningDef? intake = b.EntryOpenings.Find(o => o.Utility == "air");
            if (intake != null)
            {
                (int ix, int iy) = EquipmentFits.Inward(intake.Facing);
                for (int k = 0; k <= data.Balancing.AutoRouter.LeadInTiles + 1; k++) taken.Add((intake.X + ix * k, intake.Y + iy * k, intake.Floor));
            }
            foreach (RoomDef r in b.Rooms)
            {
                var items = FurnitureFor(r);
                if (items.Length == 0 || !rng.Chance(chance)) continue;
                foreach ((string kind, int fw, int fh) in items)
                {
                    for (int attempt = 0; attempt < 12; attempt++)
                    {
                        bool turn = rng.Chance(0.5);
                        int w = turn ? fh : fw, h = turn ? fw : fh;
                        if (w > r.Width || h > r.Height) continue;
                        int side = rng.NextInt(0, 4);
                        int x = side switch { 0 => r.X, 1 => r.X + r.Width - w, _ => r.X + rng.NextInt(0, r.Width - w + 1) };
                        int y = side switch { 2 => r.Y, 3 => r.Y + r.Height - h, _ => r.Y + rng.NextInt(0, r.Height - h + 1) };
                        bool ok = true;
                        for (int tx = x; tx < x + w && ok; tx++)
                            for (int ty = y; ty < y + h && ok; ty++)
                                if (taken.Contains((tx, ty, r.Floor)) || b.Columns.Exists(c => c.X == tx && c.Y == ty)) ok = false;
                        if (!ok) continue;
                        for (int tx = x; tx < x + w; tx++)
                            for (int ty = y; ty < y + h; ty++) taken.Add((tx, ty, r.Floor));
                        b.Furniture.Add(new FurnitureDef { X = x, Y = y, Width = w, Height = h, Floor = r.Floor, Kind = kind });
                        break;
                    }
                }
            }
        }

        /// <summary>Windows along each room's exterior walls, one every <c>windowSpacing</c> tiles (never at the intake).</summary>
        static void Windows(DataRepository data, BuildingLayoutDef b)
        {
            int spacing = data.Buildings.Generation.WindowSpacing;
            EntryOpeningDef intake = b.EntryOpenings[0];
            foreach (RoomDef r in b.Rooms)
                foreach ((string side, int dx, int dy) in Sides)
                {
                    var run = new List<(int x, int y)>();
                    for (int i = 0; i < (dx != 0 ? r.Height : r.Width); i++)
                    {
                        int x = dx != 0 ? (dx > 0 ? r.X + r.Width - 1 : r.X) : r.X + i;
                        int y = dx != 0 ? r.Y + i : (dy > 0 ? r.Y + r.Height - 1 : r.Y);
                        if (x + dx >= 0 && y + dy >= 0 && x + dx < b.Width && y + dy < b.Height) continue;
                        run.Add((x, y));
                    }
                    for (int i = spacing / 2; i < run.Count; i += spacing)
                    {
                        (int x, int y) = run[i];
                        if (x == intake.X && y == intake.Y && side == intake.Facing) continue;
                        b.Windows.Add(new WindowDef { X = x, Y = y, Side = side });
                    }
                }
        }
    }
}
