using System.Collections.Generic;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Buildings;
using UnityEngine;

namespace HeadLoss.Unity.View
{
    /// <summary>
    /// Drawn shape of a duct network: straight parts between tile centres (shortened at long-radius
    /// elbows), arcs at long-radius elbows and square caps at other nodes. Rebuilt when the layout changes.
    /// </summary>
    public sealed class DuctGeometry
    {
        /// <summary>Centreline radius of a drawn long-radius elbow, m.</summary>
        public const float SmoothRadius = 0.4f;

        public struct Segment
        {
            public int Id;
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

        public readonly List<Segment> Segments = new List<Segment>();
        public readonly List<Arc> Arcs = new List<Arc>();
        public readonly List<Cap> Caps = new List<Cap>();

        public static Vector2 Centre(TilePos t) => new Vector2(t.X + 0.5f, t.Y + 0.5f);

        public static float WidthFor(double diameterMm, DrawingDef d) => (float)(d.DuctDisplayWidthBase + diameterMm / 1000.0 * d.DuctDisplayWidthFactor);

        public void Build(DuctNetworkState net, DrawingDef drawing)
        {
            Segments.Clear();
            Arcs.Clear();
            Caps.Clear();
            var smoothElbows = new HashSet<int>();
            foreach (DuctNode n in net.Nodes)
                if (n.SmoothFittings && DuctEditor.IsElbow(net, n)) smoothElbows.Add(n.Id);

            foreach (DuctSegment s in net.Segments)
            {
                Vector2 a = Centre(net.FindNode(s.NodeA)!.Pos), b = Centre(net.FindNode(s.NodeB)!.Pos);
                Vector2 dir = (b - a).normalized;
                if (smoothElbows.Contains(s.NodeA)) a += dir * SmoothRadius;
                if (smoothElbows.Contains(s.NodeB)) b -= dir * SmoothRadius;
                Segments.Add(new Segment { Id = s.Id, A = a, B = b, Width = WidthFor(s.DiameterMm, drawing), DiameterMm = s.DiameterMm });
            }

            foreach (DuctNode n in net.Nodes)
            {
                DuctSegment? first = null, second = null;
                int count = 0;
                float maxWidth = 0;
                DuctSegment? widest = null;
                foreach (DuctSegment s in net.Segments)
                {
                    if (s.NodeA != n.Id && s.NodeB != n.Id) continue;
                    if (count == 0) first = s; else if (count == 1) second = s;
                    count++;
                    float w = WidthFor(s.DiameterMm, drawing);
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

        /// <summary>Distance from p to a segment's centreline.</summary>
        public static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = ab.sqrMagnitude > 0 ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0;
            return Vector2.Distance(p, a + ab * t);
        }
    }
}
