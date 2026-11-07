using System.Collections.Generic;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.Session;
using UnityEngine;

namespace HeadLoss.Unity.View
{
    /// <summary>One network as the view shows it on the current floor.</summary>
    public sealed class NetworkView
    {
        public string Fluid = "air";
        public DuctNetworkState Network = new DuctNetworkState();
        /// <summary>Solve matching <see cref="Network"/> (may lag behind after an edit).</summary>
        public FluidSolve? Solve;
        /// <summary>Shape on the current floor (rebuilt with the view).</summary>
        public readonly DuctGeometry Geometry = new DuctGeometry();
        /// <summary>Ghosts on the floors above and below (faint, no interaction).</summary>
        public readonly DuctGeometry Above = new DuctGeometry(), Below = new DuctGeometry();
        /// <summary>Burst segments (drawn in the danger colour; a label says so too).</summary>
        public readonly HashSet<int> BurstSegments = new HashSet<int>();
        /// <summary>Segments drawn as a ghost (part of the run being drawn).</summary>
        public readonly HashSet<int> GhostSegments = new HashSet<int>();
        /// <summary>The network the tools and the heatmap work on.</summary>
        public bool Active;
    }

    /// <summary>What the duct layer should show this frame.</summary>
    public sealed class DuctViewInput
    {
        public int Floor;
        public readonly NetworkView Air = new NetworkView { Fluid = "air" };
        public readonly NetworkView Water = new NetworkView { Fluid = "water" };
        public readonly NetworkView Gas = new NetworkView { Fluid = BuildingState.GasFluid };
        /// <summary>The whole snapshot shown (particles read it).</summary>
        public NetworkSnapshot? Snapshot;
        /// <summary>Invalid run being drawn (tile centres), drawn in the warning colour.</summary>
        public List<TilePos>? InvalidPath;
        /// <summary>Network the selection and hover belong to.</summary>
        public string SelectionFluid = "air", HoverFluid = "air";
        public HashSet<int> SelectedSegments = new HashSet<int>();
        public int SelectedNode;
        public int HoverSegment;
        public int HoverNode;
        /// <summary>Part placement: highlight this tile (good colour when the part fits, warning when not).</summary>
        public bool Placing;
        public TilePos PlaceTile;
        public bool PlaceValid;
        /// <summary>Selected / hovered tile of an intake, outlet or inline part (outlined).</summary>
        public TilePos? SelectedTile;
        public TilePos? HoverTile;
        /// <summary>Box selection in progress (world rectangle).</summary>
        public Rect? Box;
        /// <summary>Stress of a segment (fluid, segment id), 0–1+ (the stress heatmap).</summary>
        public System.Func<string, int, double>? Stress;
        /// <summary>Tiles in code violation (outlined in the danger colour, <c>09-codes.md</c>).</summary>
        public readonly List<TilePos> ViolationTiles = new List<TilePos>();
        /// <summary>Tiles of gas leaks (a ring in the danger colour; the leak list says the rest).</summary>
        public readonly List<TilePos> LeakTiles = new List<TilePos>();
        /// <summary>A surge rides on this network now (the ripple along its pipes).</summary>
        public System.Func<string, bool>? Surging;
        /// <summary>The ceiling layer is in focus (else pipes are drawn faded under the floor layer).</summary>
        public bool CeilingInFocus = true;

        public NetworkView View(string fluid) => fluid == "air" ? Air : fluid == "water" ? Water : Gas;
        public IEnumerable<NetworkView> Views
        {
            get
            {
                yield return Air;
                yield return Water;
                yield return Gas;
            }
        }
    }

