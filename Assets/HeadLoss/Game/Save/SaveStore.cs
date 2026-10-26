using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using HeadLoss.Core.Data;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Buildings;
using Newtonsoft.Json.Linq;

namespace HeadLoss.Game.Save
{
    /// <summary>What loading found.</summary>
    public sealed class LoadResult
    {
        public SaveFile File = new SaveFile();
        /// <summary>The file it came from (the main save or a backup).</summary>
        public string Path = "";
        /// <summary>Real time since the save, capped at <c>offline.maxHours</c>, s (0 if the clock went back).</summary>
        public double OfflineSeconds;
        /// <summary>Notes for the log: migrations, backups used, clock moved back, dropped edits.</summary>
        public readonly List<string> Notes = new List<string>();
    }

    /// <summary>
    /// Save files on disk (<c>12-save-offline.md</c>): one file per profile, written atomically (write
    /// <c>.tmp</c>, then replace) with a rolling backup of the last saves (<c>.bak1</c> newest), gzip-compressed
    /// above the size threshold. Loading migrates old versions, falls back to the newest readable backup and
    /// drops edits that no longer fit their building.
    /// </summary>
    public sealed class SaveStore
    {
        readonly string _folder;
        readonly SaveDef _settings;
        readonly Func<DateTime> _utcNow;

        /// <summary>Test hook: runs after the .tmp file is written and before it replaces the save (simulated crash).</summary>
        public Action? BeforeReplace;

        /// <param name="folder">The saves folder (Unity: persistentDataPath/saves).</param>
        /// <param name="utcNow">Clock (UTC), injectable for tests.</param>
        public SaveStore(string folder, SaveDef settings, Func<DateTime>? utcNow = null)
        {
            _folder = folder;
            _settings = settings;
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        public string PathFor(int profile) => System.IO.Path.Combine(_folder, $"profile-{profile}.json");
        public bool Exists(int profile) => File.Exists(PathFor(profile)) || File.Exists(PathFor(profile) + ".bak1");

        /// <summary>Writes the game atomically and rotates the backups.</summary>
        /// <param name="dataVersion">Hash of the data files (<see cref="DataRepository.Version"/>).</param>
        public void Save(int profile, GameState game, string dataVersion)
        {
            var file = new SaveFile
            {
                SaveVersion = SaveSerializer.CurrentSaveVersion,
                GeneratorVersion = SaveSerializer.CurrentGeneratorVersion,
                DataVersion = dataVersion,
                SavedAtUtc = _utcNow().ToString("o", CultureInfo.InvariantCulture),
                PlayTimeSeconds = game.PlayTimeSeconds,
                GameState = game,
            };
            byte[] bytes = Encoding.UTF8.GetBytes(SaveSerializer.Write(file));
            if (bytes.Length > _settings.GzipThresholdBytes) bytes = Gzip(bytes);

            Directory.CreateDirectory(_folder);
            string path = PathFor(profile);
            string tmp = path + ".tmp";
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(true);
            }
            BeforeReplace?.Invoke();
            RotateBackups(path);
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }

        /// <summary>The current save becomes .bak1, .bak1 becomes .bak2, … (the oldest drops off).</summary>
        void RotateBackups(string path)
        {
            int n = _settings.BackupCount;
            if (n <= 0 || !File.Exists(path)) return;
            string Bak(int i) => $"{path}.bak{i}";
            if (File.Exists(Bak(n))) File.Delete(Bak(n));
            for (int i = n - 1; i >= 1; i--)
                if (File.Exists(Bak(i))) File.Move(Bak(i), Bak(i + 1));
            File.Copy(path, Bak(1));
        }

        /// <summary>Loads a profile: the main save, else the newest readable backup. Null when nothing can be read.</summary>
        public LoadResult? Load(int profile, DataRepository data)
        {
            string path = PathFor(profile);
            var candidates = new List<string> { path };
            for (int i = 1; i <= Math.Max(_settings.BackupCount, 3); i++) candidates.Add($"{path}.bak{i}");
            var failures = new List<string>();
            foreach (string p in candidates)
            {
                if (!File.Exists(p)) continue;
                try
                {
                    LoadResult r = Read(p, data);
                    foreach (string f in failures) r.Notes.Insert(0, f);
                    return r;
                }
                catch (Exception e) when (e is FormatException || e is IOException || e is Newtonsoft.Json.JsonException || e is InvalidDataException)
                {
                    failures.Add($"Could not read {System.IO.Path.GetFileName(p)}: {e.Message}");
                }
            }
            return null;
        }

