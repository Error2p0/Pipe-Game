using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Buildings;
using UnityEngine;

namespace HeadLoss.Unity.View
{
    /// <summary>
    /// Blueprint of the floor on show: a base mesh (room fills, grid, shafts, stairs, furniture) under the room heatmap, a
    /// wall mesh above it (walls, doors, fire walls, windows, columns, entry openings), and an overlay mesh above the
    /// ducts (vent grilles, taps). Rebuilt when the floor changes.
    /// </summary>
    public sealed class BuildingRenderer : MonoBehaviour
    {
        public const float WallThickness = 0.16f;
        public const float InteriorWall = 0.1f;
        MeshBuilder? _mesh, _walls, _overlay;
        BuildingLayout? _layout;
        ViewPalette? _palette;

        public void Build(BuildingLayout layout, ViewPalette palette, Material baseMaterial, Material wallMaterial, Material overlayMaterial, int floor = 0)
        {
            _layout = layout;
            _palette = palette;
            _mesh = new MeshBuilder("Building");
            _walls = new MeshBuilder("BuildingWalls");
            _overlay = new MeshBuilder("BuildingOverlay");
            RenderLayers.Attach(gameObject, _mesh.Mesh, baseMaterial, RenderLayers.Building);
            var wallsGo = new GameObject("Walls");
            wallsGo.transform.SetParent(transform, false);
            RenderLayers.Attach(wallsGo, _walls.Mesh, wallMaterial, RenderLayers.Walls);
            var top = new GameObject("Grilles");
            top.transform.SetParent(transform, false);
            RenderLayers.Attach(top, _overlay.Mesh, overlayMaterial, RenderLayers.Overlay);
            ShowFloor(floor);
        }

        /// <summary>Redraws the blueprint for one floor.</summary>
        public void ShowFloor(int floor)
        {
            BuildingLayout layout = _layout!;
            ViewPalette palette = _palette!;
            MeshBuilder m = _mesh!;
            m.Clear();
            foreach (RoomDef r in layout.Rooms)
            {
                if (r.Floor != floor) continue;
                m.Rect(r.X, r.Y, r.X + r.Width, r.Y + r.Height, 4f, palette.RoomFill(r.Type));
                if (r.Type == "shaft") Hatch(m, r, palette);
                else if (r.Type == "stairwell") Stairs(m, r, palette);
            }
            foreach (FurnitureDef f in layout.Furniture)
            {
                if (f.Floor != floor) continue;
                Color c = palette.Walls;
                c.a = 0.12f;
                m.Rect(f.X + 0.1f, f.Y + 0.1f, f.X + f.Width - 0.1f, f.Y + f.Height - 0.1f, 3.8f, c);
            }

            // Tile grid inside the footprint.
            const float g = 0.02f;
            for (int x = 0; x < layout.Width; x++)
                for (int y = 0; y < layout.Height; y++)
                {
                    var t = new TilePos(x, y, floor);
                    if (!layout.IsInside(t)) continue;
                    if (x > 0 && layout.IsInside(new TilePos(x - 1, y, floor))) m.Rect(x - g, y, x + g, y + 1, 3.5f, palette.GridLines);
                    if (y > 0 && layout.IsInside(new TilePos(x, y - 1, floor))) m.Rect(x, y - g, x + 1, y + g, 3.5f, palette.GridLines);
                }
            m.Upload();

            m = _walls!;
            m.Clear();
            ExteriorWalls(m, layout, floor, palette);
            DrawInteriorWalls(m, layout, floor, palette);
            foreach (WindowDef w in layout.Windows) if (w.Floor == floor) DrawWindow(m, w, palette);
            foreach (EntryOpeningDef o in layout.EntryOpenings) if (o.Floor == floor) DrawOpening(m, o, palette);
            foreach (TileDef c in layout.Columns)
                if (layout.IsInside(new TilePos(c.X, c.Y, floor))) m.Rect(c.X + 0.2f, c.Y + 0.2f, c.X + 0.8f, c.Y + 0.8f, 2.9f, palette.Walls);
            m.Upload();

            _overlay!.Clear();
            foreach (OutletPlacementDef o in layout.Outlets)
            {
                if (o.Floor != floor) continue;
                string fluid = BuildingState.OutletFluid(o.Type);
                if (fluid == "water") DrawTap(_overlay, o, palette);
                else if (fluid == BuildingState.GasFluid) DrawStove(_overlay, o, palette);
                else DrawVent(_overlay, o, palette);
            }
            _overlay.Upload();
        }

        static void Hatch(MeshBuilder m, RoomDef r, ViewPalette p)
        {
            Color c = p.Walls;
            c.a = 0.25f;
            for (float d = 0.25f; d < r.Width + r.Height; d += 0.5f)
            {
                var a = new Vector2(r.X + Mathf.Min(d, r.Width), r.Y + Mathf.Max(0, d - r.Width));
                var b = new Vector2(r.X + Mathf.Max(0, d - r.Height), r.Y + Mathf.Min(d, r.Height));
                m.Line(a, b, 0.03f, 3.7f, c);
            }
        }