    /// <summary>
    /// Procedural line mesh for the networks on the current floor: width by diameter, colour from the active heatmap
    /// for the network in focus (or the fluid colour in plain view, and always for the other network), risers as
    /// markers, and faded ghosts of the floors above and below. Rebuilt only when its inputs change.
    /// </summary>
    public sealed class DuctRenderer : MonoBehaviour
    {
        const float ZDuct = 1.0f, ZCap = 0.99f, ZHighlight = 1.2f, ZGhost = 1.3f;
        MeshBuilder? _mesh;
        ViewPalette? _palette;
        HeatmapState? _heatmap;
        DrawingDef? _drawing;
        HeatmapDef? _heatmapDef;

        public void Init(ViewPalette palette, HeatmapState heatmap, DrawingDef drawing, HeatmapDef heatmapDef, Material material)
        {
            _palette = palette;
            _heatmap = heatmap;
            _drawing = drawing;
            _heatmapDef = heatmapDef;
            _mesh = new MeshBuilder("Ducts");
            RenderLayers.Attach(gameObject, _mesh.Mesh, material, RenderLayers.Ducts);
        }

        /// <summary>Colour of a segment for the active mode.</summary>
        public Color SegmentColour(DuctViewInput input, NetworkView view, int segmentId)
        {
            Color plain = _palette!.Fluid(view.Fluid);
            if (view.BurstSegments.Contains(segmentId)) return _palette.Danger;
            if (!view.Active)
            {
                plain.a = (float)_drawing!.OtherNetworkOpacity;
                return plain;
            }
            // Concentration colours the rooms only; the pipes keep their plain colour under it.
            if (_heatmap!.Mode == HeatmapMode.Plain || _heatmap.Mode == HeatmapMode.Concentration) return plain;
            if (_heatmap.Mode == HeatmapMode.Stress)
            {
                double s = input.Stress?.Invoke(view.Fluid, segmentId) ?? 0;
                return s >= 1 ? _palette.Warning : _palette.Ramp(_heatmap.Ramp, _heatmap.Normalise(s));
            }
            FluidSolve? solve = view.Solve;
            if (solve == null || !solve.Compiled.SegmentLink.TryGetValue(segmentId, out int link))
            {
                Color neutral = _palette.Walls;
                neutral.a = 0.35f;
                return neutral;
            }
            return _palette.Ramp(_heatmap.Ramp, _heatmap.Normalise(Value(_heatmap.Mode, solve, link)));
        }

        /// <summary>A segment's place on the heatmap ramp, 0–1: at its two ends and along its middle.</summary>
        struct Shade
        {
            public float A, Mid, B;
        }

        /// <summary>Length of one drawn piece where a colour is blending, m.</summary>
        const float BlendPieceMetres = 0.2f;
        readonly Dictionary<int, Shade> _shades = new Dictionary<int, Shade>();
        readonly Dictionary<int, (float sum, int count)> _joints = new Dictionary<int, (float, int)>();

        /// <summary>
        /// Place on the ramp at distance x along a piece of length l: from the joint's value at each end to the segment's own
        /// along the middle, over <paramref name="blend"/> metres at each end.
        /// </summary>
        public static float BlendAt(float x, float l, float blend, float jointA, float mid, float jointB)
        {
            if (blend <= 0) return mid;
            if (x < blend) return Mathf.Lerp(jointA, mid, x / blend);
            if (l - x < blend) return Mathf.Lerp(jointB, mid, (l - x) / blend);
            return mid;
        }

