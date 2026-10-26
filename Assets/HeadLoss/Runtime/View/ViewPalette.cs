using System.Collections.Generic;
using HeadLoss.Core;
using HeadLoss.Core.Data;
using UnityEngine;

namespace HeadLoss.Unity.View
{
    /// <summary>Theme and heatmap colours converted from data.</summary>
    public sealed class ViewPalette
    {
        public Color Paper, GridLines, Walls, Text, Warning, Danger;
        public float RoomFillAlpha;
        readonly Dictionary<string, Color> _roomFills = new Dictionary<string, Color>();
        readonly Dictionary<string, Color> _fluids = new Dictionary<string, Color>();
        readonly Dictionary<string, Color[]> _ramps = new Dictionary<string, Color[]>();

        public ViewPalette(DataRepository data)
        {
            var t = data.Game.Theme;
            Paper = Parse(t.Paper);
            GridLines = Parse(t.GridLines);
            Walls = Parse(t.Walls);
            Text = Parse(t.Text);
            Warning = Parse(t.Warning);
            Danger = Parse(t.Danger);
            RoomFillAlpha = (float)t.RoomFillAlpha;
            foreach (var kv in t.RoomFills) _roomFills[kv.Key] = Parse(kv.Value);
            foreach (var f in data.Fluids) _fluids[f.Id] = Parse(f.Colour);
            foreach (var kv in data.Balancing.Heatmap.Palettes)
            {
                var stops = new Color[kv.Value.Count];
                for (int i = 0; i < stops.Length; i++) stops[i] = Parse(kv.Value[i]);
                _ramps[kv.Key] = stops;
            }
        }

        public static Color Parse(string hex)
        {
            HexColor.TryParse(hex, out double r, out double g, out double b, out double a);
            return new Color((float)r, (float)g, (float)b, (float)a);
        }

        public Color RoomFill(string type)
        {
            Color c = _roomFills.TryGetValue(type, out Color v) ? v : GridLines;
            c.a = RoomFillAlpha;
            return c;
        }

        public Color Fluid(string id) => _fluids.TryGetValue(id, out Color c) ? c : Walls;

        /// <summary>Samples a palette ramp at t ∈ [0, 1] (linear between evenly spaced stops).</summary>
        public Color Ramp(string palette, float t)
        {
            if (!_ramps.TryGetValue(palette, out Color[] stops)) return Walls;
            t = Mathf.Clamp01(t) * (stops.Length - 1);
            int i = Mathf.Min((int)t, stops.Length - 2);
            return Color.Lerp(stops[i], stops[i + 1], t - i);
        }
    }
}
