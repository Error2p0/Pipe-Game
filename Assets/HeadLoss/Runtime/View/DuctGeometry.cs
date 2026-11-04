using System.Collections.Generic;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Buildings;
using UnityEngine;

namespace HeadLoss.Unity.View
{
    /// <summary>
    /// Drawn shape of one network on one floor: straight parts between tile centres (shortened at long-radius elbows),
    /// arcs at long-radius elbows, square caps at other nodes, and riser markers where a vertical run passes through the
    /// floor or ends on it. Rebuilt when the layout or the floor changes.
    /// </summary>
    public sealed class DuctGeometry
    {
        /// <summary>Centreline radius of a drawn long-radius elbow, m.</summary>
        public const float SmoothRadius = 0.4f;

        public struct Segment
        {
            public int Id;
            /// <summary>The nodes at A and B.</summary>
            public int NodeA, NodeB;
            public Vector2 A, B;
            public float Width;
            public double DiameterMm;
        }

        public struct Arc
        {
            public int NodeId;
            public Vector2 Centre;
            public float A0, A1;
            public float Width;
            /// <summary>Segment whose colour the arc takes.</summary>
            public int SegmentId;
        }

        public struct Cap
        {
            public int NodeId;
            public Vector2 Centre;
            public float Size;
            public int SegmentId;
        }

        /// <summary>A riser crossing or ending on this floor: it continues up and/or down from here.</summary>
        public struct Riser
        {
            public int SegmentId;
            public Vector2 Centre;
            public float Width;
            public bool Up, Down;
        }

        public readonly List<Segment> Segments = new List<Segment>();
        public readonly List<Arc> Arcs = new List<Arc>();
        public readonly List<Cap> Caps = new List<Cap>();
        public readonly List<Riser> Risers = new List<Riser>();
        /// <summary>Floor this geometry shows.</summary>
        public int Floor { get; private set; }

        public static Vector2 Centre(TilePos t) => new Vector2(t.X + 0.5f, t.Y + 0.5f);

        public static float WidthFor(double diameterMm, DrawingDef d) => (float)(d.DuctDisplayWidthBase + diameterMm / 1000.0 * d.DuctDisplayWidthFactor);

        /// <summary>Pipes are drawn a little wider than their size so a 15 mm pipe stays visible.</summary>
        public static float WidthFor(double diameterMm, DrawingDef d, bool pipe) =>
            pipe ? (float)(d.DuctDisplayWidthBase * 0.9 + diameterMm / 1000.0 * d.DuctDisplayWidthFactor * 2.5) : WidthFor(diameterMm, d);

        public void Build(DuctNetworkState net, DrawingDef drawing, int floor = 0)
        {
            Segments.Clear();
            Arcs.Clear();
            Caps.Clear();
            Risers.Clear();
            Floor = floor;
            bool pipe = net.FluidId != "air";
            var smoothElbows = new HashSet<int>();
            foreach (DuctNode n in net.Nodes)
                if (n.Floor == floor && n.SmoothFittings && DuctEditor.IsElbow(net, n) && FlatElbow(net, n)) smoothElbows.Add(n.Id);

            foreach (DuctSegment s in net.Segments)
            {
                DuctNode na = net.FindNode(s.NodeA)!, nb = net.FindNode(s.NodeB)!;
                float w = WidthFor(s.DiameterMm, drawing, pipe);
                if (na.Floor != nb.Floor)
                {
                    int lo = System.Math.Min(na.Floor, nb.Floor), hi = System.Math.Max(na.Floor, nb.Floor);
                    if (floor < lo || floor > hi) continue;
                    Risers.Add(new Riser { SegmentId = s.Id, Centre = Centre(na.Pos), Width = w, Up = floor < hi, Down = floor > lo });
                    continue;
                }
                if (na.Floor != floor) continue;
                Vector2 a = Centre(na.Pos), b = Centre(nb.Pos);
                Vector2 dir = (b - a).normalized;
                if (smoothElbows.Contains(s.NodeA)) a += dir * SmoothRadius;
                if (smoothElbows.Contains(s.NodeB)) b -= dir * SmoothRadius;
                Segments.Add(new Segment { Id = s.Id, NodeA = s.NodeA, NodeB = s.NodeB, A = a, B = b, Width = w, DiameterMm = s.DiameterMm });
            }

            foreach (DuctNode n in net.Nodes)
            {
                if (n.Floor != floor) continue;
                DuctSegment? first = null, second = null;
                int count = 0;
                float maxWidth = 0;
                DuctSegment? widest = null;
                foreach (DuctSegment s in net.Segments)
                {
                    if (s.NodeA != n.Id && s.NodeB != n.Id) continue;
                    if (count == 0) first = s; else if (count == 1) second = s;
                    count++;
                    float w = WidthFor(s.DiameterMm, drawing, pipe);
                    if (w > maxWidth) { maxWidth = w; widest = s; }
                }
                if (count == 0 || widest == null) continue;
                Vector2 p = Centre(n.Pos);
                if (smoothElbows.Contains(n.Id) && first != null && second != null)
                {
                    Vector2 d1 = (Centre(net.FindNode(first.Other(n.Id))!.Pos) - p).normalized;
                    Vector2 d2 = (Centre(net.FindNode(second.Other(n.Id))!.Pos) - p).normalized;
                    Vector2 c = p + (d1 + d2) * SmoothRadius;
                    float a0 = Mathf.Atan2(-d2.y, -d2.x), a1 = Mathf.Atan2(-d1.y, -d1.x);
                    if (a1 - a0 > Mathf.PI) a1 -= 2 * Mathf.PI;
                    if (a0 - a1 > Mathf.PI) a1 += 2 * Mathf.PI;
                    Arcs.Add(new Arc { NodeId = n.Id, Centre = c, A0 = a0, A1 = a1, Width = maxWidth, SegmentId = widest.Id });
                }
                else
                {
                    Caps.Add(new Cap { NodeId = n.Id, Centre = p, Size = maxWidth, SegmentId = widest.Id });
                }
            }
        }

        /// <summary>True when both legs of an elbow lie on its floor (a bend into a riser is drawn as a cap).</summary>
        static bool FlatElbow(DuctNetworkState net, DuctNode n)
        {
            foreach (DuctSegment s in net.Segments)
                if ((s.NodeA == n.Id || s.NodeB == n.Id) && net.FindNode(s.Other(n.Id))!.Floor != n.Floor) return false;
            return true;
        }

        /// <summary>Distance from p to a segment's centreline.</summary>
        public static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = ab.sqrMagnitude > 0 ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0;
            return Vector2.Distance(p, a + ab * t);
        }
    }
}
