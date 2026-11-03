using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using HeadLoss.Core;
using HeadLoss.Core.Data;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.Commands;
using HeadLoss.Game.Economy;
using HeadLoss.Game.Networks;
using HeadLoss.Game.Thermal;
using HeadLoss.Sim.Fluids;
using HeadLoss.Sim.Network;

namespace HeadLoss.Game.Session
{
    /// <summary>One fluid's compiled network and its solve.</summary>
    public sealed class FluidSolve
    {
        public readonly CompiledNetwork Compiled;
        public readonly SolveResult Result;

        public FluidSolve(CompiledNetwork compiled, SolveResult result)
        {
            Compiled = compiled;
            Result = result;
        }

        public string Fluid => Compiled.Fluid;
    }

    /// <summary>A building's compiled networks and their solves, always swapped together.</summary>
    public sealed class NetworkSnapshot
    {
        /// <summary>The air network (always present).</summary>
        public readonly FluidSolve Air;
        /// <summary>The water network (null when the building has none).</summary>
        public readonly FluidSolve? Water;
        /// <summary>The state the snapshot was solved from (a private copy).</summary>
        public BuildingState State;

        public NetworkSnapshot(FluidSolve air, FluidSolve? water, BuildingState state)
        {
            Air = air;
            Water = water;
            State = state;
        }

        /// <summary>Air network (the Phase 1–3 fields).</summary>
        public CompiledNetwork Compiled => Air.Compiled;
        public SolveResult Result => Air.Result;

        /// <summary>The solve of a fluid's network, or null.</summary>
        public FluidSolve? For(string fluid) => fluid == "air" ? Air : fluid == "water" ? Water : null;

        /// <summary>Every solved network.</summary>
        public IReadOnlyList<FluidSolve> All => Water != null ? new[] { Air, Water } : new[] { Air };

        public bool Converged => Air.Result.Converged && (Water == null || Water.Result.Converged);
        public double SolveMilliseconds => Air.Result.SolveMilliseconds + (Water?.Result.SolveMilliseconds ?? 0);
    }

    /// <summary>The building's city water connection (<c>03-city.md</c> §City network solve).</summary>
    public readonly struct WaterSupply
    {
        public readonly bool Connected;
        /// <summary>Connection pressure, Pa gauge at ground level.</summary>
        public readonly double PressurePa;

        public WaterSupply(bool connected, double pressurePa)
        {
            Connected = connected;
            PressurePa = pressurePa;
        }

        public static readonly WaterSupply None = new WaterSupply(false, 0);
    }

    /// <summary>A pair of solvers (one per fluid) so each keeps its structure cache across drift re-solves.</summary>
    sealed class SolverSet
    {
        public readonly NetworkSolver Air, Water;
        public SolverSet(SolverSettings s)
        {
            Air = new NetworkSolver(s);
            Water = new NetworkSolver(s);
        }
        public NetworkSolver For(string fluid) => fluid == "air" ? Air : Water;
    }

    /// <summary>Result of previewing a run while it is being drawn ("simulate before you buy").</summary>
    public sealed class DrawPreview
    {
        public DrawError Error;
        public NetworkSnapshot? Snapshot;
    }

    /// <summary>Whether the design can be committed now, and why not.</summary>
    public readonly struct CommitCheck
    {
        public readonly bool Allowed;
        /// <summary>Short calm reason when not allowed ("" when allowed).</summary>
        public readonly string Reason;

        public CommitCheck(bool allowed, string reason)
        {
            Allowed = allowed;
            Reason = reason;
        }
    }

    /// <summary>
    /// One building's live session: layout, saved record (committed state + design draft), commands, the
    /// hand crank, and the latest network solves and evaluations of both states. Re-solves on change, when
    /// the crank speed moves, and every <c>resolveIntervalSeconds</c>; solves slower than
    /// <c>asyncThresholdMs</c> move to a worker thread and swap in when done.
    /// </summary>
    public sealed class BuildingSession
    {
        public DataRepository Data { get; }
        public BuildingLayout Layout { get; }
        public BuildingRecord Record { get; }
        public GameState Game { get; }
        public CommandStack Commands { get; } = new CommandStack();

