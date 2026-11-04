using System;
using System.Collections.Generic;
using HeadLoss.Core;
using HeadLoss.Core.Data;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.Networks;
using HeadLoss.Sim.Network;
using HeadLoss.Sim.Rooms;
using HeadLoss.Sim.Thermal;

namespace HeadLoss.Game.Thermal
{
    /// <summary>Builds the room grid of a building layout: 2 × 2 cells per 1 m tile (<c>04-buildings.md</c> §Grid).</summary>
    public static class RoomGridBuilder
    {
        public static RoomGridParams Params(DataRepository data)
        {
            RoomGridDef g = data.Balancing.RoomGrid;
            ClimateDef c = data.Balancing.Climate;
            return new RoomGridParams
            {
                CellSize = (float)g.CellSize,
                LowerHeight = (float)g.LayerSplitHeight,
                UpperHeight = (float)(g.RoomHeight - g.LayerSplitHeight),
                Diffusivity = (float)g.EddyDiffusivity,
                JetLength = (float)g.JetLength,
                Stratification = (float)g.Stratification,
                LayerMixing = (float)g.LayerMixing,
                FloorExchange = (float)g.FloorExchange,
                ThermalMass = (float)g.ThermalMassFactor,
                SorIterations = g.SorIterations,
                SorOmega = (float)g.SorOmega,
                WallU = (float)c.WallU,
                GasMolarMass = (float)data.Fluid(BuildingState.GasFluid).MolarMass,
            };
        }

        /// <summary>Cells per tile side (2 for 0.5 m cells).</summary>
        public static int PerTile(DataRepository data) => Math.Max(1, (int)Math.Round(1.0 / data.Balancing.RoomGrid.CellSize));

        public static RoomGridLayout Build(DataRepository data, BuildingLayout b)
        {
            int k = PerTile(data);
            var l = new RoomGridLayout { NX = b.Width * k, NY = b.Height * k, Floors = b.Floors, RoomCount = b.Rooms.Count };
            int n = l.Cells;
            l.Room = new int[n];
            l.OpenE = new bool[n];
            l.OpenN = new bool[n];
            l.Door = new bool[n];
            l.ExteriorArea = new float[n];
            l.Stair = new bool[n];
            l.RoomVolume = new float[b.Rooms.Count];
            float cell = (float)data.Balancing.RoomGrid.CellSize, height = (float)data.Balancing.RoomGrid.RoomHeight;
            var types = BuildingLayout.RoomTypesFrom(data.Buildings);
            for (int fi = 0; fi < b.Floors; fi++)
            {
                int floor = b.LowestFloor + fi;
                TilePos TileOf(int cx, int cy) => new TilePos(cx / k, cy / k, floor);
                bool Inside(int cx, int cy) => cx >= 0 && cy >= 0 && cx < l.NX && cy < l.NY && b.IsInside(TileOf(cx, cy));
                // Air moves within a room and through doors (never through a core-drilled hole).
                bool Open(TilePos a, TilePos o) => a == o || (b.IsInside(o) && (b.RoomIndexAt(a) == b.RoomIndexAt(o) || b.HasDoor(a, o)));
                for (int cy = 0; cy < l.NY; cy++)
                    for (int cx = 0; cx < l.NX; cx++)
                    {
                        int c = l.Index(fi, cx, cy);
                        TilePos t = TileOf(cx, cy);
                        int room = b.RoomIndexAt(t);
                        l.Room[c] = room;
                        if (room < 0) continue;
                        l.RoomVolume[room] += cell * cell * height;
                        if (cx + 1 < l.NX) l.OpenE[c] = Open(t, TileOf(cx + 1, cy));
                        if (cy + 1 < l.NY) l.OpenN[c] = Open(t, TileOf(cx, cy + 1));
                        int outside = (Inside(cx - 1, cy) ? 0 : 1) + (Inside(cx + 1, cy) ? 0 : 1) + (Inside(cx, cy - 1) ? 0 : 1) + (Inside(cx, cy + 1) ? 0 : 1);
                        l.ExteriorArea[c] = outside * cell * height;
                        // Stairwells and shafts link the floors (vertical exchange).
                        l.Stair[c] = types.TryGetValue(b.Rooms[room].Type, out BuildingLayout.RoomTypeInfo info) && (info.Shaft || info.BlocksPipes);
                    }
            }
            foreach (DoorDef d in b.Doors)
            {
                var a = new TilePos(d.X, d.Y, d.Floor);
                TilePos o = BuildingLayout.Neighbour(a, d.Side);
                int ra = b.RoomIndexAt(a), rb = b.RoomIndexAt(o);
                if (ra >= 0 && rb >= 0) l.Doors.Add((ra, rb));
                foreach (TilePos t in new[] { a, o })
                    for (int i = 0; i < k; i++)
                        for (int j = 0; j < k; j++)
                            if (b.InFootprint(t)) l.Door[l.Index(t.F - b.LowestFloor, t.X * k + i, t.Y * k + j)] = true;
            }
            return l;
        }
    }

