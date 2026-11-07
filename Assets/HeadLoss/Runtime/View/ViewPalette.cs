using System.Collections.Generic;
using HeadLoss.Core;
using HeadLoss.Core.Data;
using UnityEngine;

namespace HeadLoss.Unity.View
{
    /// <summary>Theme and heatmap colours converted from data.</summary>
    public sealed class ViewPalette
    {
        public Color Paper, GridLines, Walls, Text, Warning, Danger, Good;
        public float RoomFillAlpha;
        readonly Dictionary<string, Color> _roomFills = new Dictionary<string, Color>();
        readonly Dictionary<string, Color> _fluids = new Dictionary<string, Color>();
        readonly Dictionary<string, string> _patterns = new Dictionary<string, string>();
        readonly Dictionary<string, Color[]> _ramps = new Dictionary<string, Color[]>();

        /// <summary>The theme the colours come from (light or dark, settings).</summary>
        public HeadLoss.Core.Data.Defs.ThemeDef Theme { get; }

        public ViewPalette(DataRepository data, string theme = "light")
        {
            var t = data.Game.ThemeFor(theme);
            Theme = t;
            Paper = Parse(t.Paper);
            GridLines = Parse(t.GridLines);
            Walls = Parse(t.Walls);
            Text = Parse(t.Text);
            Warning = Parse(t.Warning);
            Danger = Parse(t.Danger);
            Good = Parse(t.Good);
            RoomFillAlpha = (float)t.RoomFillAlpha;
            foreach (var kv in t.RoomFills) _roomFills[kv.Key] = Parse(kv.Value);
            foreach (var f in data.Fluids) _fluids[f.Id] = Parse(f.Colour);
            // A network takes its fluid's colour unless it has its own (the air exhaust).
            foreach (var n in data.Networks)
            {
                _fluids[n.Id] = Parse(n.Colour.Length > 0 ? n.Colour : data.Fluid(n.Fluid).Colour);
                _patterns[n.Id] = n.LinePattern.Length > 0 ? n.LinePattern : data.Fluid(n.Fluid).LinePattern;
            }
            foreach (var kv in data.Balancing.Heatmap.Palettes)
            {
                var stops = new Color[kv.Value.Count];
                for (int i = 0; i < stops.Length; i++) stops[i] = Parse(kv.Value[i]);
                _ramps[kv.Key] = stops;
            }
            var diverging = data.Balancing.Heatmap.DivergingPalette;
            if (diverging.Count >= 2)
            {
                var stops = new Color[diverging.Count];
                for (int i = 0; i < stops.Length; i++) stops[i] = Parse(diverging[i]);
                _ramps[HeatmapState.DivergingRamp] = stops;
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

        /// <summary>Plain-view colour of a network (or a fluid).</summary>
        public Color Fluid(string id) => _fluids.TryGetValue(id, out Color c) ? c : Walls;

        /// <summary>Line pattern of a network: solid, dashed, dotted or double (colour is never the only cue).</summary>
        public string Pattern(string network) => _patterns.TryGetValue(network, out string p) ? p : "solid";

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
