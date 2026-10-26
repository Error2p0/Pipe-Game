using System.Collections.Generic;

namespace HeadLoss.Game.Buildings
{
    /// <summary>Why a drawn path cannot be placed (shown as a short, calm reason).</summary>
    public enum DrawError
    {
        None,
        TooShort,
        NotOrthogonal,
        StartNotOnNetwork,
        OutsideBuilding,
        CrossesWall,
        Overlap,
        SelfCrossing,
        NodeFull,
        Duplicate,
    }

    /// <summary>What occupies a tile of the duct layer.</summary>
    public readonly struct TileOccupant
    {
        /// <summary>Node id, or 0.</summary>
        public readonly int NodeId;
        /// <summary>Segment id whose interior covers the tile, or 0.</summary>
        public readonly int SegmentId;

        public TileOccupant(int nodeId, int segmentId)
        {
            NodeId = nodeId;
            SegmentId = segmentId;
        }

        public bool IsEmpty => NodeId == 0 && SegmentId == 0;
    }

    /// <summary>
    /// Pure editing rules for the duct layer: orthogonal runs on the tile grid, corners become elbow
    /// nodes, drawing from or onto the middle of a segment makes a tee (a cross where a tee already is),
    /// one duct per tile. A free end is an open end that discharges into the room.
    /// </summary>
    public static class DuctEditor
    {
        public static string Describe(DrawError e) => e switch
        {
            DrawError.None => "",
            DrawError.TooShort => "Drag at least one tile",
            DrawError.NotOrthogonal => "Ducts run along the grid",
            DrawError.StartNotOnNetwork => "Start from the intake, an outlet or an existing duct",
            DrawError.OutsideBuilding => "Ducts stay inside the building",
            DrawError.CrossesWall => "A wall is in the way",
            DrawError.Overlap => "Another duct is already there (cross it straight to join)",
            DrawError.SelfCrossing => "The run crosses itself",
            DrawError.NodeFull => "No more connections fit here",
            DrawError.Duplicate => "These points are already connected",
            _ => e.ToString(),
        };

        /// <summary>Maps every tile covered by the network (nodes and segment interiors).</summary>
        public static Dictionary<TilePos, TileOccupant> Occupancy(DuctNetworkState net)
        {
            var map = new Dictionary<TilePos, TileOccupant>();
            foreach (DuctNode n in net.Nodes) map[n.Pos] = new TileOccupant(n.Id, 0);
            foreach (DuctSegment s in net.Segments)
            {
                TilePos a = net.FindNode(s.NodeA)!.Pos, b = net.FindNode(s.NodeB)!.Pos;
                (int dx, int dy) = a.DirectionTo(b);
                var t = new TilePos(a.X + dx, a.Y + dy);
                while (t != b)
                {
                    map[t] = new TileOccupant(0, s.Id);
                    t = new TilePos(t.X + dx, t.Y + dy);
                }
            }
            return map;
        }

        static int MaxDegree(DuctNode n, int maxFreeDegree) => n.Kind == DuctNodeKind.Free ? maxFreeDegree : 1;

        /// <summary>Checks whether <paramref name="path"/> (tiles from start to end, inclusive) can be placed.</summary>
        public static DrawError Validate(DuctNetworkState net, BuildingLayout layout, IReadOnlyList<TilePos> path, int maxFreeDegree)
        {
            if (path.Count < 2) return DrawError.TooShort;
            for (int i = 0; i < path.Count; i++)
            {
                if (!layout.IsInside(path[i])) return DrawError.OutsideBuilding;
                if (i > 0)
                {
                    if (!path[i - 1].IsAdjacentTo(path[i])) return DrawError.NotOrthogonal;
                    if (!layout.CanPass(path[i - 1], path[i])) return DrawError.CrossesWall;
                }
            }
            var seen = new HashSet<TilePos>();
            foreach (TilePos t in path)
                if (!seen.Add(t)) return DrawError.SelfCrossing;

            Dictionary<TilePos, TileOccupant> occ = Occupancy(net);
            occ.TryGetValue(path[0], out TileOccupant start);
            occ.TryGetValue(path[path.Count - 1], out TileOccupant end);
            if (start.IsEmpty) return DrawError.StartNotOnNetwork;

            for (int i = 1; i < path.Count - 1; i++)
            {
                if (!occ.TryGetValue(path[i], out TileOccupant o)) continue;
                DrawError e = CrossingError(net, path, i, o, maxFreeDegree);
                if (e != DrawError.None) return e;
            }

            // Endpoint capacity.
            if (start.NodeId != 0)
            {
                DuctNode n = net.FindNode(start.NodeId)!;
                if (net.Degree(n.Id) >= MaxDegree(n, maxFreeDegree)) return DrawError.NodeFull;
            }
            else if (maxFreeDegree < 3) return DrawError.NodeFull;
            if (end.NodeId != 0)
            {
                DuctNode n = net.FindNode(end.NodeId)!;
                if (net.Degree(n.Id) >= MaxDegree(n, maxFreeDegree)) return DrawError.NodeFull;
                if (path.Count == 2 && start.NodeId != 0 && Connected(net, start.NodeId, end.NodeId)) return DrawError.Duplicate;
            }
            else if (end.SegmentId != 0 && maxFreeDegree < 3) return DrawError.NodeFull;

            // Leaving or entering a segment's middle must be perpendicular to it (a tee, not an overlap).
            if (start.SegmentId != 0 && Collinear(net, start.SegmentId, path[0], path[1])) return DrawError.Overlap;
            if (end.SegmentId != 0 && Collinear(net, end.SegmentId, path[path.Count - 2], path[path.Count - 1])) return DrawError.Overlap;
            if (start.SegmentId != 0 && start.SegmentId == end.SegmentId) return DrawError.Overlap;
            return DrawError.None;
        }

