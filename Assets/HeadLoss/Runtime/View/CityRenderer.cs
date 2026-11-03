using System.Collections.Generic;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.City;
using UnityEngine;

namespace HeadLoss.Unity.View
{
    /// <summary>How a plot is drawn in the city view.</summary>
    public enum PlotLook
    {
        /// <summary>Its tier arrives in a later version.</summary>
        Locked,
        /// <summary>Not built, or built and offline: grey.</summary>
        Offline,
        /// <summary>Online: lit, with a soft breathing glow.</summary>
        Online,
    }

    /// <summary>Roof icons (<c>11-ui-visuals-audio.md</c> §Screens): a shape each, never colour alone.</summary>
    public enum RoofIcon { None, Burst, Weeping, Crew, PanelTripped }

    /// <summary>What the map draws over the plots: mains and cables, corridors, the route being dragged, violations and roof icons.</summary>
    public sealed class CityOverlay
    {
        public UtilityState? Utilities;
        /// <summary>Flow ÷ capacity per main or cable (the last city solve).</summary>
        public Dictionary<int, double>? Utilisation;
        /// <summary>The utilisation heatmap is on.</summary>
        public bool Heatmap;
        /// <summary>Only this utility is drawn ("" = all).</summary>
        public string Filter = "";
        public string HeatmapPalette = "viridis";
        public int SelectedMain;
        public List<CityViolation>? Violations;
        /// <summary>The route being dragged (empty = none).</summary>
        public readonly List<(int x, int y)> Route = new List<(int, int)>();
        public bool RouteValid;
        public string RouteUtility = "water";
        public double RouteDiameterMm;
        public List<CityViolation>? RouteViolations;
        public readonly List<RoofIcon> Icons = new List<RoofIcon>();
        public ViewPalette Palette = null!;
    }

    /// <summary>
    /// The city map (<c>11-ui-visuals-audio.md</c> §Visual style): a static base mesh (ground, district tints,
    /// roads, rail, parks, protected and historic sites, facilities), plot blocks drawn as flat two-tone blocks that
    /// light up when online and stay grey otherwise, and slow cloud shadows drifting over everything. World units are
    /// metres; one city tile is <c>tileMetres</c>.
    /// </summary>
    public sealed class CityRenderer : MonoBehaviour
    {
        MeshBuilder? _base, _plots, _clouds;
        CityLayout _city = null!;
        CityThemeDef _theme = null!;
        float _t;
        readonly List<Vector4> _cloudBlobs = new List<Vector4>();

        Color C(string hex) => ViewPalette.Parse(hex);

        public void Build(CityLayout city, CityThemeDef theme, Material baseMaterial, Material plotMaterial, Material cloudMaterial)
        {
            _city = city;
            _theme = theme;
            _base ??= new MeshBuilder("CityBase");
            _plots ??= new MeshBuilder("CityPlots");
            _clouds ??= new MeshBuilder("CityClouds");
            RenderLayers.Attach(gameObject, _base.Mesh, baseMaterial, RenderLayers.Building);
            var plots = new GameObject("Plots");
            plots.transform.SetParent(transform, false);
            RenderLayers.Attach(plots, _plots.Mesh, plotMaterial, RenderLayers.Ducts);
            var clouds = new GameObject("Clouds");
            clouds.transform.SetParent(transform, false);
            RenderLayers.Attach(clouds, _clouds.Mesh, cloudMaterial, RenderLayers.Equipment);
            BuildBase();

            // A few large soft shadows, placed from the city seed (presentation only).
            var rng = new HeadLoss.Core.Rng(HeadLoss.Core.Seeds.Derive(city.Seed, "clouds"));
            float size = (float)(city.Width * city.TileMetres);
            _cloudBlobs.Clear();
            for (int i = 0; i < 6; i++)
                _cloudBlobs.Add(new Vector4((float)rng.NextDouble() * size, (float)rng.NextDouble() * size, (float)(60 + rng.NextDouble() * 90), (float)rng.NextDouble()));
        }