    /// <summary>
    /// A building's rooms and duct temperatures (<c>05-simulation.md</c> §3–5): the room grid stepped at
    /// <c>roomGrid.openHz</c> while the building is open and at <c>backgroundHz</c> otherwise (with sub-steps
    /// <c>backgroundStepFactor</c> times longer), and the duct thermal solve at <c>thermalHz</c>. Vents, duct leaks
    /// and duct heat come from the latest network solve; occupants, machines, the sun and the outside from the
    /// layout and the <see cref="Climate"/>.
    /// </summary>
    public sealed class BuildingClimate
    {
        public RoomGridLayout Grid { get; }
        public RoomGridState State { get; }
        public RoomGridInputs Inputs { get; }
        public RoomGridParams Params { get; }
        /// <summary>The stepper (the CPU reference unless the Unity layer sets the Burst one).</summary>
        public IRoomGridStepper Stepper = new RoomGridReference();
        public Climate Climate { get; }
        /// <summary>Per-room values from the last step.</summary>
        public RoomSummary[] Rooms { get; private set; }
        /// <summary>Last duct thermal solve (null before the first).</summary>
        public ThermalResult? Thermal { get; private set; }
        /// <summary>The network the thermal solve belongs to.</summary>
        public CompiledNetwork? ThermalNetwork { get; private set; }
        /// <summary>Air temperature per duct segment and per duct node from the last thermal solve, K.</summary>
        public Dictionary<int, double> SegmentTemperatureK { get; private set; } = new Dictionary<int, double>();
        public Dictionary<int, double> NodeTemperatureK { get; private set; } = new Dictionary<int, double>();
        /// <summary>Increments after every room grid step (the heatmap refreshes on it).</summary>
        public int Revision { get; private set; }
        /// <summary>Gas entering the air now (set by the gas safety tick).</summary>
        public IReadOnlyList<Failures.GasInjection> GasLeaks = Array.Empty<Failures.GasInjection>();
        /// <summary>Passive vents letting fresh air in (what the rooms follow: the design while open, else what is built).</summary>
        public IReadOnlyList<SafetyDevice> PassiveVents = Array.Empty<SafetyDevice>();
        /// <summary>The air holds gas (below this share it is treated as none and the field stops being stepped).</summary>
        public bool GasPresent { get; private set; }
        const float GasTrace = 1e-7f;

        readonly DataRepository _data;
        readonly BuildingLayout _layout;
        readonly int _perTile;
        double _sinceStep, _sinceThermal;
        int _stepsLeft;
        float _stepDt;

        public BuildingClimate(DataRepository data, BuildingLayout layout, Climate climate)
        {
            _data = data;
            _layout = layout;
            Climate = climate;
            _perTile = RoomGridBuilder.PerTile(data);
            Grid = RoomGridBuilder.Build(data, layout);
            Params = RoomGridBuilder.Params(data);
            if (climate.WallUOverride > 0) Params.WallU = (float)climate.WallUOverride;
            ClimateDef c = data.Balancing.Climate;
            State = RoomGridState.Initial(Grid, (float)data.Balancing.Physics.DefaultAirTemperatureC, (float)c.OutdoorCo2Ppm, (float)c.OutdoorO2);
            Inputs = RoomGridInputs.For(Grid);
            Inputs.DoorConductance = (float)data.Balancing.RoomGrid.DoorConductance;
            Inputs.FacadeConductancePerM2 = (float)data.Balancing.RoomGrid.FacadeConductancePerM2;
            Rooms = new RoomSummary[layout.Rooms.Count];
        }

