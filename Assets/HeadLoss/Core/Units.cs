using System;
using System.Globalization;

namespace HeadLoss.Core
{
    /// <summary>
    /// SI ↔ display unit conversions and display formatting. The simulation is SI only;
    /// the UI converts through this class (CLAUDE.md rule 5).
    /// </summary>
    public static class Units
    {
        public const double SecondsPerHour = 3600.0;
        public const double PaPerBar = 1.0e5;
        public const double PaPerMbar = 100.0;
        public const double KelvinOffset = 273.15;

        /// <summary>m³/s → m³/h.</summary>
        public static double M3sToM3h(double m3s) => m3s * SecondsPerHour;
        /// <summary>m³/h → m³/s.</summary>
        public static double M3hToM3s(double m3h) => m3h / SecondsPerHour;
        /// <summary>m³/s → L/s.</summary>
        public static double M3sToLps(double m3s) => m3s * 1000.0;
        /// <summary>L/s → m³/s.</summary>
        public static double LpsToM3s(double lps) => lps / 1000.0;
        /// <summary>Pa → bar.</summary>
        public static double PaToBar(double pa) => pa / PaPerBar;
        /// <summary>bar → Pa.</summary>
        public static double BarToPa(double bar) => bar * PaPerBar;
        /// <summary>Pa → mbar.</summary>
        public static double PaToMbar(double pa) => pa / PaPerMbar;
        /// <summary>mbar → Pa.</summary>
        public static double MbarToPa(double mbar) => mbar * PaPerMbar;
        /// <summary>°C → K.</summary>
        public static double CToK(double c) => c + KelvinOffset;
        /// <summary>K → °C.</summary>
        public static double KToC(double k) => k - KelvinOffset;
        /// <summary>W → kW.</summary>
        public static double WToKW(double w) => w / 1000.0;
        /// <summary>mm → m.</summary>
        public static double MmToM(double mm) => mm / 1000.0;
        /// <summary>m → mm.</summary>
        public static double MToMm(double m) => m * 1000.0;
        /// <summary>Gauge pressure (Pa) → absolute pressure (Pa).</summary>
        public static double GaugeToAbs(double gaugePa, double atmosphericPa) => gaugePa + atmosphericPa;
        /// <summary>Absolute pressure (Pa) → gauge pressure (Pa).</summary>
        public static double AbsToGauge(double absPa, double atmosphericPa) => absPa - atmosphericPa;

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>Formats a number with a sensible number of significant digits for readouts.</summary>
        public static string Number(double value, int decimals) =>
            value.ToString("F" + Math.Max(0, decimals), Inv);

        /// <summary>Volumetric flow (m³/s) shown as m³/h.</summary>
        public static string FormatAirFlow(double m3s)
        {
            double v = M3sToM3h(m3s);
            return Number(v, Math.Abs(v) < 10 ? 1 : 0) + " m³/h";
        }

        /// <summary>Velocity (m/s).</summary>
        public static string FormatVelocity(double ms) => Number(ms, 2) + " m/s";

        /// <summary>Air-side pressure (Pa) shown in Pa.</summary>
        public static string FormatPa(double pa) => Number(pa, Math.Abs(pa) < 10 ? 2 : 1) + " Pa";

        /// <summary>Temperature (K) shown in °C.</summary>
        public static string FormatTemperatureK(double k) => Number(KToC(k), 1) + " °C";

        /// <summary>Power (W) shown as W or kW.</summary>
        public static string FormatPower(double w) =>
            Math.Abs(w) < 1000 ? Number(w, 1) + " W" : Number(WToKW(w), 2) + " kW";
    }
}
