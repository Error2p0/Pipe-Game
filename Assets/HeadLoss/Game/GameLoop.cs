using System.Collections.Generic;
using HeadLoss.Core.Data;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.City;
using HeadLoss.Game.Generation;
using HeadLoss.Game.Save;
using HeadLoss.Game.Economy;
using HeadLoss.Game.Session;

namespace HeadLoss.Game
{
    /// <summary>Everything the rules need, passed explicitly (no singletons in sim code).</summary>
    public sealed class GameContext
    {
        public DataRepository Data { get; }
        public GameState Game { get; }
        /// <summary>The city layout, regenerated from <see cref="CityState.Seed"/> (null in hand-made test fixtures).</summary>
        public CityLayout? City { get; }
        /// <summary>The city's buildings, generated lazily from their plots.</summary>
        public CityBuildings? CityBuildings { get; }
        /// <summary>False when no starter seed was solvable (a dev report item; the game still runs).</summary>
        public bool StarterSolvable { get; } = true;
        /// <summary>A live session per building record (every building earns, open or not).</summary>
        public IReadOnlyList<BuildingSession> Sessions => _sessions;
        /// <summary>The building open in the building view, if any.</summary>
        public BuildingSession? Building { get; private set; }
        /// <summary>What loading had to drop or repair (for the log).</summary>
        public IReadOnlyList<string> Notes => _notes;
        /// <summary>Outside conditions for every building (the Unity layer sets the local hour).</summary>
        public Thermal.Climate Climate { get; }

        readonly List<BuildingSession> _sessions = new List<BuildingSession>();
        readonly List<string> _notes = new List<string>();

        public GameContext(DataRepository data, GameState game)
        {
            Data = data;
            Game = game;
            if (game.City != null)
            {
                City = StarterGuarantee.NewCity(data, game.City.Seed, game.City.Index, out bool ok);
                StarterSolvable = ok;
                CityBuildings = new CityBuildings(data, City);
            }
            Climate = City != null ? new Thermal.Climate(data, City.Modifiers) : Thermal.Climate.Fixed(data, data.Balancing.Physics.DefaultAirTemperatureC);
            SaveStore.Sanitize(game, data, _notes, FindLayout);
            foreach (BuildingRecord r in game.Buildings) _sessions.Add(CreateSession(r));
            if (game.CurrentBuildingId.Length > 0 && CanOpen(game.CurrentBuildingId)) Open(game.CurrentBuildingId);
            else game.CurrentBuildingId = "";
        }

        /// <summary>A new game in a new city: the starting wallet, nothing built, the city view open.</summary>
        /// <param name="citySeed">The city seed (random unless the player enters one; chosen by the caller).</param>
        public static GameState NewGame(DataRepository data, ulong citySeed) =>
            new GameState { Wallet = data.Balancing.Economy.StartingWallet, City = new CityState { Seed = citySeed } };

        /// <summary>A test fixture: no city, a fresh record for each hand-made building, the first one open.</summary>
        public static GameState NewFixtureGame(DataRepository data, params string[] handmadeIds)
        {
            var g = new GameState { Wallet = data.Balancing.Economy.StartingWallet };
            foreach (string id in handmadeIds) g.Buildings.Add(BuildingRecord.CreateFor(LayoutFor(data, id)));
            if (handmadeIds.Length > 0) g.CurrentBuildingId = handmadeIds[0];
            return g;
        }

        /// <summary>The layout of a hand-made building (test fixtures and tools).</summary>
        public static BuildingLayout LayoutFor(DataRepository data, string buildingId) =>
            new BuildingLayout(data.HandmadeBuilding(buildingId));

        /// <summary>The layout of a building in this game (a city plot, or a hand-made fixture), or null.</summary>
        public BuildingLayout? FindLayout(string buildingId)
        {
            if (CityBuildings != null && City!.FindPlot(buildingId) != null) return CityBuildings.Get(buildingId)?.Layout;
            return Data.FindHandmadeBuilding(buildingId) != null ? LayoutFor(Data, buildingId) : null;
        }

        /// <summary>True when the building exists and can be opened in this version.</summary>
        public bool CanOpen(string buildingId) => FindLayout(buildingId) != null;