        float T => (float)_city.TileMetres;

        void BuildBase()
        {
            MeshBuilder m = _base!;
            m.Clear();
            float t = T;
            m.Rect(0, 0, _city.Width * t, _city.Height * t, 5f, C(_theme.Countryside));
            m.Rect(_city.EdgeBand * t, _city.EdgeBand * t, (_city.Width - _city.EdgeBand) * t, (_city.Height - _city.EdgeBand) * t, 4.9f, C(_theme.Ground));

            // District tints, one run per row.
            for (int y = 0; y < _city.Height; y++)
            {
                int x = 0;
                while (x < _city.Width)
                {
                    int d = _city.DistrictAt(x, y), x0 = x;
                    while (x < _city.Width && _city.DistrictAt(x, y) == d) x++;
                    if (d < 0) continue;
                    Color tint = C(_theme.Districts[District.KindId(_city.Districts[d].Kind)]);
                    tint.a = (float)_theme.DistrictAlpha;
                    m.Rect(x0 * t, y * t, x * t, (y + 1) * t, 4.8f, tint);
                }
            }

            Color road = C(_theme.Road), main = C(_theme.MainRoad), rail = C(_theme.Rail);
            Color park = C(_theme.Park), prot = C(_theme.Protected), hist = C(_theme.Historic);
            for (int y = 0; y < _city.Height; y++)
                for (int x = 0; x < _city.Width; x++)
                {
                    CityTile tile = _city.At(x, y);
                    float x0 = x * t, y0 = y * t;
                    if ((tile & CityTile.Park) != 0) m.Rect(x0, y0, x0 + t, y0 + t, 4.5f, park);
                    if ((tile & CityTile.Protected) != 0)
                    {
                        m.Rect(x0, y0, x0 + t, y0 + t, 4.5f, prot);
                        Color edge = park;
                        edge.a = 0.9f;
                        m.Outline(x0 + 0.6f, y0 + 0.6f, x0 + t - 0.6f, y0 + t - 0.6f, 0.5f, 4.45f, edge);
                    }
                    if ((tile & CityTile.Road) != 0)
                    {
                        // A road strip with a verge: the centre square plus an arm toward each road neighbour.
                        bool isMain = (tile & CityTile.MainRoad) != 0;
                        Color rc = isMain ? main : road;
                        float w = isMain ? t * 0.36f : t * 0.28f;
                        float cx = x0 + t * 0.5f, cy = y0 + t * 0.5f;
                        m.Rect(cx - w, cy - w, cx + w, cy + w, 4.4f, rc);
                        if (_city.Has(x + 1, y, CityTile.Road)) m.Rect(cx, cy - w, x0 + t, cy + w, 4.4f, rc);
                        if (_city.Has(x - 1, y, CityTile.Road)) m.Rect(x0, cy - w, cx, cy + w, 4.4f, rc);
                        if (_city.Has(x, y + 1, CityTile.Road)) m.Rect(cx - w, cy, cx + w, y0 + t, 4.4f, rc);
                        if (_city.Has(x, y - 1, CityTile.Road)) m.Rect(cx - w, y0, cx + w, cy, 4.4f, rc);
                    }
                    if ((tile & CityTile.Rail) != 0)
                    {
                        bool horizontal = _city.Has(x - 1, y, CityTile.Rail) || _city.Has(x + 1, y, CityTile.Rail);
                        for (int k = 0; k < 4; k++)
                        {
                            float f = (k + 0.5f) * t / 4;
                            if (horizontal) m.Rect(x0 + f - 0.25f, y0 + t * 0.3f, x0 + f + 0.25f, y0 + t * 0.7f, 4.3f, rail);
                            else m.Rect(x0 + t * 0.3f, y0 + f - 0.25f, x0 + t * 0.7f, y0 + f + 0.25f, 4.3f, rail);
                        }
                        if (horizontal)
                        {
                            m.Rect(x0, y0 + t * 0.38f, x0 + t, y0 + t * 0.42f, 4.25f, rail);
                            m.Rect(x0, y0 + t * 0.58f, x0 + t, y0 + t * 0.62f, 4.25f, rail);
                        }
                        else
                        {
                            m.Rect(x0 + t * 0.38f, y0, x0 + t * 0.42f, y0 + t, 4.25f, rail);
                            m.Rect(x0 + t * 0.58f, y0, x0 + t * 0.62f, y0 + t, 4.25f, rail);
                        }
                    }
                    if ((tile & CityTile.Historic) != 0) Block(m, x0 + 1.2f, y0 + 1.2f, x0 + t - 1.2f, y0 + t - 1.2f, 1.6f, hist, 4.2f);
                }

            foreach (FacilityPlacement f in _city.Facilities)
            {
                Color c = C(_theme.Facilities.TryGetValue(f.Utility, out string? hex) ? hex : _theme.PlotOffline);
                float x0 = f.X * t + 1f, y0 = f.Y * t + 1f, x1 = (f.X + f.Width) * t - 1f, y1 = (f.Y + f.Height) * t - 1f;
                Block(m, x0, y0, x1, y1, 2.4f, c, 4.2f);
                Glyph(m, f.Utility, new Vector2((x0 + x1) * 0.5f, (y0 + y1) * 0.5f + 1.2f), Mathf.Min(x1 - x0, y1 - y0) * 0.3f);
            }
            m.Upload();
        }

