using System;
using System.Collections.Generic;
using HeadLoss.Core;
using HeadLoss.Core.Data;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Buildings;

namespace HeadLoss.Game.Generation
{
    /// <summary>
    /// Generates tier 2+ buildings (<c>04-buildings.md</c> §Procedural generation): a footprint (L or U shaped from tier 3),
    /// a core band at one end of the long axis holding the stairwell, the shafts and a hall on every floor of a multi-floor
    /// building, rooms per floor by BSP from the type's program (floor hints: ground, upper, each), doors as a tree per
    /// floor, vents and taps by room type, the air intake, water connection and power connection on the ground floor
    /// (each with floor space for its reference equipment), windows, structural columns (tier 3+), fire walls on large
    /// floors and furniture. Deterministic from the request seed; <paramref name="attempt"/> nudges as in
    /// <see cref="BuildingGenerator"/>.
    /// </summary>
    public static class FloorPlanGenerator
    {
        const int MaxInternalAttempts = 40;
        public const string WaterId = "water-connection";
        public const string GasId = "gas-connection";
        public const string PowerId = "power-connection";

        public static BuildingLayoutDef? Generate(DataRepository data, BuildingRequest req, int attempt)
        {
            BuildingTypeDef type = data.BuildingType(req.BuildingType);
            if (type.Program.Count == 0) return null;
            BuildingGenerationDef gen = data.Buildings.Generation;
            int roomVariant = attempt < gen.OpeningNudges ? 0 : attempt - gen.OpeningNudges + 1;
            for (int k = 0; k < MaxInternalAttempts; k++)
            {
                ulong roomSeed = Seeds.Derive(Seeds.Derive(req.Seed, "rooms"), (ulong)(roomVariant * MaxInternalAttempts + k));
                var rng = new Rng(roomSeed);
                var outletCounts = new List<int>();
                BuildingLayoutDef? b = Plan(data, type, req, rng, outletCounts);
                if (b == null) continue;
                var openings = new Rng(Seeds.Derive(Seeds.Derive(roomSeed, "openings"), (ulong)attempt));
                if (!Finish(data, type, b, rng, openings, outletCounts)) continue;
                BuildingGenerator.AddFurniture(data, b, new Rng(Seeds.Derive(req.Seed, "furniture")));
                return b;
            }
            return null;
        }

        // ---------- plan: footprint, core band, shape, rooms ----------

        readonly struct Rect
        {
            public readonly int X, Y, W, H;
            public Rect(int x, int y, int w, int h) { X = x; Y = y; W = w; H = h; }
            public int Area => W * H;
        }

        sealed class Slot
        {
            public RoomTypeDef Type = null!;
            public double Weight;
            public int Outlets;
        }

