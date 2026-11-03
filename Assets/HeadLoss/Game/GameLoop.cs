using System.Collections.Generic;
using HeadLoss.Core.Data;
using HeadLoss.Core.Data.Defs;
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
        /// <summary>The last city solve (null before the first one, or without a city).</summary>
        public CityNetworkResult? CityResult { get; private set; }
        /// <summary>Routing violations of the mains and cables (the last city solve).</summary>
        public List<CityViolation> CityViolations { get; } = new List<CityViolation>();
        /// <summary>Recurring routing fines, $ per income cycle.</summary>
        public double FinesPerCycle { get; private set; }
        /// <summary>Set when mains, cables or a building's draw changed: the city re-solves on the next tick.</summary>
        public bool CityDirty { get; set; } = true;

        /// <summary>
        /// The city solve (<c>03-city.md</c> §City network solve): water connection pressures and shares, power allocations,
        /// utilisation and fines; then every session gets its connection. Without a city each building keeps the
        /// reference water pressure and the basic power connection.
        /// </summary>
        public void SolveCity()
        {
            CityDirty = false;
            if (City == null) return;
            UtilityState u = Game.City!.Utilities;
            CityResult = CityNetwork.Solve(Data, City, u,
                plot => FindSession(plot) is BuildingSession s ? s.WaterDemand() : 0,
                plot => FindSession(plot) is BuildingSession s ? s.RequestedPowerW / 1000 : 0);
            CityViolations.Clear();
            FinesPerCycle = Utilities.Fines(Data, City, u, CityViolations);
            foreach (BuildingSession s in _sessions)
            {
                WaterSupply supply = CityResult.Water.TryGetValue(s.Record.BuildingId, out WaterConnection w) && w.Connected
                    ? new WaterSupply(true, w.PressurePa) : WaterSupply.None;
                if (supply.Connected != s.Supply.Connected || System.Math.Abs(supply.PressurePa - s.Supply.PressurePa) > 1)
                {
                    s.Supply = supply;
                    s.MarkDirty();
                }
                s.Power = CityResult.Power.TryGetValue(s.Record.BuildingId, out PowerConnection pc) ? pc : default;
            }
        }

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
            RegenerateOlder(game);
            SaveStore.Sanitize(game, data, _notes, FindLayout);
            foreach (BuildingRecord r in game.Buildings) _sessions.Add(CreateSession(r));
            if (game.CurrentBuildingId.Length > 0 && CanOpen(game.CurrentBuildingId)) Open(game.CurrentBuildingId);
            else game.CurrentBuildingId = "";
        }

        /// <summary>
        /// Phase 4 rebuilt tier 2+ buildings (floors, water): a save from an older generator gets back what it spent on
        /// them and they start fresh (D83). Tier 1 layouts are unchanged and keep their edits.
        /// </summary>
        void RegenerateOlder(GameState game)
        {
            if (game.GeneratorVersion >= 3 || City == null)
            {
                game.GeneratorVersion = Save.SaveSerializer.CurrentGeneratorVersion;
                return;
            }
            for (int i = 0; i < game.Buildings.Count; i++)
            {
                BuildingRecord r = game.Buildings[i];
                Plot? plot = City.FindPlot(r.BuildingId);
                if (plot == null || plot.Tier < 2) continue;
                BuildingLayout? layout = FindLayout(r.BuildingId);
                if (r.SpentTotal > 0) game.Wallet += r.SpentTotal;
                _notes.Add($"{r.BuildingId}: rebuilt with floors and water; {r.SpentTotal:0.##} refunded");
                if (layout == null)
                {
                    game.Buildings.RemoveAt(i--);
                    continue;
                }
                BuildingRecord fresh = BuildingRecord.CreateFor(layout);
                fresh.LifetimeIncome = r.LifetimeIncome;
                fresh.SheetSeen = r.SheetSeen;
                game.Buildings[i] = fresh;
            }
            game.GeneratorVersion = Save.SaveSerializer.CurrentGeneratorVersion;
        }

        // ---------- city actions (03 §Routing in the city view; 07 §City equipment, §Meta upgrades) ----------

        /// <summary>Lays a main or cable along a path, paid at once. Returns the quote (Valid false = nothing laid).</summary>
        public RouteQuote LayMain(string utility, double diameterMm, double capacityKW, IReadOnlyList<(int x, int y)> path, bool drilled)
        {
            var bad = new RouteQuote();
            if (City == null || Game.City == null) { bad.Error = "No city"; return bad; }
            PipeFamilyDef? family = Utilities.FamilyFor(Data, utility);
            if (family == null) { bad.Error = "Nothing to route for this utility yet"; return bad; }
            int tier = 99;
            foreach (PipeSizeDef sz in family.Sizes)
                if ((family.Kind == "cable" ? sz.CapacityKW == capacityKW : sz.DiameterMm == diameterMm)) tier = sz.Tier;
            if (!Progression.Tech.Unlocked(Game, tier)) { bad.Error = $"Unlocks at tier {tier}"; return bad; }
            RouteQuote q = Utilities.Quote(Data, City, Game.City.Utilities, utility, diameterMm, capacityKW, path, drilled);
            if (!q.Valid) return q;
            if (!Game.CanAfford(q.Cost))
            {
                q.Valid = false;
                q.Error = Game.Debt > 0 ? "Purchases wait until the debt is repaid" : "Not enough money";
                return q;
            }
            Game.Wallet -= q.Cost;
            Utilities.Lay(Game.City.Utilities, utility, family.Id, diameterMm, capacityKW, path, drilled, q);
            CityDirty = true;
            return q;
        }

        /// <summary>
        /// What a building would need from the mains: its current inflow, or the design flow of all its taps when that
        /// is more (nothing built yet), m³/s.
        /// </summary>
        public double PlotWaterDemand(string plotId)
        {
            double built = FindSession(plotId)?.WaterDemand() ?? 0;
            BuildingLayout? layout = CityBuildings?.Get(plotId)?.Layout;
            if (layout == null) return built;
            double design = 0;
            foreach (OutletPlacementDef o in layout.Outlets)
                if (BuildingState.OutletFluid(o.Type) == "water") design += Networks.NetworkCompiler.DesignFlow(Data, layout, o);
            return System.Math.Max(built, design);
        }

        /// <summary>The route tool's live preview: the connection a water main would give its destination (null: it reaches no building).</summary>
        public WaterConnection? PreviewWater(double diameterMm, IReadOnlyList<(int x, int y)> path, bool drilled, RouteQuote quote)
        {
            if (City == null || Game.City == null || !quote.Valid || quote.Destination.Length == 0) return null;
            UtilityState u = Game.City.Utilities;
            PipeFamilyDef? family = Utilities.FamilyFor(Data, "water");
            if (family == null) return null;
            var trial = new UtilityState { Corridors = u.Corridors, NextId = u.NextId };
            trial.Mains.AddRange(u.Mains);
            Utilities.Lay(trial, "water", family.Id, diameterMm, 0, path, drilled, quote);
            CityNetworkResult r = CityNetwork.Solve(Data, City, trial, PlotWaterDemand, _ => 0);
            return r.Water.TryGetValue(quote.Destination, out WaterConnection w) ? w : (WaterConnection?)null;
        }

        /// <summary>Removes a main or cable (and branches that only it fed), refunding a share of what they cost.</summary>
        public bool RemoveMain(int mainId)
        {
            if (Game.City == null) return false;
            List<MainState> removed = Utilities.Remove(Game.City.Utilities, mainId);
            if (removed.Count == 0) return false;
            double refund = 0;
            foreach (MainState m in removed) refund += m.Paid * Data.Balancing.City.RemoveRefundFraction;
            Game.ApplyIncome(refund);
            CityDirty = true;
            return true;
        }

        /// <summary>Builds a utility corridor on a road tile (shared trench: four lines, no separation violations).</summary>
        public bool BuildCorridor(int x, int y)
        {
            if (City == null || Game.City == null || !Utilities.CanBuildCorridor(City, Game.City.Utilities, x, y)) return false;
            double price = Data.Balancing.City.CorridorPerTile;
            if (!Game.CanAfford(price)) return false;
            Game.Wallet -= price;
            Utilities.BuildCorridor(Game.City.Utilities, x, y);
            CityDirty = true;
            return true;
        }

        /// <summary>Insurance on or off (tier 3): fines halved for a premium on gross income.</summary>
        public bool SetInsurance(bool on)
        {
            if (Game.City == null) return false;
            MetaUpgradeDef? def = Data.Upgrades.Meta.Find(m => m.Id == "insurance");
            if (def == null || (on && !Progression.Tech.Unlocked(Game, def.Tier))) return false;
            Game.City.Utilities.Insurance = on;
            CityDirty = true;
            return true;
        }

        /// <summary>Buys the engineering software (tier 3, one-off): the stress forecast in design mode.</summary>
        public bool BuyEngineeringSoftware()
        {
            if (Game.City == null || Game.City.Utilities.EngineeringSoftware) return false;
            MetaUpgradeDef? def = Data.Upgrades.Meta.Find(m => m.Id == "engineering-software");
            if (def == null || !Progression.Tech.Unlocked(Game, def.Tier) || !Game.CanAfford(def.Price)) return false;
            Game.Wallet -= def.Price;
            Game.City.Utilities.EngineeringSoftware = true;
            return true;
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
            BuildingLayout.From(data, data.HandmadeBuilding(buildingId));

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
            var s = new BuildingSession(Data, layout, r, Game, g?.BudgetCap, Climate);
            // In a city, water comes only through a routed main (the first city solve sets it).
            if (City != null) s.Supply = WaterSupply.None;
            CityDirty = true;
            return s;
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
        double _citySolve;

        public GameLoop(GameContext context) => Context = context;

        /// <param name="dtSeconds">Seconds since the last tick.</param>
        public void Tick(double dtSeconds)
        {
            _citySolve += dtSeconds;
            if (Context.CityDirty || _citySolve >= Context.Data.Balancing.City.SolveIntervalSeconds)
            {
                _citySolve = 0;
                Context.SolveCity();
            }
            foreach (BuildingSession s in Context.Sessions) s.Tick(dtSeconds);
            Context.Game.PlayTimeSeconds += dtSeconds;
            double step = 1.0 / Context.Data.Balancing.Economy.TickHz;
            _economyAccumulator += dtSeconds;
            while (_economyAccumulator >= step)
            {
                _economyAccumulator -= step;
                EconomyStep(Context.Game, Context.Sessions, step);
                CityCosts(Context, step);
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
        /// <summary>
        /// City running costs, charged pro rata each tick (<c>02-economy.md</c> §Recurring fines): routing fines, and the
        /// insurance premium (a share of every building's gross) when insured.
        /// </summary>
        public static void CityCosts(GameContext ctx, double dt)
        {
            GameState g = ctx.Game;
            FinesPerSecond(ctx, out double fines);
            double cost = fines + PremiumsPerSecond(ctx);
            if (cost <= 0) return;
            g.ApplyIncome(-cost * dt);
            g.LifetimeIncome -= cost * dt;
        }

        /// <summary>Routing fines spread over the income cycle, $/s.</summary>
        public static void FinesPerSecond(GameContext ctx, out double fines) =>
            fines = ctx.Game.City == null ? 0 : ctx.FinesPerCycle / ctx.Data.Balancing.Economy.IncomeCycleSeconds;

        /// <summary>Share of a building's gross that goes to premiums: insurance (city-wide) and its maintenance contract.</summary>
        public static double PremiumFraction(GameContext ctx, BuildingSession s)
        {
            double fraction = 0;
            if (ctx.Game.City?.Utilities.Insurance == true) fraction += ctx.Data.Upgrades.Meta.Find(m => m.Id == "insurance")?.PremiumFraction ?? 0;
            if (s.Record.Failures.MaintenanceContract) fraction += ctx.Data.Upgrades.Meta.Find(m => m.Id == "maintenance-contract")?.PremiumFraction ?? 0;
            return fraction;
        }

        /// <summary>Insurance and maintenance premiums on what is earning now, $/s.</summary>
        public static double PremiumsPerSecond(GameContext ctx)
        {
            double total = 0;
            foreach (BuildingSession s in ctx.Sessions) total += s.CommittedEvaluation.Gross * PremiumFraction(ctx, s);
            return total;
        }

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
