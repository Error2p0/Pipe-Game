using System;
using System.Collections.Generic;
using HeadLoss.Core;
using HeadLoss.Core.Data;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.Economy;
using HeadLoss.Game.Networks;
using HeadLoss.Game.Session;
using HeadLoss.Sim.Fluids;
using System.Linq;
using HeadLoss.Sim.Network;

namespace HeadLoss.Game.Generation
{
    /// <summary>A reference layout: the auto-router's design for a building (<c>04-buildings.md</c> §Auto-router).</summary>
    public sealed class ReferenceResult
    {
        public BuildingState State = new BuildingState();
        public BuildingEvaluation Evaluation = new BuildingEvaluation();
        /// <summary>Catalogue cost of the layout, $ (pre-placed equipment counts 0).</summary>
        public double Cost;
        /// <summary>Every minimum met and no outlet above its code maximum.</summary>
        public bool Solvable;
        /// <summary>Why it is not solvable ("" when it is).</summary>
        public string Reason = "";
        /// <summary>Source equipment used.</summary>
        public string EquipmentId = "";
        /// <summary>Water source equipment used ("" = mains pressure only).</summary>
        public string WaterEquipmentId = "";
        /// <summary>Hand-crank speed the reference was checked at (0 = no crank).</summary>
        public double CrankSpeed;
        public int Dampers;
        /// <summary>Longest duct path from the intake to an outlet, m.</summary>
        public double MaxPathMetres;
    }

    /// <summary>
    /// Builds reference layouts (not exposed to the player; the solvability check, the budget cap, tests and the
    /// balancing report use it). Routes a tree on the duct layer: a trunk from the intake toward the centroid of
    /// the outlets, then the nearest outlet's branch to the tree, again and again (A* over tiles, walls passable only
    /// at doors, turns cost extra). Sizes every duct as the smallest tier-available size within the velocity limit
    /// (trunk 4 m/s, branch 2.5 m/s, at ideal flow), uses long-radius elbows when the tier allows, then picks the
    /// smallest source equipment from the tier list that meets every minimum, adding dampers where outlets would
    /// exceed their code maximum.
    /// </summary>
    public static class AutoRouter
    {
        /// <summary>Steps: four horizontal, then up and down (risers, only through shafts). Opposites are d ^ 1.</summary>
        static readonly (int dx, int dy, int df)[] Dirs = { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) };
        const int DirCount = 6;

        static TilePos Step(TilePos t, int d) => new TilePos(t.X + Dirs[d].dx, t.Y + Dirs[d].dy, t.F + Dirs[d].df);

        public static ReferenceResult Build(DataRepository data, BuildingLayout layout) => Build(data, layout, null);

        /// <summary>
        /// The reference layout: air first (as in Phase 3), then water on top of it when the building has a water
        /// connection. Solvable when every minimum is met, no code maximum is exceeded and no pipe runs above its rating.
        /// </summary>
        /// <param name="trace">Optional pass-by-pass log of the tuning (dev tools).</param>
        public static ReferenceResult Build(DataRepository data, BuildingLayout layout, List<string>? trace)
        {
            ReferenceResult air = BuildAir(data, layout, trace);
            if (!air.Solvable || layout.OpeningFor("water") == null) return air;
            return BuildWater(data, layout, air, trace);
        }

        static ReferenceResult BuildAir(DataRepository data, BuildingLayout layout, List<string>? trace)
        {
            var fail = new ReferenceResult();
            EntryOpeningDef? intake = null;
            foreach (EntryOpeningDef o in layout.EntryOpenings) if (o.Utility == "air") intake = o;
            if (intake == null) { fail.Reason = "no air intake"; return fail; }

            string material = data.Balancing.Drawing.DefaultMaterial;
            // Prefer branches with room for a damper; if that walls an outlet off, route without the preference.
            // Doors only first (as an engineer would), core drilling only when that cannot reach every outlet. From tier 2
            // every branch must first leave its outlet with two straight tiles (room for a damper) where that is possible.
            double soft = data.Balancing.AutoRouter.ShortApproachPenalty;
            var passes = new List<(double penalty, bool drill)>();
            foreach (bool drill in new[] { false, true })
            {
                if (drill && layout.Tier < layout.CoreDrillTier) continue;
                if (layout.Tier >= 2) passes.Add((double.PositiveInfinity, drill));
                passes.Add((soft, drill));
                passes.Add((0, drill));
            }
            BuildingState? state = null;
            string reason = "";
            foreach ((double penalty, bool drill) in passes)
            {
                state = RouteTree(data, layout, intake, "air", null, penalty, drill, out reason);
                if (state != null) break;
            }
            if (state == null)
            {
                fail.Reason = reason;
                return fail;
            }
            DuctNetworkState net = state.Air;

            Tree tree = Tree.Of(net, layout, data);
            List<double> sizes = SizesFor(data, layout.Tier, material);
            AutoRouterDef ar = data.Balancing.AutoRouter;
            foreach (DuctSegment s in net.Segments)
            {
                double q = tree.Flow.TryGetValue(s.Id, out double f) ? f : 0;
                double vMax = tree.Outlets.TryGetValue(s.Id, out int n) && n >= 2 ? ar.AirTrunkVelocity : ar.AirBranchVelocity;
                double d = sizes[sizes.Count - 1];
                foreach (double mm in sizes)
                {
                    double dm = Units.MmToM(mm);
                    if (q / (Math.PI * dm * dm / 4) <= vMax) { d = mm; break; }
                }
                s.DiameterMm = d;
            }
            DuctEditor.Normalize(net);
            FittingDef smooth = data.Fitting("elbow-90-long-radius");
            if (layout.Tier >= smooth.Tier)
                foreach (DuctNode node in net.Nodes)
                    if (DuctEditor.IsElbow(net, node)) node.SmoothFittings = true;
            PlaceFireDampers(data, layout, net);
            double maxPath = 0;
            foreach (double m in Tree.Of(net, layout, data).PathMetres.Values) maxPath = Math.Max(maxPath, m);

            // Source equipment: the crank at its reference speed, or the smallest from the tier list that works.
            var candidates = new List<(string id, double crank)>();
            if (layout.ReferenceCrankSpeed > 0)
            {
                foreach (EquipmentPlacementDef e in layout.InitialEquipment)
                    if (data.EquipmentItem(e.EquipmentId).SpeedControl == "crank") candidates.Add((e.EquipmentId, layout.ReferenceCrankSpeed));
            }
            else foreach (string id in data.BuildingTier(layout.Tier).ReferenceEquipment) candidates.Add((id, 0));

            ReferenceResult? last = null;
            foreach ((string id, double crank) in candidates)
            {
                BuildingState s = state.Clone();
                EquipmentDef eq = data.EquipmentItem(id);
                if (eq.Placement == "intake")
                    s.Equipment.Add(new EquipmentState { EquipmentId = id, OpeningId = intake.Id, On = true });
                else if (!PlaceInlineNearIntake(data, layout, s.Air, id)) continue;
                ReferenceResult r = Balance(data, layout, s, crank, trace);
                r.EquipmentId = id;
                r.CrankSpeed = crank;
                r.MaxPathMetres = maxPath;
                last = r;
                if (r.Solvable) return r;
            }
            if (last != null) return last;
            fail.Reason = "no tier equipment fits";
            return fail;
        }

