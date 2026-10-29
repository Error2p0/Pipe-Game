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

    /// <summary>Which layer of the two-layer room fields a room heatmap shows (hotkey H).</summary>
    public enum RoomLayer { Max, Upper, Lower }

    /// <summary>
    /// Selected heatmap and its scale: auto-range with fixed anchors (velocity 0–6 m/s in ducts and 0–1 m/s in
    /// rooms, pressure from 0 Pa, flow from 0 m³/h, temperature 10–30 °C, density 1.15–1.25 kg/m³), extended in whole
    /// steps when the building goes beyond them; a lock freezes the range.
    /// </summary>
    public sealed class HeatmapState
    {
        readonly HeatmapDef _def;

        public HeatmapMode Mode = HeatmapMode.Velocity;
        public string Palette;
        public bool Locked;
        public RoomLayer Layer = RoomLayer.Max;
        /// <summary>Scale of the current mode (velocity: ducts; <see cref="RoomMax"/> for rooms).</summary>
        public float RangeMin, RangeMax;
        /// <summary>Room velocity scale upper end, m/s.</summary>
        public float RoomMax;
        /// <summary>Bumped whenever the mode, palette, layer or range changes.</summary>
        public int Revision;

        public HeatmapState(HeatmapDef def)
        {
            _def = def;
            Palette = def.DefaultPalette;
            ResetRange();
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

        /// <summary>Unit shown on the legend.</summary>
        public static string Unit(HeatmapMode m) => m switch
        {
            HeatmapMode.Velocity => "m/s",
            HeatmapMode.Pressure => "Pa",
            HeatmapMode.Flow => "m³/h",
            HeatmapMode.Temperature => "°C",
            HeatmapMode.Density => "kg/m³",
            _ => "",
        };

        /// <summary>Modes built so far. Concentration (gases, Phase 5) and stress (bursts, Phase 4) show greyed out.</summary>
        public static bool IsAvailable(HeatmapMode m) => m != HeatmapMode.Concentration && m != HeatmapMode.Stress;

        /// <summary>Modes that also colour the rooms (pressure and flow colour ducts only).</summary>
        public static bool ColoursRooms(HeatmapMode m) => m == HeatmapMode.Velocity || m == HeatmapMode.Temperature || m == HeatmapMode.Density;

        public void SetMode(HeatmapMode m)
        {
            if (!IsAvailable(m) || Mode == m) return;
            Mode = m;
            if (!Locked) ResetRange();
            Revision++;
        }

        public void SetLocked(bool locked)
        {
            Locked = locked;
            Revision++;
        }

        public void SetPalette(string palette)
        {
            if (Palette == palette) return;
            Palette = palette;
            Revision++;
        }

        /// <summary>H: max → upper → lower → max.</summary>
        public void CycleLayer()
        {
            Layer = Layer == RoomLayer.Max ? RoomLayer.Upper : Layer == RoomLayer.Upper ? RoomLayer.Lower : RoomLayer.Max;
            Revision++;
        }

        void ResetRange()
        {
            (RangeMin, RangeMax) = Mode switch
            {
                HeatmapMode.Pressure => (0f, (float)_def.PressureAnchorMaxAirPa),
                HeatmapMode.Flow => (0f, (float)_def.FlowAnchorMaxM3h),
                HeatmapMode.Temperature => ((float)_def.TemperatureAnchorC[0], (float)_def.TemperatureAnchorC[1]),
                HeatmapMode.Density => ((float)_def.DensityAnchor[0], (float)_def.DensityAnchor[1]),
                _ => (0f, (float)_def.VelocityAnchorMaxAir),
            };
            RoomMax = (float)_def.RoomVelocityAnchorMax;
        }

        /// <summary>
        /// Widens the scale in whole steps when the building's values go past the anchors (it never shrinks below them).
        /// </summary>
        /// <param name="min">Lowest value in the building for the mode (display units).</param>
        /// <param name="max">Highest value.</param>
        /// <param name="roomMax">Highest room velocity, m/s (velocity mode only).</param>
        public void AutoRange(double min, double max, double roomMax = 0)
        {
            if (Locked || Mode == HeatmapMode.Plain) return;
            float lo = RangeMin, hi = RangeMax, room = RoomMax;
            switch (Mode)
            {
                case HeatmapMode.Velocity:
                    hi = Extend(max, (float)_def.VelocityAnchorMaxAir, (float)_def.AutoRangeStep);
                    room = Extend(roomMax, (float)_def.RoomVelocityAnchorMax, 0.5f);
                    break;
                case HeatmapMode.Pressure:
                    hi = Extend(max, (float)_def.PressureAnchorMaxAirPa, (float)_def.PressureStepPa);
                    break;
                case HeatmapMode.Flow:
                    hi = Extend(max, (float)_def.FlowAnchorMaxM3h, (float)_def.FlowStepM3h);
                    break;
                case HeatmapMode.Temperature:
                    (lo, hi) = Widen(min, max, _def.TemperatureAnchorC, (float)_def.TemperatureStepK);
                    break;
                case HeatmapMode.Density:
                    (lo, hi) = Widen(min, max, _def.DensityAnchor, (float)_def.DensityStep);
                    break;
            }
            if (Mathf.Approximately(lo, RangeMin) && Mathf.Approximately(hi, RangeMax) && Mathf.Approximately(room, RoomMax)) return;
            RangeMin = lo;
            RangeMax = hi;
            RoomMax = room;
            Revision++;
        }

        static float Extend(double value, float anchor, float step) =>
            value > anchor ? Mathf.Ceil((float)value / step) * step : anchor;

        static (float, float) Widen(double min, double max, System.Collections.Generic.List<double> anchor, float step)
        {
            float lo = (float)anchor[0], hi = (float)anchor[1];
            if (min < lo) lo = Mathf.Floor((float)min / step) * step;
            if (max > hi) hi = Mathf.Ceil((float)max / step) * step;
            return (lo, hi);
        }

        /// <summary>Position of a duct value on the scale, 0–1.</summary>
        public float Normalise(double value)
        {
            double v = Mode == HeatmapMode.Velocity || Mode == HeatmapMode.Flow ? System.Math.Abs(value) : value;
            return Mathf.Clamp01((float)((v - RangeMin) / System.Math.Max(1e-9, RangeMax - RangeMin)));
        }

        /// <summary>Position of a room value on the scale, 0–1 (room velocity has its own scale).</summary>
        public float NormaliseRoom(double value) =>
            Mode == HeatmapMode.Velocity ? Mathf.Clamp01((float)(value / RoomMax)) : Normalise(value);
    }
}