        static BuildingLayoutDef? Plan(DataRepository data, BuildingTypeDef type, BuildingRequest req, Rng rng, List<int> outletCounts)
        {
            BuildingTierDef tier = data.BuildingTier(type.Tier);
            BuildingGenerationDef gen = data.Buildings.Generation;
            List<int> min = type.FootprintMin.Count == 2 ? type.FootprintMin : tier.FootprintMin;
            List<int> max = type.FootprintMax.Count == 2 ? type.FootprintMax : tier.FootprintMax;
            int w = rng.NextInt(min[0], max[0] + 1), h = rng.NextInt(min[1], max[1] + 1);
            if (rng.Chance(0.5)) (w, h) = (h, w);
            int floors = type.FloorsMin > 0 ? rng.NextInt(type.FloorsMin, type.FloorsMax + 1) : rng.NextInt(tier.FloorsMin, tier.FloorsMax + 1);
            // Tier 4+: one basement (floor −1) with basementChance (04 §Grid). No draw below that tier, so older tiers keep their seeds.
            bool basement = type.Tier >= gen.BasementFromTier && rng.Chance(gen.BasementChance);
            int lowest = basement ? -1 : 0, total = floors + (basement ? 1 : 0);
            bool alongX = w >= h;
            int longLen = alongX ? w : h, shortLen = alongX ? h : w;

            // Canonical frame: l along the long axis, s along the short one.
            Rect Map(int l, int s, int ll, int ss) => alongX ? new Rect(l, s, ll, ss) : new Rect(s, l, ss, ll);

            var b = new BuildingLayoutDef
            {
                Id = req.Id, DisplayName = type.DisplayName, Tier = type.Tier, BuildingType = type.Id, StreetSide = req.StreetSide,
                Width = w, Height = h, Floors = total, LowestFloor = lowest, ReferenceCrankSpeed = type.ReferenceCrankSpeed,
            };

            // Core band: stairwell, shafts and a hall, the same on every floor (multi-floor buildings only).
            int bw = total > 1 ? gen.CoreBandWidth : 0;
            bool bandAtStart = rng.Chance(0.5);
            var bandRooms = new List<(string type, Rect r)>();
            if (bw > 0)
            {
                int shafts = rng.NextInt(tier.ShaftsMin, tier.ShaftsMax + 1);
                while (shafts > 1 && gen.StairwellLength + shafts * gen.ShaftSize + 1 > shortLen) shafts--;
                int bandL = bandAtStart ? 0 : longLen - bw;
                bool fromLow = rng.Chance(0.5);
                var pieces = new List<(string, int)> { ("stairwell", gen.StairwellLength) };
                for (int i = 0; i < shafts; i++) pieces.Add(("shaft", gen.ShaftSize));
                int used = 0;
                foreach (var (_, len) in pieces) used += len;
                if (used >= shortLen) return null;
                pieces.Add(("hall", shortLen - used));
                // Shafts sit next to the hall so pipes reach them from the rooms either side.
                int s0 = 0;
                foreach (var (t, len) in pieces)
                {
                    int s = fromLow ? s0 : shortLen - s0 - len;
                    bandRooms.Add((t, Map(bandL, s, bw, len)));
                    s0 += len;
                }
            }

            // The rest of the floor, minus an L or U cut at the far end.
            int restL = bandAtStart ? bw : 0, restLen = longLen - bw;
            var free = new List<Rect>();
            bool shaped = type.Tier >= gen.LShapeFromTier && rng.Chance(gen.LShapeChance);
            int minSide = gen.MinRoomSide;
            if (shaped)
            {
                bool u = rng.Chance(gen.UShapeShare);
                int cl = (int)Math.Round(restLen * rng.NextRange(0.3, 0.45));
                int cd = (int)Math.Round(shortLen * rng.NextRange(0.3, 0.45));
                int farL = bandAtStart ? restL + restLen - cl : restL;
                int nearL = bandAtStart ? restL : restL + cl;
                if (restLen - cl >= minSide + 2 && cl >= minSide)
                {
                    free.Add(Map(nearL, 0, restLen - cl, shortLen));
                    if (u)
                    {
                        int s0 = (shortLen - cd) / 2;
                        if (s0 >= minSide && shortLen - s0 - cd >= minSide)
                        {
                            free.Add(Map(farL, 0, cl, s0));
                            free.Add(Map(farL, s0 + cd, cl, shortLen - s0 - cd));
                        }
                        else shaped = false;
                    }
                    else
                    {
                        bool low = rng.Chance(0.5);
                        if (shortLen - cd >= minSide) free.Add(Map(farL, low ? cd : 0, cl, shortLen - cd));
                        else shaped = false;
                    }
                }
                else shaped = false;
                if (!shaped) free.Clear();
            }
            if (free.Count == 0) free.Add(Map(restL, 0, restLen, shortLen));

            for (int f = lowest; f < floors; f++)
            {
                foreach (var (t, r) in bandRooms) AddRoom(b, t, r, f, outletCounts, 0);
                var slots = SlotsFor(data, type, f, floors, rng);
                if (slots.Count == 0) slots.Add(new Slot { Type = data.RoomType("storage"), Weight = 1 });
                for (int i = slots.Count - 1; i > 0; i--)
                {
                    int j = rng.NextInt(0, i + 1);
                    (slots[i], slots[j]) = (slots[j], slots[i]);
                }
                // Share the slots among the free rectangles by area, each getting at least one.
                var groups = new List<List<Slot>>();
                double totalW = 0, totalA = 0;
                foreach (Slot s in slots) totalW += s.Weight;
                foreach (Rect r in free) { groups.Add(new List<Slot>()); totalA += r.Area; }
                var assigned = new double[free.Count];
                foreach (Slot s in slots)
                {
                    int best = 0;
                    double bestGap = double.MinValue;
                    for (int i = 0; i < free.Count; i++)
                    {
                        double gap = totalW * free[i].Area / totalA - assigned[i];
                        if (gap > bestGap) { bestGap = gap; best = i; }
                    }
                    groups[best].Add(s);
                    assigned[best] += s.Weight;
                }
                for (int i = 0; i < free.Count; i++)
                {
                    if (groups[i].Count == 0) groups[i].Add(new Slot { Type = data.RoomType("storage"), Weight = 1 });
                    var rooms = new List<RoomDef>();
                    var counts = new List<int>();
                    var split = new List<BuildingGenerator.SplitSlot>();
                    foreach (Slot s in groups[i]) split.Add(new BuildingGenerator.SplitSlot(s.Type, s.Weight, s.Outlets));
                    if (!BuildingGenerator.Split(free[i].X, free[i].Y, free[i].W, free[i].H, split, rooms, counts, minSide)) return null;
                    for (int r = 0; r < rooms.Count; r++)
                    {
                        rooms[r].Floor = f;
                        b.Rooms.Add(rooms[r]);
                        outletCounts.Add(counts[r]);
                    }
                }
            }
            var ids = new Dictionary<string, int>();
            foreach (RoomDef r in b.Rooms)
            {
                ids[r.Type] = ids.TryGetValue(r.Type, out int c) ? c + 1 : 1;
                r.Id = $"{r.Type}-{ids[r.Type]}";
            }
            return b;
        }