        /// <summary>The design draft: what the building view shows and edits.</summary>
        public BuildingState State => Record.Design;
        /// <summary>The built, paid-for state that earns.</summary>
        public BuildingState Committed => Record.Committed;

        /// <summary>Latest valid solve of the design (null before the first one).</summary>
        public NetworkSnapshot? Current { get; private set; }
        /// <summary>Latest valid solve of the committed state (the design's when nothing is pending).</summary>
        public NetworkSnapshot? CommittedSnapshot { get; private set; }
        /// <summary>Outlets, online state and money of the design ("projected").</summary>
        public BuildingEvaluation DesignEvaluation { get; private set; }
        /// <summary>Outlets, online state and money of the committed state (what is earned).</summary>
        public BuildingEvaluation CommittedEvaluation { get; private set; }

        /// <summary>The latest solve did not converge; the snapshots are the previous valid ones.</summary>
        public bool Settling { get; private set; }
        /// <summary>Increments whenever <see cref="Current"/> or <see cref="CommittedSnapshot"/> changes.</summary>
        public int SolveRevision { get; private set; }
        /// <summary>Increments on every state change.</summary>
        public int StateRevision { get; private set; }

        /// <summary>The design differs from the committed state.</summary>
        public bool HasPendingDesign { get; private set; }
        /// <summary>Cost of committing the design now.</summary>
        public CostSummary PendingCost { get; private set; } = new CostSummary();
        /// <summary>Spending limit before the first time online, $ (<c>02-economy.md</c> §Budget cap).</summary>
        public double BudgetCap { get; }
        /// <summary>Raised after a successful Commit (autosave, chime).</summary>
        public event Action? DesignCommitted;

        /// <summary>Hand-crank speed fraction, 0–1 (runtime only; 0 offline).</summary>
        public double CrankSpeed { get; private set; }
        /// <summary>Set each frame while the crank button or key is held.</summary>
        public bool CrankHeld { get; set; }

        /// <summary>Outside air temperature now (the air the intake draws), K.</summary>
        public double AirTemperatureK => Units.CToK(Climate.OutsideTemperatureC);
        public IFluidProperties Air { get; }
        public IFluidProperties Water { get; }
        /// <summary>The city water connection (set by the city solve; fixtures without a city get the reference pressure).</summary>
        public WaterSupply Supply { get; set; }
        /// <summary>The layout has a water network (taps or a water connection).</summary>
        public bool HasWater { get; }
        /// <summary>Outside conditions (shared by the city's buildings).</summary>
        public Climate Climate { get; }
        /// <summary>Rooms and duct temperatures (<c>05-simulation.md</c> §3–4).</summary>
        public BuildingClimate Rooms { get; }
        /// <summary>The building is open in the building view (full room grid rate, the design drives the rooms).</summary>
        public bool IsOpen { get; set; }

        readonly SolverSettings _settings;
        /// <summary>One solver set per state so each keeps its structure cache across drift re-solves.</summary>
        readonly SolverSet _designSolver, _committedSolver;
        bool _dirty = true;
        double _sinceSolve;
        double _lastSolveMs;
        double _solvedCrank;
        Task<(NetworkSnapshot design, NetworkSnapshot? committed)>? _pending;