        /// <summary>
        /// Heatmap positions of the network in focus, and of each joint: the mean of the segments meeting there, so neighbours
        /// run into each other (<c>11-ui-visuals-audio.md</c>). Colours that are not a value on the ramp stay flat: plain
        /// view, burst pipes, stress over the limit, and pipes without a solve.
        /// </summary>
        void PrepareShades(DuctViewInput input, NetworkView v)
        {
            _shades.Clear();
            _joints.Clear();
            HeatmapMode mode = _heatmap!.Mode;
            if (!v.Active || mode == HeatmapMode.Plain || mode == HeatmapMode.Concentration) return;
            foreach (DuctSegment seg in v.Network.Segments)
            {
                if (v.BurstSegments.Contains(seg.Id)) continue;
                Shade s;
                if (mode == HeatmapMode.Stress)
                {
                    double stress = input.Stress?.Invoke(v.Fluid, seg.Id) ?? 0;
                    if (stress >= 1) continue;
                    s.A = s.Mid = s.B = _heatmap.Normalise(stress);
                }
                else
                {
                    FluidSolve? solve = v.Solve;
                    if (solve == null || !solve.Compiled.SegmentLink.TryGetValue(seg.Id, out int link)) continue;
                    s.A = s.Mid = s.B = _heatmap.Normalise(Value(mode, solve, link));
                    // Pressure really does change along a pipe: use its two ends (a link runs From NodeA To NodeB).
                    if (mode == HeatmapMode.Pressure)
                    {
                        double unit = solve.Fluid == "air" ? 1 : solve.Fluid == BuildingState.GasFluid ? Core.Units.PaPerMbar : Core.Units.PaPerBar;
                        s.A = _heatmap.Normalise(solve.Result.PressureFrom(solve.Compiled.Graph, link) / unit);
                        s.B = _heatmap.Normalise(solve.Result.PressureTo(solve.Compiled.Graph, link) / unit);
                    }
                }
                _shades[seg.Id] = s;
                AddToJoint(seg.NodeA, s.A);
                AddToJoint(seg.NodeB, s.B);
            }
        }

        void AddToJoint(int node, float value)
        {
            _joints.TryGetValue(node, out (float sum, int count) j);
            _joints[node] = (j.sum + value, j.count + 1);
        }

        float Joint(int node, float fallback) => _joints.TryGetValue(node, out (float sum, int count) j) ? j.sum / j.count : fallback;

        /// <summary>A segment whose colour blends into its joints: short pieces where it changes, one long piece where it does not.</summary>
        void DrawBlended(MeshBuilder m, DuctGeometry.Segment s, Vector2 from, Vector2 to, Shade shade, float alpha)
        {
            float l = Vector2.Distance(from, to);
            if (l < 1e-4f) return;
            float blend = _heatmap!.Mode == HeatmapMode.Pressure ? l * 0.5f : Mathf.Min((float)_heatmapDef!.JointBlendMetres, l * 0.5f);
            float jointA = Joint(s.NodeA, shade.A), jointB = Joint(s.NodeB, shade.B);
            Color At(float x)
            {
                Color c = _palette!.Ramp(_heatmap.Ramp, BlendAt(x, l, blend, jointA, shade.Mid, jointB));
                c.a *= alpha;
                return c;
            }
            Vector2 dir = (to - from) / l;
            float x0 = 0;
            while (x0 < l)
            {
                float x1 = x0 >= blend && x0 < l - blend ? l - blend : Mathf.Min(x0 + BlendPieceMetres, x0 < blend ? blend : l);
                if (x1 <= x0) x1 = Mathf.Min(x0 + BlendPieceMetres, l);
                m.GradientLine(from + dir * x0, from + dir * x1, s.Width, ZDuct, At(x0), At(x1));
                x0 = x1;
            }
        }

        /// <summary>A link's value for a heatmap mode in display units: m/s, Pa, mbar or bar, m³/h or L/s, °C, kg/m³.</summary>
        public static double Value(HeatmapMode mode, FluidSolve solve, int link)
        {
            var r = solve.Result;
            var g = solve.Compiled.Graph;
            bool air = solve.Fluid == "air", gas = solve.Fluid == BuildingState.GasFluid;
            return mode switch
            {
                HeatmapMode.Pressure => 0.5 * (r.PressureFrom(g, link) + r.PressureTo(g, link)) / (air ? 1 : gas ? Core.Units.PaPerMbar : Core.Units.PaPerBar),
                HeatmapMode.Flow => air || gas ? System.Math.Abs(Core.Units.M3sToM3h(r.Flow[link])) : System.Math.Abs(r.Flow[link]) * 1000,
                HeatmapMode.Temperature => Core.Units.KToC(g.Links[link].Temperature),
                HeatmapMode.Density => r.Density[link],
                _ => System.Math.Abs(r.Velocity[link]),
            };
        }

