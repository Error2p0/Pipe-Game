using System;
using System.Collections.Generic;
using HeadLoss.Core.Data;
using HeadLoss.Core.Data.Defs;

namespace HeadLoss.Game.City
{
    /// <summary>A routed underground main or power cable (<c>03-city.md</c> §City utility network). Saved.</summary>
    public sealed class MainState
    {
        public int Id;
        /// <summary>water or power (gas, heating and special gas arrive with their phases).</summary>
        public string Utility = "water";
        /// <summary>Pipe family id from <c>pipes.json</c> (water-main, power-cable).</summary>
        public string Family = "";
        /// <summary>Main diameter, mm (mains).</summary>
        public double DiameterMm;
        /// <summary>Cable capacity, kW (cables).</summary>
        public double CapacityKW;
        /// <summary>City tiles from start to end, each [x, y], 4-connected.</summary>
        public List<int[]> Tiles = new List<int[]>();
        /// <summary>Laid by directional drilling: no recurring fines for what it passes under, no crossing cost.</summary>
        public bool Drilled;
        /// <summary>Gas mains: pressure class id (lp, mp, hp); "" for other utilities.</summary>
        public string PressureClass = "";
        /// <summary>Plot it connects to ("" = none: a trunk others branch from).</summary>
        public string Destination = "";
        /// <summary>What it cost to lay, $ (refunds are a share of it).</summary>
        public double Paid;

        public (int x, int y) Start => (Tiles[0][0], Tiles[0][1]);
        public (int x, int y) End => (Tiles[Tiles.Count - 1][0], Tiles[Tiles.Count - 1][1]);
    }

    /// <summary>The city's routed networks and city-wide purchases. Saved in <see cref="CityState"/>.</summary>
    public sealed class UtilityState
    {
        public List<MainState> Mains = new List<MainState>();
        /// <summary>Road tiles with a utility corridor, each [x, y].</summary>
        public List<int[]> Corridors = new List<int[]>();
        public int NextId = 1;
        /// <summary>Insurance: fines halved for a premium (<c>07-equipment.md</c> §Meta upgrades).</summary>
        public bool Insurance;
        /// <summary>Engineering software bought (stress forecast in design mode).</summary>
        public bool EngineeringSoftware;
    }

    /// <summary>A routing violation on a tile (<c>03-city.md</c> §Routing): a recurring fine until the route moves.</summary>
    public sealed class CityViolation
    {
        /// <summary>under-house, protected-site, near-substation, main-separation, historic-foundations.</summary>
        public string Kind = "";
        public int X, Y;
        public string Reason = "";
        /// <summary>Fine per income cycle, $.</summary>
        public double FinePerCycle;
    }

    /// <summary>What laying a main would cost and break (the route tool's live preview).</summary>
    public sealed class RouteQuote
    {
        public bool Valid;
        /// <summary>Why the route cannot be laid ("" when it can).</summary>
        public string Error = "";
        public double Cost;
        public double PipeCost, CrossingCost, DrillingCost;
        public readonly List<CityViolation> Violations = new List<CityViolation>();
        public double FinesPerCycle;
        /// <summary>Plot the route ends on (its building connects), or "".</summary>
        public string Destination = "";
        /// <summary>Main joined at the start (0 = it starts at a facility).</summary>
        public int JoinsMain;
    }

    /// <summary>
    /// Routing rules for mains and cables (<c>03-city.md</c>): a route runs over city tiles from a facility of its utility or
    /// a tile of an existing main of the same utility, and may end on a plot to connect that building. Under houses,
    /// through protected sites and parks, under historic buildings, gas near substations and gas next to water count as
    /// violations with a recurring fine (doubled for the industrial gas depot's mains); main roads and rail cost a
    /// one-off crossing. Directional drilling makes a route legal at a high price. A tile holds two lines, a corridor
    /// tile four.
    /// </summary>
    public static class Utilities
    {
        /// <summary>The pipe family of a utility's mains (or cables).</summary>
        public static PipeFamilyDef? FamilyFor(DataRepository data, string utility)
        {
            foreach (PipeFamilyDef f in data.PipeFamilies)
                if ((f.Kind == "main" || f.Kind == "cable") && f.Utility == utility) return f;
            return null;
        }

