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
using HeadLoss.Sim.Fluids;
using HeadLoss.Sim.Network;

namespace HeadLoss.Game.Session
{
    /// <summary>A compiled network and its solve, always swapped together.</summary>
    public sealed class NetworkSnapshot
    {
        public CompiledNetwork Compiled;
        public SolveResult Result;
        /// <summary>The state the snapshot was solved from (a private copy).</summary>
        public BuildingState State;

        public NetworkSnapshot(CompiledNetwork compiled, SolveResult result, BuildingState state)
        {
            Compiled = compiled;
            Result = result;
            State = state;
        }
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

        /// <summary>Air temperature in the ducts, K (until the thermal model exists).</summary>
        public double AirTemperatureK { get; }
        public IFluidProperties Air { get; }

        readonly SolverSettings _settings;
        /// <summary>One solver per state so each keeps its structure cache across drift re-solves.</summary>
        readonly NetworkSolver _designSolver, _committedSolver;
        bool _dirty = true;
        double _sinceSolve;
        double _lastSolveMs;
        double _solvedCrank;
        Task<(NetworkSnapshot design, NetworkSnapshot? committed)>? _pending;

        /// <param name="budgetCap">Budget cap when already known (generated buildings), $; else the auto-router computes it.</param>
        public BuildingSession(DataRepository data, BuildingLayout layout, BuildingRecord record, GameState game, double? budgetCap = null)
        {
            Data = data;
            Layout = layout;
            Record = record;
            Game = game;
            _settings = SolverSettings.FromBalancing(data.Balancing);
            _designSolver = new NetworkSolver(_settings);
            _committedSolver = new NetworkSolver(_settings);
            AirTemperatureK = Units.CToK(data.Balancing.Physics.DefaultAirTemperatureC);
            Air = new TableFluid(data.Fluid("air"), data.FluidTables["air"], data.Balancing.Physics.GasConstant);
            BudgetCap = budgetCap ?? ReferenceLayout.BudgetCap(data, layout);
            DesignEvaluation = BuildingEconomy.Evaluate(data, layout, null, null);
            CommittedEvaluation = DesignEvaluation;
            RefreshPending();
        }

        /// <summary>Opens a hand-made building with a fresh record in a fresh game (tests, tools).</summary>
        public static BuildingSession OpenHandmade(DataRepository data, string id)
        {
            var layout = new BuildingLayout(data.HandmadeBuilding(id));
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
        public DuctNode? CommittedTwin(DuctNode design)
        {
            DuctNode? c = Committed.Air.NodeAt(design.Pos);
            return c != null && c.Kind == DuctNodeKind.Inline && c.PartId == design.PartId ? c : null;
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

        /// <summary>Compiles and solves a state with the given solver (pure; safe on a worker thread).</summary>
        NetworkSnapshot SolveState(BuildingState state, SolveResult? warmFrom, NetworkSolver solver, double crank)
        {
            CompiledNetwork compiled = NetworkCompiler.Compile(Layout, state, Data, AirTemperatureK, crank);
            double[]? warm = warmFrom != null && warmFrom.TopologyKey == compiled.Graph.TopologyKey() ? warmFrom.Flow : null;
            var sw = Stopwatch.StartNew();
            SolveResult result = solver.Solve(compiled.Graph, Air, warm);
            result.SolveMilliseconds = sw.Elapsed.TotalMilliseconds;
            return new NetworkSnapshot(compiled, result, state);
        }

        (NetworkSnapshot, NetworkSnapshot?) SolveBoth(BuildingState design, BuildingState? committed, SolveResult? warmD, SolveResult? warmC, double crank)
        {
            NetworkSnapshot d = SolveState(design, warmD, _designSolver, crank);
            NetworkSnapshot? c = committed != null ? SolveState(committed, warmC, _committedSolver, crank) : null;
            return (d, c);
        }

        /// <summary>Advances the session: crank decay; re-solve when dirty, when the crank moved, or on the drift interval.</summary>
        /// <param name="dt">Seconds since the last tick.</param>
        public void Tick(double dt)
        {
            UpdateCrank(dt);
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

        void StartSolve(bool async)
        {
            _dirty = false;
            _sinceSolve = 0;
            _solvedCrank = CrankSpeed;
            BuildingState design = State.Clone();
            BuildingState? committed = HasPendingDesign ? Committed.Clone() : null;
            SolveResult? warmD = Current?.Result, warmC = CommittedSnapshot?.Result;
            double crank = CrankSpeed;
            if (async) _pending = Task.Run(() => SolveBoth(design, committed, warmD, warmC, crank));
            else Accept(SolveBoth(design, committed, warmD, warmC, crank));
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
            _lastSolveMs = d.Result.SolveMilliseconds + (c?.Result.SolveMilliseconds ?? 0);
            bool ok = d.Result.Converged && (c == null || c.Result.Converged);
            Settling = !ok;
            if (!ok && Current != null) return;
            Current = d;
            CommittedSnapshot = c ?? d;
            DesignEvaluation = BuildingEconomy.Evaluate(Data, Layout, d.Compiled, d.Result);
            CommittedEvaluation = c == null ? DesignEvaluation : BuildingEconomy.Evaluate(Data, Layout, c.Compiled, c.Result);
            SolveRevision++;
        }

        /// <summary>Validates a run in progress and solves the network as if it were placed (state untouched).</summary>
        public DrawPreview Preview(IReadOnlyList<TilePos> path, double diameterMm, string materialId, bool smoothElbows)
        {
            var p = new DrawPreview { Error = DuctEditor.Validate(State.Air, Layout, path, Data.Balancing.Drawing.MaxNodeDegree) };
            if (p.Error != DrawError.None) return p;
            BuildingState copy = State.Clone();
            DuctEditor.PlaceRun(copy.Air, path, diameterMm, materialId, smoothElbows);
            p.Snapshot = SolveState(copy, null, new NetworkSolver(_settings), CrankSpeed);
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
            NetworkSnapshot snap = SolveState(copy, null, new NetworkSolver(_settings), CrankSpeed);
            return BuildingEconomy.Evaluate(Data, Layout, snap.Compiled, snap.Result);
        }

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
            Record.Committed = Record.Design.Clone();
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
