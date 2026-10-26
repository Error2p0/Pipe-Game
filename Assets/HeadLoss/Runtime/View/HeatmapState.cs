using HeadLoss.Core.Data.Defs;
using UnityEngine;

namespace HeadLoss.Unity.View
{
    /// <summary>Heatmap modes, hotkeys 0–7 (<c>11-ui-visuals-audio.md</c>).</summary>
    public enum HeatmapMode
    {
        Plain = 0,
        Velocity = 1,
        Pressure = 2,
        Flow = 3,
        Temperature = 4,
        Density = 5,
        Concentration = 6,
        Stress = 7,
    }

    /// <summary>Selected heatmap and its scale (auto-range with fixed anchors, lockable).</summary>
    public sealed class HeatmapState
    {
        readonly HeatmapDef _def;

        public HeatmapMode Mode = HeatmapMode.Velocity;
        public string Palette;
        public bool Locked;
        /// <summary>Upper end of the scale (m/s for velocity).</summary>
        public float RangeMax;
        /// <summary>Bumped whenever the mode, palette or range changes.</summary>
        public int Revision;

        public HeatmapState(HeatmapDef def)
        {
            _def = def;
            Palette = def.DefaultPalette;
            RangeMax = (float)def.VelocityAnchorMaxAir;
        }

        public static string DisplayName(HeatmapMode m) => m switch
        {
            HeatmapMode.Plain => "Plain",
            HeatmapMode.Velocity => "Velocity",
            HeatmapMode.Pressure => "Pressure",
            HeatmapMode.Flow => "Flow rate",
            HeatmapMode.Temperature => "Temperature",
            HeatmapMode.Density => "Density",
            HeatmapMode.Concentration => "Concentration",
            HeatmapMode.Stress => "Stress",
            _ => m.ToString(),
        };

        /// <summary>Modes built so far: plain and velocity (Phase 1). The rest show greyed out.</summary>
        public static bool IsAvailable(HeatmapMode m) => m == HeatmapMode.Plain || m == HeatmapMode.Velocity;

        public void SetMode(HeatmapMode m)
        {
            if (!IsAvailable(m) || Mode == m) return;
            Mode = m;
            Revision++;
        }

        public void SetLocked(bool locked)
        {
            Locked = locked;
            Revision++;
        }

        /// <summary>Scale 0 → anchor (6 m/s for air); extends in whole steps when the building exceeds it, unless locked.</summary>
        /// <param name="maxVelocity">Highest duct velocity in the building, m/s.</param>
        public void AutoRange(double maxVelocity)
        {
            if (Locked) return;
            float anchor = (float)_def.VelocityAnchorMaxAir;
            float step = (float)_def.AutoRangeStep;
            float max = maxVelocity > anchor ? Mathf.Ceil((float)maxVelocity / step) * step : anchor;
            if (Mathf.Approximately(max, RangeMax)) return;
            RangeMax = max;
            Revision++;
        }

        public float Normalise(double velocity) => Mathf.Clamp01((float)(System.Math.Abs(velocity) / RangeMax));
    }
}