        /// <summary>Capacity of a main at line conditions, m³/s (internal area × the family's or pressure class's capacity velocity).</summary>
        public static double CapacityM3s(PipeFamilyDef family, double diameterMm, string pressureClass = "")
        {
            double d = diameterMm / 1000;
            double velocity = family.ClassById(pressureClass)?.CapacityVelocity ?? family.CapacityVelocity;
            return Math.PI * d * d / 4 * velocity;
        }

        /// <summary>Price of one tile of a main or cable, $.</summary>
        public static double PricePerTile(DataRepository data, PipeFamilyDef family, double diameterMm, double capacityKW, string pressureClass = "")
        {
            if (family.Kind == "cable")
            {
                foreach (PipeSizeDef s in family.Sizes) if (s.CapacityKW == capacityKW) return s.PricePerTile;
                return family.Sizes[0].PricePerTile;
            }
            double factor = family.ClassById(pressureClass)?.PriceFactor ?? 1;
            return factor * family.PricePerTile * Math.Pow(diameterMm / family.PriceReferenceDiameterMm, data.Balancing.Prices.DiameterExponent);
        }

        static bool IsCorridor(UtilityState u, int x, int y) => u.Corridors.Exists(c => c[0] == x && c[1] == y);

        /// <summary>Lines (mains and cables) on a tile.</summary>
        public static int LinesOn(UtilityState u, int x, int y)
        {
            int n = 0;
            foreach (MainState m in u.Mains)
                foreach (int[] t in m.Tiles)
                    if (t[0] == x && t[1] == y) { n++; break; }
            return n;
        }

        /// <summary>Facility tiles of a utility.</summary>
        static bool IsFacilityOf(CityLayout city, int x, int y, string utility)
        {
            foreach (FacilityPlacement f in city.Facilities)
                if (f.Utility == utility && x >= f.X && y >= f.Y && x < f.X + f.Width && y < f.Y + f.Height) return true;
            return false;
        }

        /// <summary>The main of a utility whose path passes a tile (0 = none).</summary>
        /// <param name="pressureClass">Gas: only mains of this class ("" = any).</param>
        public static int MainAt(UtilityState u, string utility, int x, int y, string pressureClass = "")
        {
            foreach (MainState m in u.Mains)
            {
                if (m.Utility != utility || (pressureClass.Length > 0 && m.PressureClass != pressureClass)) continue;
                foreach (int[] t in m.Tiles) if (t[0] == x && t[1] == y) return m.Id;
            }
            return 0;
        }