        /// <param name="dt">Seconds since the last tick.</param>
        /// <param name="open">The building is open in the building view (full room grid rate).</param>
        /// <param name="network">The network to read (the design while open, else what is built); see <see cref="Index"/>.</param>
        public void Tick(double dt, bool open, CompiledNetwork? network, SolveResult? result)
        {
            RoomGridDef g = _data.Balancing.RoomGrid;
            _sinceThermal += dt;
            if (network != null && result != null && _sinceThermal >= 1.0 / g.ThermalHz)
            {
                _sinceThermal = 0;
                SolveThermal(network, result);
            }
            _sinceStep += dt;
            double interval = 1.0 / (open ? g.OpenHz : g.BackgroundHz);
            if (open)
            {
                PayOut(_stepsLeft);
                if (_sinceStep < interval) return;
                double elapsed = Math.Min(_sinceStep, 4 * interval);
                _sinceStep = 0;
                Step(elapsed, elapsed, network, result);
                return;
            }
            // A background round is many sub-steps: paid out one a frame (or as many as a long tick covers), never all at once.
            double subStep = g.BackgroundStepFactor / g.OpenHz;
            if (_stepsLeft == 0)
            {
                if (_sinceStep < interval) return;
                double elapsed = Math.Min(_sinceStep, interval * 2);
                _sinceStep = 0;
                FillInputs(network, result);
                _stepsLeft = Math.Max(1, (int)Math.Ceiling(elapsed / subStep - 1e-9));
                _stepDt = (float)(elapsed / _stepsLeft);
            }
            PayOut(Math.Max(1, (int)Math.Ceiling(dt / subStep - 1e-9)));
        }

        /// <summary>Runs up to <paramref name="steps"/> sub-steps of the round in progress; its last one updates the summaries.</summary>
        void PayOut(int steps)
        {
            if (_stepsLeft == 0) return;
            steps = Math.Min(steps, _stepsLeft);
            for (int i = 0; i < steps; i++) Stepper.Step(Grid, State, Inputs, Params, _stepDt);
            _stepsLeft -= steps;
            if (_stepsLeft == 0) FinishStep();
        }

        /// <summary>
        /// Moves this building's rounds later by a share of their interval, so buildings loaded together do not all step in one
        /// frame. A scheduling matter only: the simulated time per round is unchanged.
        /// </summary>
        /// <param name="phase">Share of the interval, 0–1.</param>
        public void SetPhase(double phase)
        {
            RoomGridDef g = _data.Balancing.RoomGrid;
            _sinceStep = -phase / g.BackgroundHz;
            _sinceThermal = -phase / g.ThermalHz;
        }

        /// <summary>Advances the room grid by <paramref name="seconds"/> in sub-steps of at most <paramref name="subStep"/>.</summary>
        public void Step(double seconds, double subStep, CompiledNetwork? network, SolveResult? result)
        {
            FillInputs(network, result);
            int steps = Math.Max(1, (int)Math.Ceiling(seconds / subStep - 1e-9));
            float dt = (float)(seconds / steps);
            for (int i = 0; i < steps; i++) Stepper.Step(Grid, State, Inputs, Params, dt);
            FinishStep();
        }

        void FinishStep()
        {
            Rooms = RoomGridMath.Summaries(Grid, State, Inputs, Params);
            if (Inputs.GasActive) UpdateGasPresent();
            Revision++;
        }

        void UpdateGasPresent()
        {
            float max = 0;
            foreach (float v in State.Gas) if (v > max) max = v;
            GasPresent = max >= GasTrace;
            if (!GasPresent) Array.Clear(State.Gas, 0, State.Gas.Length);
        }

        /// <summary>Sets the gas share of a room's upper layer to at least a value (the offline well-mixed estimate carried into the grid).</summary>
        public void SeedRoomGas(int room, double share)
        {
            if (share <= 0) return;
            int n = Grid.Cells;
            for (int c = 0; c < n; c++)
                if (Grid.Room[c] == room && State.Gas[n + c] < share) State.Gas[n + c] = (float)share;
            GasPresent = true;
        }