        /// <summary>
        /// Routes the duct tree: a straight lead-in from the intake, the trunk toward the centroid of the outlets, then
        /// the outlet closest to the tree joins it next, until all are connected; dead ends are pruned. Null when an
        /// outlet cannot be reached.
        /// </summary>
        /// <param name="fluid">Network to route (air: from the intake with a lead-in; water: from the connection).</param>
        /// <param name="baseState">A state whose other networks are already routed (their ceiling use counts); null = fresh.</param>
        /// <param name="drill">Core drilling allowed (tier 2+): walls without doors may be crossed at a penalty.</param>
        static BuildingState? RouteTree(DataRepository data, BuildingLayout layout, EntryOpeningDef intake, string fluid, BuildingState? baseState,
            double approachPenalty, bool drill, out string reason)
        {
            reason = "";
            BuildingState state = baseState?.Clone() ?? BuildingState.CreateFor(layout);
            if (baseState == null) state.Equipment.Clear();
            bool water = fluid != "air";
            string material = water ? WaterMaterials(data, layout.Tier)[0] : data.Balancing.Drawing.DefaultMaterial;
            AutoRouterDef ar0 = data.Balancing.AutoRouter;
            double placeholder = SizesFor(data, layout.Tier, material)[0];
            int maxDegree = data.Balancing.Drawing.MaxNodeDegree;
            DuctNetworkState net = state.Net(fluid);
            TilePos intakePos = BuildingLayout.PosOf(intake);
            Func<TilePos, bool> blocked = Blocked(data, layout, state, fluid);
            var outlets = layout.Outlets.Where(o => BuildingState.OutletFluid(o.Type) == fluid).ToList();

            // Trunk toward the centroid of the outlets on the entry floor (upper floors join through the shafts).
            double cx = 0, cy = 0;
            int onFloor = 0;
            foreach (OutletPlacementDef o in outlets) if (o.Floor == intake.Floor) { cx += o.X; cy += o.Y; onFloor++; }
            if (onFloor == 0) foreach (OutletPlacementDef o in outlets) { cx += o.X; cy += o.Y; onFloor++; }
            cx /= Math.Max(1, onFloor);
            cy /= Math.Max(1, onFloor);
            // The air trunk starts with a straight lead-in from the facade (no branch joins it there), so the source
            // equipment and a trunk damper always fit before the first junction. Water starts with one tile.
            (int ix, int iy) = Inward(intake.Facing);
            var lead = new List<TilePos> { intakePos };
            var leadOcc = DuctEditor.Occupancy(net);
            int leadTiles = water ? 1 : ar0.LeadInTiles;
            for (int i = 0; i < leadTiles; i++)
            {
                TilePos end = lead[lead.Count - 1], next = new TilePos(end.X + ix, end.Y + iy, end.F);
                if (!layout.IsInside(next) || !layout.CanPass(end, next) || leadOcc.ContainsKey(next) || blocked(next)) break;
                lead.Add(next);
            }
            var reserved = new HashSet<TilePos>(lead);
            reserved.Remove(lead[lead.Count - 1]);
            List<TilePos>? rest = PathToTile(data, layout, net, lead.Count >= 2 ? lead[lead.Count - 1] : intakePos, cx, cy, reserved, blocked, drill);
            var trunk = new List<TilePos>(lead);
            if (rest != null) for (int i = 1; i < rest.Count; i++) trunk.Add(rest[i]);
            if (trunk.Count >= 2 && DuctEditor.Validate(net, layout, trunk, maxDegree) == DrawError.None)
                DuctEditor.PlaceRun(net, trunk, placeholder, material, false);

            // Branches: always connect the outlet closest to the tree next. Tier 1 searches from each outlet in turn (the
            // Phase 3 references, kept exactly); bigger buildings search from every remaining outlet at once.
            var remaining = new List<OutletPlacementDef>(outlets);
            while (remaining.Count > 0)
            {
                List<TilePos>? best = null;
                OutletPlacementDef? bestOutlet = null;
                double bestCost = double.MaxValue;
                if (layout.Tier <= 1)
                {
                    foreach (OutletPlacementDef o in remaining)
                    {
                        List<TilePos>? p = PathToNetwork(data, layout, net, new List<TilePos> { BuildingLayout.PosOf(o) }, maxDegree, reserved, approachPenalty, blocked, drill, bestCost, out double cost);
                        if (p != null && cost < bestCost) { bestCost = cost; best = p; bestOutlet = o; }
                    }
                }
                else
                {
                    var starts = new List<TilePos>();
                    foreach (OutletPlacementDef o in remaining) starts.Add(BuildingLayout.PosOf(o));
                    best = PathToNetwork(data, layout, net, starts, maxDegree, reserved, approachPenalty, blocked, drill, double.MaxValue, out bestCost);
                    // An outlet that cannot leave straight (a strict pass) joins with the soft preference instead.
                    if (best == null && double.IsInfinity(approachPenalty))
                        best = PathToNetwork(data, layout, net, starts, maxDegree, reserved, ar0.ShortApproachPenalty, blocked, drill, double.MaxValue, out bestCost);
                    if (best != null) bestOutlet = remaining.Find(o => BuildingLayout.PosOf(o) == best[0]);
                }
                if (best == null || bestOutlet == null)
                {
                    reason = $"cannot reach {remaining[0].Id}";
                    return null;
                }
                best.Reverse();
                DrawError err = DuctEditor.Validate(net, layout, best, maxDegree);
                if (err != DrawError.None)
                {
                    reason = $"branch to {bestOutlet.Id}: {err}";
                    return null;
                }
                DuctEditor.PlaceRun(net, best, placeholder, material, false);
                remaining.Remove(bestOutlet);
                // From tier 2 no later branch may tee in right beside a vent: its own straight stretch keeps room for a damper.
                if (!water && layout.Tier >= 2 && best.Count >= 3) reserved.Add(best[best.Count - 2]);
            }
            Prune(net);
            return state;
        }

        /// <summary>
        /// Tiles a network may not use: for water, the ceiling over an electrical room or the panel (<c>09-codes.md</c>); for
        /// any network, tiles the other networks have filled (ceiling capacity, assuming the smallest size).
        /// </summary>
        static Func<TilePos, bool> Blocked(DataRepository data, BuildingLayout layout, BuildingState state, string fluid)
        {
            var usage = new Dictionary<TilePos, int>();
            foreach (DuctNetworkState n in state.Networks)
                if (n.FluidId != fluid) DuctEditor.AddUsage(n, (net, s) => CapUnits(data, s), usage);
            int capacity = data.Balancing.Building.CeilingCapacity;
            TilePos? panel = layout.PanelTile;
            bool water = fluid != "air";
            return t => (usage.TryGetValue(t, out int u) && u + 1 > capacity)
                || (water && (layout.IsElectricalRoom(t) || (panel.HasValue && panel.Value == t)));
        }

