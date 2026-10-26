using System.Collections.Generic;
using HeadLoss.Core.Data;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.Economy;
using HeadLoss.Game.Session;

namespace HeadLoss.Game
{
    /// <summary>Everything the rules need, passed explicitly (no singletons in sim code).</summary>
    public sealed class GameContext
    {
        public DataRepository Data { get; }
        public GameState Game { get; }
        /// <summary>A live session per building record (every building earns, open or not).</summary>
        public IReadOnlyList<BuildingSession> Sessions => _sessions;
        /// <summary>The building open in the building view, if any.</summary>
        public BuildingSession? Building { get; private set; }

        readonly List<BuildingSession> _sessions = new List<BuildingSession>();

        public GameContext(DataRepository data, GameState game)
        {
            Data = data;
            Game = game;
            foreach (BuildingRecord r in game.Buildings) _sessions.Add(CreateSession(r));
            if (game.CurrentBuildingId.Length > 0) Open(game.CurrentBuildingId);
        }

        /// <summary>A new game: starting wallet and a fresh record for each starter building; opens the first.</summary>
        public static GameState NewGame(DataRepository data)
        {
            var g = new GameState { Wallet = data.Balancing.Economy.StartingWallet };
            foreach (string id in data.Game.StarterBuildings)
                g.Buildings.Add(BuildingRecord.CreateFor(LayoutFor(data, id)));
            if (data.Game.StarterBuildings.Count > 0) g.CurrentBuildingId = data.Game.StarterBuildings[0];
            return g;
        }

        /// <summary>The layout of a building id (hand-made buildings until the city generator exists).</summary>
        public static BuildingLayout LayoutFor(DataRepository data, string buildingId) =>
            new BuildingLayout(data.HandmadeBuilding(buildingId));

        BuildingSession CreateSession(BuildingRecord r) => new BuildingSession(Data, LayoutFor(Data, r.BuildingId), r, Game);

        public BuildingSession? FindSession(string buildingId) => _sessions.Find(s => s.Record.BuildingId == buildingId);

        /// <summary>Opens a building in the building view (the previous one is left).</summary>
        public BuildingSession Open(string buildingId)
        {
            BuildingSession? s = FindSession(buildingId);
            if (s == null)
            {
                BuildingRecord r = Game.FindBuilding(buildingId) ?? BuildingRecord.CreateFor(LayoutFor(Data, buildingId));
                if (!Game.Buildings.Contains(r)) Game.Buildings.Add(r);
                s = CreateSession(r);
                _sessions.Add(s);
            }
            if (Building != null && Building != s) Building.Leave();
            Building = s;
            Game.CurrentBuildingId = buildingId;
            return s;
        }

        /// <summary>Back to the building list: undo history cleared.</summary>
        public void Leave()
        {
            Building?.Leave();
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