        static void AddRoom(BuildingLayoutDef b, string type, Rect r, int floor, List<int> outletCounts, int outlets)
        {
            b.Rooms.Add(new RoomDef { Type = type, X = r.X, Y = r.Y, Width = r.W, Height = r.H, Floor = floor });
            outletCounts.Add(outlets);
        }

        /// <summary>The program's rooms for one floor (floor hints: ground, upper, each above ground, basement).</summary>
        static List<Slot> SlotsFor(DataRepository data, BuildingTypeDef type, int floor, int floors, Rng rng)
        {
            var slots = new List<Slot>();
            foreach (ProgramRoomDef p in type.Program)
            {
                bool here = p.Floor switch
                {
                    "each" => floor >= 0,
                    "upper" => floor > 0 || (floors == 1 && floor == 0),
                    "basement" => floor < 0,
                    _ => floor == 0,
                };
                if (!here) continue;
                int n = rng.NextInt(p.CountMin, p.CountMax + 1);
                for (int i = 0; i < n; i++) slots.Add(new Slot { Type = data.RoomType(p.Room), Weight = p.AreaWeight, Outlets = p.Outlets });
            }
            return slots;
        }

        // ---------- doors, outlets, openings, windows, columns, fire walls, loads ----------

        static readonly (string side, int dx, int dy)[] Sides = { ("north", 0, 1), ("south", 0, -1), ("east", 1, 0), ("west", -1, 0) };

        static int RoomAt(BuildingLayoutDef b, int x, int y, int f)
        {
            for (int i = 0; i < b.Rooms.Count; i++)
            {
                RoomDef r = b.Rooms[i];
                if (r.Floor == f && x >= r.X && y >= r.Y && x < r.X + r.Width && y < r.Y + r.Height) return i;
            }
            return -1;
        }

        static bool Kind(DataRepository data, RoomDef r, Func<RoomTypeDef, bool> test) => test(data.RoomType(r.Type));
        static bool IsCore(DataRepository data, RoomDef r) => Kind(data, r, t => t.Shaft || t.BlocksPipes);