        /// <summary>Ceiling capacity units of a segment (size, + insulation above its threshold).</summary>
        static int CapUnits(DataRepository data, DuctSegment s)
        {
            MaterialDef m = data.Material(s.MaterialId);
            int units = 1;
            foreach (PipeFamilyDef f in data.PipeFamilies)
                if (f.Kind == m.Kind)
                    foreach (PipeSizeDef size in f.Sizes) if (size.DiameterMm == s.DiameterMm) units = size.CapacityUnits;
            InsulationDef? ins = s.Insulation.Length > 0 ? data.FindInsulation(s.Insulation) : null;
            if (ins != null && s.DiameterMm > ins.CapacityAboveMm) units += ins.ExtraCapacityUnits;
            return units;
        }

        /// <summary>Water pipe materials the tier's reference may use, cheapest first.</summary>
        static List<string> WaterMaterials(DataRepository data, int tier)
        {
            List<string> list = data.BuildingTier(tier).ReferenceWaterMaterials;
            return list.Count > 0 ? list : new List<string> { "pvc" };
        }

        /// <summary>Fire dampers on every duct segment that crosses a fire wall, on the tile beside the crossing.</summary>
        static void PlaceFireDampers(DataRepository data, BuildingLayout layout, DuctNetworkState net)
        {
            if (layout.FireWalls.Count == 0) return;
            for (int guard = 0; guard < 64; guard++)
            {
                bool placed = false;
                foreach (DuctSegment s in net.Segments)
                {
                    List<TilePos> tiles = DuctEditor.Tiles(net, s);
                    for (int i = 1; i < tiles.Count && !placed; i++)
                    {
                        if (!layout.IsFireWall(tiles[i - 1], tiles[i])) continue;
                        TilePos? spot = i - 1 > 0 ? tiles[i - 1] : i < tiles.Count - 1 ? tiles[i] : (TilePos?)null;
                        if (spot == null || DuctEditor.ValidateInline(net, spot.Value) != DrawError.None) continue;
                        (int dx, int dy) = tiles[i - 1].DirectionTo(tiles[i]);
                        DuctEditor.PlaceInline(net, spot.Value, "fire-damper", dx, dy);
                        placed = true;
                    }
                    if (placed) break;
                }
                if (!placed) return;
            }
        }

        /// <summary>Duct sizes available to a tier, ascending, mm.</summary>
        public static List<double> SizesFor(DataRepository data, int tier, string materialId)
        {
            string kind = data.Material(materialId).Kind;
            var list = new List<double>();
            foreach (PipeFamilyDef fam in data.PipeFamilies)
                if (fam.Kind == kind)
                    foreach (PipeSizeDef s in fam.Sizes)
                        if (s.Tier <= tier) list.Add(s.DiameterMm);
            list.Sort();
            return list;
        }

        // ---------- path search ----------

        sealed class Search
        {
            public readonly Dictionary<(TilePos, int), double> Cost = new Dictionary<(TilePos, int), double>();
            public readonly Dictionary<(TilePos, int), (TilePos, int)> Prev = new Dictionary<(TilePos, int), (TilePos, int)>();
            readonly SortedSet<(double cost, int seq, TilePos t, int dir)> _open = new SortedSet<(double, int, TilePos, int)>(Comparer.Instance);
            int _seq;

            sealed class Comparer : IComparer<(double cost, int seq, TilePos t, int dir)>
            {
                public static readonly Comparer Instance = new Comparer();
                public int Compare((double cost, int seq, TilePos t, int dir) a, (double cost, int seq, TilePos t, int dir) b) =>
                    a.cost != b.cost ? a.cost.CompareTo(b.cost) : a.seq.CompareTo(b.seq);
            }

            public void Push(TilePos t, int dir, double cost, (TilePos, int)? prev)
            {
                if (Cost.TryGetValue((t, dir), out double old) && old <= cost) return;
                Cost[(t, dir)] = cost;
                if (prev.HasValue) Prev[(t, dir)] = prev.Value;
                _open.Add((cost, _seq++, t, dir));
            }

            public bool Pop(out TilePos t, out int dir, out double cost)
            {
                while (_open.Count > 0)
                {
                    var top = _open.Min;
                    _open.Remove(top);
                    if (Cost[(top.t, top.dir)] < top.cost) continue;
                    t = top.t;
                    dir = top.dir;
                    cost = top.cost;
                    return true;
                }
                t = default;
                dir = -1;
                cost = 0;
                return false;
            }

            public List<TilePos> Back(TilePos t, int dir)
            {
                var path = new List<TilePos> { t };
                var key = (t, dir);
                while (Prev.TryGetValue(key, out var p))
                {
                    path.Add(p.Item1);
                    key = p;
                }
                path.Reverse();
                return path;
            }
        }

        static double StepCost(DataRepository data, int fromDir, int dir) =>
            (dir >= 4 ? data.Balancing.Building.FloorHeight : 1) + (fromDir >= 0 && fromDir % DirCount != dir ? data.Balancing.AutoRouter.TurnPenalty : 0);

        /// <summary>Extra cost of a step through a wall without a door (core drilling, tier 2+); impossible in a doors-only pass.</summary>
        static double WallCost(DataRepository data, BuildingLayout layout, TilePos u, TilePos v, bool drill) =>
            layout.NeedsCoreDrill(u, v) ? (drill ? data.Balancing.AutoRouter.CoreDrillPenalty : double.PositiveInfinity) : 0;

        /// <summary>Unit step from a facade opening into the building.</summary>
        static (int dx, int dy) Inward(string facing) => facing switch
        {
            "west" => (1, 0),
            "east" => (-1, 0),
            "south" => (0, 1),
            _ => (0, -1),
        };

        /// <summary>Path over free tiles (not <paramref name="avoid"/>) from <paramref name="start"/> to the free tile nearest (cx, cy).</summary>
        static List<TilePos>? PathToTile(DataRepository data, BuildingLayout layout, DuctNetworkState net, TilePos start, double cx, double cy, HashSet<TilePos> avoid,
            Func<TilePos, bool> blocked, bool drill)
        {
            Dictionary<TilePos, TileOccupant> occ = DuctEditor.Occupancy(net);
            TilePos? target = null;
            double best = double.MaxValue;
            for (int x = 0; x < layout.Width; x++)
                for (int y = 0; y < layout.Height; y++)
                {
                    var t = new TilePos(x, y, start.F);
                    if (!layout.PipesAllowed(t) || occ.ContainsKey(t) || avoid.Contains(t) || blocked(t)) continue;
                    double d = (x - cx) * (x - cx) + (y - cy) * (y - cy);
                    if (d < best) { best = d; target = t; }
                }
            if (target == null || target.Value == start) return null;
            var s = new Search();
            s.Push(start, -1, 0, null);
            while (s.Pop(out TilePos u, out int ud, out double uc))
            {
                if (u == target.Value) return s.Back(u, ud);
                for (int d = 0; d < DirCount; d++)
                {
                    TilePos v = Step(u, d);
                    if (!layout.IsInside(v) || !layout.CanPass(u, v) || occ.ContainsKey(v) || avoid.Contains(v) || blocked(v)) continue;
                    double wall = WallCost(data, layout, u, v, drill);
                    if (double.IsInfinity(wall)) continue;
                    s.Push(v, d, uc + StepCost(data, ud, d) + wall, (u, ud));
                }
            }
            return null;
        }