        /// <summary>
        /// A run may pass straight across an existing duct (making a cross) or straight through a free node
        /// that has room for two more ducts. Anything else on the way is an overlap.
        /// </summary>
        static DrawError CrossingError(DuctNetworkState net, IReadOnlyList<TilePos> path, int i, TileOccupant o, int maxFreeDegree)
        {
            (int ax, int ay) = path[i - 1].DirectionTo(path[i]);
            (int bx, int by) = path[i].DirectionTo(path[i + 1]);
            bool straight = ax == bx && ay == by;
            if (!straight) return DrawError.Overlap;
            if (o.SegmentId != 0)
            {
                if (Collinear(net, o.SegmentId, path[i - 1], path[i])) return DrawError.Overlap;
                return maxFreeDegree >= 4 ? DrawError.None : DrawError.Overlap;
            }
            DuctNode n = net.FindNode(o.NodeId)!;
            if (n.Kind != DuctNodeKind.Free) return DrawError.Overlap;
            return net.Degree(n.Id) + 2 <= maxFreeDegree ? DrawError.None : DrawError.NodeFull;
        }

        static bool Connected(DuctNetworkState net, int a, int b)
        {
            foreach (DuctSegment s in net.Segments)
                if ((s.NodeA == a && s.NodeB == b) || (s.NodeA == b && s.NodeB == a)) return true;
            return false;
        }

        static bool Collinear(DuctNetworkState net, int segmentId, TilePos p, TilePos q)
        {
            DuctSegment s = net.FindSegment(segmentId)!;
            TilePos a = net.FindNode(s.NodeA)!.Pos, b = net.FindNode(s.NodeB)!.Pos;
            bool segHorizontal = a.Y == b.Y;
            bool stepHorizontal = p.Y == q.Y;
            return segHorizontal == stepHorizontal;
        }

        /// <summary>Places a validated run. Corners get elbow nodes with <paramref name="smoothElbows"/>.</summary>
        /// <param name="diameterMm">Duct size, mm.</param>
        public static void PlaceRun(DuctNetworkState net, IReadOnlyList<TilePos> path, double diameterMm, string materialId, bool smoothElbows)
        {
            // Tiles where the run meets the existing network on its way (crossings) become junctions.
            var crossings = new HashSet<TilePos>();
            Dictionary<TilePos, TileOccupant> occ = Occupancy(net);
            for (int i = 1; i < path.Count - 1; i++)
                if (occ.ContainsKey(path[i])) crossings.Add(path[i]);
            int startNode = NodeOrSplit(net, path[0]);
            int endNode = NodeOrSplit(net, path[path.Count - 1]);
            var keys = new List<int> { startNode };
            for (int i = 1; i < path.Count - 1; i++)
            {
                (int dx0, int dy0) = path[i - 1].DirectionTo(path[i]);
                (int dx1, int dy1) = path[i].DirectionTo(path[i + 1]);
                if (crossings.Contains(path[i])) keys.Add(NodeOrSplit(net, path[i]));
                else if (dx0 != dx1 || dy0 != dy1) keys.Add(AddNode(net, path[i], smoothElbows));
            }
            keys.Add(endNode);
            for (int i = 0; i < keys.Count - 1; i++)
                net.Segments.Add(new DuctSegment { Id = net.NextSegmentId++, NodeA = keys[i], NodeB = keys[i + 1], DiameterMm = diameterMm, MaterialId = materialId });
            Normalize(net);
        }

        static int AddNode(DuctNetworkState net, TilePos t, bool smooth)
        {
            var n = new DuctNode { Id = net.NextNodeId++, X = t.X, Y = t.Y, Kind = DuctNodeKind.Free, SmoothFittings = smooth };
            net.Nodes.Add(n);
            return n.Id;
        }

