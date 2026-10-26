using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace HeadLoss.Game.Save
{
    /// <summary>The saved file (<c>12-save-offline.md</c> §Save format).</summary>
    public sealed class SaveFile
    {
        /// <summary>Save format version; bumped with a <see cref="SaveMigrator"/> step.</summary>
        public int SaveVersion;
        /// <summary>Version of the building/city generators the layouts were made with.</summary>
        public int GeneratorVersion;
        /// <summary>Hash of the data files the save was written with.</summary>
        public string DataVersion = "";
        /// <summary>When the save was written, ISO 8601 UTC.</summary>
        public string SavedAtUtc = "";
        /// <summary>Time played, s.</summary>
        public double PlayTimeSeconds;
        public GameState GameState = new GameState();
    }

    /// <summary>JSON for saves: public fields only (properties are derived), camelCase, invariant.</summary>
    public static class SaveSerializer
    {
        public const int CurrentSaveVersion = 1;
        /// <summary>Generator version written into saves (hand-made layouts only until Phase 3).</summary>
        public const int CurrentGeneratorVersion = 1;

        sealed class FieldsOnlyResolver : CamelCasePropertyNamesContractResolver
        {
            protected override List<MemberInfo> GetSerializableMembers(Type objectType)
            {
                var members = new List<MemberInfo>();
                foreach (FieldInfo f in objectType.GetFields(BindingFlags.Public | BindingFlags.Instance))
                    if (!f.IsInitOnly) members.Add(f);
                return members;
            }
        }

        static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            ContractResolver = new FieldsOnlyResolver(),
            MissingMemberHandling = MissingMemberHandling.Ignore,
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            Culture = System.Globalization.CultureInfo.InvariantCulture,
            FloatFormatHandling = FloatFormatHandling.String,
            Formatting = Formatting.Indented,
        };

        public static string Write(SaveFile file) => JsonConvert.SerializeObject(file, Settings);

        /// <summary>Parses save JSON, keeping timestamps as strings (no local-time conversion).</summary>
        public static JObject Parse(string json)
        {
            using var reader = new JsonTextReader(new System.IO.StringReader(json)) { DateParseHandling = DateParseHandling.None };
            return JObject.Load(reader);
        }

        /// <summary>Reads a save already migrated to the current version.</summary>
        public static SaveFile FromJObject(JObject o) =>
            o.ToObject<SaveFile>(JsonSerializer.Create(Settings)) ?? throw new FormatException("Empty save");

        /// <summary>A deep copy through JSON (tests, autosave snapshots).</summary>
        public static GameState Copy(GameState g)
        {
            var f = new SaveFile { GameState = g };
            return FromJObject(Parse(Write(f))).GameState;
        }
    }

    /// <summary>
    /// Upgrades older saves one version at a time (<c>12-save-offline.md</c> §Save format). Each step turns a
    /// version-n JObject into version n+1. A test loads a sample save of every past version.
    /// </summary>
    public static class SaveMigrator
    {
        /// <summary>Step from version (key) to key + 1. Empty while only version 1 exists.</summary>
        static readonly Dictionary<int, Action<JObject>> Steps = new Dictionary<int, Action<JObject>>();

        /// <exception cref="FormatException">The save is newer than this build or has no version.</exception>
        public static JObject Migrate(JObject o, out int fromVersion)
        {
            JToken? v = o["saveVersion"];
            if (v == null || v.Type != JTokenType.Integer) throw new FormatException("Save has no saveVersion");
            fromVersion = v.Value<int>();
            if (fromVersion > SaveSerializer.CurrentSaveVersion)
                throw new FormatException($"Save version {fromVersion} is newer than this game ({SaveSerializer.CurrentSaveVersion})");
            for (int n = fromVersion; n < SaveSerializer.CurrentSaveVersion; n++)
            {
                if (!Steps.TryGetValue(n, out Action<JObject> step)) throw new FormatException($"No migration from save version {n}");
                step(o);
                o["saveVersion"] = n + 1;
            }
            return o;
        }
    }
}