        /// <param name="budgetCap">Budget cap when already known (generated buildings), $; else the auto-router computes it.</param>
        /// <param name="climate">Outside conditions (a fixed default temperature when null: tests and tools).</param>
        public BuildingSession(DataRepository data, BuildingLayout layout, BuildingRecord record, GameState game, double? budgetCap = null, Climate? climate = null)
        {
            Data = data;
            Layout = layout;
            Record = record;
            Game = game;
            _settings = SolverSettings.FromBalancing(data.Balancing);
            _designSolver = new SolverSet(_settings);
            _committedSolver = new SolverSet(_settings);
            _offlineSolver = new SolverSet(_settings);
            Climate = climate ?? Climate.Fixed(data, data.Balancing.Physics.DefaultAirTemperatureC);
            Rooms = new BuildingClimate(data, layout, Climate);
            Air = new TableFluid(data.Fluid("air"), data.FluidTables["air"], data.Balancing.Physics.GasConstant);
            Water = new TableFluid(data.Fluid("water"), data.FluidTables["water"], data.Balancing.Physics.GasConstant);
            HasWater = layout.OpeningFor("water") != null || layout.Outlets.Count != 0 && HasTaps(layout);
            Supply = new WaterSupply(true, ReferenceWaterPressure(data));
            Failures = new Failures.BuildingFailures(data, layout, record, Seeds.Derive(0x0FA1_0000UL, layout.Id));
            BudgetCap = budgetCap ?? ReferenceLayout.BudgetCap(data, layout);
            DesignEvaluation = BuildingEconomy.Evaluate(data, layout, (NetworkSnapshot?)null);
            CommittedEvaluation = DesignEvaluation;
            RefreshPending();
        }

        static bool HasTaps(BuildingLayout layout)
        {
            foreach (var o in layout.Outlets) if (BuildingState.OutletFluid(o.Type) == "water") return true;
            return false;
        }

        /// <summary>Connection pressure the reference layouts (and fixtures without a city) assume, Pa.</summary>
        public static double ReferenceWaterPressure(DataRepository data) =>
            data.Balancing.City.FacilityPressure.TryGetValue("water", out double p) ? p * data.Balancing.Water.ReferenceConnectionFactor : 0;

        /// <summary>Opens a hand-made building with a fresh record in a fresh game (tests, tools).</summary>
        public static BuildingSession OpenHandmade(DataRepository data, string id)
        {
            var layout = BuildingLayout.From(data, data.HandmadeBuilding(id));
            var game = new GameState { Wallet = data.Balancing.Economy.StartingWallet };
            BuildingRecord record = BuildingRecord.CreateFor(layout);
            game.Buildings.Add(record);
            return new BuildingSession(data, layout, record, game);
        }

        public void MarkDirty()
        {
            _dirty = true;
            StateRevision++;
            RefreshPending();
        }

        void RefreshPending()
        {
            HasPendingDesign = Record.Design.Fingerprint() != Record.Committed.Fingerprint();
            PendingCost = HasPendingDesign ? Pricing.Diff(Data, Layout, Record.Committed, Record.Design) : new CostSummary();
        }

        /// <summary>Runs a command through the stack.</summary>
        public bool Execute(ICommand command) => Commands.Execute(command, this);
        public bool Undo() => Commands.Undo(this);
        public bool Redo() => Commands.Redo(this);

        /// <summary>True when items of this tier are unlocked in the game's city (<c>03-city.md</c> §Progression).</summary>
        public bool Unlocked(int tier) => Progression.Tech.Unlocked(Game, tier);

        /// <summary>The committed equipment matching a design item (same opening and model), if built.</summary>
        public EquipmentState? CommittedTwin(EquipmentState design)
        {
            EquipmentState? c = Committed.EquipmentAt(design.OpeningId);
            return c != null && c.EquipmentId == design.EquipmentId ? c : null;
        }

        /// <summary>The committed inline part matching a design one (same tile and part), if built.</summary>
        public DuctNode? CommittedTwin(DuctNode design, string fluid = "air")
        {
            DuctNode? c = Committed.Net(fluid).NodeAt(design.Pos);
            return c != null && c.Kind == DuctNodeKind.Inline && c.PartId == design.PartId ? c : null;
        }

        /// <summary>True when any ceiling tile holds more capacity units than it has.</summary>
        public bool CeilingOverfull(BuildingState state)
        {
            var usage = new Dictionary<TilePos, int>();
            foreach (DuctNetworkState net in state.Networks) DuctEditor.AddUsage(net, CapacityUnits, usage);
            foreach (var kv in usage) if (kv.Value > CeilingCapacity) return true;
            return false;
        }

        // ---------- hand crank ----------

        /// <summary>True when the committed or design state has crank equipment.</summary>
        public bool HasCrank
        {
            get
            {
                foreach (EquipmentState e in Committed.Equipment) if (IsCrank(e)) return true;
                foreach (EquipmentState e in State.Equipment) if (IsCrank(e)) return true;
                return false;
            }
        }

