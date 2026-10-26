using System;
using System.Collections.Generic;
using System.IO;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.Commands;
using HeadLoss.Game.Save;
using HeadLoss.Game.Session;
using NUnit.Framework;
using static HeadLoss.Game.Tests.GameTestUtil;

namespace HeadLoss.Game.Tests
{
    public class SaveTests
    {
        string _folder = "";
        DateTime _now;

        [SetUp]
        public void SetUp()
        {
            _folder = Path.Combine(Path.GetTempPath(), "headloss-save-tests-" + Guid.NewGuid().ToString("N"));
            _now = new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
        }

        SaveStore Store(SaveDef? settings = null) => new SaveStore(_folder, settings ?? Data.Balancing.Save, () => _now);

        /// <summary>A game with built ducts, a pending design, a damper, debt and play time.</summary>
        static GameContext SampleGame()
        {
            var ctx = new GameContext(Data, GameContext.NewGame(Data));
            BuildingSession s = ctx.Open("test-house");
            Draw(s, Run((0, 3), (5, 3), (5, 5), (3, 5)), 125);
            s.Execute(new PlaceInlineCommand(new TilePos(2, 3), "damper"));
            s.Execute(new SetDamperOpeningCommand(new TilePos(2, 3), 0.35));
            s.Commit();
            Draw(s, Run((5, 4), (8, 4), (8, 5)), 100);
            ctx.Game.PlayTimeSeconds = 321.5;
            ctx.Game.LifetimeIncome = 12.25;
            return ctx;
        }

        [Test]
        public void RoundTrip_KeepsEverything()
        {
            GameContext ctx = SampleGame();
            Store().Save(1, ctx.Game, Data.Version);
            LoadResult r = Store().Load(1, Data)!;
            Assert.IsNotNull(r);
            GameState a = ctx.Game, b = r.File.GameState;
            Assert.AreEqual(a.Wallet, b.Wallet);
            Assert.AreEqual(a.Debt, b.Debt);
            Assert.AreEqual(a.PlayTimeSeconds, b.PlayTimeSeconds);
            Assert.AreEqual(a.LifetimeIncome, b.LifetimeIncome);
            Assert.AreEqual(a.CurrentBuildingId, b.CurrentBuildingId);
            Assert.AreEqual(a.Buildings.Count, b.Buildings.Count);
            for (int i = 0; i < a.Buildings.Count; i++)
            {
                BuildingRecord x = a.Buildings[i], y = b.Buildings[i];
                Assert.AreEqual(x.BuildingId, y.BuildingId);
                Assert.AreEqual(x.Committed.Fingerprint(), y.Committed.Fingerprint());
                Assert.AreEqual(x.Design.Fingerprint(), y.Design.Fingerprint());
                Assert.AreEqual(x.SpentTotal, y.SpentTotal);
                Assert.AreEqual(x.EverOnline, y.EverOnline);
            }
            Assert.AreEqual(Data.Version, r.File.DataVersion);
            Assert.AreEqual(SaveSerializer.CurrentSaveVersion, r.File.SaveVersion);
            Assert.AreEqual("2026-10-26T12:00:00.0000000Z", r.File.SavedAtUtc);

            // The loaded game runs: same pending cost, same solve.
            var ctx2 = new GameContext(Data, b);
            Assert.IsTrue(ctx2.Building!.HasPendingDesign);
            Assert.AreEqual(ctx.Building!.PendingCost.Net, ctx2.Building.PendingCost.Net, 1e-12);
        }

        [Test]
        public void Save_RotatesBackups_NewestFirst()
        {
            GameContext ctx = SampleGame();
            SaveStore store = Store();
            for (int i = 1; i <= 5; i++)
            {
                ctx.Game.Wallet = i;
                store.Save(1, ctx.Game, Data.Version);
            }
            string path = store.PathFor(1);
            for (int i = 1; i <= 3; i++) Assert.IsTrue(File.Exists($"{path}.bak{i}"), $".bak{i}");
            Assert.IsFalse(File.Exists($"{path}.bak4"));
            Assert.IsFalse(File.Exists($"{path}.tmp"));
            StringAssert.Contains("\"wallet\": 4.0", File.ReadAllText($"{path}.bak1"));
            StringAssert.Contains("\"wallet\": 2.0", File.ReadAllText($"{path}.bak3"));
        }

        [Test]
        public void AtomicWrite_SurvivesACrashBeforeReplace()
        {
            GameContext ctx = SampleGame();
            SaveStore store = Store();
            ctx.Game.Wallet = 111;
            store.Save(1, ctx.Game, Data.Version);
            ctx.Game.Wallet = 222;
            store.BeforeReplace = () => throw new IOException("power cut");
            Assert.Throws<IOException>(() => store.Save(1, ctx.Game, Data.Version));
            LoadResult r = Store().Load(1, Data)!;
            Assert.AreEqual(111, r.File.GameState.Wallet, "the old save is intact");
        }

