using System;
using System.Collections.Generic;
using HeadLoss.Core;
using HeadLoss.Core.Data;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.Economy;
using HeadLoss.Game.Networks;
using HeadLoss.Sim.Fluids;
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
        static readonly (int dx, int dy)[] Dirs = { (1, 0), (-1, 0), (0, 1), (0, -1) };

        public static ReferenceResult Build(DataRepository data, BuildingLayout layout) => Build(data, layout, null);

        /// <param name="trace">Optional pass-by-pass log of the tuning (dev tools).</param>
        public static ReferenceResult Build(DataRepository data, BuildingLayout layout, List<string>? trace)
        {
            var fail = new ReferenceResult();
            EntryOpeningDef? intake = null;
            foreach (EntryOpeningDef o in layout.EntryOpenings) if (o.Utility == "air") intake = o;
            if (intake == null) { fail.Reason = "no air intake"; return fail; }

            string material = data.Balancing.Drawing.DefaultMaterial;
            // Prefer branches with room for a damper; if that walls an outlet off, route without the preference.
            BuildingState? state = RouteTree(data, layout, intake, data.Balancing.AutoRouter.ShortApproachPenalty, out string reason)
                ?? RouteTree(data, layout, intake, 0, out reason);
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
        static BuildingState? RouteTree(DataRepository data, BuildingLayout layout, EntryOpeningDef intake, double approachPenalty, out string reason)
        {
            reason = "";
            BuildingState state = BuildingState.CreateFor(layout);
            state.Equipment.Clear();
            string material = data.Balancing.Drawing.DefaultMaterial;
            AutoRouterDef ar0 = data.Balancing.AutoRouter;
            double placeholder = SizesFor(data, layout.Tier, material)[0];
            int maxDegree = data.Balancing.Drawing.MaxNodeDegree;
            DuctNetworkState net = state.Air;
            var intakePos = new TilePos(intake.X, intake.Y);

            // Trunk toward the centroid of the outlets.
            double cx = 0, cy = 0;
            foreach (OutletPlacementDef o in layout.Outlets) { cx += o.X; cy += o.Y; }
            cx /= Math.Max(1, layout.Outlets.Count);
            cy /= Math.Max(1, layout.Outlets.Count);
            // The trunk starts with a straight lead-in from the facade (no branch joins it there), so the source
            // equipment and a trunk damper always fit before the first junction.
            (int ix, int iy) = Inward(intake.Facing);
            var lead = new List<TilePos> { intakePos };
            var leadOcc = DuctEditor.Occupancy(net);
            for (int i = 0; i < ar0.LeadInTiles; i++)
            {
                TilePos end = lead[lead.Count - 1], next = new TilePos(end.X + ix, end.Y + iy);
                if (!layout.IsInside(next) || !layout.CanPass(end, next) || leadOcc.ContainsKey(next)) break;
                lead.Add(next);
            }
            var reserved = new HashSet<TilePos>(lead);
            reserved.Remove(lead[lead.Count - 1]);
            List<TilePos>? rest = lead.Count >= 2 ? PathToTile(data, layout, net, lead[lead.Count - 1], cx, cy, reserved) : PathToTile(data, layout, net, intakePos, cx, cy, reserved);
            var trunk = new List<TilePos>(lead);
            if (rest != null) for (int i = 1; i < rest.Count; i++) trunk.Add(rest[i]);
            if (trunk.Count >= 2 && DuctEditor.Validate(net, layout, trunk, maxDegree) == DrawError.None)
                DuctEditor.PlaceRun(net, trunk, placeholder, material, false);

            // Branches: always connect the outlet closest to the tree next.
            var remaining = new List<OutletPlacementDef>(layout.Outlets);
            while (remaining.Count > 0)
            {
                List<TilePos>? best = null;
                OutletPlacementDef? bestOutlet = null;
                double bestCost = double.MaxValue;
                foreach (OutletPlacementDef o in remaining)
                {
                    List<TilePos>? p = PathToNetwork(data, layout, net, new TilePos(o.X, o.Y), maxDegree, reserved, approachPenalty, out double cost);
                    if (p != null && cost < bestCost) { bestCost = cost; best = p; bestOutlet = o; }
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
            }
            Prune(net);
            return state;
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
            1 + (fromDir >= 0 && fromDir % 4 != dir ? data.Balancing.AutoRouter.TurnPenalty : 0);

        /// <summary>Unit step from a facade opening into the building.</summary>
        static (int dx, int dy) Inward(string facing) => facing switch
        {
            "west" => (1, 0),
            "east" => (-1, 0),
            "south" => (0, 1),
            _ => (0, -1),
        };

        /// <summary>Path over free tiles (not <paramref name="avoid"/>) from <paramref name="start"/> to the free tile nearest (cx, cy).</summary>
        static List<TilePos>? PathToTile(DataRepository data, BuildingLayout layout, DuctNetworkState net, TilePos start, double cx, double cy, HashSet<TilePos> avoid)
        {
            Dictionary<TilePos, TileOccupant> occ = DuctEditor.Occupancy(net);
            TilePos? target = null;
            double best = double.MaxValue;
            for (int x = 0; x < layout.Width; x++)
                for (int y = 0; y < layout.Height; y++)
                {
                    var t = new TilePos(x, y);
                    if (!layout.IsInside(t) || occ.ContainsKey(t) || avoid.Contains(t)) continue;
                    double d = (x - cx) * (x - cx) + (y - cy) * (y - cy);
                    if (d < best) { best = d; target = t; }
                }
            if (target == null || target.Value == start) return null;
            var s = new Search();
            s.Push(start, -1, 0, null);
            while (s.Pop(out TilePos u, out int ud, out double uc))
            {
                if (u == target.Value) return s.Back(u, ud);
                for (int d = 0; d < 4; d++)
                {
                    var v = new TilePos(u.X + Dirs[d].dx, u.Y + Dirs[d].dy);
                    if (!layout.IsInside(v) || !layout.CanPass(u, v) || occ.ContainsKey(v) || avoid.Contains(v)) continue;
                    s.Push(v, d, uc + StepCost(data, ud, d), (u, ud));
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
        static List<TilePos>? PathToNetwork(DataRepository data, BuildingLayout layout, DuctNetworkState net, TilePos start, int maxDegree,
            HashSet<TilePos> reserved, double penalty, out double cost)
        {
            cost = double.MaxValue;
            Dictionary<TilePos, TileOccupant> occ = DuctEditor.Occupancy(net);
            var s = new Search();
            // State key: direction (0–3, −1 at the start) + 4 × phase (0 start, 1 one step out, 2 two straight steps or more).
            s.Push(start, -1, 0, null);
            List<TilePos>? found = null;
            while (s.Pop(out TilePos u, out int key, out double uc))
            {
                if (uc >= cost) break;
                int ud = key < 0 ? -1 : key % 4, phase = key < 0 ? 0 : key / 4;
                for (int d = 0; d < 4; d++)
                {
                    if (ud >= 0 && d == (ud ^ 1)) continue;  // no turning back
                    var v = new TilePos(u.X + Dirs[d].dx, u.Y + Dirs[d].dy);
                    if (!layout.IsInside(v) || !layout.CanPass(u, v)) continue;
                    double vc = uc + StepCost(data, ud, d);
                    int nextPhase = phase == 0 ? 1 : 2;
                    if (phase == 1 && d != ud) vc += penalty;
                    if (occ.TryGetValue(v, out TileOccupant o))
                    {
                        if (phase == 0) vc += penalty;
                        if (reserved.Contains(v) || !CanJoin(net, o, Dirs[d], maxDegree) || vc >= cost) continue;
                        List<TilePos> path = s.Back(u, key);
                        path.Add(v);
                        if (new HashSet<TilePos>(path).Count != path.Count) continue;
                        cost = vc;
                        found = path;
                        continue;
                    }
                    s.Push(v, d + 4 * nextPhase, vc, (u, key));
                }
            }
            return found;
        }

        static bool CanJoin(DuctNetworkState net, TileOccupant o, (int dx, int dy) step, int maxDegree)
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
            bool horizontal = a.Y == b.Y;
            return horizontal ? step.dx == 0 : step.dy == 0;
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
                        dist[v] = dist[u] + nu.Pos.ManhattanTo(nv.Pos);
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
                        q += BuildingEconomy.Requirement(data, layout, layout.FindOutlet(id)!).Ideal;
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
                    tile = new TilePos(a.X + dir.dx, a.Y + dir.dy);
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
            CompiledNetwork c = NetworkCompiler.Compile(layout, s, data, Units.CToK(data.Balancing.Physics.DefaultAirTemperatureC), crank);
            SolveResult res = new NetworkSolver(SolverSettings.FromBalancing(data.Balancing)).Solve(c.Graph, air);
            return (BuildingEconomy.Evaluate(data, layout, c, res), c, res);
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

                // Dampers on demand: an outlet above its target (other than the weakest) gets one on its branch.
                foreach (OutletStatus o in ev.Outlets)
                {
                    if (o == weakest || Ratio(o, ar) <= 1 || dampers.ContainsKey(o.OutletId)) continue;
                    if (PlaceBranchDamper(data, layout, s, o.OutletId, out TilePos tile))
                    {
                        dampers[o.OutletId] = tile;
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

                // Branch dampers bring their outlets to target (the weakest one's opens fully).
                bool weakestOpen = true;
                foreach (var kv in dampers)
                {
                    OutletStatus o = ev.Find(kv.Key)!;
                    DuctNode n = s.Air.NodeAt(kv.Value)!;
                    changed |= Trim(n, Math.Pow(1 / Math.Max(Ratio(o, ar), 1e-6), gain), damper.MinOpening);
                    if (o == weakest) weakestOpen = n.Opening >= 1;
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
                    tile = new TilePos(down.X + dx, down.Y + dy);
                    DuctEditor.PlaceInline(net, tile, "damper", -dx, -dy);
                    return true;
                }
                node = upNode;
            }
            return false;
        }
    }
}
