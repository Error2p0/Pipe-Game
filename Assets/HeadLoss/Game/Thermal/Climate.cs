using System;
using System.Collections.Generic;
using HeadLoss.Core.Data;
using HeadLoss.Core.Data.Defs;

namespace HeadLoss.Game.Thermal
{
    /// <summary>
    /// Outside conditions shared by every building (<c>13-balancing.md</c> §Climate): a mean temperature with a
    /// daily sine following the player's local clock (warmest at 15:00), shifted by city modifiers, and solar gain
    /// on windows by facade during daylight. The Unity layer sets <see cref="LocalHour"/>; game rules never read a
    /// clock themselves.
    /// </summary>
    public sealed class Climate
    {
        readonly ClimateDef _def;
        readonly double _shift;

        /// <summary>Local time of day, hours (0–24).</summary>
        public double LocalHour = 12;
        /// <summary>When set, the outside temperature is this fixed value, °C (test fixtures and tools).</summary>
        public double? FixedTemperatureC;

        public Climate(DataRepository data, IReadOnlyList<string> modifiers)
        {
            _def = data.Balancing.Climate;
            foreach (string id in modifiers)
            {
                CityModifierDef? m = data.City.Modifiers.Find(x => x.Id == id);
                if (m != null) _shift += m.OutsideTemperatureShift;
            }
        }

        /// <summary>A climate with a fixed temperature (test fixtures).</summary>
        public static Climate Fixed(DataRepository data, double temperatureC) =>
            new Climate(data, Array.Empty<string>()) { FixedTemperatureC = temperatureC };

        /// <summary>Outside air temperature now, °C.</summary>
        public double OutsideTemperatureC => FixedTemperatureC ?? OutsideTemperatureAt(LocalHour);

        /// <summary>Outside air temperature at a local hour, °C.</summary>
        public double OutsideTemperatureAt(double hour) =>
            _def.MeanTemperatureC + _shift + _def.DailyAmplitudeK * Math.Cos(2 * Math.PI * (hour - _def.WarmestHour) / 24);

        /// <summary>Daylight factor 0–1 (half sine between sunrise and sunset, 0 at night).</summary>
        public double Daylight(double hour)
        {
            if (hour <= _def.SunriseHour || hour >= _def.SunsetHour) return 0;
            return Math.Sin(Math.PI * (hour - _def.SunriseHour) / (_def.SunsetHour - _def.SunriseHour));
        }

        /// <summary>Solar gain through one window now, W.</summary>
        /// <param name="facade">north, south, east or west.</param>
        /// <param name="areaM2">Glazed area, m².</param>
        public double SolarGainW(string facade, double areaM2)
        {
            if (FixedTemperatureC.HasValue) return 0;
            double factor = _def.FacadeFactors.TryGetValue(facade, out double f) ? f : 0;
            return _def.SolarPeakWm2 * factor * Daylight(LocalHour) * areaM2;
        }
    }
}