        /// <summary>The air network's value (the Phase 1–3 signature).</summary>
        public static double Value(HeatmapMode mode, NetworkSnapshot snap, int link) => Value(mode, snap.Air, link);

        /// <summary>Square ring just inside a tile (the symbol on the tile stays readable).</summary>
        static void TileOutline(MeshBuilder m, TilePos t, Color c) =>
            m.Outline(t.X + 0.03f, t.Y + 0.03f, t.X + 0.97f, t.Y + 0.97f, 0.07f, ZHighlight + 0.04f, c);

        public void Rebuild(DuctViewInput input)
        {
            MeshBuilder m = _mesh!;
            ViewPalette p = _palette!;
            m.Clear();
            foreach (NetworkView v in input.Views)
            {
                v.Geometry.Build(v.Network, _drawing!, input.Floor);
                v.Above.Build(v.Network, _drawing!, input.Floor + 1);
                v.Below.Build(v.Network, _drawing!, input.Floor - 1);
            }

            // Ghosts of the floors above and below, faint and plain.
            foreach (NetworkView v in input.Views)
            {
                Color ghost = p.Fluid(v.Fluid);
                ghost.a = (float)_drawing!.GhostOpacity;
                foreach (DuctGeometry g in new[] { v.Above, v.Below }) DrawGhost(m, g, ghost);
            }

            Color select = p.Text;
            select.a = 0.75f;
            Color hover = p.Text;
            hover.a = 0.3f;
            foreach (NetworkView v in input.Views)
            {
                bool selNet = v.Fluid == input.SelectionFluid, hovNet = v.Fluid == input.HoverFluid;
                DuctGeometry g = v.Geometry;
                foreach (DuctGeometry.Segment s in g.Segments)
                {
                    bool selected = selNet && input.SelectedSegments.Contains(s.Id);
                    if (selected || (hovNet && input.HoverSegment == s.Id))
                        m.Line(s.A, s.B, s.Width + 0.12f, ZHighlight, selected ? select : hover);
                }
                foreach (DuctGeometry.Riser r in g.Risers)
                {
                    bool selected = selNet && input.SelectedSegments.Contains(r.SegmentId);
                    if (selected || (hovNet && input.HoverSegment == r.SegmentId))
                        m.Arc(r.Centre, r.Width * 0.5f + 0.16f, 0.12f, 0, 2 * Mathf.PI, 20, ZHighlight, selected ? select : hover);
                }
                foreach (DuctGeometry.Arc a in g.Arcs)
                {
                    bool sel = selNet && a.NodeId == input.SelectedNode, hov = hovNet && a.NodeId == input.HoverNode;
                    if (sel || hov) m.Arc(a.Centre, DuctGeometry.SmoothRadius, a.Width + 0.12f, a.A0, a.A1, 10, ZHighlight, sel ? select : hover);
                }
                foreach (DuctGeometry.Cap c in g.Caps)
                {
                    bool sel = selNet && c.NodeId == input.SelectedNode, hov = hovNet && c.NodeId == input.HoverNode;
                    if (!sel && !hov) continue;
                    float h = c.Size * 0.5f + 0.06f;
                    m.Rect(c.Centre.x - h, c.Centre.y - h, c.Centre.x + h, c.Centre.y + h, ZHighlight, sel ? select : hover);
                }
            }

            // The network in focus draws last (on top of the other where they share a tile).
            foreach (NetworkView v in input.Views) if (!v.Active) DrawNetwork(m, input, v);
            foreach (NetworkView v in input.Views) if (v.Active) DrawNetwork(m, input, v);

            Color violation = p.Danger;
            violation.a = 0.8f;
            foreach (TilePos t in input.ViolationTiles)
                if (t.F == input.Floor) TileOutline(m, t, violation);
            foreach (TilePos t in input.LeakTiles)
                if (t.F == input.Floor) m.Arc(DuctGeometry.Centre(t), 0.34f, 0.07f, 0, 2 * Mathf.PI, 20, ZCap - 0.02f, p.Danger);
            if (input.HoverTile is TilePos ht && ht != input.SelectedTile)
                TileOutline(m, ht, hover);
            if (input.SelectedTile is TilePos st)
                TileOutline(m, st, select);
            if (input.Box is Rect box)
            {
                Color fill = p.Text;
                fill.a = 0.06f;
                m.Rect(box.xMin, box.yMin, box.xMax, box.yMax, ZHighlight + 0.05f, fill);
                Color edge = p.Text;
                edge.a = 0.55f;
                m.Outline(box.xMin, box.yMin, box.xMax, box.yMax, 0.04f, ZHighlight + 0.06f, edge);
            }

            if (input.Placing)
            {
                Color c = input.PlaceValid ? p.Good : p.Warning;
                c.a = 0.35f;
                TilePos t = input.PlaceTile;
                m.Rect(t.X + 0.08f, t.Y + 0.08f, t.X + 0.92f, t.Y + 0.92f, ZHighlight + 0.05f, c);
            }

            if (input.InvalidPath != null && input.InvalidPath.Count > 1)
            {
                Color warn = p.Warning;
                warn.a = 0.55f;
                float w = DuctGeometry.WidthFor(_drawing!.DefaultDuctSizeMm, _drawing);
                for (int i = 1; i < input.InvalidPath.Count; i++)
                {
                    TilePos a = input.InvalidPath[i - 1], b = input.InvalidPath[i];
                    if (a.F != input.Floor || b.F != input.Floor) continue;
                    m.Line(DuctGeometry.Centre(a), DuctGeometry.Centre(b), w, ZDuct - 0.05f, warn);
                }
            }
            m.Upload();
        }