        /// <summary>
        /// Cheapest path from an outlet tile over free tiles to a tile where it can join the network: a free node
        /// with room for another duct, the bare intake, or the middle of a segment entered perpendicularly (a tee).
        /// Paths that do not leave the outlet with two straight tiles cost <paramref name="penalty"/> tiles
        /// extra, so most branches keep a straight duct before the outlet where a damper fits.
        /// </summary>
        /// <param name="starts">Outlet tiles searched from at once (the path found starts at one of them).</param>
        /// <param name="bound">Only paths cheaper than this are of interest (the best found so far).</param>
        static List<TilePos>? PathToNetwork(DataRepository data, BuildingLayout layout, DuctNetworkState net, List<TilePos> starts, int maxDegree,
            HashSet<TilePos> reserved, double penalty, Func<TilePos, bool> blocked, bool drill, double bound, out double cost)
        {
            cost = bound;
            Dictionary<TilePos, TileOccupant> occ = DuctEditor.Occupancy(net);
            var s = new Search();
            // State key: direction (0–5, −1 at the start) + 6 × phase (0 start, 1 one step out, 2 two straight steps or more).
            foreach (TilePos start in starts) s.Push(start, -1, 0, null);
            List<TilePos>? found = null;
            while (s.Pop(out TilePos u, out int key, out double uc))
            {
                if (uc >= cost) break;
                int ud = key < 0 ? -1 : key % DirCount, phase = key < 0 ? 0 : key / DirCount;
                for (int d = 0; d < DirCount; d++)
                {
                    if (ud >= 0 && d == (ud ^ 1)) continue;  // no turning back
                    TilePos v = Step(u, d);
                    if (!layout.IsInside(v) || !layout.CanPass(u, v) || blocked(v)) continue;
                    double wall = WallCost(data, layout, u, v, drill);
                    if (double.IsInfinity(wall)) continue;
                    double vc = uc + StepCost(data, ud, d) + wall;
                    int nextPhase = phase == 0 ? 1 : 2;
                    if (phase == 1 && d != ud) vc += penalty;
                    if (double.IsInfinity(vc)) continue;
                    if (occ.TryGetValue(v, out TileOccupant o))
                    {
                        if (phase == 0) vc += penalty;
                        if (double.IsInfinity(vc)) continue;
                        if (reserved.Contains(v) || !CanJoin(net, o, u, v, maxDegree) || vc >= cost) continue;
                        List<TilePos> path = s.Back(u, key);
                        path.Add(v);
                        if (new HashSet<TilePos>(path).Count != path.Count) continue;
                        cost = vc;
                        found = path;
                        continue;
                    }
                    s.Push(v, d + DirCount * nextPhase, vc, (u, key));
                }
            }
            return found;
        }

        static bool CanJoin(DuctNetworkState net, TileOccupant o, TilePos from, TilePos to, int maxDegree)
        {
            if (o.NodeId != 0)
            {
                DuctNode n = net.FindNode(o.NodeId)!;
                int deg = net.Degree(n.Id);
                return (n.Kind == DuctNodeKind.Free && deg >= 1 && deg < maxDegree) || (n.Kind == DuctNodeKind.Intake && deg == 0);
            }
            if (maxDegree < 3) return false;
            DuctSegment seg = net.FindSegment(o.SegmentId)!;
            TilePos a = net.FindNode(seg.NodeA)!.Pos, b = net.FindNode(seg.NodeB)!.Pos;
            return DuctEditor.Axis(a, b) != DuctEditor.Axis(from, to);
        }

        /// <summary>Removes dead-end ducts (free ends that reach no outlet), repeatedly.</summary>
        static void Prune(DuctNetworkState net)
        {
            while (true)
            {
                DuctNode? end = net.Nodes.Find(n => n.Kind == DuctNodeKind.Free && net.Degree(n.Id) == 1);
                if (end == null) return;
                DuctSegment seg = net.Segments.Find(s => s.NodeA == end.Id || s.NodeB == end.Id)!;
                DuctEditor.DeleteSegment(net, seg.Id);
            }
        }

        // ---------- tree analysis ----------

        /// <summary>The duct tree rooted at the intake: per segment the outlets it feeds and their ideal flow.</summary>
        sealed class Tree
        {
            /// <summary>Segment id → design flow (sum of downstream ideal flows), m³/s.</summary>
            public readonly Dictionary<int, double> Flow = new Dictionary<int, double>();
            /// <summary>Segment id → number of outlets downstream.</summary>
            public readonly Dictionary<int, int> Outlets = new Dictionary<int, int>();
            /// <summary>Segment id → the outlet ids downstream.</summary>
            public readonly Dictionary<int, List<string>> Served = new Dictionary<int, List<string>>();
            /// <summary>Segment id → node id at its upstream end.</summary>
            public readonly Dictionary<int, int> Upstream = new Dictionary<int, int>();
            /// <summary>Outlet id → duct path length from the intake, m.</summary>
            public readonly Dictionary<string, double> PathMetres = new Dictionary<string, double>();

            public static Tree Of(DuctNetworkState net, BuildingLayout layout, DataRepository data)
            {
                var t = new Tree();
                DuctNode? root = net.Nodes.Find(n => n.Kind == DuctNodeKind.Intake);
                if (root == null) return t;
                var order = new List<int>();
                var parentSeg = new Dictionary<int, int>();
                var dist = new Dictionary<int, double> { [root.Id] = 0 };
                var stack = new Stack<int>();
                stack.Push(root.Id);
                while (stack.Count > 0)
                {
                    int u = stack.Pop();
                    order.Add(u);
                    foreach (DuctSegment s in net.Segments)
                    {
                        if (s.NodeA != u && s.NodeB != u) continue;
                        int v = s.Other(u);
                        if (dist.ContainsKey(v)) continue;
                        DuctNode nu = net.FindNode(u)!, nv = net.FindNode(v)!;
                        dist[v] = dist[u] + nu.Pos.LengthTo(nv.Pos, data.Balancing.Building.FloorHeight);
                        parentSeg[v] = s.Id;
                        t.Upstream[s.Id] = u;
                        stack.Push(v);
                    }
                }
                for (int i = order.Count - 1; i >= 0; i--)
                {
                    int v = order[i];
                    if (!parentSeg.TryGetValue(v, out int seg)) continue;
                    DuctNode node = net.FindNode(v)!;
                    var served = t.Served.TryGetValue(seg, out var l) ? l : t.Served[seg] = new List<string>();
                    if (node.Kind == DuctNodeKind.Outlet)
                    {
                        served.Add(node.AnchorId);
                        t.PathMetres[node.AnchorId] = dist[v];
                    }
                    foreach (DuctSegment s in net.Segments)
                        if (t.Upstream.TryGetValue(s.Id, out int up) && up == v && t.Served.TryGetValue(s.Id, out var below)) served.AddRange(below);
                }
                foreach (var kv in t.Served)
                {
                    double q = 0;
                    foreach (string id in kv.Value)
                    {
                        OutletPlacementDef o = layout.FindOutlet(id)!;
                        q += BuildingState.OutletFluid(o.Type) == "air" ? BuildingEconomy.Requirement(data, layout, o).Ideal : NetworkCompiler.DesignFlow(data, layout, o);
                    }
                    t.Flow[kv.Key] = q;
                    t.Outlets[kv.Key] = kv.Value.Count;
                }
                return t;
            }
        }

