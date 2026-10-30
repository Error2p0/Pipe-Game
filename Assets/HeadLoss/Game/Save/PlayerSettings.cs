using System;
using System.IO;
using System.Text;
using HeadLoss.Core.Data;
using Newtonsoft.Json;

namespace HeadLoss.Game.Save
{
    /// <summary>
    /// The settings screen's values (<c>11-ui-visuals-audio.md</c> §Screens 7). Kept in their own file next to the
    /// saves, so they apply from the main menu, in every profile and across new cities (D68).
    /// </summary>
    public sealed class PlayerSettings
    {
        /// <summary>Volumes, 0–1.</summary>
        public double MasterVolume = 0.5, AmbienceVolume = 1, UiVolume = 1;
        /// <summary>Heatmap palette id (balancing.heatmap.palettes).</summary>
        public string Palette = "";
        /// <summary>light or dark.</summary>
        public string Theme = "light";
        /// <summary>UI scale, 0.8–1.5.</summary>
        public double UiScale = 1;
        /// <summary>Autosave interval, s (10–300).</summary>
        public double AutosaveSeconds = 30;
        /// <summary>No particle speed variation, glow breathing, cloud drift or ripples.</summary>
        public bool ReduceMotion;
        /// <summary>Input System binding overrides (JSON from SaveBindingOverridesAsJson; "" = defaults).</summary>
        public string Bindings = "";

        /// <summary>Defaults from the data files.</summary>
        public static PlayerSettings Defaults(DataRepository data) => new PlayerSettings
        {
            MasterVolume = data.Balancing.Audio.MasterVolume,
            Palette = data.Balancing.Heatmap.DefaultPalette,
            AutosaveSeconds = data.Balancing.Save.AutosaveSeconds,
        };

        /// <summary>Clamps every value into its range and replaces unknown ids with defaults.</summary>
        public void Sanitize(DataRepository data)
        {
            MasterVolume = Clamp(MasterVolume, 0, 1);
            AmbienceVolume = Clamp(AmbienceVolume, 0, 1);
            UiVolume = Clamp(UiVolume, 0, 1);
            UiScale = Clamp(UiScale, 0.8, 1.5);
            AutosaveSeconds = Clamp(AutosaveSeconds, 10, 300);
            if (!data.Balancing.Heatmap.Palettes.ContainsKey(Palette)) Palette = data.Balancing.Heatmap.DefaultPalette;
            if (Theme != "light" && Theme != "dark") Theme = "light";
            Bindings ??= "";
        }

        static double Clamp(double v, double lo, double hi) => double.IsNaN(v) ? lo : Math.Max(lo, Math.Min(hi, v));

        public PlayerSettings Clone() => (PlayerSettings)MemberwiseClone();
    }

    /// <summary>Reads and writes <c>settings.json</c> (atomic: write .tmp, then replace).</summary>
    public sealed class SettingsStore
    {
        readonly string _path;

        /// <param name="folder">The saves folder.</param>
        public SettingsStore(string folder) => _path = Path.Combine(folder, "settings.json");

        public string FilePath => _path;

        /// <summary>The stored settings, or the defaults when there are none or they cannot be read.</summary>
        public PlayerSettings Load(DataRepository data)
        {
            PlayerSettings s = PlayerSettings.Defaults(data);
            try
            {
                if (File.Exists(_path)) JsonConvert.PopulateObject(File.ReadAllText(_path, Encoding.UTF8), s);
            }
            catch (Exception e) when (e is IOException || e is JsonException || e is UnauthorizedAccessException)
            {
                s = PlayerSettings.Defaults(data);
            }
            s.Sanitize(data);
            return s;
        }

        public void Save(PlayerSettings s)
        {
            string? dir = Path.GetDirectoryName(_path);
            if (dir != null) Directory.CreateDirectory(dir);
            string tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(s, Formatting.Indented), Encoding.UTF8);
            if (File.Exists(_path)) File.Replace(tmp, _path, null);
            else File.Move(tmp, _path);
        }
    }
}