        static void Stairs(MeshBuilder m, RoomDef r, ViewPalette p)
        {
            Color c = p.Walls;
            c.a = 0.3f;
            bool alongY = r.Height >= r.Width;
            int steps = (alongY ? r.Height : r.Width) * 3;
            for (int i = 1; i < steps; i++)
            {
                float f = i / 3f;
                if (alongY) m.Rect(r.X + 0.1f, r.Y + f - 0.015f, r.X + r.Width - 0.1f, r.Y + f + 0.015f, 3.7f, c);
                else m.Rect(r.X + f - 0.015f, r.Y + 0.1f, r.X + f + 0.015f, r.Y + r.Height - 0.1f, 3.7f, c);
            }
        }

        /// <summary>Exterior walls along every tile side that faces outside (L and U shapes included), with a gap at each opening.</summary>
        static void ExteriorWalls(MeshBuilder m, BuildingLayout layout, int floor, ViewPalette p)
        {
            float t = WallThickness;
            for (int x = 0; x < layout.Width; x++)
                for (int y = 0; y < layout.Height; y++)
                {
                    var tile = new TilePos(x, y, floor);
                    if (!layout.IsInside(tile)) continue;
                    if (!layout.IsInside(new TilePos(x, y - 1, floor)) && !HasOpening(layout, tile, "south")) m.Rect(x - t, y - t, x + 1 + t, y, 3f, p.Walls);
                    if (!layout.IsInside(new TilePos(x, y + 1, floor)) && !HasOpening(layout, tile, "north")) m.Rect(x - t, y + 1, x + 1 + t, y + 1 + t, 3f, p.Walls);
                    if (!layout.IsInside(new TilePos(x - 1, y, floor)) && !HasOpening(layout, tile, "west")) m.Rect(x - t, y, x, y + 1, 3f, p.Walls);
                    if (!layout.IsInside(new TilePos(x + 1, y, floor)) && !HasOpening(layout, tile, "east")) m.Rect(x + 1, y, x + 1 + t, y + 1, 3f, p.Walls);
                }
        }

        /// <summary>Walls between tiles of different rooms; a door leaves a gap with two short jambs; fire walls are thicker.</summary>
        static void DrawInteriorWalls(MeshBuilder m, BuildingLayout layout, int floor, ViewPalette p)
        {
            float h = InteriorWall * 0.5f;
            Color jamb = p.Walls;
            jamb.a = 0.6f;
            Color shaftWall = p.Walls;
            shaftWall.a = 0.45f;
            for (int x = 0; x < layout.Width; x++)
                for (int y = 0; y < layout.Height; y++)
                {
                    var t = new TilePos(x, y, floor);
                    int r = layout.RoomIndexAt(t);
                    if (r < 0) continue;
                    foreach ((TilePos o, bool east) in new[] { (new TilePos(x + 1, y, floor), true), (new TilePos(x, y + 1, floor), false) })
                    {
                        int ro = layout.RoomIndexAt(o);
                        if (ro < 0 || ro == r) continue;
                        bool fire = layout.IsFireWall(t, o);
                        float hw = fire ? h * 2.2f : h;
                        Color c = layout.WallBetween(t, o) == WallKind.Shaft ? shaftWall : p.Walls;
                        if (!layout.HasDoor(t, o))
                        {
                            if (east) m.Rect(x + 1 - hw, y - hw, x + 1 + hw, y + 1 + hw, 3f, c);
                            else m.Rect(x - hw, y + 1 - hw, x + 1 + hw, y + 1 + hw, 3f, c);
                        }
                        else if (east)
                        {
                            m.Rect(x + 1 - hw, y - hw, x + 1 + hw, y + 0.12f, 3f, c);
                            m.Rect(x + 1 - hw, y + 0.88f, x + 1 + hw, y + 1 + hw, 3f, c);
                            m.Rect(x + 1 - 0.02f, y + 0.12f, x + 1 + 0.02f, y + 0.88f, 3.1f, jamb);
                        }
                        else
                        {
                            m.Rect(x - hw, y + 1 - hw, x + 0.12f, y + 1 + hw, 3f, c);
                            m.Rect(x + 0.88f, y + 1 - hw, x + 1 + hw, y + 1 + hw, 3f, c);
                            m.Rect(x + 0.12f, y + 1 - 0.02f, x + 0.88f, y + 1 + 0.02f, 3.1f, jamb);
                        }
                    }
                }
        }