        static bool Finish(DataRepository data, BuildingTypeDef type, BuildingLayoutDef b, Rng rng, Rng openings, List<int> outletCounts)
        {
            for (int f = b.LowestFloor; f < b.LowestFloor + b.Floors; f++) if (!Doors(data, b, f, rng)) return false;
            // People first: where the indoor air quality code is in force, they set the vent count (D155).
            foreach (RoomDef r in b.Rooms)
            {
                RoomTypeDef t = data.RoomType(r.Type);
                r.Occupants = t.Occupants + t.OccupantsPerM2 * r.Width * r.Height;
                r.MachinesW = t.MachinesW;
            }
            Outlets(data, b, rng, outletCounts);
            if (b.Outlets.Count == 0) return false;
            BuildingTierDef tier = data.BuildingTier(type.Tier);
            if (!PlaceOpening(data, type, b, openings, "air", BuildingGenerator.AirIntakeId, tier.ReferenceEquipment)) return false;
            bool water = b.Outlets.Exists(o => BuildingState.OutletFluid(o.Type) == "water");
            if (water && !PlaceOpening(data, type, b, openings, "water", WaterId, tier.ReferenceWaterEquipment)) return false;
            bool gas = b.Outlets.Exists(o => BuildingState.OutletFluid(o.Type) == BuildingState.GasFluid);
            if (gas && !PlaceOpening(data, type, b, openings, "gas", GasId, tier.ReferenceGasEquipment)) return false;
            if (!PlaceOpening(data, type, b, openings, "power", PowerId, new List<string> { "panel-basic" })) return false;
            Windows(data, b);
            Columns(data, b, rng);
            FireWalls(data, b);
            foreach (CodeDef c in data.Codes)
                if (c.Tiers.Contains(type.Tier) || type.Codes.Contains(c.Id)) b.Codes.Add(c.Id);
            foreach (string e in type.InitialEquipment)
                b.Equipment.Add(new EquipmentPlacementDef { EquipmentId = e, OpeningId = BuildingGenerator.AirIntakeId, On = true });
            b.Equipment.Add(new EquipmentPlacementDef { EquipmentId = "panel-basic", OpeningId = PowerId, On = true });
            return true;
        }

        /// <summary>One door per room on a floor, as a tree from the main hub (shafts get none). False when a room cannot be reached.</summary>
        static bool Doors(DataRepository data, BuildingLayoutDef b, int floor, Rng rng)
        {
            var rooms = new List<int>();
            for (int i = 0; i < b.Rooms.Count; i++)
                if (b.Rooms[i].Floor == floor && !Kind(data, b.Rooms[i], t => t.Shaft)) rooms.Add(i);
            var edges = new Dictionary<(int, int), List<(int x, int y, string side)>>();
            for (int x = 0; x < b.Width; x++)
                for (int y = 0; y < b.Height; y++)
                {
                    int a = RoomAt(b, x, y, floor);
                    if (a < 0 || !rooms.Contains(a)) continue;
                    foreach ((string side, int dx, int dy) in new[] { ("east", 1, 0), ("north", 0, 1) })
                    {
                        int o = RoomAt(b, x + dx, y + dy, floor);
                        if (o < 0 || o == a || !rooms.Contains(o)) continue;
                        var key = a < o ? (a, o) : (o, a);
                        if (!edges.TryGetValue(key, out var list)) edges[key] = list = new List<(int, int, string)>();
                        list.Add((x, y, side));
                    }
                }
            bool Hub(int i) => data.RoomType(b.Rooms[i].Type).Hub;
            int root = rooms[0];
            foreach (int i in rooms)
            {
                bool stair = b.Rooms[i].Type == "stairwell", rootStair = b.Rooms[root].Type == "stairwell";
                int ai = b.Rooms[i].Width * b.Rooms[i].Height, ar = b.Rooms[root].Width * b.Rooms[root].Height;
                if (stair && !rootStair) { root = i; continue; }
                if (rootStair) continue;
                if ((Hub(i) && !Hub(root)) || (Hub(i) == Hub(root) && ai > ar)) root = i;
            }
            var connected = new HashSet<int> { root };
            var keys = new List<(int, int)>(edges.Keys);
            keys.Sort();
            while (connected.Count < rooms.Count)
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
                if (best.Count == 0) return false;
                var pick = best[rng.NextInt(0, best.Count)];
                var spots = edges[pick];
                var spot = spots[rng.NextInt(0, spots.Count)];
                b.Doors.Add(new DoorDef { X = spot.x, Y = spot.y, Side = spot.side, Floor = floor });
                connected.Add(pick.Item1);
                connected.Add(pick.Item2);
            }
            return true;
        }