        LoadResult Read(string path, DataRepository data)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length >= 2 && bytes[0] == 0x1f && bytes[1] == 0x8b) bytes = Gunzip(bytes);
            JObject o = SaveSerializer.Parse(Encoding.UTF8.GetString(bytes));
            var r = new LoadResult { Path = path };
            SaveMigrator.Migrate(o, out int from);
            if (from != SaveSerializer.CurrentSaveVersion) r.Notes.Add($"Migrated save from version {from}");
            r.File = SaveSerializer.FromJObject(o);
            if (r.File.GeneratorVersion != SaveSerializer.CurrentGeneratorVersion)
                r.Notes.Add($"Generator version changed ({r.File.GeneratorVersion} → {SaveSerializer.CurrentGeneratorVersion}); keeping edits that still fit");
            if (r.File.DataVersion != data.Version) r.Notes.Add("Data files changed since this save");
            r.OfflineSeconds = OfflineSeconds(r.File.SavedAtUtc, data.Balancing.Offline.MaxHours, r.Notes);
            Sanitize(r.File.GameState, data, r.Notes);
            return r;
        }

        double OfflineSeconds(string savedAtUtc, double maxHours, List<string> notes)
        {
            if (!DateTime.TryParse(savedAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime saved))
            {
                notes.Add("Save time unreadable; no offline time");
                return 0;
            }
            double s = (_utcNow() - saved.ToUniversalTime()).TotalSeconds;
            if (s < 0)
            {
                notes.Add("The clock moved back since the save; offline time is 0");
                return 0;
            }
            return Math.Min(s, maxHours * 3600.0);
        }

        /// <summary>
        /// Keeps only what still fits: unknown buildings are dropped; in each state, nodes outside the building,
        /// segments that cross walls or miss a node, unknown parts and equipment are removed; anchors are re-added.
        /// </summary>
        public static void Sanitize(GameState game, DataRepository data, List<string> notes)
        {
            game.Buildings.RemoveAll(b =>
            {
                bool unknown = data.FindHandmadeBuilding(b.BuildingId) == null;
                if (unknown) notes.Add($"Dropped unknown building '{b.BuildingId}'");
                return unknown;
            });
            foreach (BuildingRecord r in game.Buildings)
            {
                BuildingLayout layout = GameContext.LayoutFor(data, r.BuildingId);
                int dropped = SanitizeState(r.Committed, layout, data) + SanitizeState(r.Design, layout, data);
                if (dropped > 0) notes.Add($"{r.BuildingId}: dropped {dropped} parts that no longer fit");
            }
            if (game.CurrentBuildingId.Length > 0 && game.FindBuilding(game.CurrentBuildingId) == null) game.CurrentBuildingId = "";
        }

        static int SanitizeState(BuildingState s, BuildingLayout layout, DataRepository data)
        {
            int dropped = 0;
            DuctNetworkState net = s.Air;
            dropped += net.Nodes.RemoveAll(n => !layout.InFootprint(n.Pos)
                || (n.Kind == DuctNodeKind.Intake && layout.FindOpening(n.AnchorId) == null)
                || (n.Kind == DuctNodeKind.Outlet && layout.FindOutlet(n.AnchorId) == null));
            foreach (DuctNode n in net.Nodes)
            {
                if (n.Kind != DuctNodeKind.Inline || data.FindEquipment(n.PartId) != null || data.FindFitting(n.PartId) != null) continue;
                n.Kind = DuctNodeKind.Free;
                n.PartId = "";
                dropped++;
            }
            dropped += net.Segments.RemoveAll(seg =>
            {
                DuctNode? a = net.FindNode(seg.NodeA), b = net.FindNode(seg.NodeB);
                if (a == null || b == null || data.FindMaterial(seg.MaterialId) == null) return true;
                if (a.X != b.X && a.Y != b.Y) return true;
                (int dx, int dy) = a.Pos.DirectionTo(b.Pos);
                for (TilePos t = a.Pos; t != b.Pos; t = new TilePos(t.X + dx, t.Y + dy))
                    if (!layout.CanPass(t, new TilePos(t.X + dx, t.Y + dy))) return true;
                return false;
            });
            dropped += s.Equipment.RemoveAll(e => data.FindEquipment(e.EquipmentId) == null || layout.FindOpening(e.OpeningId) == null);
            foreach (DuctNode n in net.Nodes)
            {
                if (n.Kind != DuctNodeKind.Inline || net.Degree(n.Id) == 2) continue;
                n.Kind = DuctNodeKind.Free;
                n.PartId = "";
            }
            net.Nodes.RemoveAll(n => n.Kind == DuctNodeKind.Free && net.Degree(n.Id) == 0);

            // Anchors at every opening and outlet, as a fresh state has.
            BuildingState fresh = BuildingState.CreateFor(layout);
            foreach (DuctNode anchor in fresh.Air.Nodes)
            {
                if (net.Nodes.Exists(n => n.Kind == anchor.Kind && n.AnchorId == anchor.AnchorId)) continue;
                anchor.Id = Math.Max(net.NextNodeId, MaxId(net) + 1);
                net.NextNodeId = anchor.Id + 1;
                net.Nodes.Add(anchor);
            }
            return dropped;
        }

        static int MaxId(DuctNetworkState net)
        {
            int m = 0;
            foreach (DuctNode n in net.Nodes) m = Math.Max(m, n.Id);
            return m;
        }

        static byte[] Gzip(byte[] data)
        {
            using var ms = new MemoryStream();
            using (var gz = new GZipStream(ms, CompressionLevel.Optimal)) gz.Write(data, 0, data.Length);
            return ms.ToArray();
        }

        static byte[] Gunzip(byte[] data)
        {
            using var input = new MemoryStream(data);
            using var gz = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            gz.CopyTo(output);
            return output.ToArray();
        }
    }
}