        bool IsCrank(EquipmentState e) => Data.FindEquipment(e.EquipmentId)?.SpeedControl == "crank";

        /// <summary>One click on the crank (<c>02-economy.md</c> §Hand crank).</summary>
        public void Crank()
        {
            if (!HasCrank) return;
            CrankSpeed = Math.Min(1.0, CrankSpeed + Data.Balancing.Crank.SpeedPerClick);
        }

        void UpdateCrank(double dt)
        {
            CrankDef c = Data.Balancing.Crank;
            if (CrankHeld && HasCrank) CrankSpeed += c.HoldClicksPerSecond * c.SpeedPerClick * dt;
            CrankSpeed = Math.Min(1.0, CrankSpeed * Math.Exp(-dt / c.DecaySeconds));
            if (CrankSpeed < 1e-4) CrankSpeed = 0;
        }

        // ---------- solving ----------

        /// <summary>Compile options for a state now (pure inputs captured on the main thread).</summary>
        CompileOptions OptionsFor(double crank, double outsideK, IReadOnlyDictionary<int, double>? segmentK, IReadOnlyDictionary<int, double>? nodeK, bool committed) =>
            new CompileOptions
            {
                AirTemperatureK = outsideK,
                CrankSpeed = crank,
                SegmentTemperatureK = segmentK,
                NodeTemperatureK = nodeK,
                WaterConnected = Supply.Connected,
                WaterPressurePa = Supply.PressurePa,
                PowerSpeed = PowerSpeedFor(committed),
                BurstSegments = Failures.AllBurstSegments(committed ? Committed : State),
            };

        /// <summary>Speed factor on powered devices: 0 while the panel is tripped, cbrt(available/draw) in a brownout.</summary>
        public double PowerSpeed { get; set; } = 1;

        double PowerSpeedFor(bool committed) => PowerSpeed;

        /// <summary>Compiles and solves a state with the given solvers (pure; safe on a worker thread).</summary>
        NetworkSnapshot SolveState(BuildingState state, NetworkSnapshot? warmFrom, SolverSet solvers, CompileOptions options)
        {
            FluidSolve air = SolveFluid(state, "air", warmFrom?.Air, solvers.Air, options, Air);
            FluidSolve? water = HasWater ? SolveFluid(state, "water", warmFrom?.Water, solvers.Water, options, Water) : null;
            return new NetworkSnapshot(air, water, state);
        }

        FluidSolve SolveFluid(BuildingState state, string fluid, FluidSolve? warmFrom, NetworkSolver solver, CompileOptions options, IFluidProperties props)
        {
            CompiledNetwork compiled = NetworkCompiler.Compile(Layout, state, Data, fluid, options);
            bool same = warmFrom != null && warmFrom.Result.TopologyKey == compiled.Graph.TopologyKey();
            var sw = Stopwatch.StartNew();
            SolveResult result = solver.Solve(compiled.Graph, props, same ? warmFrom!.Result.Flow : null, same ? warmFrom!.Result.Status : null);
            result.SolveMilliseconds = sw.Elapsed.TotalMilliseconds;
            return new FluidSolve(compiled, result);
        }

        (NetworkSnapshot, NetworkSnapshot?) SolveBoth(BuildingState design, BuildingState? committed, NetworkSnapshot? warmD, NetworkSnapshot? warmC,
            CompileOptions designOptions, CompileOptions committedOptions)
        {
            NetworkSnapshot d = SolveState(design, warmD, _designSolver, designOptions);
            NetworkSnapshot? c = committed != null ? SolveState(committed, warmC, _committedSolver, committedOptions) : null;
            return (d, c);
        }