        /// <summary>A flat block: roof plus a darker south face (reads as a little height from the top-down view).</summary>
        void Block(MeshBuilder m, float x0, float y0, float x1, float y1, float height, Color roof, float z)
        {
            Color side = roof * (float)_theme.SideShade;
            side.a = roof.a;
            m.Rect(x0, y0, x1, y0 + height, z, side);
            m.Rect(x0, y0 + height, x1, y1 + height * 0.25f, z - 0.01f, roof);
        }

        /// <summary>A simple pale symbol per utility on a facility roof.</summary>
        static void Glyph(MeshBuilder m, string utility, Vector2 c, float r)
        {
            Color g = new Color(1, 1, 1, 0.75f);
            const float z = 4.1f;
            switch (utility)
            {
                case "water":
                    m.Disc(c + Vector2.down * r * 0.2f, r * 0.55f, 16, z, g);
                    m.Quad(new Vector3(c.x - r * 0.48f, c.y - r * 0.05f, z), new Vector3(c.x + r * 0.48f, c.y - r * 0.05f, z), new Vector3(c.x, c.y + r * 0.9f, z), new Vector3(c.x, c.y + r * 0.9f, z), g);
                    break;
                case "gas":
                    m.Quad(new Vector3(c.x - r * 0.5f, c.y - r * 0.6f, z), new Vector3(c.x + r * 0.5f, c.y - r * 0.6f, z), new Vector3(c.x + r * 0.15f, c.y + r * 0.8f, z), new Vector3(c.x - r * 0.15f, c.y + r * 0.2f, z), g);
                    break;
                case "heating":
                    for (int i = -1; i <= 1; i++) m.Rect(c.x + i * r * 0.4f - r * 0.08f, c.y - r * 0.6f, c.x + i * r * 0.4f + r * 0.08f, c.y + r * 0.6f, z, g);
                    break;
                case "power":
                    m.Line(c + new Vector2(r * 0.25f, r * 0.8f), c + new Vector2(-r * 0.2f, 0), r * 0.22f, z, g);
                    m.Line(c + new Vector2(-r * 0.2f, 0), c + new Vector2(r * 0.2f, 0), r * 0.22f, z, g);
                    m.Line(c + new Vector2(r * 0.2f, 0), c + new Vector2(-r * 0.25f, -r * 0.8f), r * 0.22f, z, g);
                    break;
                default:
                    m.Quad(new Vector3(c.x, c.y - r * 0.7f, z), new Vector3(c.x + r * 0.7f, c.y, z), new Vector3(c.x, c.y + r * 0.7f, z), new Vector3(c.x - r * 0.7f, c.y, z), g);
                    break;
            }
        }

