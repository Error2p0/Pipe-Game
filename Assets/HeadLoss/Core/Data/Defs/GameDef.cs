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
        /// <summary>Hand-made buildings offered before the city exists (Phase 2); a new game opens the first.</summary>
        public List<string> StarterBuildings = new List<string>();
        /// <summary>What the building toolbox offers until tech unlocks arrive (Phase 3).</summary>
        public ToolboxDef Toolbox = new ToolboxDef();
        public ThemeDef Theme = new ThemeDef();
    }

    public sealed class ToolboxDef
    {
        /// <summary>Equipment offered at an entry opening.</summary>
        public List<string> IntakeEquipment = new List<string>();
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
    }
}
