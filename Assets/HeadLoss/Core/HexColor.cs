using System.Globalization;

namespace HeadLoss.Core
{
    /// <summary>Parses <c>#RRGGBB</c> / <c>#RRGGBBAA</c> colours from data into 0–1 channels.</summary>
    public static class HexColor
    {
        public static bool TryParse(string? hex, out double r, out double g, out double b, out double a)
        {
            r = g = b = 0;
            a = 1;
            if (hex == null || hex.Length < 7 || hex[0] != '#') return false;
            string h = hex.Substring(1);
            if (h.Length != 6 && h.Length != 8) return false;
            if (!uint.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint v)) return false;
            if (h.Length == 6) v = (v << 8) | 0xFF;
            r = ((v >> 24) & 0xFF) / 255.0;
            g = ((v >> 16) & 0xFF) / 255.0;
            b = ((v >> 8) & 0xFF) / 255.0;
            a = (v & 0xFF) / 255.0;
            return true;
        }
    }
}
