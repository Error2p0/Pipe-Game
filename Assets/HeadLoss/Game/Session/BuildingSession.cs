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
using HeadLoss.Game.Generation;
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

        /// <summary>
        /// Heat outlets on this network: heat delivered, W (radiators: given to the room; cooling terminals: taken out of it), rated at
        /// the setpoint (<c>02-economy.md</c>: "for heating/cooling outlets it is heat delivered in W"). Filled with the solve.
        /// </summary>
        public readonly Dictionary<string, double> OutletHeat = new Dictionary<string, double>();

        /// <summary>The thermal solve of a piped liquid network (null for air and gases: the air's lives in <see cref="Thermal.BuildingClimate"/>).</summary>
        public HeadLoss.Sim.Thermal.ThermalResult? Thermal;
        /// <summary>Temperatures from <see cref="Thermal"/>, by segment and duct node (fed into the next solve).</summary>
        public readonly Thermal.PipeTemperatures Temperatures = new Thermal.PipeTemperatures();
    }

    /// <summary>A building's compiled networks and their solves, always swapped together.</summary>
    public sealed class NetworkSnapshot
    {
        /// <summary>Every solved network, the air supply first, then in solve order.</summary>
        public readonly IReadOnlyList<FluidSolve> All;
        /// <summary>The state the snapshot was solved from (a private copy).</summary>
        public BuildingState State;
        /// <summary>Coupled devices: what each drew at its in or fuel port, m³/s, by device key (<c>05-simulation.md</c> §1.5).</summary>
        public readonly Dictionary<string, double> CouplingDemand = new Dictionary<string, double>();
        /// <summary>Coupled devices: the pressure at each one's in port, Pa gauge (its out port fed at this).</summary>
        public readonly Dictionary<string, double> CouplingPressure = new Dictionary<string, double>();
        /// <summary>Coupled devices: the temperature of the water entering each one, K.</summary>
        public readonly Dictionary<string, double> CouplingTemperatureK = new Dictionary<string, double>();

        /// <param name="solves">The air supply's solve first.</param>
        public NetworkSnapshot(IReadOnlyList<FluidSolve> solves, BuildingState state)
        {
            if (solves.Count == 0 || solves[0].Fluid != "air") throw new ArgumentException("A snapshot starts with the air supply");
            All = solves;
            State = state;
        }

        /// <summary>The Phase 4–5 networks (tests, the auto-router).</summary>
        public NetworkSnapshot(FluidSolve air, FluidSolve? water, BuildingState state, FluidSolve? gas = null)
            : this(Collect(air, water, gas), state) { }

        static List<FluidSolve> Collect(FluidSolve air, FluidSolve? water, FluidSolve? gas)
        {
            var l = new List<FluidSolve> { air };
            if (water != null) l.Add(water);
            if (gas != null) l.Add(gas);
            return l;
        }

        /// <summary>The air supply network (always present).</summary>
        public FluidSolve Air => All[0];
        /// <summary>The cold water network (null when the building has none).</summary>
        public FluidSolve? Water => For("water");
        /// <summary>The natural gas network (null when the building has none).</summary>
        public FluidSolve? Gas => For(BuildingState.GasFluid);

        /// <summary>Air network (the Phase 1–3 fields).</summary>
        public CompiledNetwork Compiled => Air.Compiled;
        public SolveResult Result => Air.Result;

        /// <summary>The solve of a network, or null.</summary>
        public FluidSolve? For(string network)
        {
            foreach (FluidSolve fs in All) if (fs.Fluid == network) return fs;
            return null;
        }

        public bool Converged
        {
            get
            {
                foreach (FluidSolve fs in All) if (!fs.Result.Converged) return false;
                return true;
            }
        }

        public double SolveMilliseconds
        {
            get
            {
                double ms = 0;
                foreach (FluidSolve fs in All) ms += fs.Result.SolveMilliseconds;
                return ms;
            }
        }
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

    /// <summary>One solver per network so each keeps its structure cache across drift re-solves.</summary>
    sealed class SolverSet
    {
        readonly SolverSettings _settings;
        readonly Dictionary<string, NetworkSolver> _solvers = new Dictionary<string, NetworkSolver>();

        public SolverSet(SolverSettings s) => _settings = s;

        public NetworkSolver For(string network)
        {
            if (!_solvers.TryGetValue(network, out NetworkSolver? solver)) _solvers[network] = solver = new NetworkSolver(_settings);
            return solver;
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
    /// the crank speed moves, while a device changes speed, and every <c>resolveIntervalSeconds</c>; solves slower than
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
        public IFluidProperties GasProperties { get; }
        /// <summary>The city water connection (set by the city solve; fixtures without a city get the reference pressure).</summary>
        public WaterSupply Supply { get; set; }
        /// <summary>The city gas connection (set by the city solve; fixtures without a city get the reference pressure).</summary>
        public WaterSupply GasSupply { get; set; }
        /// <summary>The layout has a water network (taps or a water connection).</summary>
        public bool HasWater => HasNetwork("water");
        /// <summary>The layout has a gas network (gas outlets or a gas connection).</summary>
        public bool HasGas => HasNetwork(BuildingState.GasFluid);
        /// <summary>The networks this building has, in solve order (the air supply first, then <c>networks.json</c> order).</summary>
        public IReadOnlyList<string> NetworkIds => _networkIds;
        /// <summary>The building has this network (outlets on it or an opening that starts it).</summary>
        public bool HasNetwork(string network) => _networkIds.Contains(network);
        readonly List<string> _networkIds = new List<string>();
        readonly Dictionary<string, IFluidProperties> _properties = new Dictionary<string, IFluidProperties>();

        /// <summary>Property tables of the fluid a network carries.</summary>
        public IFluidProperties Properties(string network)
        {
            string fluid = Data.FluidIdOf(network);
            if (!_properties.TryGetValue(fluid, out IFluidProperties? p))
                _properties[fluid] = p = new TableFluid(Data.Fluid(fluid), Data.FluidTables[fluid], Data.Balancing.Physics.GasConstant);
            return p;
        }
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
        /// <summary>Fans and pumps slowing down or starting up, one tracker per state.</summary>
        readonly DeviceSpin _designSpin = new DeviceSpin(), _committedSpin = new DeviceSpin();
        int _spinRevision;
        /// <summary>The power speed things coast from after a panel trip.</summary>
        double _powerBeforeTrip;
        /// <summary>Share of speed moved since the last solve.</summary>
        double _spinMoved;
        Task<(NetworkSnapshot design, NetworkSnapshot? committed)>? _pending;

        /// <param name="budgetCap">Budget cap when already known (generated buildings), $; else the auto-router computes it.</param>
        /// <param name="climate">Outside conditions (a fixed default temperature when null: tests and tools).</param>
        /// <param name="reference">The auto-router's reference layout when already known (generated buildings); else it is routed when first needed.</param>
        public BuildingSession(DataRepository data, BuildingLayout layout, BuildingRecord record, GameState game, double? budgetCap = null, Climate? climate = null,
            ReferenceResult? reference = null)
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
            _networkIds.Add("air");
            List<string> used = layout.NetworkIds();
            foreach (NetworkDef n in data.Networks) if (n.Id != "air" && used.Contains(n.Id)) _networkIds.Add(n.Id);
            Air = Properties("air");
            Water = Properties("water");
            GasProperties = Properties(BuildingState.GasFluid);
            Supply = new WaterSupply(true, ReferenceWaterPressure(data));
            GasSupply = new WaterSupply(true, ReferenceGasPressure(data));
            Failures = new Failures.BuildingFailures(data, layout, record, Seeds.Derive(0x0FA1_0000UL, layout.Id))
            {
                RepairTimeFactor = Progression.PrestigeEffects.RepairTimeFactor(data, game),
            };
            GasSafety = new Failures.GasSafety(this);
            Inspections = new Inspections.BuildingInspections(this, Seeds.Derive(0x1A5B_0000UL, layout.Id));
            CeilingCapacityOverride = layout.Def.CeilingCapacity;
            _airQuality = Codes.IndoorAirQuality.InForce(data, layout);
            if (record.PanelTripped) PowerSpeed = 0;
            if (budgetCap == null && reference == null) reference = ReferenceLayout.Route(data, layout);
            BudgetCap = budgetCap ?? data.Balancing.Building.BudgetCapFactor * reference!.Cost;
            if (reference != null) _energyReference = HeadLoss.Game.Inspections.EnergyReference.From(reference.Evaluation);
            DesignEvaluation = BuildingEconomy.Evaluate(data, layout, (NetworkSnapshot?)null);
            CommittedEvaluation = DesignEvaluation;
            RefreshPending();
            SettleSpin();
        }

        /// <summary>Gas leaks, alarm, explosion timer and the certificate.</summary>
        public Failures.GasSafety GasSafety { get; }

        /// <summary>Random inspections (<c>10-events.md</c> §Inspections).</summary>
        public Inspections.BuildingInspections Inspections { get; }

        Inspections.EnergyReference? _energyReference;

        /// <summary>The reference layout's power and delivery the energy rating compares against (routed when first needed).</summary>
        public Inspections.EnergyReference EnergyReference =>
            _energyReference ??= HeadLoss.Game.Inspections.EnergyReference.From(ReferenceLayout.Route(Data, Layout).Evaluation);

        /// <summary>The design's energy rating while a design is pending ("Projected rating: B"), else what is built has.</summary>
        public string ProjectedGrade { get; private set; } = "";
        /// <summary>Score behind <see cref="ProjectedGrade"/>.</summary>
        public double ProjectedScore { get; private set; }
        double _nextRateTry;

        /// <summary>Connection pressure the reference layouts (and fixtures without a city) assume for gas, Pa: a share of the LP pressure.</summary>
        public static double ReferenceGasPressure(DataRepository data) =>
            data.Balancing.City.FacilityPressure.TryGetValue("gas-lp", out double p) ? p * data.Balancing.Gas.ReferenceConnectionFactor : 0;

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

        /// <summary>True when an item may be used in this game: its tier is unlocked or a prestige bonus grants it.</summary>
        public bool ItemUnlocked(string itemId) => Progression.Tech.ItemUnlocked(Data, Game, itemId);

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
                RoomTemperatureC = RoomTemperatures(),
                PipeTemperatures = PipeTemperaturesOf(committed ? CommittedSnapshot : Current),
                AirTemperatureK = outsideK,
                CrankSpeed = crank,
                SegmentTemperatureK = segmentK,
                NodeTemperatureK = nodeK,
                WaterConnected = Supply.Connected,
                WaterPressurePa = Supply.PressurePa,
                GasConnected = GasSupply.Connected,
                GasPressurePa = GasSupply.PressurePa,
                PowerSpeed = PowerSpeedFor(committed),
                BurstSegments = Failures.AllBurstSegments(committed ? Committed : State),
                // Leaks and damage are on what is built; a pending design previews them only where it still matches.
                Leaks = Record.Failures.Leaks.Count > 0 ? Record.Failures.Leaks.ConvertAll(l => l.Clone()) : null,
                AutoClosed = HasGas ? GasSafety.AutoClosedTiles() : null,
                Damaged = Record.Failures.DamagedEquipment.Count > 0 ? new HashSet<string>(Record.Failures.DamagedEquipment) : null,
            };

        /// <summary>Speed factor on powered devices: 0 while the panel is tripped, cbrt(available/draw) in a brownout.</summary>
        public double PowerSpeed { get; set; } = 1;

        /// <summary>Mean temperature per room now, °C (a copy: a worker thread reads it while the room grid steps).</summary>
        float[] RoomTemperatures()
        {
            var t = new float[Layout.Rooms.Count];
            HeadLoss.Sim.Rooms.RoomSummary[] rooms = Rooms.Rooms;
            for (int i = 0; i < t.Length && i < rooms.Length; i++) t[i] = rooms[i].MeanTemperature;
            return t;
        }

        /// <summary>Last temperatures of each piped liquid network of a snapshot (the next solve compiles with them).</summary>
        static Dictionary<string, Thermal.PipeTemperatures>? PipeTemperaturesOf(NetworkSnapshot? snap)
        {
            if (snap == null) return null;
            Dictionary<string, Thermal.PipeTemperatures>? d = null;
            foreach (FluidSolve fs in snap.All)
                if (fs.Thermal != null) (d ??= new Dictionary<string, Thermal.PipeTemperatures>())[fs.Fluid] = fs.Temperatures;
            return d;
        }

        /// <summary>The building's power connection from the city (the basic connection until a cable reaches it).</summary>
        public City.PowerConnection Power { get; set; }
        /// <summary>What the equipment asks for at full speed, W.</summary>
        public double RequestedPowerW { get; private set; }
        double _overPanel;

        /// <summary>The panel's limit, kW (the basic panel when none is placed).</summary>
        public double PanelLimitKW
        {
            get
            {
                foreach (EquipmentState e in Committed.Equipment)
                    if (Data.FindEquipment(e.EquipmentId) is EquipmentDef d && d.Category == "panel") return d.PanelLimitKW;
                return Data.FindEquipment("panel-basic")?.PanelLimitKW ?? 4;
            }
        }

        /// <summary>The power connection, kW: the cable's capacity, or the basic connection.</summary>
        public double ConnectionKW => Power.Cabled ? Power.ConnectionKW : Data.Balancing.Power.BasicConnectionKW;

        /// <summary>Power the equipment can have, kW: min(panel limit, connection, city allocation).</summary>
        public double AvailableKW => Math.Min(PanelLimitKW, Power.Cabled ? Math.Min(ConnectionKW, Power.AllocatedKW) : ConnectionKW);

        /// <summary>
        /// What the building draws from its gas main, m³/s at its own pressure: the inflow of the built gas network's last
        /// solve (leaks included), or the design flow of every connected stove.
        /// </summary>
        public double GasDemand()
        {
            FluidSolve? g = CommittedSnapshot?.Gas;
            if (g != null && g.Compiled.SupplyNode >= 0) return Math.Max(0, g.Compiled.SupplyFlow(g.Result));
            double q = 0;
            foreach (OutletPlacementDef o in Layout.Outlets)
            {
                if (Layout.NetworkOf(o) != BuildingState.GasFluid) continue;
                DuctNode? n = Committed.Gas.Nodes.Find(x => x.Kind == DuctNodeKind.Outlet && x.AnchorId == o.Id);
                if (n != null && Committed.Gas.Degree(n.Id) > 0) q += NetworkCompiler.DesignFlow(Data, Layout, o);
            }
            return q;
        }

        /// <summary>
        /// What the building draws from its water main, m³/s: the inflow of the built water network's last solve, or
        /// (before one) the design flow of every connected tap.
        /// </summary>
        public double WaterDemand()
        {
            FluidSolve? w = CommittedSnapshot?.Water;
            if (w != null && w.Compiled.SupplyNode >= 0) return Math.Max(0, w.Compiled.SupplyFlow(w.Result));
            double q = 0;
            foreach (OutletPlacementDef o in Layout.Outlets)
            {
                if (Layout.NetworkOf(o) != "water") continue;
                DuctNode? n = Committed.Water.Nodes.Find(x => x.Kind == DuctNodeKind.Outlet && x.AnchorId == o.Id);
                if (n != null && Committed.Water.Degree(n.Id) > 0) q += NetworkCompiler.DesignFlow(Data, Layout, o);
            }
            return q;
        }

        // ---------- purchases that are not placed (07 §Meta upgrades) ----------

        /// <summary>Buys pipe-wall leak sensors for a network (tier 3, paid at once).</summary>
        public bool BuyLeakSensors(string fluid)
        {
            MetaUpgradeDef? def = Data.Upgrades.Meta.Find(m => m.Id == "leak-sensors");
            if (def == null || Record.Failures.LeakSensors.Contains(fluid) || !Unlocked(def.Tier) || !Game.CanAfford(def.Price)) return false;
            Game.Wallet -= def.Price;
            Record.SpentTotal += def.Price;
            Record.Failures.LeakSensors.Add(fluid);
            return true;
        }

        /// <summary>Starts or ends the maintenance contract (a share of gross per cycle; crews come while you are away).</summary>
        public bool SetMaintenanceContract(bool on, bool preferEmergency)
        {
            MetaUpgradeDef? def = Data.Upgrades.Meta.Find(m => m.Id == "maintenance-contract");
            if (def == null || (on && !Unlocked(def.Tier))) return false;
            Record.Failures.MaintenanceContract = on;
            Record.Failures.PreferEmergency = preferEmergency;
            return true;
        }

        /// <summary>
        /// Books a crew for a burst on what is built (<c>08-failures.md</c> §Repairs), optionally fitting another material or size
        /// in the same visit (its price difference is paid with the repair). Paid now, from the wallet.
        /// </summary>
        public bool Repair(string fluid, int committedSegmentId, bool emergency, string upgradeMaterial = "", double upgradeDiameterMm = 0)
        {
            DuctNetworkState net = Committed.Net(fluid);
            DuctSegment? s = net.FindSegment(committedSegmentId);
            if (s == null) return false;
            string? edge = null;
            List<string> edges = HeadLoss.Game.Failures.BuildingFailures.Edges(fluid, net, s);
            foreach (Failures.BurstState b in Record.Failures.Bursts) if (b.Fluid == fluid && edges.Contains(b.Edge)) edge = b.Edge;
            if (edge == null) return false;
            double extra = 0;
            if (upgradeMaterial.Length > 0 || upgradeDiameterMm > 0)
            {
                var up = s.Clone();
                if (upgradeMaterial.Length > 0) up.MaterialId = upgradeMaterial;
                if (upgradeDiameterMm > 0) up.DiameterMm = upgradeDiameterMm;
                double metres = edges.Count * (DuctEditor.IsRiser(net, s) ? Data.Balancing.Building.FloorHeight : 1);
                extra = Math.Max(0, (Pricing.SegmentPerMetre(Data, up) - Pricing.SegmentPerMetre(Data, s)) * metres);
                if (!ItemUnlocked(up.MaterialId) || !Unlocked(Progression.Tech.SizeTier(Data, up.MaterialId, up.DiameterMm))) return false;
            }
            bool ok = Failures.StartRepair(fluid, edge, emergency, upgradeMaterial, upgradeDiameterMm, net, s, price =>
            {
                if (!Game.CanAfford(price + extra)) return false;
                Game.Wallet -= price + extra;
                Record.SpentTotal += extra;
                return true;
            });
            if (ok) MarkDirty();
            return ok;
        }

        /// <summary>Books a crew for a gas leak (paid now, from the wallet).</summary>
        public bool RepairLeak(string leakKey, bool emergency)
        {
            bool ok = Failures.RepairLeak(leakKey, emergency, price =>
            {
                if (!Game.CanAfford(price)) return false;
                Game.Wallet -= price;
                return true;
            });
            if (ok) MarkDirty();
            return ok;
        }

        /// <summary>Books a crew for equipment knocked offline by a blast (paid now, from the wallet).</summary>
        public bool RepairEquipment(string key, bool emergency)
        {
            bool ok = Failures.RepairEquipment(key, emergency, price =>
            {
                if (!Game.CanAfford(price)) return false;
                Game.Wallet -= price;
                return true;
            });
            if (ok) MarkDirty();
            return ok;
        }

        /// <summary>Resets a tripped panel (the player's click; it never resets by itself).</summary>
        public bool ResetPanel()
        {
            if (!Record.PanelTripped) return false;
            Record.PanelTripped = false;
            _overPanel = 0;
            PowerSpeed = 1;
            MarkDirty();
            return true;
        }

        /// <summary>
        /// Panel trips and brownouts (<c>04-buildings.md</c> §Electrical panel, <c>03-city.md</c> §Electricity): a draw above the
        /// panel limit for longer than the trip delay stops everything; a draw above what the connection and the city give
        /// slows every fan and pump to s = cbrt(available / draw).
        /// </summary>
        void TickPower(double dt)
        {
            double speedNow = PowerSpeed;
            double drawn = CommittedEvaluation.PowerW;
            if (speedNow > 0.05) RequestedPowerW = drawn / (speedNow * speedNow * speedNow);
            else if (!Record.PanelTripped) RequestedPowerW = drawn;
            double requestedKW = RequestedPowerW / 1000;
            if (Record.PanelTripped)
            {
                if (PowerSpeed != 0) { PowerSpeed = 0; MarkDirty(); }
                return;
            }
            if (requestedKW > PanelLimitKW)
            {
                _overPanel += dt;
                if (_overPanel > Data.Balancing.Power.TripDelaySeconds)
                {
                    Record.PanelTripped = true;
                    _powerBeforeTrip = PowerSpeed;
                    PowerSpeed = 0;
                    MarkDirty();
                    PanelTrippedEvent?.Invoke();
                    return;
                }
            }
            else _overPanel = 0;
            double supply = Power.Cabled ? Math.Min(ConnectionKW, Power.AllocatedKW) : ConnectionKW;
            double target = requestedKW > supply && requestedKW > 0 ? Math.Cbrt(Math.Max(0, supply) / requestedKW) : 1;
            if (Math.Abs(target - PowerSpeed) > 0.01)
            {
                PowerSpeed = target;
                MarkDirty();
            }
        }

        /// <summary>Raised when the panel trips (a calm notice).</summary>
        public event Action? PanelTrippedEvent;

        double PowerSpeedFor(bool committed) => PowerSpeed;

        // ---------- slowing down and starting up (05 §2) ----------

        /// <summary>Share of full speed of a fan or pump in the design that is changing speed (0 = steady).</summary>
        /// <param name="deviceKey">Its <see cref="HeadLoss.Game.Failures.DamageKeys"/> key.</param>
        public double SpinShare(string deviceKey) => _designSpin.Share(deviceKey);

        /// <summary>True when that device is starting up rather than slowing down.</summary>
        public bool SpinningUp(string deviceKey) => _designSpin.Rising(deviceKey);

        bool Spinning => _designSpin.Active || _committedSpin.Active;

        void SyncSpin(bool start)
        {
            bool powered = !Record.PanelTripped;
            _designSpin.Sync(State, Data, powered, start);
            _committedSpin.Sync(Committed, Data, powered, start);
            _spinRevision = StateRevision;
        }

        /// <summary>Notices devices switched since the last tick and moves the ones changing speed.</summary>
        void TickSpin(double dt)
        {
            if (StateRevision != _spinRevision) SyncSpin(true);
            if (!Spinning) return;
            double stopBelow = Data.Balancing.Devices.SpinResolveStep;
            _spinMoved += Math.Max(_designSpin.Advance(dt, stopBelow), _committedSpin.Advance(dt, stopBelow));
            // Everything has settled: one last solve to show it.
            if (!Spinning) _dirty = true;
        }

        /// <summary>Ends every change of speed: what is off has stopped and what is on runs (a new session, offline catch-up).</summary>
        void SettleSpin()
        {
            bool powered = !Record.PanelTripped;
            _designSpin.Settle(State, Data, powered);
            _committedSpin.Settle(Committed, Data, powered);
            _spinRevision = StateRevision;
        }

        /// <summary>Puts the live speeds into a solve's options. Only live solves take them: previews and offline are steady states.</summary>
        void ApplySpin(CompileOptions o, DeviceSpin spin)
        {
            o.Spin = spin.Snapshot();
            if (Record.PanelTripped && spin.Active) o.PowerSpeed = _powerBeforeTrip;
        }

        /// <summary>Compiles and solves a state with the given solvers (pure; safe on a worker thread).</summary>
        /// <summary>The building's networks plus any a placed device connects to (in <c>networks.json</c> order after the air).</summary>
        List<string> NetworksOf(BuildingState state)
        {
            if (state.Devices.Count == 0) return _networkIds;
            var extra = new HashSet<string>();
            foreach (PlacedDevice d in state.Devices)
                if (Data.FindEquipment(d.EquipmentId) is EquipmentDef def)
                    foreach (PortDef p in def.Ports) if (!_networkIds.Contains(p.Network)) extra.Add(p.Network);
            if (extra.Count == 0) return _networkIds;
            var ids = new List<string> { "air" };
            foreach (NetworkDef n in Data.Networks) if (n.Id != "air" && (_networkIds.Contains(n.Id) || extra.Contains(n.Id))) ids.Add(n.Id);
            return ids;
        }

        NetworkSnapshot SolveState(BuildingState state, NetworkSnapshot? warmFrom, SolverSet solvers, CompileOptions options)
        {
            List<string> networks = NetworksOf(state);
            List<Coupling> couplings = Couplings.Of(Data, state, networks);
            if (couplings.Count == 0)
            {
                var solves = new List<FluidSolve>(networks.Count);
                foreach (string id in networks) solves.Add(SolveFluid(state, id, warmFrom?.For(id), solvers.For(id), options, Properties(id)));
                return new NetworkSnapshot(solves, state);
            }

            // Coupled networks (05 §1.5): a device's child is solved before its parent (hot before cold, heating before gas), the
            // parent learns what the device draws and the child the pressure and temperature at the device's in port; twice over.
            var demand = new Dictionary<string, double>();
            var pressure = new Dictionary<string, double>();
            var temperature = new Dictionary<string, double>();
            foreach (Coupling c in couplings)
            {
                demand[c.Key] = warmFrom != null && warmFrom.CouplingDemand.TryGetValue(c.Key, out double q) ? q : 0;
                pressure[c.Key] = warmFrom != null && warmFrom.CouplingPressure.TryGetValue(c.Key, out double p) ? p : options.SupplyFor(c.Parent).pressurePa;
                temperature[c.Key] = warmFrom != null && warmFrom.CouplingTemperatureK.TryGetValue(c.Key, out double t) ? t : Thermal.NetworkThermal.SupplyK(Data, c.Parent);
            }
            options.CouplingDemand = demand;
            options.CouplingPressure = pressure;
            options.CouplingTemperatureK = temperature;
            var coupled = new HashSet<string>();
            foreach (Coupling c in couplings)
            {
                coupled.Add(c.Parent);
                coupled.Add(c.Child);
            }
            var solved = new Dictionary<string, FluidSolve>();
            List<string> order = Couplings.Order(networks, couplings);
            for (int pass = 0; pass < Couplings.Passes; pass++)
                foreach (string id in order)
                {
                    if (pass > 0 && !coupled.Contains(id)) continue;
                    FluidSolve fs = SolveFluid(state, id, solved.TryGetValue(id, out FluidSolve? prev) ? prev : warmFrom?.For(id), solvers.For(id), options, Properties(id));
                    solved[id] = fs;
                    foreach (Coupling c in couplings)
                    {
                        if (c.Child == id) demand[c.Key] = Draw(c, fs, pressure[c.Key], temperature[c.Key]);
                        if (c.Parent == id && fs.Compiled.DeviceInlet.TryGetValue(c.Key, out int node) && fs.Result.NodeActive[node])
                        {
                            pressure[c.Key] = fs.Result.NodePressure[node];
                            if (fs.Thermal != null) temperature[c.Key] = fs.Thermal.NodeT[node];
                        }
                    }
                }
            var list = new List<FluidSolve>(networks.Count);
            foreach (string id in networks) list.Add(solved[id]);
            var snap = new NetworkSnapshot(list, state);
            foreach (var kv in demand) snap.CouplingDemand[kv.Key] = kv.Value;
            foreach (var kv in pressure) snap.CouplingPressure[kv.Key] = kv.Value;
            foreach (var kv in temperature) snap.CouplingTemperatureK[kv.Key] = kv.Value;
            return snap;
        }

        /// <summary>
        /// What a coupled device draws from its parent network after its child's solve, m³/s: a water heater the mass it sends on
        /// (at the cold water's density), a boiler the gas for the heat it gave the loop.
        /// </summary>
        /// <param name="inletPa">Pressure at the device's in port, Pa gauge.</param>
        /// <param name="inletK">Temperature at the device's in port, K.</param>
        double Draw(Coupling c, FluidSolve child, double inletPa, double inletK)
        {
            double pAbs = Data.Balancing.Physics.AtmosphericPressurePa + Math.Max(0, inletPa);
            if (c.Fuel)
            {
                double heat = child.Thermal != null && child.Compiled.HeaterLink.TryGetValue(c.Key, out int link) ? Math.Max(0, child.Thermal.HeatAdded[link]) : 0;
                double rho = Properties(c.Parent).Density(pAbs, Units.CToK(Data.Balancing.Gas.SupplyTemperatureC));
                return Couplings.FuelFlow(heat, c.Def.ThermalEfficiency, Data.FluidOf(c.Parent).LowerHeatingValueMJPerKg, rho);
            }
            if (!child.Compiled.DeviceSource.TryGetValue(c.Key, out int source)) return 0;
            double mass = 0;
            NetworkGraph g = child.Compiled.Graph;
            for (int k = 0; k < g.Links.Count; k++)
            {
                if (g.Links[k].From == source) mass += child.Result.MassFlow[k];
                else if (g.Links[k].To == source) mass -= child.Result.MassFlow[k];
            }
            return Math.Max(0, mass) / Properties(c.Parent).Density(pAbs, inletK);
        }

        FluidSolve SolveFluid(BuildingState state, string fluid, FluidSolve? warmFrom, NetworkSolver solver, CompileOptions options, IFluidProperties props)
        {
            CompiledNetwork compiled = NetworkCompiler.Compile(Layout, state, Data, fluid, options);
            bool same = warmFrom != null && warmFrom.Result.TopologyKey == compiled.Graph.TopologyKey();
            var sw = Stopwatch.StartNew();
            SolveResult result = solver.Solve(compiled.Graph, props, same ? warmFrom!.Result.Flow : null, same ? warmFrom!.Result.Status : null);
            result.SolveMilliseconds = sw.Elapsed.TotalMilliseconds;
            var fs = new FluidSolve(compiled, result);
            Thermal.HeatDelivery.FillAirTerminals(Data, Layout, fs, props);
            if (Thermal.NetworkThermal.Applies(Data, fluid))
                Thermal.NetworkThermal.Solve(Data, Layout, state.Net(fluid), fs, props, options.RoomTemperatureC, compiled.Heaters, compiled.BoundaryK, warmFrom);
            return fs;
        }

        (NetworkSnapshot, NetworkSnapshot?) SolveBoth(BuildingState design, BuildingState? committed, NetworkSnapshot? warmD, NetworkSnapshot? warmC,
            CompileOptions designOptions, CompileOptions committedOptions)
        {
            NetworkSnapshot d = SolveState(design, warmD, _designSolver, designOptions);
            NetworkSnapshot? c = committed != null ? SolveState(committed, warmC, _committedSolver, committedOptions) : null;
            return (d, c);
        }

        /// <summary>Where this session's periodic work falls within its interval, 0–1 (<see cref="SetPhase"/>).</summary>
        public double Phase { get; private set; }

        /// <summary>
        /// Spreads this session's periodic solve and room rounds away from other sessions' (a city loads them all in one frame).
        /// Scheduling only: what is simulated does not change.
        /// </summary>
        /// <param name="phase">Share of each interval, 0–1.</param>
        public void SetPhase(double phase)
        {
            Phase = phase;
            _solveDelay = phase * Data.Balancing.Solver.ResolveIntervalSeconds;
            _sinceSolve = -_solveDelay;
            Rooms.SetPhase(phase);
        }

        double _solveDelay;

        /// <summary>Advances the session: crank decay; re-solve when dirty, when the crank or a device changing speed moved, or on the drift interval.</summary>
        /// <param name="dt">Seconds since the last tick.</param>
        public void Tick(double dt)
        {
            UpdateCrank(dt);
            TickRooms(dt);
            TickPower(dt);
            TickSpin(dt);
            Failures.Tick(dt, CommittedSnapshot, Committed, Game.PlayTimeSeconds);
            Failures.TickRepairs(dt, Record);
            GasSafety.Tick(dt);
            Inspections.Tick(dt);
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
            bool spinMoved = _spinMoved >= Data.Balancing.Devices.SpinResolveStep;
            if (!_dirty && !crankMoved && !spinMoved && _sinceSolve < Data.Balancing.Solver.ResolveIntervalSeconds) return;
            StartSolve(async: _lastSolveMs > Data.Balancing.Solver.AsyncThresholdMs);
        }

        /// <summary>Rooms and duct temperatures follow the design while the building is open, else what is built.</summary>
        void TickRooms(double dt)
        {
            NetworkSnapshot? snap = IsOpen ? Current : CommittedSnapshot;
            if (snap != null) Rooms.Index(snap.Compiled, snap.State.Air);
            Rooms.PassiveVents = PassiveVents(snap?.State ?? Committed);
            Rooms.PipeHeat = PipeHeat(snap);
            Rooms.Tick(dt, IsOpen, snap?.Compiled, snap?.Result);
        }

        /// <summary>The passive vents of a state (the list is rebuilt only when the state changes).</summary>
        IReadOnlyList<SafetyDevice> PassiveVents(BuildingState state)
        {
            if (state == _ventsOf) return _vents;
            _ventsOf = state;
            _vents = state.Safety.FindAll(d => d.EquipmentId == HeadLoss.Game.Failures.GasSafetyIds.PassiveVent);
            return _vents;
        }

        /// <summary>Heat the piped networks of a snapshot give to the rooms (rebuilt only when the snapshot changes).</summary>
        IReadOnlyList<Thermal.HeatInjection> PipeHeat(NetworkSnapshot? snap)
        {
            if (snap == _pipeHeatOf) return _pipeHeat;
            _pipeHeatOf = snap;
            _pipeHeat = new List<Thermal.HeatInjection>();
            if (snap != null)
                foreach (FluidSolve fs in snap.All)
                    if (fs.Thermal != null) Thermal.NetworkThermal.AddPipeHeat(fs, snap.State.Net(fs.Fluid), _pipeHeat);
            return _pipeHeat;
        }

        NetworkSnapshot? _pipeHeatOf;
        List<Thermal.HeatInjection> _pipeHeat = new List<Thermal.HeatInjection>();
        BuildingState? _ventsOf;
        List<SafetyDevice> _vents = new List<SafetyDevice>();

        void StartSolve(bool async)
        {
            _dirty = false;
            // The first solve after a phase is set keeps the phase (a new session is dirty at once, and that would undo it).
            _sinceSolve = -_solveDelay;
            _solveDelay = 0;
            _solvedCrank = CrankSpeed;
            _spinMoved = 0;
            BuildingState design = State.Clone();
            BuildingState? committed = HasPendingDesign ? Committed.Clone() : null;
            NetworkSnapshot? warmD = Current, warmC = CommittedSnapshot;
            // The thermal solve replaces these dictionaries rather than changing them, so a worker can read them.
            CompileOptions od = OptionsFor(CrankSpeed, AirTemperatureK, Rooms.SegmentTemperatureK, Rooms.NodeTemperatureK, !HasPendingDesign);
            CompileOptions oc = OptionsFor(CrankSpeed, AirTemperatureK, Rooms.SegmentTemperatureK, Rooms.NodeTemperatureK, true);
            ApplySpin(od, _designSpin);
            ApplySpin(oc, _committedSpin);
            if (async) _pending = Task.Run(() => SolveBoth(design, committed, warmD, warmC, od, oc));
            else Accept(SolveBoth(design, committed, warmD, warmC, od, oc));
        }

        /// <summary>
        /// Solves immediately on the calling thread (tests, first frame). Time does not pass, so a device switched since the
        /// last tick counts as already there; one already changing speed carries on.
        /// </summary>
        public void SolveNow()
        {
            if (_pending != null)
            {
                _pending.Wait();
                Accept(_pending.Result);
                _pending = null;
            }
            SyncSpin(false);
            StartSolve(async: false);
        }

        /// <summary>A solve gave up on non-finite numbers (a dev report, once per session): the building keeps its last good solve.</summary>
        public event Action<string>? SolveProblem;
        bool _solveProblemReported;

        void ReportNonFinite(NetworkSnapshot snap)
        {
            foreach (FluidSolve fs in snap.All)
            {
                if (_solveProblemReported || !fs.Result.NonFinite) continue;
                _solveProblemReported = true;
                var f = Record.Failures;
                SolveProblem?.Invoke($"{Record.BuildingId} (tier {Layout.Tier}), {fs.Fluid}: non-finite solve, {fs.Compiled.Graph.Nodes.Count} nodes, {fs.Compiled.Graph.Links.Count} links, water {Supply.PressurePa:0} Pa, gas {GasSupply.PressurePa:0} Pa, power speed {PowerSpeed:0.###}, crank {CrankSpeed:0.###}, air {AirTemperatureK:0.#} K, leaks {f.Leaks.Count}, bursts {f.Bursts.Count}, damaged {f.DamagedEquipment.Count}");
            }
        }

        void Accept((NetworkSnapshot design, NetworkSnapshot? committed) snaps)
        {
            (NetworkSnapshot d, NetworkSnapshot? c) = snaps;
            ReportNonFinite(d);
            if (c != null) ReportNonFinite(c);
            _lastSolveMs = d.SolveMilliseconds + (c?.SolveMilliseconds ?? 0);
            bool ok = d.Converged && (c == null || c.Converged);
            Settling = !ok;
            if (!ok && Current != null) return;
            NetworkSnapshot? before = CommittedSnapshot;
            Current = d;
            CommittedSnapshot = c ?? d;
            Failures.DetectSurges(before, CommittedSnapshot);
            // A building that has just loaded (or come back from the time away) starts its rooms' CO₂ where the built
            // ventilation holds it, not at the outdoor level, so the indoor air quality code does not wait an hour to bite.
            if (before == null) Rooms.SeedCo2(Codes.IndoorAirQuality.SteadyPpm(Data, Layout, Committed, CommittedSnapshot));
            DesignEvaluation = BuildingEconomy.Evaluate(Data, Layout, d);
            CommittedEvaluation = c == null ? BuildingEconomy.Evaluate(Data, Layout, d) : BuildingEconomy.Evaluate(Data, Layout, c);
            DesignEvaluation.ApplyViolations(Codes.CodeChecker.Check(Data, Layout, d.State, d));
            CommittedEvaluation.ApplyViolations(Codes.CodeChecker.Check(Data, Layout, CommittedSnapshot.State, CommittedSnapshot));
            if (_airQuality)
            {
                Codes.IndoorAirQuality.Apply(Data, Layout, DesignEvaluation, d.State, d);
                Codes.IndoorAirQuality.Apply(Data, Layout, CommittedEvaluation, CommittedSnapshot.State, CommittedSnapshot);
            }
            UpdateRating();
            ApplyEffects(CommittedEvaluation, CommittedSnapshot, Committed, OfflineRules, Record.Inspection.Grade);
            ApplyEffects(DesignEvaluation, d, State, OfflineRules, HasPendingDesign ? ProjectedGrade : Record.Inspection.Grade);
            GasSafety.UpdateInjections();
            SolveRevision++;
            Solved?.Invoke();
        }

        /// <summary>Raised on the main thread after a new solve is accepted.</summary>
        public event Action? Solved;

        /// <summary>The indoor air quality code is in force here (<c>09-codes.md</c>).</summary>
        readonly bool _airQuality;

        /// <summary>
        /// Stress a segment shows on the heatmap: what it has, or with the engineering software and a design pending, what it
        /// would have after <c>failure.forecastHours</c> at the previewed steady state (<c>07-equipment.md</c> §Meta upgrades).
        /// </summary>
        public double StressShown(string fluid, int segmentId)
        {
            double now = Failures.StressOf(fluid, segmentId, State);
            if (Game.City?.Utilities.EngineeringSoftware != true || !HasPendingDesign) return now;
            FluidSolve? fs = Current?.For(fluid);
            DuctNetworkState net = State.Net(fluid);
            DuctSegment? s = net.FindSegment(segmentId);
            if (fs == null || s == null) return now;
            return now + Failures.StressRate(Failures.RatingRatio(fs, net, s, 0)) * Data.Balancing.Failure.ForecastHours * 3600;
        }

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
            if (m == null || !m.Fluids.Contains(Data.FluidIdOf(fluid))) return DrawError.WrongMaterial;
            if (!DuctEditor.FitsCeiling(State, net, path, CapacityUnits(materialId, diameterMm, ""), CapacityUnits, CeilingCapacity, out _))
                return DrawError.NoCeilingSpace;
            if (!Unlocked(Progression.Tech.SizeTier(Data, materialId, diameterMm)) || !ItemUnlocked(materialId)
                || (smoothElbows && !Unlocked(Progression.Tech.SmoothFittingsTier(Data))))
                return DrawError.Locked;
            return DrawError.None;
        }

        /// <summary>
        /// Everything after the outlets, in the order of the income formula (<c>02-economy.md</c>): prestige pay and electricity,
        /// the certificate penalty, the burst factor (0 in game, 0.25 offline, while a burst is not isolated), evacuation, a tripped panel.
        /// </summary>
        void ApplyEffects(BuildingEvaluation ev, NetworkSnapshot snap, BuildingState state, bool offline, string grade)
        {
            // gross ×= energyRatingMultiplier × inspectionMultiplier × eventMultiplier × prestigePayMultiplier (02 §Income formula).
            ev.EnergyGrade = grade;
            ev.EnergyFactor = HeadLoss.Game.Inspections.EnergyRating.PayFactor(Data, grade);
            ev.ApplyPayFactor(ev.EnergyFactor);
            if (Inspections.Bonus)
            {
                ev.InspectionBonus = true;
                ev.ApplyPayFactor(Inspections.PayFactor);
            }
            ev.ApplyPayFactor(Progression.PrestigeEffects.PayFactor(Data, Game));
            ev.Electricity *= Progression.PrestigeEffects.ElectricityFactor(Data, Game);
            if (GasSafety.CertificateExpired)
            {
                ev.CertificateExpired = true;
                ev.ApplyPayFactor(Data.Balancing.Inspection.GasCertExpiredFactor);
            }
            if (Failures.HasActiveBurst(snap, state))
            {
                EconomyDef e = Data.Balancing.Economy;
                ev.ApplyBurst(offline ? e.OfflineBurstFactor : e.InGameBurstFactor);
            }
            if (GasSafety.Evacuated) ev.ApplyEvacuation();
            if (Record.PanelTripped) ev.ApplyPanelTrip();
            if (Inspections.Closed) ev.ApplyInspectionClosed();
        }

        /// <summary>
        /// The energy rating (<c>10-events.md</c>): what is built is rated the first time it is online after a Commit (or a
        /// load), from a steady solve, and keeps its grade until the next Commit; the design's projected grade follows each
        /// preview. Runs before the effects, on evaluations that are online by their outlets and codes.
        /// </summary>
        void UpdateRating()
        {
            HeadLoss.Game.Inspections.InspectionState st = Record.Inspection;
            if (!st.Rated && CommittedEvaluation.Online && Game.PlayTimeSeconds >= _nextRateTry) RateCommitted();
            if (HasPendingDesign)
            {
                ProjectedGrade = HeadLoss.Game.Inspections.EnergyRating.Grade(Data, EnergyReference, DesignEvaluation, out double score);
                ProjectedScore = score;
            }
            else
            {
                ProjectedGrade = st.Grade;
                ProjectedScore = st.Score;
            }
        }

        /// <summary>Rates what is built from a steady solve: crank at 0, every device at full speed, today's outside air.</summary>
        void RateCommitted()
        {
            var none = new Dictionary<int, double>();
            NetworkSnapshot snap = SolveState(Committed.Clone(), CommittedSnapshot, _offlineSolver, OptionsFor(0, AirTemperatureK, none, none, true));
            BuildingEvaluation ev = BuildingEconomy.Evaluate(Data, Layout, snap);
            ev.ApplyViolations(Codes.CodeChecker.Check(Data, Layout, snap.State, snap));
            HeadLoss.Game.Inspections.InspectionState st = Record.Inspection;
            st.Grade = HeadLoss.Game.Inspections.EnergyRating.Grade(Data, EnergyReference, ev, out double score);
            st.Score = score;
            // Not online at steady state (a crank building, a moment without supply): try again in a minute.
            bool reference = EnergyReference.PowerW >= Data.Balancing.Inspection.RatingMinReferenceW;
            if (st.Grade.Length > 0 || !reference) st.Rated = true;
            else _nextRateTry = Game.PlayTimeSeconds + 60;
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
            NetworkSnapshot snap = SolveCopy(copy);
            BuildingEvaluation ev = BuildingEconomy.Evaluate(Data, Layout, snap);
            ev.ApplyViolations(Codes.CodeChecker.Check(Data, Layout, copy, snap));
            return ev;
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
            ev.ApplyViolations(Codes.CodeChecker.Check(Data, Layout, snap.State, snap));
            if (_airQuality) Codes.IndoorAirQuality.Apply(Data, Layout, ev, snap.State, snap);
            LastSteadySnapshot = snap;
            if (Failures.HasActiveBurst(snap, snap.State))
            {
                // Offline a burst cuts the income by 75%: a quarter of what the building earns whole (12 §Offline), even
                // when the broken pipe has left its outlets short and the building could not count as online.
                CompileOptions whole = OptionsFor(0, Units.CToK(outsideTemperatureC), none, none, true);
                whole.BurstSegments = null;
                NetworkSnapshot intactSnap = SolveState(Committed.Clone(), null, _offlineSolver, whole);
                BuildingEvaluation intact = BuildingEconomy.Evaluate(Data, Layout, intactSnap);
                intact.ApplyViolations(Codes.CodeChecker.Check(Data, Layout, snap.State, intactSnap));
                if (_airQuality) Codes.IndoorAirQuality.Apply(Data, Layout, intact, snap.State, intactSnap);
                ev.Gross = intact.Gross;
            }
            ApplyEffects(ev, snap, snap.State, true, Record.Inspection.Grade);
            return ev;
        }

        /// <summary>
        /// Before the catch-up: solves what is built, reads its draw and sets the power speed (a tripped panel stays
        /// tripped; a brownout keeps its speed). Runs on the calling thread.
        /// </summary>
        public void PrepareOffline()
        {
            SettleSpin();
            SolveNow();
            TickPower(0);
            if (Record.PanelTripped) PowerSpeed = 0;
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
            double net = NetCommitCost();
            if (net <= 0) return new CommitCheck(true, "");
            string symbol = Data.Game.CurrencySymbol;
            if (Game.Debt > 0) return new CommitCheck(false, "Purchases wait until the debt is repaid");
            if (!Record.EverOnline && Record.SpentTotal + net > BudgetCap + 1e-9)
                return new CommitCheck(false, $"Over budget cap by {Units.FormatMoney(Record.SpentTotal + net - BudgetCap, symbol)} · the cap lifts once the building has been online");
            if (Game.Wallet < net - 1e-9)
                return new CommitCheck(false, $"Need {Units.FormatMoney(net - Game.Wallet, symbol)} more");
            return new CommitCheck(true, "");
        }

        /// <summary>What a Commit takes now, $: the pending cost less the starter items in hand it places (prestige starter kit).</summary>
        public double NetCommitCost() => PendingCost.Net - StarterItemsValue(Record.Committed, Record.Design);

        /// <summary>The worth of starter items in hand the design newly places, $ (the first of each is free).</summary>
        public double StarterItemsValue(BuildingState before, BuildingState after)
        {
            if (Game.City == null || Game.City.StarterItems.Count == 0) return 0;
            double value = 0;
            foreach (string id in Game.City.StarterItems)
                if (Count(after, id) > Count(before, id)) value += Data.FindEquipment(id)?.Price ?? 0;
            return value;
        }

        static int Count(BuildingState s, string equipmentId)
        {
            int n = 0;
            foreach (EquipmentState e in s.Equipment) if (e.EquipmentId == equipmentId) n++;
            foreach (DuctNetworkState net in s.Networks)
                foreach (DuctNode d in net.Nodes) if (d.Kind == DuctNodeKind.Inline && d.PartId == equipmentId) n++;
            return n;
        }

        void UseStarterItems(BuildingState before, BuildingState after)
        {
            if (Game.City == null) return;
            Game.City.StarterItems.RemoveAll(id => Count(after, id) > Count(before, id));
        }

        /// <summary>Pays for the design and makes it the committed state. Use <see cref="CommitDesignCommand"/> from the UI.</summary>
        public bool Commit()
        {
            if (!CheckCommit().Allowed) return false;
            double net = NetCommitCost();
            if (net > 0) Game.Wallet -= net;
            else Game.ApplyIncome(-net);
            Record.SpentTotal += net;
            BuildingState before = Record.Committed;
            UseStarterItems(before, Record.Design);
            Record.Committed = Record.Design.Clone();
            Record.Inspection.Rated = false;
            _nextRateTry = 0;
            Failures.OnCommit(before, Record.Committed);
            GasSafety.OnCommit();
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
