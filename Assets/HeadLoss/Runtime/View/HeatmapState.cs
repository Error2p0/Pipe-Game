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
        /// <summary>Network the heatmap colours (air or water): its anchors and display units.</summary>
        public string Fluid { get; private set; } = "air";

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

        /// <summary>Unit shown on the legend (air in Pa and m³/h, water in bar and L/s).</summary>
        public static string Unit(HeatmapMode m, string fluid = "air") => m switch
        {
            HeatmapMode.Velocity => "m/s",
            HeatmapMode.Pressure => fluid == "air" ? "Pa" : "bar",
            HeatmapMode.Flow => fluid == "air" ? "m³/h" : "L/s",
            HeatmapMode.Temperature => "°C",
            HeatmapMode.Density => "kg/m³",
            HeatmapMode.Stress => "",
            _ => "",
        };

        /// <summary>Modes built so far. Concentration (gases, Phase 5) shows greyed out.</summary>
        public static bool IsAvailable(HeatmapMode m) => m != HeatmapMode.Concentration;

        /// <summary>Modes that also colour the rooms (pressure, flow and stress colour pipes only).</summary>
        public static bool ColoursRooms(HeatmapMode m) => m == HeatmapMode.Velocity || m == HeatmapMode.Temperature || m == HeatmapMode.Density;

        /// <summary>The rooms take the heatmap only while the air network is in focus (the room grid is air).</summary>
        public bool RoomsColoured => Fluid == "air" && ColoursRooms(Mode);

        /// <summary>Switches the network the heatmap colours (its anchors and units).</summary>
        public void SetFluid(string fluid)
        {
            if (Fluid == fluid) return;
            Fluid = fluid;
            if (!Locked) ResetRange();
            Revision++;
        }

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
            bool air = Fluid == "air";
            (RangeMin, RangeMax) = Mode switch
            {
                HeatmapMode.Pressure => (0f, (float)(air ? _def.PressureAnchorMaxAirPa : _def.PressureAnchorMaxWaterBar)),
                HeatmapMode.Flow => (0f, (float)(air ? _def.FlowAnchorMaxM3h : _def.FlowAnchorMaxWaterLps)),
                HeatmapMode.Temperature => ((float)_def.TemperatureAnchorC[0], (float)_def.TemperatureAnchorC[1]),
                HeatmapMode.Density => air ? ((float)_def.DensityAnchor[0], (float)_def.DensityAnchor[1]) : ((float)_def.DensityAnchorWater[0], (float)_def.DensityAnchorWater[1]),
                HeatmapMode.Stress => (0f, 1f),
                _ => (0f, (float)(air ? _def.VelocityAnchorMaxAir : _def.VelocityAnchorMaxWater)),
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
            bool air = Fluid == "air";
            switch (Mode)
            {
                case HeatmapMode.Velocity:
                    hi = Extend(max, (float)(air ? _def.VelocityAnchorMaxAir : _def.VelocityAnchorMaxWater), (float)_def.AutoRangeStep);
                    room = Extend(roomMax, (float)_def.RoomVelocityAnchorMax, 0.5f);
                    break;
                case HeatmapMode.Pressure:
                    hi = air ? Extend(max, (float)_def.PressureAnchorMaxAirPa, (float)_def.PressureStepPa) : Extend(max, (float)_def.PressureAnchorMaxWaterBar, (float)_def.PressureStepWaterBar);
                    break;
                case HeatmapMode.Flow:
                    hi = air ? Extend(max, (float)_def.FlowAnchorMaxM3h, (float)_def.FlowStepM3h) : Extend(max, (float)_def.FlowAnchorMaxWaterLps, (float)_def.FlowStepWaterLps);
                    break;
                case HeatmapMode.Temperature:
                    (lo, hi) = Widen(min, max, _def.TemperatureAnchorC, (float)_def.TemperatureStepK);
                    break;
                case HeatmapMode.Density:
                    (lo, hi) = Widen(min, max, air ? _def.DensityAnchor : _def.DensityAnchorWater, air ? (float)_def.DensityStep : 1f);
                    break;
                case HeatmapMode.Stress:
                    // Stress 0 → 1 with anything at or above 1 in the warning colour (11 §Heatmap modes); the scale never moves.
                    return;
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
