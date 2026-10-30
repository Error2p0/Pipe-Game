using System.IO;
using HeadLoss.Game.Save;
using NUnit.Framework;
using static HeadLoss.Game.Tests.GameTestUtil;

namespace HeadLoss.Game.Tests
{
    public class SettingsTests
    {
        string _folder = "";

        [SetUp]
        public void SetUp() => _folder = Path.Combine(Path.GetTempPath(), "hl-settings-" + System.Guid.NewGuid().ToString("N"));

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
        }

        [Test]
        public void Settings_DefaultsComeFromData_AndRoundTrip()
        {
            var store = new SettingsStore(_folder);
            PlayerSettings s = store.Load(Data);
            Assert.AreEqual(Data.Balancing.Audio.MasterVolume, s.MasterVolume);
            Assert.AreEqual(Data.Balancing.Heatmap.DefaultPalette, s.Palette);
            Assert.AreEqual(Data.Balancing.Save.AutosaveSeconds, s.AutosaveSeconds);
            s.Theme = "dark";
            s.UiScale = 1.25;
            s.ReduceMotion = true;
            s.Bindings = "{\"bindings\":[]}";
            store.Save(s);
            PlayerSettings t = store.Load(Data);
            Assert.AreEqual("dark", t.Theme);
            Assert.AreEqual(1.25, t.UiScale);
            Assert.IsTrue(t.ReduceMotion);
            Assert.AreEqual(s.Bindings, t.Bindings);
        }

        [Test]
        public void Settings_OutOfRangeValues_AreClamped_BrokenFileGivesDefaults()
        {
            var store = new SettingsStore(_folder);
            PlayerSettings s = store.Load(Data);
            s.UiScale = 3;
            s.MasterVolume = -1;
            s.AutosaveSeconds = 1;
            s.Palette = "rainbow";
            s.Theme = "neon";
            s.Sanitize(Data);
            Assert.AreEqual(1.5, s.UiScale);
            Assert.AreEqual(0, s.MasterVolume);
            Assert.AreEqual(10, s.AutosaveSeconds);
            Assert.AreEqual(Data.Balancing.Heatmap.DefaultPalette, s.Palette);
            Assert.AreEqual("light", s.Theme);
            Directory.CreateDirectory(_folder);
            File.WriteAllText(store.FilePath, "{ not json");
            Assert.AreEqual("light", store.Load(Data).Theme);
        }
    }
}