        static bool IsDoorTile(BuildingLayoutDef b, int x, int y, int f)
        {
            foreach (DoorDef d in b.Doors)
            {
                if (d.Floor != f) continue;
                if (d.X == x && d.Y == y) return true;
                int nx = d.X + (d.Side == "east" ? 1 : d.Side == "west" ? -1 : 0), ny = d.Y + (d.Side == "north" ? 1 : d.Side == "south" ? -1 : 0);
                if (nx == x && ny == y) return true;
            }
            return false;
        }

        static bool OutletAt(BuildingLayoutDef b, int x, int y, int f) => b.Outlets.Exists(o => o.X == x && o.Y == y && o.Floor == f);

        static double Round(double v, double step) => Math.Round(Math.Round(v / step, MidpointRounding.AwayFromZero) * step, 6);

        /// <summary>Vents spread along the room's long axis (ceiling); taps along its walls (floor layer).</summary>
        static void Outlets(DataRepository data, BuildingLayoutDef b, Rng rng, List<int> outletCounts)
        {
            BuildingGenerationDef gen = data.Buildings.Generation;
            int vents = 0, taps = 0, stoves = 0;
            bool airQuality = false;
            foreach (CodeDef c in data.Codes) if (c.Id == Codes.IndoorAirQuality.CodeId && c.Tiers.Contains(b.Tier)) airQuality = true;
            for (int ri = 0; ri < b.Rooms.Count; ri++)
            {
                RoomDef r = b.Rooms[ri];
                RoomTypeDef t = data.RoomType(r.Type);
                foreach (RoomOutletRuleDef rule in t.Outlets)
                {
                    OutletDef outlet = data.Outlet(rule.Type);
                    if (rule.FromTier > b.Tier || !gen.PhaseFluids.Contains(outlet.Fluid)) continue;
                    if (!outlet.Tiers.TryGetValue(b.Tier.ToString(), out OutletRequirementDef? req)) continue;
                    // Taps and stoves stand along the walls on the floor layer; vents throw across the room from the ceiling.
                    string fluid = BuildingState.OutletFluid(rule.Type);
                    bool tap = fluid != "air";
                    int area = r.Width * r.Height;
                    int count = rule.AreaPerOutlet > 0 ? Math.Max(rule.Min, (int)Math.Ceiling(area / rule.AreaPerOutlet)) : rule.Min;
                    if (!tap && outletCounts[ri] > 0) count = outletCounts[ri];
                    // Enough vents that each needs at most a share of its ideal flow for the room's people (D155).
                    if (!tap && airQuality && r.Occupants > 0)
                        count = Math.Max(count, (int)Math.Ceiling(Codes.IndoorAirQuality.RequiredSupply(data, r.Occupants)
                            / (Codes.IndoorAirQuality.VentShareOfIdeal(data) * req.Ideal * Economy.OutletRequirement.ToSiFactor(outlet.Unit)) - 1e-9));
                    count = Math.Min(count, Math.Max(1, area / 4));
                    List<(int x, int y)> spots = tap ? WallTiles(b, r) : new List<(int, int)>();
                    bool alongX = r.Width >= r.Height;
                    int len = alongX ? r.Width : r.Height;
                    for (int i = 0; i < count; i++)
                    {
                        int x, y;
                        string facing = "";
                        if (tap)
                        {
                            spots.RemoveAll(s => IsDoorTile(b, s.x, s.y, r.Floor) || OutletAt(b, s.x, s.y, r.Floor));
                            if (spots.Count == 0) break;
                            (x, y) = spots[(int)Math.Floor((i + 0.5) * spots.Count / count) % spots.Count];
                        }
                        else
                        {
                            int along = (int)Math.Floor((i + 0.5) * len / count);
                            int across = (alongX ? r.Height : r.Width) / 2;
                            x = r.X + (alongX ? along : across);
                            y = r.Y + (alongX ? across : along);
                            if (!FreeTileNear(b, r, ref x, ref y)) continue;
                            facing = count == 1
                                ? (alongX ? (rng.Chance(0.5) ? "east" : "west") : (rng.Chance(0.5) ? "north" : "south"))
                                : alongX ? (along < len / 2.0 ? "east" : "west") : (along < len / 2.0 ? "north" : "south");
                        }
                        double variation = outlet.RequirementVariation > 0 ? outlet.RequirementVariation : gen.RequirementVariation;
                        double f = 1 + variation * (2 * rng.NextDouble() - 1);
                        double step = gen.RoundingSteps.TryGetValue(outlet.Unit, out double st) ? st : 1;
                        double mn = Round(req.Min * f, step), ideal = Math.Max(mn + step, Round(req.Ideal * f, step));
                        double mx = req.Max > 0 ? Math.Max(ideal + step, Round(req.Max * f, step)) : 0;
                        b.Outlets.Add(new OutletPlacementDef
                        {
                            Id = fluid == "water" ? $"tap-{++taps}" : tap ? $"stove-{++stoves}" : $"vent-{++vents}",
                            Type = rule.Type, X = x, Y = y, Floor = r.Floor, Min = mn, Ideal = ideal, Max = mx, Facing = facing,
                        });
                    }
                }
            }
        }