        BuildingSession CreateSession(BuildingRecord r)
        {
            GeneratedBuilding? g = CityBuildings != null ? CityBuildings.Get(r.BuildingId) : null;
            BuildingLayout layout = g?.Layout ?? FindLayout(r.BuildingId)!;
            return new BuildingSession(Data, layout, r, Game, g?.BudgetCap, Climate);
        }

        public BuildingSession? FindSession(string buildingId) => _sessions.Find(s => s.Record.BuildingId == buildingId);

        /// <summary>Raised when a tech tier unlocks (the tier number).</summary>
        public event System.Action<int>? TierUnlocked;

        /// <summary>Checks the unlock conditions (<see cref="Progression.Tech.Update"/>) and raises <see cref="TierUnlocked"/>.</summary>
        public void UpdateUnlocks(System.Func<BuildingSession, bool>? online = null)
        {
            int before = Progression.Tech.UnlockedTier(Game);
            int now = Progression.Tech.Update(this, online);
            for (int t = before + 1; now > 0 && t <= now; t++) TierUnlocked?.Invoke(t);
        }

        /// <summary>Raised when a session is created for a building opened for the first time.</summary>
        public event System.Action<BuildingSession>? SessionCreated;

        /// <summary>Opens a building in the building view (the previous one is left).</summary>
        public BuildingSession Open(string buildingId)
        {
            BuildingSession? s = FindSession(buildingId);
            if (s == null)
            {
                BuildingLayout layout = FindLayout(buildingId) ?? throw new System.ArgumentException($"Building '{buildingId}' cannot be opened");
                BuildingRecord r = Game.FindBuilding(buildingId) ?? BuildingRecord.CreateFor(layout);
                if (!Game.Buildings.Contains(r)) Game.Buildings.Add(r);
                s = CreateSession(r);
                _sessions.Add(s);
                SessionCreated?.Invoke(s);
            }
            if (Building != null && Building != s) Building.Leave();
            if (Building != null) Building.IsOpen = false;
            Building = s;
            s.IsOpen = true;
            Game.CurrentBuildingId = buildingId;
            return s;
        }

        /// <summary>Back to the city view: undo history cleared.</summary>
        public void Leave()
        {
            Building?.Leave();
            if (Building != null) Building.IsOpen = false;
            Building = null;
            Game.CurrentBuildingId = "";
        }
    }

    /// <summary>
    /// Advances the game rules. The Unity layer calls <see cref="Tick"/> from Update. Sessions solve on change;
    /// the economy runs at <c>economy.tickHz</c> so the wallet moves smoothly (<c>05-simulation.md</c> §5).
    /// </summary>
    public sealed class GameLoop
    {
        public GameContext Context { get; }
        double _economyAccumulator;
        double _unlockAccumulator;

        public GameLoop(GameContext context) => Context = context;

        /// <param name="dtSeconds">Seconds since the last tick.</param>
        public void Tick(double dtSeconds)
        {
            foreach (BuildingSession s in Context.Sessions) s.Tick(dtSeconds);
            Context.Game.PlayTimeSeconds += dtSeconds;
            double step = 1.0 / Context.Data.Balancing.Economy.TickHz;
            _economyAccumulator += dtSeconds;
            while (_economyAccumulator >= step)
            {
                _economyAccumulator -= step;
                EconomyStep(Context.Game, Context.Sessions, step);
            }
            _unlockAccumulator += dtSeconds;
            if (_unlockAccumulator >= 1.0)
            {
                _unlockAccumulator = 0;
                Context.UpdateUnlocks();
            }
        }

        /// <summary>
        /// One economy step: each building's committed evaluation pays its net income for <paramref name="dt"/>
        /// seconds (gross only when online; electricity always). A building online for the first time drops its
        /// budget cap.
        /// </summary>
        public static void EconomyStep(GameState game, IReadOnlyList<BuildingSession> sessions, double dt)
        {
            foreach (BuildingSession s in sessions)
            {
                BuildingEvaluation ev = s.CommittedEvaluation;
                if (ev.Online) s.Record.EverOnline = true;
                double net = ev.Net * dt;
                game.ApplyIncome(net);
                game.LifetimeIncome += net;
                s.Record.LifetimeIncome += ev.Gross * dt;
            }
        }
    }
}
