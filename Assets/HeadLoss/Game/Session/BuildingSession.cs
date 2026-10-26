using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using HeadLoss.Core;
using HeadLoss.Core.Data;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.Commands;
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

    /// <summary>
    /// The building currently open in the building view: layout, player state, commands and the
    /// latest network solve. Re-solves on change and every <c>resolveIntervalSeconds</c>; solves slower
    /// than <c>asyncThresholdMs</c> move to a worker thread and swap in when done.
    /// </summary>
    public sealed class BuildingSession
    {
        public DataRepository Data { get; }
        public BuildingLayout Layout { get; }
        public BuildingState State { get; }
        public CommandStack Commands { get; } = new CommandStack();

        /// <summary>Latest valid solve (null before the first one).</summary>
        public NetworkSnapshot? Current { get; private set; }
        /// <summary>The latest solve did not converge; <see cref="Current"/> is the previous valid one.</summary>
        public bool Settling { get; private set; }
        /// <summary>Increments whenever <see cref="Current"/> changes.</summary>
        public int SolveRevision { get; private set; }
        /// <summary>Increments on every state change.</summary>
        public int StateRevision { get; private set; }

        /// <summary>Air temperature in the ducts, K (until the thermal model exists).</summary>
        public double AirTemperatureK { get; }
        public IFluidProperties Air { get; }

        readonly SolverSettings _settings;
        /// <summary>Reused for the session's own solves (one in flight at a time) so the structure cache survives drift re-solves.</summary>
        readonly NetworkSolver _solver;
        bool _dirty = true;
        double _sinceSolve;
        double _lastSolveMs;
        Task<NetworkSnapshot>? _pending;

        public BuildingSession(DataRepository data, BuildingLayout layout, BuildingState state)
        {
            Data = data;
            Layout = layout;
            State = state;
            _settings = SolverSettings.FromBalancing(data.Balancing);
            _solver = new NetworkSolver(_settings);
            AirTemperatureK = Units.CToK(data.Balancing.Physics.DefaultAirTemperatureC);
            Air = new TableFluid(data.Fluid("air"), data.FluidTables["air"], data.Balancing.Physics.GasConstant);
        }

        /// <summary>Opens a hand-made building with a fresh state.</summary>
        public static BuildingSession OpenHandmade(DataRepository data, string id)
        {
            var layout = new BuildingLayout(data.HandmadeBuilding(id));
            return new BuildingSession(data, layout, BuildingState.CreateFor(layout));
        }

        public void MarkDirty()
        {
            _dirty = true;
            StateRevision++;
        }

        /// <summary>Runs a command through the stack.</summary>
        public bool Execute(ICommand command) => Commands.Execute(command, this);

        /// <summary>Compiles and solves a state with the given solver (pure; safe on a worker thread).</summary>
        NetworkSnapshot SolveState(BuildingState state, SolveResult? warmFrom, NetworkSolver solver)
        {
            CompiledNetwork compiled = NetworkCompiler.Compile(Layout, state, Data, AirTemperatureK);
            double[]? warm = warmFrom != null && warmFrom.TopologyKey == compiled.Graph.TopologyKey() ? warmFrom.Flow : null;
            var sw = Stopwatch.StartNew();
            SolveResult result = solver.Solve(compiled.Graph, Air, warm);
            result.SolveMilliseconds = sw.Elapsed.TotalMilliseconds;
            return new NetworkSnapshot(compiled, result, state);
        }

        /// <summary>Advances the session: re-solve when dirty or on the drift interval; collect worker results.</summary>
        /// <param name="dt">Seconds since the last tick.</param>
        public void Tick(double dt)
        {
            _sinceSolve += dt;
            if (_pending != null)
            {
                if (!_pending.IsCompleted) return;
                Accept(_pending.Result);
                _pending = null;
            }
            if (!_dirty && _sinceSolve < Data.Balancing.Solver.ResolveIntervalSeconds) return;
            _dirty = false;
            _sinceSolve = 0;
            BuildingState copy = State.Clone();
            SolveResult? warm = Current?.Result;
            if (_lastSolveMs <= Data.Balancing.Solver.AsyncThresholdMs)
                Accept(SolveState(copy, warm, _solver));
            else
                _pending = Task.Run(() => SolveState(copy, warm, _solver));
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
            _dirty = false;
            _sinceSolve = 0;
            Accept(SolveState(State.Clone(), Current?.Result, _solver));
        }

        void Accept(NetworkSnapshot snap)
        {
            _lastSolveMs = snap.Result.SolveMilliseconds;
            Settling = !snap.Result.Converged;
            if (snap.Result.Converged || Current == null)
            {
                Current = snap;
                SolveRevision++;
            }
        }

        /// <summary>Validates a run in progress and solves the network as if it were placed (state untouched).</summary>
        public DrawPreview Preview(IReadOnlyList<TilePos> path, double diameterMm, string materialId, bool smoothElbows)
        {
            var p = new DrawPreview { Error = DuctEditor.Validate(State.Air, Layout, path, Data.Balancing.Drawing.MaxNodeDegree) };
            if (p.Error != DrawError.None) return p;
            BuildingState copy = State.Clone();
            DuctEditor.PlaceRun(copy.Air, path, diameterMm, materialId, smoothElbows);
            p.Snapshot = SolveState(copy, null, new NetworkSolver(_settings));
            return p;
        }

        /// <summary>Volumetric flow delivered by an outlet, m³/s (0 when not connected).</summary>
        public double OutletFlow(string outletId)
        {
            if (Current == null || !Current.Compiled.OutletLink.TryGetValue(outletId, out int link)) return 0;
            return Current.Result.Flow[link];
        }
    }
}