        /// <summary>
        /// Redraws the plot blocks: each look, the hovered and selected outlines, and the online glow breathing on a
        /// 4 s cycle (steady when <paramref name="reduceMotion"/>).
        /// </summary>
        public void DrawPlots(IReadOnlyList<PlotLook> looks, int hovered, int selected, int starter, float time, bool reduceMotion, Color outline, CityOverlay? overlay = null)
        {
            MeshBuilder m = _plots!;
            m.Clear();
            float t = T;
            Color offline = C(_theme.PlotOffline), locked = C(_theme.PlotLocked), online = C(_theme.PlotOnline), glow = C(_theme.Glow);
            float breath = reduceMotion ? 0.5f : 0.5f + 0.5f * Mathf.Sin(time * Mathf.PI * 2 / 4f);
            for (int i = 0; i < _city.Plots.Count; i++)
            {
                Plot p = _city.Plots[i];
                float inset = 1.6f;
                float x0 = p.X * t + inset, y0 = p.Y * t + inset, x1 = (p.X + p.Width) * t - inset, y1 = (p.Y + p.Height) * t - inset;
                float height = 1.2f + p.Tier * 0.7f;
                PlotLook look = looks[i];
                if (look == PlotLook.Online)
                {
                    Color g = glow;
                    g.a = 0.10f + 0.12f * breath;
                    m.Rect(x0 - 1.4f, y0 - 1.4f, x1 + 1.4f, y1 + height * 0.25f + 1.4f, 3.95f, g);
                }
                Block(m, x0, y0, x1, y1, height, look == PlotLook.Online ? online : look == PlotLook.Locked ? locked : offline, 3.9f);
                if (i == selected) m.Outline(x0 - 0.9f, y0 - 0.9f, x1 + 0.9f, y1 + height * 0.25f + 0.9f, 0.7f, 3.8f, outline);
                else if (i == hovered) m.Outline(x0 - 0.6f, y0 - 0.6f, x1 + 0.6f, y1 + height * 0.25f + 0.6f, 0.35f, 3.8f, outline);
                if (i == starter && look != PlotLook.Online)
                {
                    Color a = online;
                    a.a = 0.45f + 0.35f * breath;
                    m.Outline(x0 - 2.2f, y0 - 2.2f, x1 + 2.2f, y1 + height * 0.25f + 2.2f, 0.5f, 3.85f, a);
                }
            }
            if (overlay != null) DrawOverlay(m, overlay);
            m.Upload();
        }

        const float ZLines = 3.7f, ZIcons = 3.6f;

        /// <summary>Line width of a main by diameter, or of a cable by capacity, m.</summary>
        static float WidthOf(MainState main) => main.Utility == "power" ? 1.0f : Mathf.Clamp((float)main.DiameterMm / 100f, 0.8f, 3.2f);

        /// <summary>Lines sharing a tile sit apart: water on one side, power on the other.</summary>
        static Vector2 OffsetOf(string utility) => utility == "power" ? new Vector2(1.4f, 1.4f) : new Vector2(-1.4f, -1.4f);

        Vector2 TileCentre(int x, int y) => new Vector2((x + 0.5f) * T, (y + 0.5f) * T);

        /// <summary>Power runs dashed, water solid (colour is never the only cue).</summary>
        static void Stroke(MeshBuilder m, Vector2 a, Vector2 b, float width, string utility, float z, Color c)
        {
            if (utility != "power")
            {
                m.Line(a, b, width, z, c);
                return;
            }
            m.Line(a, Vector2.Lerp(a, b, 0.3f), width, z, c);
            m.Line(Vector2.Lerp(a, b, 0.55f), Vector2.Lerp(a, b, 0.85f), width, z, c);
        }