        /// <summary>Advances the session: crank decay; re-solve when dirty, when the crank moved, or on the drift interval.</summary>
        /// <param name="dt">Seconds since the last tick.</param>
        public void Tick(double dt)
        {
            UpdateCrank(dt);
            TickRooms(dt);
            Failures.Tick(dt, CommittedSnapshot, Committed, Game.PlayTimeSeconds);
            Failures.TickRepairs(dt, Record);
            if (Failures.Revision != _failRev)
            {
                _failRev = Failures.Revision;
                MarkDirty();
            }
            _sinceSolve += dt;
            if (_pending != null)
            {
                if (!_pending.IsCompleted) return;
                Accept(_pending.Result);
                _pending = null;
            }
            bool crankMoved = Math.Abs(CrankSpeed - _solvedCrank) >= Data.Balancing.Crank.ResolveSpeedStep
                || (CrankSpeed == 0 && _solvedCrank != 0);
            if (!_dirty && !crankMoved && _sinceSolve < Data.Balancing.Solver.ResolveIntervalSeconds) return;
            StartSolve(async: _lastSolveMs > Data.Balancing.Solver.AsyncThresholdMs);
        }

        /// <summary>Rooms and duct temperatures follow the design while the building is open, else what is built.</summary>
        void TickRooms(double dt)
        {
            NetworkSnapshot? snap = IsOpen ? Current : CommittedSnapshot;
            if (snap != null) Rooms.Index(snap.Compiled, snap.State.Air);
            Rooms.Tick(dt, IsOpen, snap?.Compiled, snap?.Result);
        }

        void StartSolve(bool async)
        {
            _dirty = false;
            _sinceSolve = 0;
            _solvedCrank = CrankSpeed;
            BuildingState design = State.Clone();
            BuildingState? committed = HasPendingDesign ? Committed.Clone() : null;
            NetworkSnapshot? warmD = Current, warmC = CommittedSnapshot;
            // The thermal solve replaces these dictionaries rather than changing them, so a worker can read them.
            CompileOptions od = OptionsFor(CrankSpeed, AirTemperatureK, Rooms.SegmentTemperatureK, Rooms.NodeTemperatureK, !HasPendingDesign);
            CompileOptions oc = OptionsFor(CrankSpeed, AirTemperatureK, Rooms.SegmentTemperatureK, Rooms.NodeTemperatureK, true);
            if (async) _pending = Task.Run(() => SolveBoth(design, committed, warmD, warmC, od, oc));
            else Accept(SolveBoth(design, committed, warmD, warmC, od, oc));
        }

        /// <summary>Solves immediately on the calling thread (tests, first frame).</summary>
        public void SolveNow()
        {
            if (_pending != null)
            {
                _pending.Wait();
                Accept(_pending.Result);
                _pending = null;
            }
            StartSolve(async: false);
        }

        void Accept((NetworkSnapshot design, NetworkSnapshot? committed) snaps)
        {
            (NetworkSnapshot d, NetworkSnapshot? c) = snaps;
            _lastSolveMs = d.SolveMilliseconds + (c?.SolveMilliseconds ?? 0);
            bool ok = d.Converged && (c == null || c.Converged);
            Settling = !ok;
            if (!ok && Current != null) return;
            NetworkSnapshot? before = CommittedSnapshot;
            Current = d;
            CommittedSnapshot = c ?? d;
            Failures.DetectSurges(before, CommittedSnapshot);
            DesignEvaluation = BuildingEconomy.Evaluate(Data, Layout, d);
            CommittedEvaluation = c == null ? BuildingEconomy.Evaluate(Data, Layout, d) : BuildingEconomy.Evaluate(Data, Layout, c);
            ApplyBurstCut(CommittedEvaluation, CommittedSnapshot, Committed);
            ApplyBurstCut(DesignEvaluation, d, State);
            SolveRevision++;
            Solved?.Invoke();
        }

        /// <summary>Raised on the main thread after a new solve is accepted.</summary>
        public event Action? Solved;

        /// <summary>Stress, bursts, surges and repairs of what is built.</summary>
        public Failures.BuildingFailures Failures { get; }
        /// <summary>The player is away: bursts cut income to 25% instead of 0 (<c>12-save-offline.md</c>).</summary>
        public bool OfflineRules { get; set; }
        int _failRev;