        /// <summary>
        /// Prices and checks a route (tiles from start to end). Valid routes start at a facility of the utility or on an
        /// existing main of it, step between neighbouring tiles inside the map, fit each tile's line capacity, and end on a
        /// plot (to connect it) or anywhere else (a trunk to branch from later).
        /// </summary>
        public static RouteQuote Quote(DataRepository data, CityLayout city, UtilityState u, string utility, double diameterMm, double capacityKW,
            IReadOnlyList<(int x, int y)> path, bool drilled, int tier = 1, string pressureClass = "")
        {
            var q = new RouteQuote();
            PipeFamilyDef? family = FamilyFor(data, utility);
            if (family == null) { q.Error = "Nothing to route for this utility yet"; return q; }
            if (path.Count < 2) { q.Error = "Drag at least one tile"; return q; }
            for (int i = 0; i < path.Count; i++)
            {
                if (!city.InMap(path[i].x, path[i].y)) { q.Error = "Stay on the map"; return q; }
                if (i > 0 && Math.Abs(path[i].x - path[i - 1].x) + Math.Abs(path[i].y - path[i - 1].y) != 1) { q.Error = "Mains run along the tile grid"; return q; }
            }
            var seen = new HashSet<(int, int)>();
            foreach (var t in path) if (!seen.Add(t)) { q.Error = "The route crosses itself"; return q; }
            (int sx, int sy) = path[0];
            q.JoinsMain = MainAt(u, utility, sx, sy, pressureClass);
            if (!IsFacilityOf(city, sx, sy, utility) && q.JoinsMain == 0)
            {
                q.Error = utility == "power" ? "Start at a substation or an existing cable" : "Start at the facility or an existing main";
                return q;
            }
            (int ex, int ey) = path[path.Count - 1];
            Plot? dest = city.PlotAt(ex, ey);
            if (dest != null) q.Destination = dest.Id;
            CityUtilitiesDef cu = data.Balancing.City;
            double tileCost = PricePerTile(data, family, diameterMm, capacityKW, pressureClass);
            bool depot = false;
            foreach (FacilityPlacement f in city.Facilities)
                if (f.TypeId == "industrial-gas-depot" && sx >= f.X && sy >= f.Y && sx < f.X + f.Width && sy < f.Y + f.Height) depot = true;
            double tierFactor = data.Balancing.Economy.TierFactor[Math.Max(1, Math.Min(5, dest?.Tier ?? tier)) - 1];
            double fineFactor = tierFactor * (depot ? cu.DepotFineFactor : 1) * (u.Insurance ? InsuranceFineFactor(data) : 1);
            for (int i = 0; i < path.Count; i++)
            {
                (int x, int y) = path[i];
                bool joinTile = i == 0, connection = i == path.Count - 1 && dest != null;
                if (!joinTile && !connection)
                {
                    int cap = IsCorridor(u, x, y) ? cu.CorridorCapacity : cu.TileCapacity;
                    if (LinesOn(u, x, y) + 1 > cap && !IsFacilityOf(city, x, y, utility))
                    {
                        q.Error = IsCorridor(u, x, y) ? "The utility corridor is full here" : "No room for another line here (a utility corridor holds four)";
                        return q;
                    }
                }
                if (IsFacilityOf(city, x, y, utility)) continue;
                q.PipeCost += tileCost;
                if (drilled)
                {
                    q.DrillingCost += cu.DrillingPerTile;
                    continue;
                }
                if (city.Has(x, y, CityTile.MainRoad) || city.Has(x, y, CityTile.Rail)) q.CrossingCost += cu.CrossingCost;
                Plot? under = city.PlotAt(x, y);
                if (under != null && under.Id != q.Destination) Add(q, "under-house", x, y, "Under a house that is not its destination", cu, fineFactor);
                if (city.Has(x, y, CityTile.Park) || city.Has(x, y, CityTile.Protected)) Add(q, "protected-site", x, y, "Through a park or protected site", cu, fineFactor);
                if (city.Has(x, y, CityTile.Historic)) Add(q, "historic-foundations", x, y, "Under historic foundations", cu, fineFactor);
                if (utility == "gas" && NearFacility(city, x, y, "power", cu.SubstationClearance)) Add(q, "near-substation", x, y, "Gas main near a substation", cu, fineFactor);
                if ((utility == "gas" || utility == "water") && !IsCorridor(u, x, y) && NearMain(u, utility == "gas" ? "water" : "gas", x, y, cu.MainSeparation))
                    Add(q, "main-separation", x, y, "Gas and water mains too close (not in a utility corridor)", cu, fineFactor);
            }
            q.Cost = q.PipeCost + q.CrossingCost + q.DrillingCost;
            foreach (CityViolation v in q.Violations) q.FinesPerCycle += v.FinePerCycle;
            q.Valid = true;
            return q;
        }

        /// <summary>Insurance's multiplier on fines.</summary>
        public static double InsuranceFineFactor(DataRepository data)
        {
            MetaUpgradeDef? ins = data.Upgrades.Meta.Find(m => m.Id == "insurance");
            return ins?.FineFactor ?? 1;
        }

        static void Add(RouteQuote q, string kind, int x, int y, string reason, CityUtilitiesDef cu, double factor)
        {
            double fine = (cu.Fines.TryGetValue(kind, out double f) ? f : 0) * factor;
            q.Violations.Add(new CityViolation { Kind = kind, X = x, Y = y, Reason = reason, FinePerCycle = fine });
        }