        Color UtilityColour(CityOverlay o, string utility)
        {
            if (utility == "power") return C(_theme.Facilities.TryGetValue("power", out string? hex) ? hex : _theme.PlotOffline);
            return o.Palette.Fluid(utility);
        }

        void DrawOverlay(MeshBuilder m, CityOverlay o)
        {
            float t = T;
            ViewPalette p = o.Palette;
            if (o.Utilities != null)
            {
                Color corridor = p.Text;
                corridor.a = 0.12f;
                foreach (int[] c in o.Utilities.Corridors)
                    m.Outline(c[0] * t + 0.6f, c[1] * t + 0.6f, (c[0] + 1) * t - 0.6f, (c[1] + 1) * t - 0.6f, 0.6f, ZLines + 0.05f, corridor);
                foreach (MainState main in o.Utilities.Mains)
                {
                    if (o.Filter.Length > 0 && o.Filter != main.Utility) continue;
                    Color c = UtilityColour(o, main.Utility);
                    if (o.Heatmap)
                    {
                        double use = o.Utilisation != null && o.Utilisation.TryGetValue(main.Id, out double v) ? v : 0;
                        c = use >= 1 ? p.Warning : p.Ramp(o.HeatmapPalette, (float)use);
                    }
                    Vector2 off = OffsetOf(main.Utility);
                    float w = WidthOf(main);
                    if (main.Id == o.SelectedMain)
                    {
                        Color sel = p.Text;
                        sel.a = 0.7f;
                        for (int i = 1; i < main.Tiles.Count; i++)
                            m.Line(TileCentre(main.Tiles[i - 1][0], main.Tiles[i - 1][1]) + off, TileCentre(main.Tiles[i][0], main.Tiles[i][1]) + off, w + 1.0f, ZLines + 0.02f, sel);
                    }
                    for (int i = 1; i < main.Tiles.Count; i++)
                        Stroke(m, TileCentre(main.Tiles[i - 1][0], main.Tiles[i - 1][1]) + off, TileCentre(main.Tiles[i][0], main.Tiles[i][1]) + off, w, main.Utility, ZLines, c);
                }
            }
            if (o.Violations != null)
                foreach (CityViolation v in o.Violations) ViolationTile(m, v.X, v.Y, p.Danger, 0.55f);
            if (o.Route.Count > 0)
            {
                Color c = o.RouteValid ? UtilityColour(o, o.RouteUtility) : p.Warning;
                c.a = 0.85f;
                Vector2 off = OffsetOf(o.RouteUtility);
                float w = o.RouteUtility == "power" ? 1.0f : Mathf.Clamp((float)o.RouteDiameterMm / 100f, 0.8f, 3.2f);
                m.Disc(TileCentre(o.Route[0].x, o.Route[0].y) + off, w, 12, ZLines - 0.02f, c);
                for (int i = 1; i < o.Route.Count; i++)
                    Stroke(m, TileCentre(o.Route[i - 1].x, o.Route[i - 1].y) + off, TileCentre(o.Route[i].x, o.Route[i].y) + off, w, o.RouteUtility, ZLines - 0.02f, c);
            }
            if (o.RouteViolations != null)
                foreach (CityViolation v in o.RouteViolations) ViolationTile(m, v.X, v.Y, p.Danger, 1f);

            for (int i = 0; i < o.Icons.Count && i < _city.Plots.Count; i++)
                if (o.Icons[i] != RoofIcon.None) RoofIconAt(m, _city.Plots[i], o.Icons[i], p);
        }

        void ViolationTile(MeshBuilder m, int x, int y, Color c, float alpha)
        {
            float t = T;
            c.a = alpha;
            m.Outline(x * t + 0.4f, y * t + 0.4f, (x + 1) * t - 0.4f, (y + 1) * t - 0.4f, 0.7f, ZLines - 0.04f, c);
        }