        /// <summary>A fresh snapshot of a changed state (previews; the session is untouched).</summary>
        NetworkSnapshot SolveCopy(BuildingState copy) =>
            SolveState(copy, null, new SolverSet(_settings), OptionsFor(CrankSpeed, AirTemperatureK, Rooms.SegmentTemperatureK, Rooms.NodeTemperatureK, false));

        /// <summary>Capacity units a segment takes in the ceiling (size units, + insulation above its threshold).</summary>
        public int CapacityUnits(DuctNetworkState net, DuctSegment s) => CapacityUnits(s.MaterialId, s.DiameterMm, s.Insulation);

        /// <summary>Capacity units of a pipe or duct size with optional insulation.</summary>
        public int CapacityUnits(string materialId, double diameterMm, string insulation)
        {
            MaterialDef m = Data.Material(materialId);
            int units = 1;
            foreach (PipeFamilyDef f in Data.PipeFamilies)
            {
                if (f.Kind != m.Kind) continue;
                foreach (PipeSizeDef size in f.Sizes) if (size.DiameterMm == diameterMm) units = size.CapacityUnits;
            }
            InsulationDef? ins = insulation.Length > 0 ? Data.FindInsulation(insulation) : null;
            if (ins != null && diameterMm > ins.CapacityAboveMm) units += ins.ExtraCapacityUnits;
            return units;
        }

        /// <summary>Ceiling capacity per tile in this city (the old-buildings modifier lowers it).</summary>
        public int CeilingCapacity => CeilingCapacityOverride > 0 ? CeilingCapacityOverride : Data.Balancing.Building.CeilingCapacity;
        /// <summary>Set by the city (modifier); 0 = the data default.</summary>
        public int CeilingCapacityOverride { get; set; }

        /// <summary>Every rule a new run must pass: geometry, ceiling space, the material's fluid and unlocks.</summary>
        public DrawError ValidateRun(string fluid, IReadOnlyList<TilePos> path, double diameterMm, string materialId, bool smoothElbows)
        {
            DuctNetworkState net = State.Net(fluid);
            DrawError e = DuctEditor.Validate(net, Layout, path, Data.Balancing.Drawing.MaxNodeDegree);
            if (e != DrawError.None) return e;
            MaterialDef? m = Data.FindMaterial(materialId);
            if (m == null || !m.Fluids.Contains(fluid)) return DrawError.WrongMaterial;
            if (!DuctEditor.FitsCeiling(State, net, path, CapacityUnits(materialId, diameterMm, ""), CapacityUnits, CeilingCapacity, out _))
                return DrawError.NoCeilingSpace;
            if (!Unlocked(Progression.Tech.SizeTier(Data, materialId, diameterMm)) || !Unlocked(Progression.Tech.ItemTier(Data, materialId))
                || (smoothElbows && !Unlocked(Progression.Tech.SmoothFittingsTier(Data))))
                return DrawError.Locked;
            return DrawError.None;
        }

        /// <summary>The burst factor on gross (02 §Income formula): 0 in game, 0.25 offline, while a burst is not isolated.</summary>
        void ApplyBurstCut(BuildingEvaluation ev, NetworkSnapshot snap, BuildingState state)
        {
            if (!Failures.HasActiveBurst(snap, state)) return;
            EconomyDef e = Data.Balancing.Economy;
            ev.ApplyBurst(OfflineRules ? e.OfflineBurstFactor : e.InGameBurstFactor);
        }

        /// <summary>Validates a run in progress and solves the networks as if it were placed (state untouched).</summary>
        public DrawPreview Preview(IReadOnlyList<TilePos> path, double diameterMm, string materialId, bool smoothElbows, string fluid = "air")
        {
            DuctNetworkState net = State.Net(fluid);
            var p = new DrawPreview { Error = DuctEditor.Validate(net, Layout, path, Data.Balancing.Drawing.MaxNodeDegree) };
            if (p.Error == DrawError.None && !DuctEditor.FitsCeiling(State, net, path, CapacityUnits(materialId, diameterMm, ""), CapacityUnits, CeilingCapacity, out _))
                p.Error = DrawError.NoCeilingSpace;
            if (p.Error != DrawError.None) return p;
            BuildingState copy = State.Clone();
            DuctEditor.PlaceRun(copy.Net(fluid), path, diameterMm, materialId, smoothElbows);
            p.Snapshot = SolveCopy(copy);
            return p;
        }

