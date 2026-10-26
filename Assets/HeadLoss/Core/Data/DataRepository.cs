using System;
using System.Collections.Generic;
using HeadLoss.Core.Data.Defs;
using Newtonsoft.Json;

namespace HeadLoss.Core.Data
{
    /// <summary>Thrown when a data file is missing or cannot be parsed.</summary>
    public sealed class DataLoadException : Exception
    {
        public DataLoadException(string message, Exception? inner = null) : base(message, inner) { }
    }

    /// <summary>
    /// All game data, loaded from JSON through one parser (Newtonsoft). Lists keep file order;
    /// lookups are by id. Unknown fields are load errors so typos never pass silently.
    /// </summary>
    public sealed class DataRepository
    {
        public const string HandmadeFolder = "handmade/";

        public GameDef Game { get; private set; } = new GameDef();
        public BalancingDef Balancing { get; private set; } = new BalancingDef();
        public IReadOnlyList<FluidDef> Fluids => _fluids;
        public IReadOnlyList<MaterialDef> Materials => _materials;
        public IReadOnlyList<PipeFamilyDef> PipeFamilies => _pipeFamilies;
        public IReadOnlyList<FittingDef> Fittings => _fittings;
        public IReadOnlyList<EquipmentDef> Equipment => _equipment;
        public IReadOnlyList<OutletDef> Outlets => _outlets;
        public IReadOnlyList<HandmadeBuildingDef> HandmadeBuildings => _handmade;

        /// <summary>Fluid tables by fluid id.</summary>
        public IReadOnlyDictionary<string, FluidTableDef> FluidTables => _fluidTables;

        /// <summary>Problems found while loading (duplicate ids). The validator reports them too.</summary>
        public IReadOnlyList<string> LoadIssues => _loadIssues;

        readonly List<FluidDef> _fluids = new List<FluidDef>();
        readonly List<MaterialDef> _materials = new List<MaterialDef>();
        readonly List<PipeFamilyDef> _pipeFamilies = new List<PipeFamilyDef>();
        readonly List<FittingDef> _fittings = new List<FittingDef>();
        readonly List<EquipmentDef> _equipment = new List<EquipmentDef>();
        readonly List<OutletDef> _outlets = new List<OutletDef>();
        readonly List<HandmadeBuildingDef> _handmade = new List<HandmadeBuildingDef>();
        readonly Dictionary<string, FluidTableDef> _fluidTables = new Dictionary<string, FluidTableDef>();
        readonly List<string> _loadIssues = new List<string>();

        readonly Dictionary<string, FluidDef> _fluidById = new Dictionary<string, FluidDef>();
        readonly Dictionary<string, MaterialDef> _materialById = new Dictionary<string, MaterialDef>();
        readonly Dictionary<string, PipeFamilyDef> _familyById = new Dictionary<string, PipeFamilyDef>();
        readonly Dictionary<string, FittingDef> _fittingById = new Dictionary<string, FittingDef>();
        readonly Dictionary<string, EquipmentDef> _equipmentById = new Dictionary<string, EquipmentDef>();
        readonly Dictionary<string, OutletDef> _outletById = new Dictionary<string, OutletDef>();
        readonly Dictionary<string, HandmadeBuildingDef> _handmadeById = new Dictionary<string, HandmadeBuildingDef>();

        static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            MissingMemberHandling = MissingMemberHandling.Error,
            NullValueHandling = NullValueHandling.Ignore,
            ObjectCreationHandling = ObjectCreationHandling.Replace,
        };

        DataRepository() { }