        /// <summary>Tiles of a room along its walls, in order around the room.</summary>
        static List<(int x, int y)> WallTiles(BuildingLayoutDef b, RoomDef r)
        {
            var list = new List<(int, int)>();
            for (int x = r.X; x < r.X + r.Width; x++) list.Add((x, r.Y));
            for (int y = r.Y + 1; y < r.Y + r.Height; y++) list.Add((r.X + r.Width - 1, y));
            if (r.Height > 1) for (int x = r.X + r.Width - 2; x >= r.X; x--) list.Add((x, r.Y + r.Height - 1));
            if (r.Width > 1) for (int y = r.Y + r.Height - 2; y > r.Y; y--) list.Add((r.X, y));
            return list;
        }

        static bool FreeTileNear(BuildingLayoutDef b, RoomDef r, ref int x, ref int y)
        {
            int bx = x, by = y, best = int.MaxValue;
            for (int tx = r.X; tx < r.X + r.Width; tx++)
                for (int ty = r.Y; ty < r.Y + r.Height; ty++)
                {
                    if (IsDoorTile(b, tx, ty, r.Floor) || OutletAt(b, tx, ty, r.Floor)) continue;
                    int d = Math.Abs(tx - bx) + Math.Abs(ty - by);
                    if (d < best) { best = d; x = tx; y = ty; }
                }
            return best != int.MaxValue;
        }

