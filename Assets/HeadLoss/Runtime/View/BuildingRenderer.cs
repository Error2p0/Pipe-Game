using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Buildings;
using UnityEngine;

namespace HeadLoss.Unity.View
{
    /// <summary>
    /// Static blueprint of the building, built once per building: a base mesh under the ducts (room fills,
    /// grid, walls, intake louvres) and an overlay mesh above them (outlet grilles).
    /// </summary>
    public sealed class BuildingRenderer : MonoBehaviour
    {
        public const float WallThickness = 0.16f;
        MeshBuilder? _mesh, _overlay;

        public void Build(BuildingLayout layout, ViewPalette palette, Material baseMaterial, Material overlayMaterial)
        {
            _mesh ??= new MeshBuilder("Building");
            _overlay ??= new MeshBuilder("BuildingOverlay");
            RenderLayers.Attach(gameObject, _mesh.Mesh, baseMaterial, RenderLayers.Building);
            var top = new GameObject("Grilles");
            top.transform.SetParent(transform, false);
            RenderLayers.Attach(top, _overlay.Mesh, overlayMaterial, RenderLayers.Overlay);

            MeshBuilder m = _mesh;
            m.Clear();
            foreach (RoomDef r in layout.Rooms)
                m.Rect(r.X, r.Y, r.X + r.Width, r.Y + r.Height, 4f, palette.RoomFill(r.Type));

            // Tile grid inside the footprint.
            const float g = 0.02f;
            for (int x = 1; x < layout.Width; x++) m.Rect(x - g, 0, x + g, layout.Height, 3.5f, palette.GridLines);
            for (int y = 1; y < layout.Height; y++) m.Rect(0, y - g, layout.Width, y + g, 3.5f, palette.GridLines);

            // Exterior walls with a gap at each air intake.
            float t = WallThickness;
            for (int x = 0; x < layout.Width; x++)
            {
                if (!HasOpening(layout, x, 0, "south")) m.Rect(x - (x == 0 ? t : 0), -t, x + 1 + (x == layout.Width - 1 ? t : 0), 0, 3f, palette.Walls);
                if (!HasOpening(layout, x, layout.Height - 1, "north")) m.Rect(x - (x == 0 ? t : 0), layout.Height, x + 1 + (x == layout.Width - 1 ? t : 0), layout.Height + t, 3f, palette.Walls);
            }
            for (int y = 0; y < layout.Height; y++)
            {
                if (!HasOpening(layout, 0, y, "west")) m.Rect(-t, y, 0, y + 1, 3f, palette.Walls);
                if (!HasOpening(layout, layout.Width - 1, y, "east")) m.Rect(layout.Width, y, layout.Width + t, y + 1, 3f, palette.Walls);
            }

            foreach (EntryOpeningDef o in layout.EntryOpenings) DrawIntake(m, layout, o, palette);
            m.Upload();
            _overlay.Clear();
            foreach (OutletPlacementDef o in layout.Outlets) DrawVent(_overlay, o, palette);
            _overlay.Upload();
        }

        static bool HasOpening(BuildingLayout layout, int x, int y, string facing)
        {
            foreach (EntryOpeningDef o in layout.EntryOpenings)
                if (o.X == x && o.Y == y && o.Facing == facing) return true;
            return false;
        }

        /// <summary>A louvre in the wall gap: thin slats across the opening.</summary>
        static void DrawIntake(MeshBuilder m, BuildingLayout layout, EntryOpeningDef o, ViewPalette p)
        {
            Color slat = p.Walls;
            slat.a = 0.55f;
            float t = WallThickness;
            for (int i = 0; i < 4; i++)
            {
                float f = 0.15f + i * 0.233f;
                switch (o.Facing)
                {
                    case "west": m.Rect(-t, o.Y + f - 0.03f, 0, o.Y + f + 0.03f, 3f, slat); break;
                    case "east": m.Rect(layout.Width, o.Y + f - 0.03f, layout.Width + t, o.Y + f + 0.03f, 3f, slat); break;
                    case "south": m.Rect(o.X + f - 0.03f, -t, o.X + f + 0.03f, 0, 3f, slat); break;
                    case "north": m.Rect(o.X + f - 0.03f, layout.Height, o.X + f + 0.03f, layout.Height + t, 3f, slat); break;
                }
            }
        }

        /// <summary>A supply grille: outlined square with three bars.</summary>
        static void DrawVent(MeshBuilder m, OutletPlacementDef o, ViewPalette p)
        {
            float x0 = o.X + 0.18f, y0 = o.Y + 0.18f, x1 = o.X + 0.82f, y1 = o.Y + 0.82f;
            Color c = p.Walls;
            m.Rect(x0, y0, x1, y1, 0.9f, p.Paper);
            m.Outline(x0, y0, x1, y1, 0.05f, 0.85f, c);
            Color bar = c;
            bar.a = 0.6f;
            for (int i = 1; i <= 3; i++)
            {
                float y = y0 + (y1 - y0) * i / 4f;
                m.Rect(x0 + 0.08f, y - 0.02f, x1 - 0.08f, y + 0.02f, 0.85f, bar);
            }
        }
    }
}
