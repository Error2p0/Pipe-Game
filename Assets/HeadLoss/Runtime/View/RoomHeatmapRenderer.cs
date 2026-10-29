using HeadLoss.Game.Thermal;
using HeadLoss.Sim.Rooms;
using UnityEngine;

namespace HeadLoss.Unity.View
{
    /// <summary>
    /// The room part of the heatmap (<c>11-ui-visuals-audio.md</c> §Heatmap modes): one RGFloat texture at room-grid
    /// resolution over the footprint, coloured by the palette ramp in the shader. Velocity, temperature and density
    /// colour the rooms; two-layer fields show the upper layer, the lower one, or the larger of the two (H).
    /// Rewritten only when the room grid steps or the heatmap changes, into a buffer kept between frames.
    /// </summary>
    public sealed class RoomHeatmapRenderer : MonoBehaviour
    {
        Texture2D? _field, _ramp;
        Material? _material;
        MeshRenderer? _renderer;
        Mesh? _mesh;
        float[] _pixels = System.Array.Empty<float>();
        string _rampPalette = "";
        int _climateRev = -1, _heatmapRev = -1;

        public void Init(Material template, RoomGridLayout grid, float widthM, float heightM)
        {
            _material = new Material(template) { name = "HeadLoss RoomHeatmap" };
            _field = new Texture2D(grid.NX, grid.NY, TextureFormat.RGFloat, false, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            _ramp = new Texture2D(256, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            _pixels = new float[grid.NX * grid.NY * 2];
            _material.SetTexture("_Field", _field);
            _material.SetTexture("_Ramp", _ramp);
            _mesh = new Mesh { name = "RoomHeatmap" };
            _mesh.SetVertices(new[] { new Vector3(0, 0, 0), new Vector3(widthM, 0, 0), new Vector3(widthM, heightM, 0), new Vector3(0, heightM, 0) });
            _mesh.SetUVs(0, new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
            _mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            _renderer = RenderLayers.Attach(gameObject, _mesh, _material, RenderLayers.RoomHeat);
        }

        /// <summary>Redraws when the rooms stepped or the heatmap changed; hides itself for modes that leave rooms neutral.</summary>
        public void Refresh(BuildingClimate climate, HeatmapState hm, ViewPalette palette, float alpha)
        {
            if (_renderer == null || _field == null || _ramp == null || _material == null) return;
            bool show = HeatmapState.ColoursRooms(hm.Mode);
            _renderer.enabled = show;
            if (!show || (climate.Revision == _climateRev && hm.Revision == _heatmapRev)) return;
            _climateRev = climate.Revision;
            _heatmapRev = hm.Revision;
            if (_rampPalette != hm.Palette)
            {
                for (int i = 0; i < 256; i++) _ramp.SetPixel(i, 0, palette.Ramp(hm.Palette, i / 255f));
                _ramp.Apply();
                _rampPalette = hm.Palette;
            }
            _material.SetFloat("_Alpha", alpha);
            RoomGridLayout g = climate.Grid;
            RoomGridState s = climate.State;
            int n = g.Cells;
            for (int c = 0; c < g.NX * g.NY; c++)
            {
                if (g.Room[c] < 0)
                {
                    _pixels[2 * c] = 0;
                    _pixels[2 * c + 1] = 0;
                    continue;
                }
                float t = hm.NormaliseRoom(Value(climate, hm, c, n));
                _pixels[2 * c] = t;
                _pixels[2 * c + 1] = 1;
            }
            _field.SetPixelData(_pixels, 0);
            _field.Apply(false);
        }

        /// <summary>A cell's value in display units for the mode and layer.</summary>
        public static float Value(BuildingClimate climate, HeatmapState hm, int c, int n)
        {
            RoomGridState s = climate.State;
            switch (hm.Mode)
            {
                case HeatmapMode.Velocity:
                    return Mathf.Sqrt(s.Vx[c] * s.Vx[c] + s.Vy[c] * s.Vy[c]);
                case HeatmapMode.Temperature:
                    return Pick(hm.Layer, s.T[c], s.T[n + c]);
                case HeatmapMode.Density:
                    float lo = RoomGridMath.Density(climate.Params, s.T[c], s.Co2[c]), up = RoomGridMath.Density(climate.Params, s.T[n + c], s.Co2[n + c]);
                    return Pick(hm.Layer, lo, up);
                default:
                    return 0;
            }
        }

        static float Pick(RoomLayer layer, float lower, float upper) =>
            layer == RoomLayer.Upper ? upper : layer == RoomLayer.Lower ? lower : Mathf.Max(lower, upper);

        void OnDestroy()
        {
            if (_field != null) Destroy(_field);
            if (_ramp != null) Destroy(_ramp);
            if (_material != null) Destroy(_material);
            if (_mesh != null) Destroy(_mesh);
        }
    }
}