        /// <summary>One floor's ghost.</summary>
        void DrawGhost(MeshBuilder m, DuctGeometry g, Color ghost)
        {
            NoteCaps(g);
            foreach (DuctGeometry.Cap c in g.Caps)
            {
                float h = c.Size * 0.5f;
                m.Rect(c.Centre.x - h, c.Centre.y - h, c.Centre.x + h, c.Centre.y + h, ZGhost, ghost);
            }
            foreach (DuctGeometry.Segment s in g.Segments)
                if (ClearOfCaps(s, out Vector2 a, out Vector2 b)) m.Line(a, b, s.Width, ZGhost, ghost);
            foreach (DuctGeometry.Arc a in g.Arcs) m.Arc(a.Centre, DuctGeometry.SmoothRadius, a.Width, a.A0, a.A1, 10, ZGhost, ghost);
        }

        readonly Dictionary<Vector2, float> _capHalf = new Dictionary<Vector2, float>();

        void NoteCaps(DuctGeometry g)
        {
            _capHalf.Clear();
            foreach (DuctGeometry.Cap c in g.Caps) _capHalf[c.Centre] = c.Size * 0.5f;
        }

        /// <summary>
        /// A segment's ends, stopped at the edge of the caps it meets: where two see-through pieces overlap they add up, and the
        /// darker patch at every joint draws the eye. False when nothing is left between the caps.
        /// </summary>
        bool ClearOfCaps(DuctGeometry.Segment s, out Vector2 a, out Vector2 b)
        {
            a = s.A;
            b = s.B;
            Vector2 dir = (b - a).normalized;
            if (_capHalf.TryGetValue(s.A, out float ha)) a += dir * ha;
            if (_capHalf.TryGetValue(s.B, out float hb)) b -= dir * hb;
            return Vector2.Dot(b - a, dir) > 0;
        }