        /// <summary>
        /// An entry opening on a ground-floor facade tile (street side with <c>streetSideChance</c>): not a door or outlet
        /// tile, not next to another opening, in a room pipes may use, and with floor space for the largest reference
        /// equipment that goes there. The air intake also needs its straight lead-in; the power connection prefers an
        /// electrical room.
        /// </summary>
        internal static bool PlaceOpening(DataRepository data, BuildingTypeDef type, BuildingLayoutDef b, Rng rng, string utility, string id, List<string> equipment)
        {
            int floor = b.LowestFloor < 0 && (utility == "water") ? b.LowestFloor : 0;
            var all = new List<(int x, int y, string side)>();
            bool electricalExists = utility == "power" && b.Rooms.Exists(r => r.Floor == floor && Kind(data, r, t => t.Electrical));
            for (int x = 0; x < b.Width; x++)
                for (int y = 0; y < b.Height; y++)
                {
                    int ri = RoomAt(b, x, y, floor);
                    if (ri < 0) continue;
                    RoomDef room = b.Rooms[ri];
                    if (IsCore(data, room) || Kind(data, room, t => t.Protected)) continue;
                    bool electrical = Kind(data, room, t => t.Electrical);
                    if (utility == "power" ? electricalExists && !electrical : electrical) continue;
                    if (OutletAt(b, x, y, floor) || IsDoorTile(b, x, y, floor)) continue;
                    if (b.EntryOpenings.Exists(o => o.Floor == floor && Math.Abs(o.X - x) + Math.Abs(o.Y - y) <= 1)) continue;
                    // Gas separation: the gas connection and the panel keep their distance, as do stoves and the panel.
                    if (utility == "gas" || utility == "power")
                    {
                        int clear = (int)(data.FindCode("gas-separation")?.Parameters.GetValueOrDefault("tiles", 2) ?? 2);
                        string other = utility == "gas" ? "power" : "gas";
                        if (b.EntryOpenings.Exists(o => o.Utility == other && o.Floor == floor && Math.Max(Math.Abs(o.X - x), Math.Abs(o.Y - y)) <= clear + 1)) continue;
                        if (utility == "power" && b.Outlets.Exists(o => BuildingState.OutletFluid(o.Type) == BuildingState.GasFluid && o.Floor == floor && Math.Max(Math.Abs(o.X - x), Math.Abs(o.Y - y)) <= clear)) continue;
                    }
                    foreach ((string side, int dx, int dy) in Sides)
                    {
                        if (RoomAt(b, x + dx, y + dy, floor) >= 0) continue;
                        if (utility == "air" && !LeadInFits(data, b, ri, x, y, -dx, -dy, floor)) continue;
                        if (utility == "air" && type.MaxIntakeDistance > 0)
                        {
                            int far = 0;
                            foreach (OutletPlacementDef o in b.Outlets) far = Math.Max(far, Math.Abs(o.X - x) + Math.Abs(o.Y - y));
                            if (far > type.MaxIntakeDistance) continue;
                        }
                        all.Add((x, y, side));
                    }
                }
            // Equipment floor space is checked on the chosen candidates (largest reference item that sits on the floor).
            EquipmentDef? biggest = null;
            foreach (string e in equipment)
            {
                EquipmentDef? d = data.FindEquipment(e);
                if (d != null && d.Placement == "intake" && d.Footprint.Layer == "floor" && (biggest == null || d.Footprint.Width * d.Footprint.Height > biggest.Footprint.Width * biggest.Footprint.Height)) biggest = d;
            }
            var pools = new List<List<(int x, int y, string side)>>();
            if (rng.Chance(data.Buildings.Generation.StreetSideChance))
            {
                var street = all.FindAll(c => c.side == b.StreetSide);
                if (street.Count > 0) pools.Add(street);
            }
            pools.Add(all);
            foreach (var pool in pools)
            {
                var order = new List<(int x, int y, string side)>(pool);
                for (int i = order.Count - 1; i > 0; i--)
                {
                    int j = rng.NextInt(0, i + 1);
                    (order[i], order[j]) = (order[j], order[i]);
                }
                foreach (var c in order)
                {
                    var opening = new EntryOpeningDef { Id = id, Utility = utility, X = c.x, Y = c.y, Floor = floor, Facing = c.side };
                    if (biggest != null && !FitsAt(data, b, opening, biggest)) continue;
                    b.EntryOpenings.Add(opening);
                    return true;
                }
            }
            return false;
        }

        static bool FitsAt(DataRepository data, BuildingLayoutDef b, EntryOpeningDef opening, EquipmentDef def)
        {
            b.EntryOpenings.Add(opening);
            BuildingLayout layout = BuildingLayout.From(data, b);
            var state = new BuildingState { BuildingId = b.Id };
            foreach (EntryOpeningDef o in b.EntryOpenings)
            {
                if (o.Id == opening.Id) continue;
                // Earlier openings keep room for their own reference equipment.
                string? eq = o.Utility switch { "air" => data.BuildingTier(b.Tier).ReferenceEquipment.Find(x => data.FindEquipment(x)?.Placement == "intake"), _ => null };
                if (eq != null) state.Equipment.Add(new EquipmentState { EquipmentId = eq, OpeningId = o.Id });
            }
            bool ok = EquipmentFits.Footprint(data, layout, state, opening, def) != null;
            b.EntryOpenings.Remove(opening);
            return ok;
        }

        static bool LeadInFits(DataRepository data, BuildingLayoutDef b, int room, int x, int y, int ix, int iy, int floor)
        {
            for (int k = 1; k <= data.Balancing.AutoRouter.LeadInTiles; k++)
            {
                int tx = x + ix * k, ty = y + iy * k;
                if (RoomAt(b, tx, ty, floor) != room || IsDoorTile(b, tx, ty, floor) || OutletAt(b, tx, ty, floor)) return false;
            }
            return true;
        }