        /// <summary>A small badge floating over the roof, with a glyph that says what happened.</summary>
        void RoofIconAt(MeshBuilder m, Plot plot, RoofIcon icon, ViewPalette p)
        {
            float t = T;
            var c = new Vector2((plot.X + plot.Width * 0.5f) * t, (plot.Y + plot.Height) * t + 1.5f);
            const float r = 3.2f;
            Color back = icon == RoofIcon.Burst ? p.Danger : icon == RoofIcon.Crew ? p.Good : p.Warning;
            m.Disc(c, r, 20, ZIcons, back);
            Color g = new Color(1, 1, 1, 0.95f);
            const float z = ZIcons - 0.02f;
            switch (icon)
            {
                case RoofIcon.Burst:
                    // A spray: three lines out of a point.
                    m.Line(c + new Vector2(0, -r * 0.5f), c + new Vector2(0, r * 0.6f), 0.55f, z, g);
                    m.Line(c + new Vector2(0, -r * 0.5f), c + new Vector2(-r * 0.5f, r * 0.35f), 0.55f, z, g);
                    m.Line(c + new Vector2(0, -r * 0.5f), c + new Vector2(r * 0.5f, r * 0.35f), 0.55f, z, g);
                    break;
                case RoofIcon.Weeping:
                    m.Disc(c + Vector2.down * r * 0.2f, r * 0.33f, 12, z, g);
                    m.Quad(new Vector3(c.x - r * 0.3f, c.y - r * 0.1f, z), new Vector3(c.x + r * 0.3f, c.y - r * 0.1f, z), new Vector3(c.x, c.y + r * 0.55f, z), new Vector3(c.x, c.y + r * 0.55f, z), g);
                    break;
                case RoofIcon.Crew:
                    // A cross of tools.
                    m.Line(c + new Vector2(-r * 0.5f, -r * 0.5f), c + new Vector2(r * 0.5f, r * 0.5f), 0.6f, z, g);
                    m.Line(c + new Vector2(-r * 0.5f, r * 0.5f), c + new Vector2(r * 0.5f, -r * 0.5f), 0.6f, z, g);
                    break;
                case RoofIcon.PanelTripped:
                    // A bolt.
                    m.Line(c + new Vector2(r * 0.2f, r * 0.6f), c + new Vector2(-r * 0.2f, 0), 0.55f, z, g);
                    m.Line(c + new Vector2(-r * 0.2f, 0), c + new Vector2(r * 0.2f, 0), 0.55f, z, g);
                    m.Line(c + new Vector2(r * 0.2f, 0), c + new Vector2(-r * 0.2f, -r * 0.6f), 0.55f, z, g);
                    break;
            }
        }

        /// <summary>Moves the cloud shadows (still when <paramref name="reduceMotion"/>).</summary>
        public void DrawClouds(float dt, bool reduceMotion)
        {
            if (!reduceMotion) _t += dt;
            MeshBuilder m = _clouds!;
            m.Clear();
            float size = (float)(_city.Width * _city.TileMetres);
            Color c = C(_theme.Cloud);
            var wind = new Vector2(3.2f, 1.1f);
            foreach (Vector4 b in _cloudBlobs)
            {
                float speed = 0.7f + b.w * 0.6f;
                float x = Mathf.Repeat(b.x + wind.x * speed * _t, size + 2 * b.z) - b.z, y = Mathf.Repeat(b.y + wind.y * speed * _t, size + 2 * b.z) - b.z;
                // Soft edge: stacked discs of falling opacity.
                for (int k = 0; k < 4; k++)
                {
                    Color s = c;
                    s.a = (float)_theme.CloudAlpha * 0.4f;
                    float r = b.z * (1 - k * 0.2f);
                    m.Disc(new Vector2(x, y), r, 28, 2f - k * 0.01f, s);
                    m.Disc(new Vector2(x + b.z * 0.6f, y + b.z * 0.15f), r * 0.7f, 24, 2f - k * 0.01f, s);
                }
            }
            m.Upload();
        }
    }
}