        /// <summary>A window: a pale pane in the exterior wall with a thin glazing line (blueprint style).</summary>
        static void DrawWindow(MeshBuilder m, WindowDef w, ViewPalette p)
        {
            float t = WallThickness, a = 0.2f, b = 0.8f;
            Color pane = p.Paper, glaze = p.Walls;
            glaze.a = 0.55f;
            switch (w.Side)
            {
                case "west": m.Rect(w.X - t, w.Y + a, w.X, w.Y + b, 2.9f, pane); m.Rect(w.X - t * 0.6f, w.Y + a, w.X - t * 0.4f, w.Y + b, 2.8f, glaze); break;
                case "east": m.Rect(w.X + 1, w.Y + a, w.X + 1 + t, w.Y + b, 2.9f, pane); m.Rect(w.X + 1 + t * 0.4f, w.Y + a, w.X + 1 + t * 0.6f, w.Y + b, 2.8f, glaze); break;
                case "south": m.Rect(w.X + a, w.Y - t, w.X + b, w.Y, 2.9f, pane); m.Rect(w.X + a, w.Y - t * 0.6f, w.X + b, w.Y - t * 0.4f, 2.8f, glaze); break;
                case "north": m.Rect(w.X + a, w.Y + 1, w.X + b, w.Y + 1 + t, 2.9f, pane); m.Rect(w.X + a, w.Y + 1 + t * 0.4f, w.X + b, w.Y + 1 + t * 0.6f, 2.8f, glaze); break;
            }
        }

        static bool HasOpening(BuildingLayout layout, TilePos t, string facing)
        {
            foreach (EntryOpeningDef o in layout.EntryOpenings)
                if (o.X == t.X && o.Y == t.Y && o.Floor == t.F && o.Facing == facing) return true;
            return false;
        }

        /// <summary>In the wall gap: louvre slats (air), a pipe stub with a valve (water) or a cable gland (power).</summary>
        static void DrawOpening(MeshBuilder m, EntryOpeningDef o, ViewPalette p)
        {
            float t = WallThickness;
            Color slat = o.Utility == "air" ? p.Walls : p.Fluid(o.Utility == "water" ? "water" : o.Utility == "gas" ? BuildingState.GasFluid : "air");
            if (o.Utility == "power") slat = p.Warning;
            slat.a = 0.75f;
            (float x0, float y0, float x1, float y1) = o.Facing switch
            {
                "west" => (o.X - t, (float)o.Y, (float)o.X, o.Y + 1f),
                "east" => (o.X + 1f, (float)o.Y, o.X + 1 + t, o.Y + 1f),
                "south" => ((float)o.X, o.Y - t, o.X + 1f, (float)o.Y),
                _ => ((float)o.X, o.Y + 1f, o.X + 1f, o.Y + 1 + t),
            };
            bool vertical = o.Facing == "west" || o.Facing == "east";
            if (o.Utility == "air")
            {
                for (int i = 0; i < 4; i++)
                {
                    float f = 0.15f + i * 0.233f;
                    if (vertical) m.Rect(x0, o.Y + f - 0.03f, x1, o.Y + f + 0.03f, 3f, slat);
                    else m.Rect(o.X + f - 0.03f, y0, o.X + f + 0.03f, y1, 3f, slat);
                }
                return;
            }
            // Water and power: a solid block in the gap, narrower than the tile.
            if (vertical) m.Rect(x0, o.Y + 0.3f, x1, o.Y + 0.7f, 3f, slat);
            else m.Rect(o.X + 0.3f, y0, o.X + 0.7f, y1, 3f, slat);
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

        /// <summary>A gas stove: a square hob with a ring burner, in the gas colour.</summary>
        static void DrawStove(MeshBuilder m, OutletPlacementDef o, ViewPalette p)
        {
            float x0 = o.X + 0.14f, y0 = o.Y + 0.14f, x1 = o.X + 0.86f, y1 = o.Y + 0.86f;
            m.Rect(x0, y0, x1, y1, 0.9f, p.Paper);
            m.Outline(x0, y0, x1, y1, 0.05f, 0.85f, p.Walls);
            var c = new Vector2(o.X + 0.5f, o.Y + 0.5f);
            Color gas = p.Fluid(BuildingState.GasFluid);
            m.Arc(c, 0.2f, 0.07f, 0, 2 * Mathf.PI, 18, 0.84f, gas);
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI / 2 + Mathf.PI / 4;
                var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                m.Line(c + d * 0.25f, c + d * 0.31f, 0.04f, 0.84f, gas);
            }
        }

        /// <summary>A tap: a round basin with the spout over it, in the water colour.</summary>
        static void DrawTap(MeshBuilder m, OutletPlacementDef o, ViewPalette p)
        {
            var c = new Vector2(o.X + 0.5f, o.Y + 0.5f);
            Color water = p.Fluid("water");
            m.Arc(c, 0.26f, 0.42f, 0, 2 * Mathf.PI, 18, 0.9f, p.Paper);
            m.Arc(c, 0.3f, 0.06f, 0, 2 * Mathf.PI, 18, 0.85f, p.Walls);
            m.Rect(c.x - 0.04f, c.y - 0.02f, c.x + 0.04f, c.y + 0.3f, 0.84f, water);
            m.Rect(c.x - 0.12f, c.y - 0.06f, c.x + 0.12f, c.y + 0.02f, 0.84f, water);
        }
    }
}