        static bool NearFacility(CityLayout city, int x, int y, string utility, int distance)
        {
            foreach (FacilityPlacement f in city.Facilities)
            {
                if (f.Utility != utility) continue;
                int dx = Math.Max(0, Math.Max(f.X - x, x - (f.X + f.Width - 1)));
                int dy = Math.Max(0, Math.Max(f.Y - y, y - (f.Y + f.Height - 1)));
                if (Math.Max(dx, dy) <= distance) return true;
            }
            return false;
        }

        static bool NearMain(UtilityState u, string utility, int x, int y, int distance)
        {
            foreach (MainState m in u.Mains)
            {
                if (m.Utility != utility) continue;
                foreach (int[] t in m.Tiles)
                    if (Math.Max(Math.Abs(t[0] - x), Math.Abs(t[1] - y)) <= distance) return true;
            }
            return false;
        }

        /// <summary>Lays a quoted route. The caller has checked and charged the cost.</summary>
        public static MainState Lay(UtilityState u, string utility, string family, double diameterMm, double capacityKW, IReadOnlyList<(int x, int y)> path,
            bool drilled, RouteQuote quote, string pressureClass = "")
        {
            var m = new MainState
            {
                Id = u.NextId++, Utility = utility, Family = family, DiameterMm = diameterMm, CapacityKW = capacityKW, Drilled = drilled, PressureClass = pressureClass,
                Destination = quote.Destination, Paid = quote.Cost,
            };
            foreach (var t in path) m.Tiles.Add(new[] { t.x, t.y });
            u.Mains.Add(m);
            return m;
        }

        /// <summary>
        /// Recurring fines of the routed mains, $ per income cycle (each main's violations at its destination's tier), and
        /// the violations themselves for the city view.
        /// </summary>
        public static double Fines(DataRepository data, CityLayout city, UtilityState u, List<CityViolation>? into = null)
        {
            double total = 0;
            var others = new UtilityState { Corridors = u.Corridors, Insurance = u.Insurance };
            foreach (MainState m in u.Mains)
            {
                var path = new List<(int, int)>();
                foreach (int[] t in m.Tiles) path.Add((t[0], t[1]));
                others.Mains.Clear();
                foreach (MainState o in u.Mains) if (o != m) others.Mains.Add(o);
                // Re-check against the other lines (capacity is not re-checked: the main is already laid).
                RouteQuote q = Quote(data, city, others, m.Utility, m.DiameterMm, m.CapacityKW, path, m.Drilled, 1, m.PressureClass);
                if (!q.Valid) continue;
                total += q.FinesPerCycle;
                into?.AddRange(q.Violations);
            }
            return total;
        }

        /// <summary>A utility corridor on a road tile, $ (none on other tiles).</summary>
        public static bool CanBuildCorridor(CityLayout city, UtilityState u, int x, int y) =>
            city.Has(x, y, CityTile.Road) && !IsCorridor(u, x, y);

        public static void BuildCorridor(UtilityState u, int x, int y) => u.Corridors.Add(new[] { x, y });

        /// <summary>Removes a main or cable (and any it alone fed, by branches starting on it). Returns what was removed.</summary>
        public static List<MainState> Remove(UtilityState u, int mainId)
        {
            var removed = new List<MainState>();
            MainState? m = u.Mains.Find(x => x.Id == mainId);
            if (m == null) return removed;
            u.Mains.Remove(m);
            removed.Add(m);
            // Branches that started on a tile of the removed main lose their supply: they go too.
            bool again = true;
            while (again)
            {
                again = false;
                foreach (MainState b in new List<MainState>(u.Mains))
                {
                    (int sx, int sy) = b.Start;
                    bool fed = false;
                    foreach (MainState o in u.Mains)
                        if (o != b && o.Utility == b.Utility && o.PressureClass == b.PressureClass && o.Tiles.Exists(t => t[0] == sx && t[1] == sy)) fed = true;
                    if (fed || removed.Exists(r => r.Utility == b.Utility && r.PressureClass == b.PressureClass && r.Tiles.Exists(t => t[0] == sx && t[1] == sy)) == false) continue;
                    u.Mains.Remove(b);
                    removed.Add(b);
                    again = true;
                }
            }
            return removed;
        }
    }
}
