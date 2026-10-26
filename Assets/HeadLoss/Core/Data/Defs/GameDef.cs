using System.Collections.Generic;

namespace HeadLoss.Core.Data.Defs
{
    /// <summary><c>game.json</c>: name and presentation constants.</summary>
    public sealed class GameDef
    {
        public string DisplayName = "";
        public string Version = "";
        public ThemeDef Theme = new ThemeDef();
    }

    /// <summary>UI palette (hex colours) from <c>11-ui-visuals-audio.md</c>.</summary>
    public sealed class ThemeDef
    {
        public string Paper = "";
        public string GridLines = "";
        public string Walls = "";
        public string Text = "";
        public string Warning = "";
        public string Danger = "";
        /// <summary>Room fill tint strength (0–1).</summary>
        public double RoomFillAlpha;
        /// <summary>Room fill colour per room type (hex), drawn at <see cref="RoomFillAlpha"/>.</summary>
        public Dictionary<string, string> RoomFills = new Dictionary<string, string>();
    }
}
