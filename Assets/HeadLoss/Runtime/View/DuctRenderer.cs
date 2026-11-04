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
        /// <summary>Ghosts on the floors above and below (20% opacity, no interaction).</summary>
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

        public void Init(ViewPalette palette, HeatmapState heatmap, DrawingDef drawing, Material material)
        {
            _palette = palette;
            _heatmap = heatmap;
            _drawing = drawing;
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
                plain.a = 0.55f;
                return plain;
            }
            // Concentration colours the rooms only; the pipes keep their plain colour under it.
            if (_heatmap!.Mode == HeatmapMode.Plain || _heatmap.Mode == HeatmapMode.Concentration) return plain;
            if (_heatmap.Mode == HeatmapMode.Stress)
            {
                double s = input.Stress?.Invoke(view.Fluid, segmentId) ?? 0;
                return s >= 1 ? _palette.Warning : _palette.Ramp(_heatmap.Palette, _heatmap.Normalise(s));
            }
            FluidSolve? solve = view.Solve;
            if (solve == null || !solve.Compiled.SegmentLink.TryGetValue(segmentId, out int link))
            {
                Color neutral = _palette.Walls;
                neutral.a = 0.35f;
                return neutral;
            }
            return _palette.Ramp(_heatmap.Palette, _heatmap.Normalise(Value(_heatmap.Mode, solve, link)));
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
                ghost.a = 0.2f;
                foreach (DuctGeometry g in new[] { v.Above, v.Below })
                {
                    foreach (DuctGeometry.Segment s in g.Segments) m.Line(s.A, s.B, s.Width, ZGhost, ghost);
                    foreach (DuctGeometry.Cap c in g.Caps)
                    {
                        float h = c.Size * 0.5f;
                        m.Rect(c.Centre.x - h, c.Centre.y - h, c.Centre.x + h, c.Centre.y + h, ZGhost, ghost);
                    }
                }
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

        void DrawNetwork(MeshBuilder m, DuctViewInput input, NetworkView v)
        {
            DuctGeometry g = v.Geometry;
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
                m.Line(s.A, s.B, s.Width, ZDuct, Col(s.Id));
                // Gas runs dotted, so colour is never the only cue (06-fluids.md).
                if (v.Fluid != BuildingState.GasFluid) continue;
                float length = Vector2.Distance(s.A, s.B);
                Color dot = _palette!.Paper;
                dot.a = 0.8f * fade;
                for (float d = 0.2f; d < length; d += 0.4f)
                    m.Disc(Vector2.Lerp(s.A, s.B, d / length), s.Width * 0.22f, 8, ZCap - 0.004f, dot);
            }
            foreach (DuctGeometry.Arc a in g.Arcs) m.Arc(a.Centre, DuctGeometry.SmoothRadius, a.Width, a.A0, a.A1, 10, ZDuct, Col(a.SegmentId));
            foreach (DuctGeometry.Cap c in g.Caps)
            {
                float h = c.Size * 0.5f;
                m.Rect(c.Centre.x - h, c.Centre.y - h, c.Centre.x + h, c.Centre.y + h, ZCap, Col(c.SegmentId));
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