        /// <summary>
        /// The first straight duct tile downstream of the intake and before the first junction, past any inline parts
        /// already there; its direction points away from the intake.
        /// </summary>
        static bool FirstStraightTile(DataRepository data, BuildingLayout layout, DuctNetworkState net, out TilePos tile, out (int dx, int dy) dir)
        {
            tile = default;
            dir = default;
            Tree tree = Tree.Of(net, layout, data);
            DuctNode? node = net.Nodes.Find(n => n.Kind == DuctNodeKind.Intake);
            for (int hop = 0; node != null && hop < 256; hop++)
            {
                DuctSegment? next = null;
                int children = 0;
                foreach (DuctSegment x in net.Segments)
                {
                    if (!tree.Upstream.TryGetValue(x.Id, out int up) || up != node.Id) continue;
                    children++;
                    next = x;
                }
                // Only before the first junction: equipment after a tee would draw air back out of the branch.
                if (next == null || children > 1) return false;
                TilePos a = node.Pos, b = net.FindNode(next.Other(node.Id))!.Pos;
                if (a.ManhattanTo(b) >= 2)
                {
                    dir = a.DirectionTo(b);
                    tile = new TilePos(a.X + dir.dx, a.Y + dir.dy, a.F);
                    return true;
                }
                node = net.FindNode(next.Other(node.Id));
            }
            return false;
        }

        /// <summary>An inline fan on the first straight tile after the intake, blowing away from it.</summary>
        static bool PlaceInlineNearIntake(DataRepository data, BuildingLayout layout, DuctNetworkState net, string partId)
        {
            if (!FirstStraightTile(data, layout, net, out TilePos tile, out var dir)) return false;
            DuctEditor.PlaceInline(net, tile, partId, dir.dx, dir.dy);
            return true;
        }

        // ---------- equipment check and damper balancing ----------

        /// <summary>Solves a state at the default air temperature and evaluates it.</summary>
        public static BuildingEvaluation Evaluate(DataRepository data, BuildingLayout layout, BuildingState s, double crank) =>
            Solve(data, layout, s, crank).ev;

        static (BuildingEvaluation ev, CompiledNetwork c, SolveResult res) Solve(DataRepository data, BuildingLayout layout, BuildingState s, double crank)
        {
            var air = new TableFluid(data.Fluid("air"), data.FluidTables["air"], data.Balancing.Physics.GasConstant);
            CompiledNetwork c = NetworkCompiler.Compile(layout, s, data, HeadLoss.Core.Units.CToK(data.Balancing.Physics.DefaultAirTemperatureC), crank);
            SolveResult res = new NetworkSolver(SolverSettings.FromBalancing(data.Balancing)).Solve(c.Graph, air);
            return (BuildingEconomy.Evaluate(data, layout, new[] { new FluidSolve(c, res) }, "air"), c, res);
        }

        /// <summary>
        /// Evaluates the equipment choice and balances it the way an HVAC engineer does (proportional balancing).
        /// Each outlet is measured against its target in the overdelivery band (<c>autoRouter.balanceTarget</c>).
        /// The <em>index</em> outlets (those that cannot have their own damper, plus the weakest one) set the level:
        /// a trunk damper after the source scales every flow so they land in their bands. Every other outlet above its
        /// target gets a damper on its own branch, trimmed down to the target. While the index outlets are still short
        /// with the trunk fully open, the fastest duct on the weakest outlet's path grows one size (the velocity limits
        /// are upper bounds, not exact sizes).
        /// </summary>
        static ReferenceResult Balance(DataRepository data, BuildingLayout layout, BuildingState s, double crank, List<string>? trace)
        {
            AutoRouterDef ar = data.Balancing.AutoRouter;
            FittingDef damper = data.Fitting("damper");
            var dampers = new Dictionary<string, TilePos>();
            TilePos? trunk = null;
            (BuildingEvaluation ev, CompiledNetwork c, SolveResult res) = Solve(data, layout, s, crank);
            List<double> sizes = SizesFor(data, layout.Tier, data.Balancing.Drawing.DefaultMaterial);
            const double gain = 0.8;
            // A variable-speed source (built in, or a drive fitted where the tier has drives) sets the level by speed
            // instead of throttling: far less electricity. A trunk damper is the fallback below the minimum speed.
            Func<double>? getSpeed = null;
            Action<double>? setSpeed = null;
            EquipmentDef? drive = null;
            foreach (EquipmentDef d in data.Equipment) if (d.Placement == "addon" && d.FitsCategories.Contains("air-mover")) drive = d;
            bool driveOk = drive != null && layout.Tier >= drive.Tier && crank <= 0;
            foreach (EquipmentState e in s.Equipment)
            {
                string control = data.EquipmentItem(e.EquipmentId).SpeedControl;
                if (control != "variable" && !(control == "on-off" && driveOk)) continue;
                e.Vsd = control == "on-off";
                EquipmentState fan = e;
                getSpeed = () => fan.Speed;
                setSpeed = v => fan.Speed = v;
            }
            foreach (DuctNode n in s.Air.Nodes)
            {
                if (getSpeed != null || n.Kind != DuctNodeKind.Inline || data.FindEquipment(n.PartId) is not EquipmentDef inl) continue;
                if (inl.SpeedControl != "variable" && !(inl.SpeedControl == "on-off" && driveOk)) continue;
                n.Vsd = inl.SpeedControl == "on-off";
                DuctNode fan = n;
                getSpeed = () => fan.Speed;
                setSpeed = v => fan.Speed = v;
            }
            if (getSpeed != null) (ev, c, res) = Solve(data, layout, s, crank);
            for (int pass = 0; pass < ar.BalancingPasses; pass++)
            {
                if (trace != null)
                {
                    var line = new System.Text.StringBuilder($"pass {pass}:");
                    foreach (OutletStatus o in ev.Outlets) line.Append($" {o.OutletId}={Units.M3sToM3h(o.Delivery):0}/{Units.M3sToM3h(o.Requirement.Min):0}");
                    if (trunk.HasValue) line.Append($" trunk θ={s.Air.NodeAt(trunk.Value)!.Opening:0.00}");
                    foreach (var kv in dampers) line.Append($" d[{kv.Key}]={s.Air.NodeAt(kv.Value)!.Opening:0.00}");
                    trace.Add(line.ToString());
                }
                if (ev.Online && ev.AboveCodeMax == 0) break;
                bool changed = false;

                OutletStatus? weakest = null;
                foreach (OutletStatus o in ev.Outlets)
                    if (weakest == null || Ratio(o, ar) < Ratio(weakest, ar)) weakest = o;
                if (weakest == null) break;

                // Dampers on demand: an outlet above its target (other than the weakest) gets one on its branch. From tier 2,
                // an outlet whose branch is too short shares one with its neighbours on the nearest upstream duct that only
                // feeds outlets above their targets.
                foreach (OutletStatus o in ev.Outlets)
                {
                    if (o == weakest || Ratio(o, ar) <= 1 || dampers.ContainsKey(o.OutletId)) continue;
                    if (PlaceBranchDamper(data, layout, s, o.OutletId, out TilePos tile))
                    {
                        dampers[o.OutletId] = tile;
                        changed = true;
                    }
                    else if (layout.Tier >= 2 && PlaceGroupDamper(data, layout, s, o.OutletId, ev, ar, dampers, weakest, out List<string> group, out tile))
                    {
                        foreach (string id in group) dampers[id] = tile;
                        changed = true;
                    }
                }

                // Level from the index outlets: the scale that puts them nearest their targets inside their bands.
                double lo = 0, hi = double.MaxValue, logSum = 0;
                int count = 0;
                foreach (OutletStatus o in ev.Outlets)
                {
                    if (o != weakest && dampers.ContainsKey(o.OutletId)) continue;
                    double q = Math.Max(o.Delivery, 1e-9);
                    lo = Math.Max(lo, o.Requirement.Min / q);
                    if (o.Requirement.HasMax) hi = Math.Min(hi, o.Requirement.Max / q);
                    logSum += Math.Log(Target(o.Requirement, ar) / q);
                    count++;
                }
                double scale = Math.Exp(logSum / Math.Max(1, count));
                scale = lo <= hi ? Math.Max(lo, Math.Min(hi, scale)) : Math.Sqrt(lo * hi);
                bool trunkOpen = true, speedAtMinimum = false;
                if (getSpeed != null)
                {
                    double speed = getSpeed();
                    double next = Math.Round(Math.Max(0.2, Math.Min(1, speed * Math.Pow(scale, gain))) * 100) / 100;
                    if (Math.Abs(next - speed) > 1e-9) { setSpeed!(next); changed = true; }
                    trunkOpen = next >= 1;
                    speedAtMinimum = next <= 0.2 + 1e-9;
                }
                if (scale < 0.97 && trunk == null && (getSpeed == null || speedAtMinimum) && FirstStraightTile(data, layout, s.Air, out TilePos tt, out var td))
                {
                    DuctEditor.PlaceInline(s.Air, tt, "damper", td.dx, td.dy);
                    trunk = tt;
                    changed = true;
                }
                if (trunk.HasValue)
                {
                    DuctNode tn = s.Air.NodeAt(trunk.Value)!;
                    changed |= Trim(tn, Math.Pow(scale, gain), damper.MinOpening);
                    trunkOpen &= tn.Opening >= 1;
                }

                // Branch dampers bring their outlets to target (the weakest one's opens fully); a shared damper follows
                // the outlet nearest its target, so none of its outlets drops below.
                bool weakestOpen = true;
                var byTile = new Dictionary<TilePos, double>();
                var order = new List<TilePos>();
                foreach (var kv in dampers)
                {
                    double ratio = Ratio(ev.Find(kv.Key)!, ar);
                    if (byTile.TryGetValue(kv.Value, out double r0)) byTile[kv.Value] = Math.Min(r0, ratio);
                    else { byTile[kv.Value] = ratio; order.Add(kv.Value); }
                }
                foreach (TilePos t in order)
                {
                    DuctNode n = s.Air.NodeAt(t)!;
                    changed |= Trim(n, Math.Pow(1 / Math.Max(byTile[t], 1e-6), gain), damper.MinOpening);
                    if (dampers.TryGetValue(weakest.OutletId, out TilePos wt) && wt == t) weakestOpen = n.Opening >= 1;
                }

                if (ev.BelowMinimum > 0 && scale > 1 && trunkOpen && weakestOpen)
                {
                    bool grown = UpsizeWorstPath(data, layout, s, ev, c, res, sizes);
                    if (trace != null) trace.Add(grown ? "  upsized" : "  nothing left to upsize");
                    changed |= grown;
                }
                if (!changed) break;
                (ev, c, res) = Solve(data, layout, s, crank);
            }
            var r = new ReferenceResult
            {
                State = s,
                Evaluation = ev,
                Cost = Pricing.Value(data, layout, s),
                Dampers = dampers.Count + (trunk.HasValue ? 1 : 0),
                Solvable = ev.Online && ev.AboveCodeMax == 0,
            };
            if (!r.Solvable) r.Reason = ev.BelowMinimum > 0 ? $"{ev.BelowMinimum} outlets below minimum" : $"{ev.AboveCodeMax} outlets above code max";
            return r;
        }