        /// <summary>Loads every data file from <paramref name="source"/>.</summary>
        /// <exception cref="DataLoadException">A required file is missing or malformed.</exception>
        public static DataRepository Load(IDataSource source)
        {
            var repo = new DataRepository();
            repo.Game = Parse<GameDef>(source, "game.json");
            repo.Balancing = Parse<BalancingDef>(source, "balancing.json");
            AddAll(repo, Parse<List<FluidDef>>(source, "fluids.json"), repo._fluids, repo._fluidById, f => f.Id, "fluids.json");
            AddAll(repo, Parse<List<MaterialDef>>(source, "materials.json"), repo._materials, repo._materialById, m => m.Id, "materials.json");
            AddAll(repo, Parse<List<PipeFamilyDef>>(source, "pipes.json"), repo._pipeFamilies, repo._familyById, p => p.Id, "pipes.json");
            AddAll(repo, Parse<List<FittingDef>>(source, "fittings.json"), repo._fittings, repo._fittingById, f => f.Id, "fittings.json");
            AddAll(repo, Parse<List<EquipmentDef>>(source, "equipment.json"), repo._equipment, repo._equipmentById, e => e.Id, "equipment.json");
            AddAll(repo, Parse<List<OutletDef>>(source, "outlets.json"), repo._outlets, repo._outletById, o => o.Id, "outlets.json");

            foreach (FluidDef fluid in repo._fluids)
            {
                if (string.IsNullOrEmpty(fluid.Table)) continue;
                if (source.ReadText(fluid.Table) == null)
                {
                    repo._loadIssues.Add($"fluids.json: fluid '{fluid.Id}' table '{fluid.Table}' not found");
                    continue;
                }
                repo._fluidTables[fluid.Id] = Parse<FluidTableDef>(source, fluid.Table);
            }

            var handmade = new List<HandmadeBuildingDef>();
            foreach (string path in source.ListFiles())
            {
                if (path.StartsWith(HandmadeFolder, StringComparison.Ordinal))
                    handmade.Add(Parse<HandmadeBuildingDef>(source, path));
            }
            AddAll(repo, handmade, repo._handmade, repo._handmadeById, b => b.Id, HandmadeFolder);
            return repo;
        }

        static void AddAll<T>(DataRepository repo, List<T> items, List<T> list, Dictionary<string, T> byId, Func<T, string> id, string file)
        {
            foreach (T item in items)
            {
                string key = id(item);
                if (byId.ContainsKey(key))
                {
                    repo._loadIssues.Add($"{file}: duplicate id '{key}'");
                    continue;
                }
                byId[key] = item;
                list.Add(item);
            }
        }

        static T Parse<T>(IDataSource source, string path) where T : class
        {
            string? text = source.ReadText(path);
            if (text == null) throw new DataLoadException($"Data file '{path}' not found");
            try
            {
                T? value = JsonConvert.DeserializeObject<T>(text, Settings);
                if (value == null) throw new DataLoadException($"Data file '{path}' is empty");
                return value;
            }
            catch (JsonException e)
            {
                throw new DataLoadException($"Data file '{path}': {e.Message}", e);
            }
        }

        public FluidDef? FindFluid(string id) => _fluidById.TryGetValue(id, out var v) ? v : null;
        public MaterialDef? FindMaterial(string id) => _materialById.TryGetValue(id, out var v) ? v : null;
        public PipeFamilyDef? FindPipeFamily(string id) => _familyById.TryGetValue(id, out var v) ? v : null;
        public FittingDef? FindFitting(string id) => _fittingById.TryGetValue(id, out var v) ? v : null;
        public EquipmentDef? FindEquipment(string id) => _equipmentById.TryGetValue(id, out var v) ? v : null;
        public OutletDef? FindOutlet(string id) => _outletById.TryGetValue(id, out var v) ? v : null;
        public HandmadeBuildingDef? FindHandmadeBuilding(string id) => _handmadeById.TryGetValue(id, out var v) ? v : null;

        public FluidDef Fluid(string id) => FindFluid(id) ?? throw new KeyNotFoundException($"Unknown fluid '{id}'");
        public MaterialDef Material(string id) => FindMaterial(id) ?? throw new KeyNotFoundException($"Unknown material '{id}'");
        public PipeFamilyDef PipeFamily(string id) => FindPipeFamily(id) ?? throw new KeyNotFoundException($"Unknown pipe family '{id}'");
        public FittingDef Fitting(string id) => FindFitting(id) ?? throw new KeyNotFoundException($"Unknown fitting '{id}'");
        public EquipmentDef EquipmentItem(string id) => FindEquipment(id) ?? throw new KeyNotFoundException($"Unknown equipment '{id}'");
        public OutletDef Outlet(string id) => FindOutlet(id) ?? throw new KeyNotFoundException($"Unknown outlet '{id}'");
        public HandmadeBuildingDef HandmadeBuilding(string id) => FindHandmadeBuilding(id) ?? throw new KeyNotFoundException($"Unknown hand-made building '{id}'");
    }
}