        /// <summary>
        /// Solves a changed copy of the design and evaluates it (the shop's "compare with placed"; the session is
        /// untouched). Runs on the calling thread.
        /// </summary>
        public BuildingEvaluation EvaluateWith(Action<BuildingState> change)
        {
            BuildingState copy = State.Clone();
            change(copy);
            return BuildingEconomy.Evaluate(Data, Layout, SolveCopy(copy));
        }

        /// <summary>
        /// What is built, at steady state: crank at 0, intake air at <paramref name="outsideTemperatureC"/> along every
        /// duct (offline catch-up, <c>12-save-offline.md</c>). Runs on the calling thread; the session is untouched.
        /// </summary>
        public BuildingEvaluation SteadyState(double outsideTemperatureC)
        {
            var none = new Dictionary<int, double>();
            NetworkSnapshot snap = SolveState(Committed.Clone(), CommittedSnapshot, _offlineSolver, OptionsFor(0, Units.CToK(outsideTemperatureC), none, none, true));
            BuildingEvaluation ev = BuildingEconomy.Evaluate(Data, Layout, snap);
            if (Failures.HasActiveBurst(snap, snap.State)) ev.ApplyBurst(Data.Balancing.Economy.OfflineBurstFactor);
            LastSteadySnapshot = snap;
            return ev;
        }

        /// <summary>The solve behind the last <see cref="SteadyState"/> (offline stress and burst sampling read it).</summary>
        public NetworkSnapshot? LastSteadySnapshot { get; private set; }

        /// <summary>The design's steady state at the current conditions (the engineering software forecast).</summary>
        public NetworkSnapshot? DesignSnapshot => Current;


        readonly SolverSet _offlineSolver;

        /// <summary>Volumetric flow delivered by an outlet in the design, m³/s (0 when not connected).</summary>
        public double OutletFlow(string outletId)
        {
            if (Current == null || !Current.Compiled.OutletLink.TryGetValue(outletId, out int link)) return 0;
            return Current.Result.Flow[link];
        }

        // ---------- commit ----------

        /// <summary>Checks the Commit rules: something to commit, no debt for purchases, enough money, budget cap.</summary>
        public CommitCheck CheckCommit()
        {
            if (!HasPendingDesign) return new CommitCheck(false, "Nothing to commit");
            double net = PendingCost.Net;
            if (net <= 0) return new CommitCheck(true, "");
            string symbol = Data.Game.CurrencySymbol;
            if (Game.Debt > 0) return new CommitCheck(false, "Purchases wait until the debt is repaid");
            if (!Record.EverOnline && Record.SpentTotal + net > BudgetCap + 1e-9)
                return new CommitCheck(false, $"Over budget cap by {Units.FormatMoney(Record.SpentTotal + net - BudgetCap, symbol)} · the cap lifts once the building has been online");
            if (Game.Wallet < net - 1e-9)
                return new CommitCheck(false, $"Need {Units.FormatMoney(net - Game.Wallet, symbol)} more");
            return new CommitCheck(true, "");
        }

        /// <summary>Pays for the design and makes it the committed state. Use <see cref="CommitDesignCommand"/> from the UI.</summary>
        public bool Commit()
        {
            if (!CheckCommit().Allowed) return false;
            double net = PendingCost.Net;
            if (net > 0) Game.Wallet -= net;
            else Game.ApplyIncome(-net);
            Record.SpentTotal += net;
            BuildingState before = Record.Committed;
            Record.Committed = Record.Design.Clone();
            Failures.OnCommit(before, Record.Committed);
            MarkDirty();
            DesignCommitted?.Invoke();
            return true;
        }

        /// <summary>Leaving the building: the undo history is cleared and the crank stops being held.</summary>
        public void Leave()
        {
            Commands.Clear();
            CrankHeld = false;
        }
    }
}