        IEnumerable<int> CellsOf(TilePos t)
        {
            int f = t.F - _layout.LowestFloor;
            if (f < 0 || f >= Grid.Floors) yield break;
            for (int i = 0; i < _perTile; i++)
                for (int j = 0; j < _perTile; j++)
                    yield return Grid.Index(f, t.X * _perTile + i, t.Y * _perTile + j);
        }

        void AddSupply(TilePos t, double flow, double temperatureC)
        {
            if (flow <= 0 || !_layout.InFootprint(t)) return;
            float share = (float)(flow / (_perTile * _perTile));
            foreach (int c in CellsOf(t))
            {
                float before = Inputs.SupplyFlow[c];
                Inputs.SupplyFlow[c] = before + share;
                Inputs.SupplyTemperature[c] = (Inputs.SupplyTemperature[c] * before + (float)temperatureC * share) / (before + share);
            }
        }

        void AddLowSupply(TilePos t, double flow, double temperatureC)
        {
            if (flow <= 0 || !_layout.InFootprint(t)) return;
            float share = (float)(flow / (_perTile * _perTile));
            foreach (int c in CellsOf(t))
            {
                float before = Inputs.SupplyLower[c];
                Inputs.SupplyLower[c] = before + share;
                // One supply temperature per cell: flow-weighted with what the upper layer gets.
                float other = Inputs.SupplyFlow[c];
                Inputs.SupplyTemperature[c] = (Inputs.SupplyTemperature[c] * (before + other) + (float)temperatureC * share) / (before + other + share);
            }
        }

        void FillInputs(CompiledNetwork? network, SolveResult? result)
        {
            ClimateDef cd = _data.Balancing.Climate;
            Inputs.Clear();
            Inputs.OutsideTemperature = (float)Climate.OutsideTemperatureC;
            Inputs.OutdoorCo2Ppm = (float)cd.OutdoorCo2Ppm;
            Inputs.OutdoorO2 = (float)cd.OutdoorO2;

            // Gas leaks and passive vents do not need an air solve.
            bool leaking = false;
            foreach (Failures.GasInjection g in GasLeaks)
            {
                if (g.Flow <= 0 || !_layout.InFootprint(g.Tile)) continue;
                leaking = true;
                float share = (float)(g.Flow / (_perTile * _perTile));
                float[] into = g.Upper ? Inputs.GasUpper : Inputs.GasLower;
                foreach (int c in CellsOf(g.Tile)) into[c] += share;
            }
            Inputs.GasActive = leaking || GasPresent;
            if (leaking) GasPresent = true;
            foreach (SafetyDevice v in PassiveVents)
            {
                double flow = Units.M3hToM3s(_data.Balancing.Gas.PassiveVentFlowM3h) * Climate.WindFactor;
                if (v.High) AddSupply(v.Pos, flow, Climate.OutsideTemperatureC);
                else AddLowSupply(v.Pos, flow, Climate.OutsideTemperatureC);
            }

            // People and machines over their room's lower layer; the sun through each window.
            var cellsPerRoom = new int[_layout.Rooms.Count];
            for (int c = 0; c < Grid.Cells; c++) if (Grid.Room[c] >= 0) cellsPerRoom[Grid.Room[c]]++;
            for (int c = 0; c < Grid.Cells; c++)
            {
                int r = Grid.Room[c];
                if (r < 0 || cellsPerRoom[r] == 0) continue;
                RoomDef room = _layout.Rooms[r];
                Inputs.HeatLower[c] += (float)((room.Occupants * cd.OccupantSensibleW + room.MachinesW) / cellsPerRoom[r]);
                Inputs.Co2Lower[c] += (float)(room.Occupants * cd.OccupantCo2Lps * 1e-3 / cellsPerRoom[r]);
            }
            foreach (WindowDef w in _layout.Windows)
            {
                double gain = Climate.SolarGainW(w.Side, _data.Buildings.Generation.WindowAreaM2);
                if (gain <= 0) continue;
                var cells = new List<int>(CellsOf(new TilePos(w.X, w.Y, w.Floor)));
                foreach (int c in cells) Inputs.HeatLower[c] += (float)(gain / cells.Count);
            }
            if (network == null || result == null) return;

            // Vents inject their air (at the duct temperature there) and throw a jet across the room.
            foreach (var kv in network.OutletLink)
            {
                OutletPlacementDef? o = _layout.FindOutlet(kv.Key);
                if (o == null) continue;
                double q = Math.Max(0, result.Flow[kv.Value]);
                double tK = Thermal != null && ThermalNetwork == network ? Thermal.LinkInT[kv.Value] : network.Graph.Links[kv.Value].Temperature;
                TilePos tile = BuildingLayout.PosOf(o);
                AddSupply(tile, q, Units.KToC(tK));
                OutletDef def = _data.Outlet(o.Type);
                (int dx, int dy) = Facing(o, tile);
                int cx = tile.X * _perTile + (dx > 0 ? _perTile - 1 : 0), cy = tile.Y * _perTile + (dy > 0 ? _perTile - 1 : 0);
                if (q > 0) Inputs.Jets.Add(new VentJet { Cell = Grid.Index(tile.F - _layout.LowestFloor, cx, cy), DirX = dx, DirY = dy, FaceVelocity = (float)(q / def.GrilleFaceAreaM2) });
            }
            // Open ends discharge into the room; duct leaks enter the room under the duct.
            foreach (var kv in network.OpenEndLink)
            {
                TilePos? t = NodeTile(network, kv.Key);
                if (t.HasValue) AddSupply(t.Value, Math.Max(0, result.Flow[kv.Value]), Units.KToC(network.Graph.Links[kv.Value].Temperature));
            }
            foreach (var kv in network.SegmentLeaks)
            {
                (TilePos a, TilePos b)? ends = SegmentTiles(network, kv.Key);
                if (!ends.HasValue) continue;
                AddSupply(ends.Value.a, Math.Max(0, result.Flow[kv.Value.a]), Units.KToC(network.Graph.Links[kv.Value.a].Temperature));
                AddSupply(ends.Value.b, Math.Max(0, result.Flow[kv.Value.b]), Units.KToC(network.Graph.Links[kv.Value.b].Temperature));
            }
            // Heat the duct air gives off warms the upper layer under the duct.
            if (Thermal != null && ThermalNetwork == network)
                foreach (var kv in network.SegmentLink)
                {
                    double heat = Thermal.HeatToAmbient[kv.Value];
                    if (Math.Abs(heat) < 1e-9) continue;
                    List<TilePos> tiles = SegmentPath(network, kv.Key);
                    if (tiles.Count == 0) continue;
                    foreach (TilePos t in tiles)
                        foreach (int c in CellsOf(t))
                            Inputs.HeatUpper[c] += (float)(heat / (tiles.Count * _perTile * _perTile));
                }
        }