        /// <summary>Returns the node at <paramref name="t"/>, splitting the segment there (or creating a free end) if needed.</summary>
        static int NodeOrSplit(DuctNetworkState net, TilePos t)
        {
            DuctNode? existing = net.NodeAt(t);
            if (existing != null) return existing.Id;
            Occupancy(net).TryGetValue(t, out TileOccupant occ);
            int id = AddNode(net, t, false);
            if (occ.SegmentId != 0)
            {
                DuctSegment s = net.FindSegment(occ.SegmentId)!;
                net.Segments.Remove(s);
                net.Segments.Add(new DuctSegment { Id = net.NextSegmentId++, NodeA = s.NodeA, NodeB = id, DiameterMm = s.DiameterMm, MaterialId = s.MaterialId });
                net.Segments.Add(new DuctSegment { Id = net.NextSegmentId++, NodeA = id, NodeB = s.NodeB, DiameterMm = s.DiameterMm, MaterialId = s.MaterialId });
            }
            return id;
        }

        /// <summary>Removes a segment and any free node left unconnected.</summary>
        public static bool DeleteSegment(DuctNetworkState net, int segmentId) => DeleteSegments(net, new[] { segmentId });

        /// <summary>Removes several segments at once (ids stay valid until the final merge).</summary>
        public static bool DeleteSegments(DuctNetworkState net, IEnumerable<int> segmentIds)
        {
            bool any = false;
            foreach (int id in segmentIds)
            {
                DuctSegment? s = net.FindSegment(id);
                if (s == null) continue;
                net.Segments.Remove(s);
                RemoveIfOrphan(net, s.NodeA);
                RemoveIfOrphan(net, s.NodeB);
                any = true;
            }
            if (any) Normalize(net);
            return any;
        }

        /// <summary>Changes the size of several segments, merging equal straight runs afterwards.</summary>
        /// <param name="diameterMm">mm.</param>
        public static bool SetSize(DuctNetworkState net, IEnumerable<int> segmentIds, double diameterMm)
        {
            bool any = false;
            foreach (int id in segmentIds)
            {
                DuctSegment? s = net.FindSegment(id);
                if (s == null || s.DiameterMm == diameterMm) continue;
                s.DiameterMm = diameterMm;
                any = true;
            }
            if (any) Normalize(net);
            return any;
        }

        static void RemoveIfOrphan(DuctNetworkState net, int nodeId)
        {
            DuctNode? n = net.FindNode(nodeId);
            if (n != null && n.Kind == DuctNodeKind.Free && net.Degree(nodeId) == 0) net.Nodes.Remove(n);
        }

        /// <summary>Merges straight-through free nodes joining two equal segments, so every node is a real fitting.</summary>
        public static void Normalize(DuctNetworkState net)
        {
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (DuctNode n in net.Nodes)
                {
                    if (n.Kind != DuctNodeKind.Free) continue;
                    DuctSegment? s1 = null, s2 = null;
                    int count = 0;
                    foreach (DuctSegment s in net.Segments)
                    {
                        if (s.NodeA != n.Id && s.NodeB != n.Id) continue;
                        count++;
                        if (s1 == null) s1 = s; else s2 = s;
                    }
                    if (count != 2 || s1 == null || s2 == null) continue;
                    if (s1.DiameterMm != s2.DiameterMm || s1.MaterialId != s2.MaterialId) continue;
                    TilePos a = net.FindNode(s1.Other(n.Id))!.Pos, b = net.FindNode(s2.Other(n.Id))!.Pos;
                    if (a.X != b.X && a.Y != b.Y) continue;  // a corner
                    net.Segments.Remove(s1);
                    net.Segments.Remove(s2);
                    net.Nodes.Remove(n);
                    net.Segments.Add(new DuctSegment { Id = net.NextSegmentId++, NodeA = s1.Other(n.Id), NodeB = s2.Other(n.Id), DiameterMm = s1.DiameterMm, MaterialId = s1.MaterialId });
                    changed = true;
                    break;
                }
            }
        }

        /// <summary>True when the node is a 90° corner between exactly two segments.</summary>
        public static bool IsElbow(DuctNetworkState net, DuctNode n)
        {
            if (n.Kind != DuctNodeKind.Free) return false;
            TilePos? first = null;
            int count = 0;
            foreach (DuctSegment s in net.Segments)
            {
                if (s.NodeA != n.Id && s.NodeB != n.Id) continue;
                TilePos o = net.FindNode(s.Other(n.Id))!.Pos;
                if (count == 0) first = o;
                else if (count == 1 && first.HasValue) { if (first.Value.X == o.X || first.Value.Y == o.Y) return false; }
                count++;
            }
            return count == 2;
        }
    }
}
