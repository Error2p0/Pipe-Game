using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace HeadLoss.Unity.View
{
    /// <summary>
    /// Accumulates flat coloured triangles and uploads them to a reusable mesh. Lists are kept between
    /// rebuilds so steady-state rebuilds do not allocate.
    /// </summary>
    public sealed class MeshBuilder
    {
        readonly List<Vector3> _vertices = new List<Vector3>(1024);
        readonly List<Color> _colors = new List<Color>(1024);
        readonly List<int> _indices = new List<int>(2048);
        readonly bool _linear;

        public Mesh Mesh { get; }

        public MeshBuilder(string name)
        {
            Mesh = new Mesh { name = name };
            Mesh.MarkDynamic();
            _linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
        }

        public void Clear()
        {
            _vertices.Clear();
            _colors.Clear();
            _indices.Clear();
        }

        Color Convert(Color c) => _linear ? c.linear : c;

        /// <summary>Axis-aligned rectangle.</summary>
        public void Rect(float x0, float y0, float x1, float y1, float z, Color color)
        {
            Quad(new Vector3(x0, y0, z), new Vector3(x1, y0, z), new Vector3(x1, y1, z), new Vector3(x0, y1, z), color);
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
        {
            int i = _vertices.Count;
            Color k = Convert(color);
            _vertices.Add(a); _vertices.Add(b); _vertices.Add(c); _vertices.Add(d);
            _colors.Add(k); _colors.Add(k); _colors.Add(k); _colors.Add(k);
            _indices.Add(i); _indices.Add(i + 1); _indices.Add(i + 2);
            _indices.Add(i); _indices.Add(i + 2); _indices.Add(i + 3);
        }

        /// <summary>Thick straight line from a to b.</summary>
        public void Line(Vector2 a, Vector2 b, float width, float z, Color color)
        {
            Vector2 dir = b - a;
            if (dir.sqrMagnitude < 1e-12f) return;
            Vector2 n = new Vector2(-dir.y, dir.x).normalized * (width * 0.5f);
            Quad(new Vector3(a.x - n.x, a.y - n.y, z), new Vector3(b.x - n.x, b.y - n.y, z),
                 new Vector3(b.x + n.x, b.y + n.y, z), new Vector3(a.x + n.x, a.y + n.y, z), color);
        }

        /// <summary>Thick circular arc (centre, radius of the centreline, start/end angle in radians).</summary>
        public void Arc(Vector2 centre, float radius, float width, float a0, float a1, int steps, float z, Color color)
        {
            float r0 = radius - width * 0.5f, r1 = radius + width * 0.5f;
            for (int s = 0; s < steps; s++)
            {
                float t0 = Mathf.Lerp(a0, a1, (float)s / steps), t1 = Mathf.Lerp(a0, a1, (float)(s + 1) / steps);
                Vector2 d0 = new Vector2(Mathf.Cos(t0), Mathf.Sin(t0)), d1 = new Vector2(Mathf.Cos(t1), Mathf.Sin(t1));
                Quad((Vector3)(centre + d0 * r0) + Vector3.forward * z, (Vector3)(centre + d1 * r0) + Vector3.forward * z,
                     (Vector3)(centre + d1 * r1) + Vector3.forward * z, (Vector3)(centre + d0 * r1) + Vector3.forward * z, color);
            }
        }

        /// <summary>Filled regular polygon (circle approximation).</summary>
        public void Disc(Vector2 centre, float radius, int sides, float z, Color color)
        {
            int c = _vertices.Count;
            Color k = Convert(color);
            _vertices.Add(new Vector3(centre.x, centre.y, z));
            _colors.Add(k);
            for (int s = 0; s <= sides; s++)
            {
                float a = s * Mathf.PI * 2 / sides;
                _vertices.Add(new Vector3(centre.x + Mathf.Cos(a) * radius, centre.y + Mathf.Sin(a) * radius, z));
                _colors.Add(k);
                if (s > 0)
                {
                    _indices.Add(c); _indices.Add(c + s); _indices.Add(c + s + 1);
                }
            }
        }

        /// <summary>Rectangle outline of the given thickness.</summary>
        public void Outline(float x0, float y0, float x1, float y1, float thickness, float z, Color color)
        {
            Rect(x0, y0, x1, y0 + thickness, z, color);
            Rect(x0, y1 - thickness, x1, y1, z, color);
            Rect(x0, y0 + thickness, x0 + thickness, y1 - thickness, z, color);
            Rect(x1 - thickness, y0 + thickness, x1, y1 - thickness, z, color);
        }

        public void Upload()
        {
            Mesh.Clear();
            Mesh.indexFormat = _vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            Mesh.SetVertices(_vertices);
            Mesh.SetColors(_colors);
            Mesh.SetTriangles(_indices, 0, false);
            Mesh.bounds = new Bounds(Vector3.zero, new Vector3(10000, 10000, 100));
        }
    }
}