        /// <summary>Multiplies a damper's opening by a factor (clamped to 0.5–2 per step and to its range). True when it moved.</summary>
        static bool Trim(DuctNode n, double factor, double minOpening)
        {
            double theta = Math.Max(minOpening, Math.Min(1, n.Opening * Math.Max(0.5, Math.Min(2, factor))));
            if (Math.Abs(theta - n.Opening) <= 1e-4) return false;
            n.Opening = theta;
            return true;
        }

        /// <summary>Balancing target inside the overdelivery band: ideal + balanceTarget × (max − ideal).</summary>
        static double Target(OutletRequirement req, AutoRouterDef ar) =>
            req.HasMax ? req.Ideal + ar.BalanceTarget * (req.Max - req.Ideal) : req.Ideal;

        static double Ratio(OutletStatus o, AutoRouterDef ar) => o.Delivery / Target(o.Requirement, ar);

        /// <summary>Grows the fastest duct on the path to the outlet furthest below its minimum by one size.</summary>
        static bool UpsizeWorstPath(DataRepository data, BuildingLayout layout, BuildingState s, BuildingEvaluation ev,
            CompiledNetwork c, SolveResult res, List<double> sizes)
        {
            OutletStatus? worst = null;
            foreach (OutletStatus o in ev.Outlets)
                if (worst == null || o.Delivery / o.Requirement.Min < worst.Delivery / worst.Requirement.Min) worst = o;
            if (worst == null) return false;
            DuctNetworkState net = s.Air;
            Tree tree = Tree.Of(net, layout, data);
            DuctNode? node = net.Nodes.Find(n => n.Kind == DuctNodeKind.Outlet && n.AnchorId == worst.OutletId);
            DuctSegment? fastest = null;
            double vMax = -1;
            for (int hop = 0; node != null && hop < 256; hop++)
            {
                DuctSegment? seg = null;
                foreach (DuctSegment x in net.Segments)
                    if (tree.Upstream.TryGetValue(x.Id, out int up) && x.Other(up) == node.Id) seg = x;
                if (seg == null) break;
                double v = c.SegmentLink.TryGetValue(seg.Id, out int link) ? Math.Abs(res.Velocity[link]) : 0;
                if (seg.DiameterMm < sizes[sizes.Count - 1] && v > vMax) { vMax = v; fastest = seg; }
                node = net.FindNode(tree.Upstream[seg.Id]);
            }
            if (fastest == null) return false;
            fastest.DiameterMm = sizes[sizes.IndexOf(fastest.DiameterMm) + 1];
            DuctEditor.Normalize(net);
            return true;
        }

        // ---------- water ----------

