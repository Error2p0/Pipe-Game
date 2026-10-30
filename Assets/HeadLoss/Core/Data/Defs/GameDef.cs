using System.Collections.Generic;

namespace HeadLoss.Core.Data.Defs
{
    /// <summary><c>game.json</c>: name and presentation constants.</summary>
    public sealed class GameDef
    {
        public string DisplayName = "";
        public string Version = "";
        /// <summary>Currency symbol shown before money amounts.</summary>
        public string CurrencySymbol = "";
        /// <summary>What the building toolbox offers until tech unlocks arrive (Phase 3).</summary>
        public ToolboxDef Toolbox = new ToolboxDef();
        public ThemeDef Theme = new ThemeDef();
        /// <summary>The dark theme (settings).</summary>
        public ThemeDef ThemeDark = new ThemeDef();

        /// <summary>The theme for a settings value: dark, else light.</summary>
        public ThemeDef ThemeFor(string id) => id == "dark" ? ThemeDark : Theme;
    }

    public sealed class ToolboxDef
    {
        /// <summary>Inline parts (fitting or equipment ids) placed on a duct.</summary>
        public List<string> Parts = new List<string>();
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
        /// <summary>Calm positive accent (outlets in overdelivery, online).</summary>
        public string Good = "";
        /// <summary>Room fill tint strength (0–1).</summary>
        public double RoomFillAlpha;
        /// <summary>Room fill colour per room type (hex), drawn at <see cref="RoomFillAlpha"/>.</summary>
        public Dictionary<string, string> RoomFills = new Dictionary<string, string>();
        public CityThemeDef City = new CityThemeDef();
    }

    /// <summary>City view colours (hex): flat blocks that light up when online (<c>11-ui-visuals-audio.md</c>).</summary>
    public sealed class CityThemeDef
    {
        public string Ground = "";
        public string Countryside = "";
        /// <summary>District tints (residential, commercial, industrial) drawn at <see cref="DistrictAlpha"/>.</summary>
        public Dictionary<string, string> Districts = new Dictionary<string, string>();
        public double DistrictAlpha;
        public string Road = "";
        public string MainRoad = "";
        public string Rail = "";
        public string Park = "";
        public string Protected = "";
        public string Historic = "";
        /// <summary>Facility colour per utility.</summary>
        public Dictionary<string, string> Facilities = new Dictionary<string, string>();
        /// <summary>A plot whose building is not built or offline (grey).</summary>
        public string PlotOffline = "";
        /// <summary>A plot whose tier arrives in a later version.</summary>
        public string PlotLocked = "";
        /// <summary>An online building's roof.</summary>
        public string PlotOnline = "";
        /// <summary>The soft breathing glow around an online building.</summary>
        public string Glow = "";
        /// <summary>Side faces are the roof colour times this (0–1).</summary>
        public double SideShade;
        /// <summary>Drifting cloud shadows.</summary>
        public string Cloud = "";
        public double CloudAlpha;
    }
}