        /// <summary>The vent's throw direction: its facing, or toward the room centre.</summary>
        (int dx, int dy) Facing(OutletPlacementDef o, TilePos tile)
        {
            switch (o.Facing)
            {
                case "north": return (0, 1);
                case "south": return (0, -1);
                case "east": return (1, 0);
                case "west": return (-1, 0);
            }
            RoomDef? r = _layout.RoomOf(tile);
            if (r == null) return (1, 0);
            double cx = r.X + r.Width * 0.5 - (tile.X + 0.5), cy = r.Y + r.Height * 0.5 - (tile.Y + 0.5);
            return Math.Abs(cx) >= Math.Abs(cy) ? (cx >= 0 ? 1 : -1, 0) : (0, cy >= 0 ? 1 : -1);
        }

        // Where each duct node and segment of the indexed network lies, and what its segments are made of.
        readonly Dictionary<int, TilePos> _nodeTiles = new Dictionary<int, TilePos>();
        readonly Dictionary<int, (TilePos a, TilePos b)> _segmentTiles = new Dictionary<int, (TilePos, TilePos)>();
        CompiledNetwork? _tilesFor;

        /// <summary>Remembers where the duct nodes and segments of a state are (call when a network is compiled from it).</summary>
        public void Index(CompiledNetwork network, DuctNetworkState state)
        {
            if (_tilesFor == network) return;
            _tilesFor = network;
            _nodeTiles.Clear();
            _segmentTiles.Clear();
            foreach (DuctNode n in state.Nodes) _nodeTiles[n.Id] = n.Pos;
            _segmentSpecs.Clear();
            foreach (DuctSegment s in state.Segments)
            {
                if (_nodeTiles.TryGetValue(s.NodeA, out TilePos a) && _nodeTiles.TryGetValue(s.NodeB, out TilePos b)) _segmentTiles[s.Id] = (a, b);
                _segmentSpecs[s.Id] = (s.MaterialId, s.Insulation);
            }
        }