        void DrawNetwork(MeshBuilder m, DuctViewInput input, NetworkView v)
        {
            DuctGeometry g = v.Geometry;
            NoteCaps(g);
            PrepareShades(input, v);
            float fade = input.CeilingInFocus ? 1f : 0.45f;
            Color Col(int seg)
            {
                Color c = SegmentColour(input, v, seg);
                if (v.GhostSegments.Contains(seg)) c.a *= 0.6f;
                c.a *= fade;
                return c;
            }
            foreach (DuctGeometry.Segment s in g.Segments)
            {
                if (ClearOfCaps(s, out Vector2 from, out Vector2 to))
                {
                    if (_shades.TryGetValue(s.Id, out Shade shade)) DrawBlended(m, s, from, to, shade, Col(s.Id).a);
                    else m.Line(from, to, s.Width, ZDuct, Col(s.Id));
                }
                // Each network has its line pattern, so colour is never the only cue (06-fluids.md): dotted, dashed (gaps across
                // the line) or double (a paper stripe down the middle).
                string pattern = _palette!.Pattern(v.Fluid);
                if (pattern == "solid") continue;
                float length = Vector2.Distance(s.A, s.B);
                if (length < 1e-4f) continue;
                Color paper = _palette.Paper;
                paper.a = 0.8f * fade * (v.Active ? 1f : (float)_drawing!.OtherNetworkOpacity);
                Vector2 along = (s.B - s.A) / length;
                switch (pattern)
                {
                    case "dotted":
                        for (float d = 0.2f; d < length; d += 0.4f)
                            m.Disc(Vector2.Lerp(s.A, s.B, d / length), s.Width * 0.22f, 8, ZCap - 0.004f, paper);
                        break;
                    case "dashed":
                        for (float d = 0.35f; d < length - 0.05f; d += 0.5f)
                            m.Line(s.A + along * (d - 0.05f), s.A + along * (d + 0.05f), s.Width * 1.02f, ZCap - 0.004f, paper);
                        break;
                    case "double":
                        m.Line(from, to, s.Width * 0.3f, ZCap - 0.004f, paper);
                        break;
                }
            }
            // Bends and joints take the joint's value, so the segments on either side run into them.
            Color JointCol(int node, int seg)
            {
                Color c = Col(seg);
                if (!_shades.TryGetValue(seg, out Shade shade)) return c;
                Color at = _palette!.Ramp(_heatmap!.Ramp, Joint(node, shade.Mid));
                at.a *= c.a;
                return at;
            }
            foreach (DuctGeometry.Arc a in g.Arcs) m.Arc(a.Centre, DuctGeometry.SmoothRadius, a.Width, a.A0, a.A1, 10, ZDuct, JointCol(a.NodeId, a.SegmentId));
            foreach (DuctGeometry.Cap c in g.Caps)
            {
                float h = c.Size * 0.5f;
                m.Rect(c.Centre.x - h, c.Centre.y - h, c.Centre.x + h, c.Centre.y + h, ZCap, JointCol(c.NodeId, c.SegmentId));
            }
            // Risers: a ring in the shaft with a chevron for each direction the run continues.
            foreach (DuctGeometry.Riser r in g.Risers)
            {
                Color c = Col(r.SegmentId);
                float rad = r.Width * 0.5f + 0.06f;
                m.Arc(r.Centre, rad, 0.08f, 0, 2 * Mathf.PI, 20, ZCap - 0.005f, c);
                if (r.Up) Chevron(m, r.Centre + new Vector2(0, 0.06f), 1, c);
                if (r.Down) Chevron(m, r.Centre - new Vector2(0, 0.06f), -1, c);
            }
        }

        static void Chevron(MeshBuilder m, Vector2 at, int dir, Color c)
        {
            float s = 0.12f * dir;
            m.Line(at + new Vector2(-0.12f, 0), at + new Vector2(0, s), 0.05f, ZCap - 0.01f, c);
            m.Line(at + new Vector2(0.12f, 0), at + new Vector2(0, s), 0.05f, ZCap - 0.01f, c);
        }
    }
}