        /// <summary>
        /// The water reference on top of the air one: a tree from the connection to every tap (risers through shafts,
        /// never over the electrics), each pipe the smallest size within the water velocity limit at the taps' design
        /// flow, in the cheapest reference material. Mains pressure alone is tried first, then the tier's pumps. Pipes
        /// above their rating move to the next material; taps above their code maximum get a regulator on the trunk set
        /// to bring the strongest one to its target; taps below their minimum grow the fastest pipe on their path.
        /// </summary>
        static ReferenceResult BuildWater(DataRepository data, BuildingLayout layout, ReferenceResult air, List<string>? trace)
        {
            EntryOpeningDef connection = layout.OpeningFor("water")!;
            BuildingState? routed = RouteTree(data, layout, connection, "water", air.State, 0, false, out string reason)
                ?? RouteTree(data, layout, connection, "water", air.State, 0, true, out reason);
            var result = new ReferenceResult { State = air.State, Evaluation = air.Evaluation, Cost = air.Cost, EquipmentId = air.EquipmentId,
                CrankSpeed = air.CrankSpeed, Dampers = air.Dampers, MaxPathMetres = air.MaxPathMetres };
            if (routed == null)
            {
                result.Reason = "water: " + reason;
                return result;
            }
            DuctNetworkState net = routed.Water;
            List<string> materials = WaterMaterials(data, layout.Tier);
            Tree tree = Tree.Of(net, layout, data);
            foreach (DuctSegment s in net.Segments)
            {
                s.MaterialId = materials[0];
                s.DiameterMm = WaterSize(data, layout.Tier, s.MaterialId, tree.Flow.TryGetValue(s.Id, out double q) ? q : 0);
            }
            DuctEditor.Normalize(net);
            if (layout.Tier >= data.Fitting("elbow-90-long-radius").Tier)
                foreach (DuctNode node in net.Nodes)
                    if (DuctEditor.IsElbow(net, node)) node.SmoothFittings = true;

            var candidates = new List<string> { "" };
            candidates.AddRange(data.BuildingTier(layout.Tier).ReferenceWaterEquipment);
            ReferenceResult? last = null;
            foreach (string pump in candidates)
            {
                BuildingState s = routed.Clone();
                if (pump.Length > 0)
                {
                    EquipmentDef def = data.EquipmentItem(pump);
                    if (EquipmentFits.Footprint(data, layout, s, connection, def) == null) continue;
                    s.Equipment.Add(new EquipmentState { EquipmentId = pump, OpeningId = connection.Id, On = true });
                }
                ReferenceResult r = BalanceWater(data, layout, s, materials, trace);
                r.EquipmentId = air.EquipmentId;
                r.CrankSpeed = air.CrankSpeed;
                r.Dampers = air.Dampers;
                r.MaxPathMetres = air.MaxPathMetres;
                r.WaterEquipmentId = pump;
                last = r;
                if (r.Solvable) return r;
            }
            return last ?? result;
        }

        /// <summary>Smallest pipe of a tier whose internal velocity stays within the water limit at a flow, mm.</summary>
        static double WaterSize(DataRepository data, int tier, string material, double flow)
        {
            List<double> sizes = SizesFor(data, tier, material);
            MaterialDef m = data.Material(material);
            foreach (double mm in sizes)
            {
                double d = HeadLoss.Core.Units.MmToM(m.InternalDiameterMm(mm));
                if (flow / (Math.PI * d * d / 4) <= data.Balancing.AutoRouter.WaterVelocity) return mm;
            }
            return sizes[sizes.Count - 1];
        }

        /// <summary>Solves both networks at the reference conditions (air at the default temperature, water at the reference connection pressure).</summary>
        static (BuildingEvaluation ev, NetworkSnapshot snap) SolveAll(DataRepository data, BuildingLayout layout, BuildingState s, double crank)
        {
            var settings = SolverSettings.FromBalancing(data.Balancing);
            var options = new CompileOptions
            {
                AirTemperatureK = HeadLoss.Core.Units.CToK(data.Balancing.Physics.DefaultAirTemperatureC),
                CrankSpeed = crank,
                WaterConnected = true,
                WaterPressurePa = BuildingSession.ReferenceWaterPressure(data),
            };
            var airProps = new TableFluid(data.Fluid("air"), data.FluidTables["air"], data.Balancing.Physics.GasConstant);
            var waterProps = new TableFluid(data.Fluid("water"), data.FluidTables["water"], data.Balancing.Physics.GasConstant);
            CompiledNetwork ca = NetworkCompiler.Compile(layout, s, data, "air", options);
            CompiledNetwork cw = NetworkCompiler.Compile(layout, s, data, "water", options);
            var snap = new NetworkSnapshot(new FluidSolve(ca, new NetworkSolver(settings).Solve(ca.Graph, airProps)),
                new FluidSolve(cw, new NetworkSolver(settings).Solve(cw.Graph, waterProps)), s);
            return (BuildingEconomy.Evaluate(data, layout, snap), snap);
        }

        /// <summary>Highest pressure along each water segment over its rating (x = p_max / p_rated), and the worst one.</summary>
        public static double WorstRatingRatio(DataRepository data, DuctNetworkState net, FluidSolve w, out DuctSegment? worst)
        {
            worst = null;
            double max = 0;
            foreach (DuctSegment s in net.Segments)
            {
                if (!w.Compiled.SegmentLink.TryGetValue(s.Id, out int link) || !w.Result.LinkActive[link]) continue;
                NetworkLink l = w.Compiled.Graph.Links[link];
                double p = Math.Max(w.Result.NodePressure[l.From], w.Result.NodePressure[l.To]);
                double x = p / data.Material(s.MaterialId).RatedPressure(s.DiameterMm);
                if (x > max) { max = x; worst = s; }
            }
            return max;
        }

