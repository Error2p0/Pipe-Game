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
        /// <summary>Real time since the save before the cap, s (0 if the clock went back).</summary>
        public double AwaySeconds;
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
            r.AwaySeconds = OfflineSeconds(r.File.SavedAtUtc, double.PositiveInfinity, r.Notes);
            r.OfflineSeconds = Math.Min(r.AwaySeconds, data.Balancing.Offline.MaxHours * 3600.0);
            // Edits that no longer fit are dropped when the game context opens (it regenerates the city first).
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
        /// <param name="layouts">The layout of a building id in this game, or null when it no longer exists.</param>
        public static void Sanitize(GameState game, DataRepository data, List<string> notes, Func<string, BuildingLayout?> layouts)
        {
            var known = new Dictionary<string, BuildingLayout>();
            game.Buildings.RemoveAll(b =>
            {
                BuildingLayout? l = layouts(b.BuildingId);
                if (l == null) notes.Add($"Dropped unknown building '{b.BuildingId}'");
                else known[b.BuildingId] = l;
                return l == null;
            });
            foreach (BuildingRecord r in game.Buildings)
            {
                BuildingLayout layout = known[r.BuildingId];
                r.Inspection ??= new Inspections.InspectionState();
                r.Inspection.Findings ??= new List<string>();
                // A grade the data no longer has is worked out again.
                if (r.Inspection.Grade.Length > 0 && !data.Inspections.EnergyRating.Exists(b => b.Grade == r.Inspection.Grade))
                {
                    r.Inspection.Grade = "";
                    r.Inspection.Rated = false;
                }
                int dropped = SanitizeState(r.Committed, layout, data) + SanitizeState(r.Design, layout, data);
                if (dropped > 0) notes.Add($"{r.BuildingId}: dropped {dropped} parts that no longer fit");
            }
            if (game.CurrentBuildingId.Length > 0 && game.FindBuilding(game.CurrentBuildingId) == null) game.CurrentBuildingId = "";
        }

        static int SanitizeState(BuildingState s, BuildingLayout layout, DataRepository data)
        {
            int dropped = 0;
            BuildingState fresh = BuildingState.CreateFor(layout);
            // Networks the data no longer has go; every network the layout has anchors for is checked against a fresh one.
            foreach (DuctNetworkState gone in s.Networks.FindAll(n => data.FindNetwork(n.FluidId) == null))
            {
                dropped += gone.Nodes.Count + gone.Segments.Count;
                s.Networks.Remove(gone);
            }
            var ids = new List<string>();
            foreach (DuctNetworkState n in s.Networks) ids.Add(n.FluidId);
            foreach (DuctNetworkState n in fresh.Networks) if (!ids.Contains(n.FluidId)) ids.Add(n.FluidId);
            foreach (string id in ids) dropped += SanitizeNetwork(s.Net(id), fresh.Net(id), layout, data);
            dropped += s.Equipment.RemoveAll(e => data.FindEquipment(e.EquipmentId) == null || layout.FindOpening(e.OpeningId) == null);
            dropped += s.Safety.RemoveAll(d => data.FindEquipment(d.EquipmentId) == null || !layout.IsInside(d.Pos));
            // Devices whose item is gone or that no longer stand inside one room go, with their ports.
            foreach (PlacedDevice d in s.Devices.ToArray())
            {
                EquipmentDef? def = data.FindEquipment(d.EquipmentId);
                bool fits = def != null && def.Ports.Count > 0;
                if (fits)
                    foreach (TilePos t in Devices.Tiles(def!, d.Pos, d.Facing)) if (layout.RoomIndexAt(t) != layout.RoomIndexAt(d.Pos) || layout.RoomIndexAt(t) < 0) fits = false;
                if (fits) continue;
                if (def != null) Devices.Remove(data, s, d.Key);
                else s.Devices.Remove(d);
                dropped++;
            }
            // Port anchors of devices that are gone.
            foreach (DuctNetworkState net in s.Networks)
                dropped += net.Nodes.RemoveAll(n => (n.Kind == DuctNodeKind.DeviceIn || n.Kind == DuctNodeKind.DeviceOut) && Devices.Find(s, n.AnchorId) == null);
            // Pre-placed equipment the layout starts with (the basic panel since Phase 4) is added when missing, for free.
            foreach (EquipmentState e in fresh.Equipment)
                if (s.EquipmentAt(e.OpeningId) == null) s.Equipment.Add(e.Clone());
            return dropped;
        }

        static int SanitizeNetwork(DuctNetworkState net, DuctNetworkState freshNet, BuildingLayout layout, DataRepository data)
        {
            int dropped = 0;
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
                if (a == null || b == null || data.FindMaterial(seg.MaterialId)?.Fluids.Contains(data.FluidIdOf(net.FluidId)) != true) return true;
                int axes = (a.X != b.X ? 1 : 0) + (a.Y != b.Y ? 1 : 0) + (a.Floor != b.Floor ? 1 : 0);
                if (axes != 1) return true;
                for (TilePos t = a.Pos; t != b.Pos; t = t.Toward(b.Pos))
                    if (!layout.CanPass(t, t.Toward(b.Pos))) return true;
                return false;
            });
            foreach (DuctNode n in net.Nodes)
            {
                if (n.Kind != DuctNodeKind.Inline || net.Degree(n.Id) == 2) continue;
                n.Kind = DuctNodeKind.Free;
                n.PartId = "";
            }
            net.Nodes.RemoveAll(n => n.Kind == DuctNodeKind.Free && net.Degree(n.Id) == 0);

            // Anchors at every opening and outlet, as a fresh state has.
            foreach (DuctNode anchor in freshNet.Nodes)
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