        static void Windows(DataRepository data, BuildingLayoutDef b)
        {
            int spacing = data.Buildings.Generation.WindowSpacing;
            foreach (RoomDef r in b.Rooms)
            {
                // Below ground: no windows.
                if (IsCore(data, r) || r.Floor < 0) continue;
                foreach ((string side, int dx, int dy) in Sides)
                {
                    var run = new List<(int x, int y)>();
                    for (int i = 0; i < (dx != 0 ? r.Height : r.Width); i++)
                    {
                        int x = dx != 0 ? (dx > 0 ? r.X + r.Width - 1 : r.X) : r.X + i;
                        int y = dx != 0 ? r.Y + i : (dy > 0 ? r.Y + r.Height - 1 : r.Y);
                        if (RoomAt(b, x + dx, y + dy, r.Floor) >= 0) continue;
                        run.Add((x, y));
                    }
                    for (int i = spacing / 2; i < run.Count; i += spacing)
                    {
                        (int x, int y) = run[i];
                        if (b.EntryOpenings.Exists(o => o.Floor == r.Floor && o.X == x && o.Y == y && o.Facing == side)) continue;
                        b.Windows.Add(new WindowDef { X = x, Y = y, Side = side, Floor = r.Floor });
                    }
                }
            }
        }

        /// <summary>
        /// Structural columns on a grid every 6–8 m (tier 3+): a grid point becomes a column when on every floor it is inside
        /// a room that is not a hub, shaft or stairwell, and is not a door, outlet or opening tile or an intake lead-in.
        /// </summary>
        static void Columns(DataRepository data, BuildingLayoutDef b, Rng rng)
        {
            BuildingGenerationDef gen = data.Buildings.Generation;
            if (b.Tier < gen.ColumnFromTier) return;
            int s = rng.NextInt(gen.ColumnSpacingMin, gen.ColumnSpacingMax + 1);
            int ox = s / 2, oy = s / 2;
            var reserved = new HashSet<(int, int)>();
            foreach (EntryOpeningDef o in b.EntryOpenings)
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++) reserved.Add((o.X + dx, o.Y + dy));
            EntryOpeningDef? intake = b.EntryOpenings.Find(o => o.Utility == "air");
            if (intake != null)
            {
                (int ix, int iy) = EquipmentFits.Inward(intake.Facing);
                for (int k = 0; k <= data.Balancing.AutoRouter.LeadInTiles + 1; k++) reserved.Add((intake.X + ix * k, intake.Y + iy * k));
            }
            for (int x = ox; x < b.Width; x += s)
                for (int y = oy; y < b.Height; y += s)
                {
                    if (reserved.Contains((x, y))) continue;
                    bool ok = true;
                    for (int f = b.LowestFloor; f < b.LowestFloor + b.Floors && ok; f++)
                    {
                        int ri = RoomAt(b, x, y, f);
                        if (ri < 0 || IsCore(data, b.Rooms[ri]) || data.RoomType(b.Rooms[ri].Type).Hub) ok = false;
                        else if (IsDoorTile(b, x, y, f) || OutletAt(b, x, y, f)) ok = false;
                    }
                    if (ok) b.Columns.Add(new TileDef { X = x, Y = y });
                }
        }

        /// <summary>Floors larger than <c>fireWallAreaM2</c> get a fire wall along a room boundary near the middle.</summary>
        static void FireWalls(DataRepository data, BuildingLayoutDef b)
        {
            if (b.Width * b.Height <= data.Buildings.Generation.FireWallAreaM2) return;
            bool alongX = b.Width >= b.Height;
            int len = alongX ? b.Width : b.Height;
            for (int off = 0; off < len / 2; off++)
                foreach (int c in new[] { len / 2 - off, len / 2 + off })
                {
                    if (c <= 0 || c >= len) continue;
                    bool clean = true;
                    foreach (RoomDef r in b.Rooms)
                    {
                        int lo = alongX ? r.X : r.Y, hi = lo + (alongX ? r.Width : r.Height);
                        if (lo < c && c < hi) { clean = false; break; }
                    }
                    if (!clean) continue;
                    for (int f = b.LowestFloor; f < b.LowestFloor + b.Floors; f++)
                        for (int i = 0; i < (alongX ? b.Height : b.Width); i++)
                        {
                            int x = alongX ? c - 1 : i, y = alongX ? i : c - 1;
                            int nx = alongX ? c : i, ny = alongX ? i : c;
                            if (RoomAt(b, x, y, f) >= 0 && RoomAt(b, nx, ny, f) >= 0)
                                b.FireWalls.Add(new DoorDef { X = x, Y = y, Floor = f, Side = alongX ? "east" : "north" });
                        }
                    return;
                }
        }
    }
}