        [Test]
        public void CorruptSave_FallsBackToNewestBackup()
        {
            GameContext ctx = SampleGame();
            SaveStore store = Store();
            ctx.Game.Wallet = 10;
            store.Save(1, ctx.Game, Data.Version);
            ctx.Game.Wallet = 20;
            store.Save(1, ctx.Game, Data.Version);
            File.WriteAllText(store.PathFor(1), "{ this is not json");
            LoadResult r = Store().Load(1, Data)!;
            Assert.AreEqual(10, r.File.GameState.Wallet);
            StringAssert.EndsWith(".bak1", r.Path);
            Assert.IsTrue(r.Notes.Exists(n => n.StartsWith("Could not read profile-1.json")));
        }

        [Test]
        public void LargeSave_IsGzipped_AndLoads()
        {
            var settings = new SaveDef { AutosaveSeconds = 30, BackupCount = 3, GzipThresholdBytes = 64 };
            GameContext ctx = SampleGame();
            SaveStore store = Store(settings);
            store.Save(1, ctx.Game, Data.Version);
            byte[] head = File.ReadAllBytes(store.PathFor(1));
            Assert.AreEqual(0x1f, head[0]);
            Assert.AreEqual(0x8b, head[1]);
            Assert.AreEqual(ctx.Game.Wallet, Store(settings).Load(1, Data)!.File.GameState.Wallet);
        }

        [Test]
        public void OfflineSeconds_ClockMovedBack_IsZero()
        {
            GameContext ctx = SampleGame();
            Store().Save(1, ctx.Game, Data.Version);
            _now = _now.AddHours(-2);
            LoadResult r = Store().Load(1, Data)!;
            Assert.AreEqual(0.0, r.OfflineSeconds);
            Assert.IsTrue(r.Notes.Exists(n => n.Contains("clock moved back")));
        }

        [Test]
        public void OfflineSeconds_CappedAtMaxHours()
        {
            GameContext ctx = SampleGame();
            Store().Save(1, ctx.Game, Data.Version);
            _now = _now.AddMinutes(90);
            Assert.AreEqual(5400.0, Store().Load(1, Data)!.OfflineSeconds, 1e-6);
            _now = _now.AddDays(30);
            Assert.AreEqual(Data.Balancing.Offline.MaxHours * 3600, Store().Load(1, Data)!.OfflineSeconds, 1e-6);
        }

        [Test]
        public void NewerSaveVersion_IsNotLoaded()
        {
            Directory.CreateDirectory(_folder);
            File.WriteAllText(Store().PathFor(1), "{ \"saveVersion\": 999, \"gameState\": {} }");
            Assert.IsNull(Store().Load(1, Data));
        }

        /// <summary>Every past save version in SaveSamples loads and migrates to the current version.</summary>
        [Test]
        public void SampleSaves_EveryPastVersionLoads()
        {
            string samples = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "HeadLoss", "Tests", "EditMode", "Game", "SaveSamples");
            string[] files = Directory.GetFiles(samples, "save-v*.json");
            Assert.GreaterOrEqual(files.Length, SaveSerializer.CurrentSaveVersion, "one sample per save version");
            Directory.CreateDirectory(_folder);
            foreach (string f in files)
            {
                File.Copy(f, Store().PathFor(1), true);
                LoadResult? r = Store().Load(1, Data);
                Assert.IsNotNull(r, f);
                Assert.AreEqual(SaveSerializer.CurrentSaveVersion, r!.File.SaveVersion, f);
                var ctx = new GameContext(Data, r.File.GameState);
                Assert.Greater(ctx.Sessions.Count, 0, f);
            }
        }

        [Test]
        public void Sanitize_DropsWhatNoLongerFits()
        {
            GameState g = GameContext.NewGame(Data);
            g.Buildings.Add(new BuildingRecord { BuildingId = "demolished-house" });
            BuildingRecord house = g.FindBuilding("test-house")!;
            house.Design.Air.Nodes.Add(new DuctNode { Id = 900, X = 40, Y = 40, Kind = DuctNodeKind.Free });
            house.Design.Air.Nodes.RemoveAll(n => n.AnchorId == "vent-2");
            var notes = new List<string>();
            SaveStore.Sanitize(g, Data, notes);
            Assert.IsNull(g.FindBuilding("demolished-house"));
            Assert.IsNull(house.Design.Air.FindNode(900));
            Assert.IsTrue(house.Design.Air.Nodes.Exists(n => n.AnchorId == "vent-2"), "anchors come back");
            Assert.IsTrue(notes.Exists(n => n.Contains("demolished-house")));
        }
    }
}
