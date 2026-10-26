using System.Collections.Generic;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.Session;
using UnityEngine;

namespace HeadLoss.Unity.View
{
    /// <summary>What the duct layer should show this frame.</summary>
    public sealed class DuctViewInput
    {
        public DuctNetworkState Network = new DuctNetworkState();
        /// <summary>Solve matching <see cref="Network"/> (may lag behind after an edit).</summary>
        public NetworkSnapshot? Snapshot;
        /// <summary>Segments drawn as a ghost (part of the run being drawn).</summary>
        public HashSet<int> GhostSegments = new HashSet<int>();
        /// <summary>Invalid run being drawn (tile centres), drawn in the warning colour.</summary>
        public List<TilePos>? InvalidPath;
        public HashSet<int> SelectedSegments = new HashSet<int>();
        public int SelectedNode;
        public int HoverSegment;
        public int HoverNode;
        public string FluidId = "air";
        /// <summary>Part placement: highlight this tile (good colour when the part fits, warning when not).</summary>
        public bool Placing;
        public TilePos PlaceTile;
        public bool PlaceValid;
        /// <summary>Selected / hovered tile of an intake, outlet or inline part (outlined).</summary>
        public TilePos? SelectedTile;
        public TilePos? HoverTile;
        /// <summary>Box selection in progress (world rectangle).</summary>
        public Rect? Box;
    }

    /// <summary>
    /// Procedural line mesh for the duct network: width by diameter, colour from the active heatmap
    /// (or the fluid colour in plain view). Rebuilt only when its inputs change.
    /// </summary>
    public sealed class DuctRenderer : MonoBehaviour
    {
        const float ZDuct = 1.0f, ZCap = 0.99f, ZHighlight = 1.2f;
        MeshBuilder? _mesh;
        ViewPalette? _palette;
        HeatmapState? _heatmap;
        DrawingDef? _drawing;
        public DuctGeometry Geometry { get; } = new DuctGeometry();

        public void Init(ViewPalette palette, HeatmapState heatmap, DrawingDef drawing, Material material)
        {
            _palette = palette;
            _heatmap = heatmap;
            _drawing = drawing;
            _mesh = new MeshBuilder("Ducts");
            RenderLayers.Attach(gameObject, _mesh.Mesh, material, RenderLayers.Ducts);
        }

        /// <summary>Colour of a segment for the active mode.</summary>
        public Color SegmentColour(DuctViewInput input, int segmentId)
        {
            if (_heatmap!.Mode == HeatmapMode.Plain) return _palette!.Fluid(input.FluidId);
            NetworkSnapshot? snap = input.Snapshot;
            if (snap == null || !snap.Compiled.SegmentLink.TryGetValue(segmentId, out int link))
            {
                Color neutral = _palette!.Walls;
                neutral.a = 0.35f;
                return neutral;
            }
            return _palette!.Ramp(_heatmap.Palette, _heatmap.Normalise(snap.Result.Velocity[link]));
        }

        /// <summary>Square ring just inside a tile (the symbol on the tile stays readable).</summary>
        static void TileOutline(MeshBuilder m, TilePos t, Color c) =>
            m.Outline(t.X + 0.03f, t.Y + 0.03f, t.X + 0.97f, t.Y + 0.97f, 0.07f, ZHighlight + 0.04f, c);

        public void Rebuild(DuctViewInput input)
        {
            MeshBuilder m = _mesh!;
            ViewPalette p = _palette!;
            m.Clear();
            Geometry.Build(input.Network, _drawing!);

            Color select = p.Text;
            select.a = 0.75f;
            Color hover = p.Text;
            hover.a = 0.3f;
            foreach (DuctGeometry.Segment s in Geometry.Segments)
            {
                bool selected = input.SelectedSegments.Contains(s.Id);
                if (selected || input.HoverSegment == s.Id)
                    m.Line(s.A, s.B, s.Width + 0.12f, ZHighlight, selected ? select : hover);
            }
            foreach (DuctGeometry.Arc a in Geometry.Arcs)
            {
                if (a.NodeId == input.SelectedNode || a.NodeId == input.HoverNode)
                    m.Arc(a.Centre, DuctGeometry.SmoothRadius, a.Width + 0.12f, a.A0, a.A1, 10, ZHighlight, a.NodeId == input.SelectedNode ? select : hover);
            }
            foreach (DuctGeometry.Cap c in Geometry.Caps)
            {
                if (c.NodeId == input.SelectedNode || c.NodeId == input.HoverNode)
                {
                    float h = c.Size * 0.5f + 0.06f;
                    m.Rect(c.Centre.x - h, c.Centre.y - h, c.Centre.x + h, c.Centre.y + h, ZHighlight, c.NodeId == input.SelectedNode ? select : hover);
                }
            }

            foreach (DuctGeometry.Segment s in Geometry.Segments)
            {
                Color col = SegmentColour(input, s.Id);
                if (input.GhostSegments.Contains(s.Id)) col.a *= 0.6f;
                m.Line(s.A, s.B, s.Width, ZDuct, col);
            }
            foreach (DuctGeometry.Arc a in Geometry.Arcs)
            {
                Color col = SegmentColour(input, a.SegmentId);
                if (input.GhostSegments.Contains(a.SegmentId)) col.a *= 0.6f;
                m.Arc(a.Centre, DuctGeometry.SmoothRadius, a.Width, a.A0, a.A1, 10, ZDuct, col);
            }
            foreach (DuctGeometry.Cap c in Geometry.Caps)
            {
                Color col = SegmentColour(input, c.SegmentId);
                if (input.GhostSegments.Contains(c.SegmentId)) col.a *= 0.6f;
                float h = c.Size * 0.5f;
                m.Rect(c.Centre.x - h, c.Centre.y - h, c.Centre.x + h, c.Centre.y + h, ZCap, col);
            }

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
                    m.Line(DuctGeometry.Centre(input.InvalidPath[i - 1]), DuctGeometry.Centre(input.InvalidPath[i]), w, ZDuct - 0.05f, warn);
            }
            m.Upload();
        }
    }
}