        static ReferenceResult BalanceWater(DataRepository data, BuildingLayout layout, BuildingState s, List<string> materials, List<string>? trace)
        {
            AutoRouterDef ar = data.Balancing.AutoRouter;
            DuctNetworkState net = s.Water;
            FittingDef prvDef = data.Fitting("pressure-reducing-valve");
            TilePos? prv = null;
            (BuildingEvaluation ev, NetworkSnapshot snap) = SolveAll(data, layout, s, 0);
            for (int pass = 0; pass < ar.BalancingPasses; pass++)
            {
                FluidSolve w = snap.Water!;
                var taps = ev.Outlets.Where(o => o.Def.Fluid == "water").ToList();
                double ratio = WorstRatingRatio(data, net, w, out DuctSegment? over);
                trace?.Add($"water pass {pass}: " + string.Join(" ", taps.Select(t => $"{t.OutletId}={t.Delivery / 1e5:0.00}bar")) + $" rating x={ratio:0.00}");
                bool tapsOk = taps.TrueForAll(t => t.Zone != OutletZone.BelowMinimum && t.Zone != OutletZone.NotConnected && t.Zone != OutletZone.AboveCodeMax);
                if (tapsOk && ratio <= 1) break;
                bool changed = false;
                if (over != null && ratio > 1)
                {
                    int i = materials.IndexOf(over.MaterialId);
                    if (i >= 0 && i + 1 < materials.Count && layout.Tier >= data.Material(materials[i + 1]).Tier)
                    {
                        DuctEditor.SetSpec(net, new[] { over.Id }, materials[i + 1], null, null);
                        changed = true;
                    }
                }
                OutletStatus? high = null;
                foreach (OutletStatus t in taps)
                    if (t.Zone == OutletZone.AboveCodeMax && (high == null || t.Delivery - t.Requirement.Max > high.Delivery - high.Requirement.Max)) high = t;
                if (!changed && high != null)
                {
                    if (prv == null && FirstStraightTile(data, layout, net, out TilePos tile, out var dir))
                    {
                        DuctEditor.PlaceInline(net, tile, prvDef.Id, dir.dx, dir.dy);
                        prv = tile;
                        net.NodeAt(tile)!.Setting = prvDef.DefaultSetting;
                        changed = true;
                    }
                    else if (prv != null)
                    {
                        DuctNode n = net.NodeAt(prv.Value)!;
                        double target = Target(high.Requirement, ar);
                        double next = Commands.SetValveSettingCommand.Snap(prvDef, n.Setting - (high.Delivery - target));
                        if (Math.Abs(next - n.Setting) > 1) { n.Setting = next; changed = true; }
                    }
                }
                OutletStatus? low = null;
                foreach (OutletStatus t in taps)
                    if ((t.Zone == OutletZone.BelowMinimum || t.Zone == OutletZone.NotConnected) && (low == null || t.Delivery < low.Delivery)) low = t;
                if (!changed && low != null)
                {
                    if (prv != null && net.NodeAt(prv.Value)!.Setting < prvDef.SettingMax)
                    {
                        DuctNode n = net.NodeAt(prv.Value)!;
                        n.Setting = Commands.SetValveSettingCommand.Snap(prvDef, n.Setting + (low.Requirement.Min - low.Delivery) + 0.2e5);
                        changed = true;
                    }
                    else changed = UpsizeWaterPath(data, layout, s, low.OutletId, w);
                }
                if (!changed) break;
                (ev, snap) = SolveAll(data, layout, s, 0);
            }
            double x = WorstRatingRatio(data, net, snap.Water!, out _);
            var r = new ReferenceResult
            {
                State = s,
                Evaluation = ev,
                Cost = Pricing.Value(data, layout, s),
                Solvable = ev.Online && ev.AboveCodeMax == 0 && x <= 1,
            };
            if (!r.Solvable)
                r.Reason = ev.BelowMinimum > 0 ? $"{ev.BelowMinimum} outlets below minimum" : ev.AboveCodeMax > 0 ? $"{ev.AboveCodeMax} outlets above code max" : $"pipe above rating (x {x:0.00})";
            return r;
        }

        /// <summary>Grows the fastest water pipe on the path to a tap by one size.</summary>
        static bool UpsizeWaterPath(DataRepository data, BuildingLayout layout, BuildingState s, string outletId, FluidSolve w)
        {
            DuctNetworkState net = s.Water;
            Tree tree = Tree.Of(net, layout, data);
            DuctNode? node = net.Nodes.Find(n => n.Kind == DuctNodeKind.Outlet && n.AnchorId == outletId);
            DuctSegment? fastest = null;
            double vMax = -1;
            for (int hop = 0; node != null && hop < 256; hop++)
            {
                DuctSegment? seg = null;
                foreach (DuctSegment x in net.Segments)
                    if (tree.Upstream.TryGetValue(x.Id, out int up) && x.Other(up) == node.Id) seg = x;
                if (seg == null) break;
                List<double> sizes = SizesFor(data, layout.Tier, seg.MaterialId);
                double v = w.Compiled.SegmentLink.TryGetValue(seg.Id, out int link) ? Math.Abs(w.Result.Velocity[link]) : 0;
                if (seg.DiameterMm < sizes[sizes.Count - 1] && v > vMax) { vMax = v; fastest = seg; }
                node = net.FindNode(tree.Upstream[seg.Id]);
            }
            if (fastest == null) return false;
            List<double> all = SizesFor(data, layout.Tier, fastest.MaterialId);
            fastest.DiameterMm = all[all.IndexOf(fastest.DiameterMm) + 1];
            DuctEditor.Normalize(net);
            return true;
        }

        /// <summary>
        /// A damper shared by several outlets: walking up from the outlet, the first straight duct tile on a segment whose
        /// outlets are all above their targets and have no damper yet (never the weakest outlet's path).
        /// </summary>
        static bool PlaceGroupDamper(DataRepository data, BuildingLayout layout, BuildingState s, string outletId, BuildingEvaluation ev, AutoRouterDef ar,
            Dictionary<string, TilePos> dampers, OutletStatus weakest, out List<string> group, out TilePos tile)
        {
            tile = default;
            group = new List<string>();
            DuctNetworkState net = s.Air;
            Tree tree = Tree.Of(net, layout, data);
            DuctNode? outlet = net.Nodes.Find(n => n.Kind == DuctNodeKind.Outlet && n.AnchorId == outletId);
            if (outlet == null) return false;
            int node = outlet.Id;
            for (int hop = 0; hop < 64; hop++)
            {
                DuctSegment? seg = null;
                foreach (DuctSegment x in net.Segments)
                    if (tree.Upstream.TryGetValue(x.Id, out int up) && x.Other(up) == node) seg = x;
                if (seg == null || !tree.Served.TryGetValue(seg.Id, out List<string>? served)) return false;
                foreach (string id in served)
                {
                    OutletStatus? o = ev.Find(id);
                    if (o == null || o == weakest || Ratio(o, ar) <= 1 || dampers.ContainsKey(id)) return false;
                }
                int upNode = tree.Upstream[seg.Id];
                TilePos down = net.FindNode(node)!.Pos, upPos = net.FindNode(upNode)!.Pos;
                if (down.F == upPos.F && down.ManhattanTo(upPos) >= 2)
                {
                    (int dx, int dy) = down.DirectionTo(upPos);
                    tile = new TilePos(down.X + dx, down.Y + dy, down.F);
                    if (DuctEditor.ValidateInline(net, tile) != DrawError.None) return false;
                    DuctEditor.PlaceInline(net, tile, "damper", -dx, -dy);
                    group.AddRange(served);
                    return true;
                }
                node = upNode;
            }
            return false;
        }

        /// <summary>A damper on the outlet's own branch: the straight tile nearest the outlet on a duct serving only it.</summary>
        static bool PlaceBranchDamper(DataRepository data, BuildingLayout layout, BuildingState s, string outletId, out TilePos tile)
        {
            tile = default;
            DuctNetworkState net = s.Air;
            Tree tree = Tree.Of(net, layout, data);
            DuctNode? outlet = net.Nodes.Find(n => n.Kind == DuctNodeKind.Outlet && n.AnchorId == outletId);
            if (outlet == null) return false;
            int node = outlet.Id;
            for (int hop = 0; hop < 64; hop++)
            {
                DuctSegment? seg = null;
                foreach (DuctSegment x in net.Segments)
                    if (tree.Upstream.TryGetValue(x.Id, out int up) && x.Other(up) == node) seg = x;
                if (seg == null || tree.Outlets[seg.Id] != 1) return false;
                int upNode = tree.Upstream[seg.Id];
                TilePos down = net.FindNode(node)!.Pos, upPos = net.FindNode(upNode)!.Pos;
                if (down.ManhattanTo(upPos) >= 2)
                {
                    (int dx, int dy) = down.DirectionTo(upPos);
                    tile = new TilePos(down.X + dx, down.Y + dy, down.F);
                    DuctEditor.PlaceInline(net, tile, "damper", -dx, -dy);
                    return true;
                }
                node = upNode;
            }
            return false;
        }
    }
}