        TilePos? NodeTile(CompiledNetwork network, int nodeId) => _tilesFor == network && _nodeTiles.TryGetValue(nodeId, out TilePos t) ? t : (TilePos?)null;

        (TilePos a, TilePos b)? SegmentTiles(CompiledNetwork network, int segmentId) =>
            _tilesFor == network && _segmentTiles.TryGetValue(segmentId, out var e) ? e : ((TilePos, TilePos)?)null;

        List<TilePos> SegmentPath(CompiledNetwork network, int segmentId)
        {
            var list = new List<TilePos>();
            if (!(SegmentTiles(network, segmentId) is (TilePos a, TilePos b))) return list;
            for (TilePos t = a; ; t = t.Toward(b))
            {
                list.Add(t);
                if (t == b || list.Count > 512) break;
            }
            return list;
        }

        /// <summary>
        /// Duct temperatures (<c>05-simulation.md</c> §4): outside air enters at the intake, each duct exchanges heat with
        /// the upper layer of the rooms it runs over (U from its material or insulation), junctions mix by mass flow.
        /// </summary>
        public void SolveThermal(CompiledNetwork network, SolveResult result)
        {
            NetworkGraph g = network.Graph;
            int links = g.Links.Count, nodes = g.Nodes.Count;
            double outside = Units.CToK(Climate.OutsideTemperatureC);
            double meanRoom = 0;
            int rooms = 0;
            foreach (RoomSummary r in Rooms) if (r.MeanTemperature != 0) { meanRoom += r.MeanTemperature; rooms++; }
            double roomK = Units.CToK(rooms > 0 ? meanRoom / rooms : _data.Balancing.Physics.DefaultAirTemperatureC);
            var boundary = new double[nodes];
            for (int i = 0; i < nodes; i++)
                boundary[i] = g.Nodes[i].Label.StartsWith("room", StringComparison.Ordinal) ? roomK : outside;
            var ambient = new double[links];
            var u = new double[links];
            var outer = new double[links];
            for (int k = 0; k < links; k++) ambient[k] = roomK;
            if (_tilesFor == network)
                foreach (var kv in network.SegmentLink)
                {
                    int k = kv.Value;
                    List<TilePos> tiles = SegmentPath(network, kv.Key);
                    double sum = 0;
                    int cnt = 0;
                    foreach (TilePos t in tiles)
                        foreach (int c in CellsOf(t)) { sum += State.T[Grid.Cells + c]; cnt++; }
                    if (cnt > 0) ambient[k] = Units.CToK(sum / cnt);
                    (double uu, double d) = Exchange(network, kv.Key, g.Links[k].Diameter);
                    u[k] = uu;
                    outer[k] = d;
                }
            double cp = 1005;
            Thermal = PipeThermal.Solve(g, result, boundary, ambient, u, outer, cp);
            ThermalNetwork = network;
            var seg = new Dictionary<int, double>();
            foreach (var kv in network.SegmentLink) seg[kv.Key] = 0.5 * (Thermal.LinkInT[kv.Value] + Thermal.LinkOutT[kv.Value]);
            var node = new Dictionary<int, double>();
            foreach (var kv in network.NodeIndex) node[kv.Key] = Thermal.NodeT[kv.Value];
            SegmentTemperatureK = seg;
            NodeTemperatureK = node;
        }

        /// <summary>U and outer diameter of a duct segment from its material and insulation.</summary>
        (double u, double outer) Exchange(CompiledNetwork network, int segmentId, double innerDiameter)
        {
            if (!_segmentSpecs.TryGetValue(segmentId, out (string material, string insulation) spec)) return (0, innerDiameter);
            InsulationDef? ins = spec.insulation.Length > 0 ? _data.FindInsulation(spec.insulation) : null;
            if (ins != null) return (ins.U, innerDiameter + 2 * Units.MmToM(ins.ThicknessMm));
            return (_data.Material(spec.material).UUninsulated, innerDiameter);
        }

        readonly Dictionary<int, (string, string)> _segmentSpecs = new Dictionary<int, (string, string)>();
    }
}
