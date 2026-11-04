using System;
using System.Collections.Generic;
using HeadLoss.Core;
using HeadLoss.Core.Data;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.Session;
using HeadLoss.Game.Thermal;
using HeadLoss.Sim.Network;
using HeadLoss.Sim.Rooms;

namespace HeadLoss.Game.Failures
{
    /// <summary>Gas entering the room air at a tile (a leak's flow from the last gas solve).</summary>
    public readonly struct GasInjection
    {
        public readonly TilePos Tile;
        /// <summary>m³/s at room pressure.</summary>
        public readonly double Flow;
        /// <summary>Ceiling pipes leak into the upper layer.</summary>
        public readonly bool Upper;

        public GasInjection(TilePos tile, double flow, bool upper)
        {
            Tile = tile;
            Flow = flow;
            Upper = upper;
        }
    }

    /// <summary>
    /// Flammable gas escalation for one building (<c>08-failures.md</c>): leaks fill the room grid; past a threshold the alarm
    /// evacuates the building and starts the explosion timer; when the timer ends with gas in range near an ignition source,
    /// the building explodes. Automatic shutoff valves stop the escalation; the timer pauses while the player is away. Also
    /// the gas safety certificate (<c>10-events.md</c> §Inspections).
    /// </summary>
    public sealed class GasSafety
    {
        readonly BuildingSession _s;
        readonly List<GasInjection> _injections = new List<GasInjection>();
        double _sinceTick;

        public GasSafety(BuildingSession session) => _s = session;

        DataRepository Data => _s.Data;
        BuildingFailures F => _s.Failures;
        GasDef G => Data.Balancing.Gas;
        FailureState State => _s.Record.Failures;
        GasEmergencyState E => State.Gas;
        FluidDef Fluid => Data.Fluid(BuildingState.GasFluid);

        /// <summary>Lower flammable limit, volume fraction.</summary>
        public double Lfl => Fluid.FlammableLower;
        /// <summary>Upper flammable limit, volume fraction.</summary>
        public double Ufl => Fluid.FlammableUpper;

        /// <summary>The building is evacuated: pays nothing until the leak is repaired and the air is clear.</summary>
        public bool Evacuated => E.Alarm;
        /// <summary>The explosion timer is counting.</summary>
        public bool TimerRunning => E.Alarm && E.TimerRunning;
        /// <summary>Explosion timer left, s.</summary>
        public double TimerLeft => E.TimerLeft;
        /// <summary>The certificate has run out: pay × <c>inspection.gasCertExpiredFactor</c>.</summary>
        public bool CertificateExpired => State.Certificate.Issued && State.Certificate.SecondsLeft <= 0 && State.Certificate.CheckLeft <= 0;

        /// <summary>Raised when the building explodes, with the blast energy, J (the city applies ruin, neighbours and fines).</summary>
        public event Action<double>? Exploded;

        /// <summary>Set while the offline catch-up runs: the timer stays paused and the certificate is advanced by the timeline.</summary>
        public bool OfflineMode { get; set; }

        // ---------- the in-game tick ----------

        /// <summary>Advances at <c>failure.tickHz</c>: certificate, random fitting leaks, leak injections, alarm, timer, all clear.</summary>
        public void Tick(double dt)
        {
            if (!_s.HasGas) return;
            _sinceTick += dt;
            double step = 1.0 / Math.Max(0.01, Data.Balancing.Failure.TickHz);
            if (_sinceTick < step) return;
            double elapsed = _sinceTick;
            _sinceTick = 0;
            TickCertificate(elapsed);
            RollFittingLeak(elapsed);
            UpdateInjections();
            if (E.Alarm) UpdateEmergency(elapsed);
            else CheckAlarm(elapsed);
        }

        /// <summary>Copies the leak flows of the last gas solve into the room grid's sources.</summary>
        public void UpdateInjections()
        {
            _injections.Clear();
            FluidSolve? gas = _s.CommittedSnapshot?.Gas;
            if (gas != null)
                foreach (LeakState l in State.Leaks)
                    if (gas.Compiled.LeakLinks.TryGetValue(l.Key, out int link) && gas.Result.LinkActive[link] && gas.Result.Flow[link] > 0)
                        _injections.Add(new GasInjection(l.Pos, gas.Result.Flow[link], true));
            _s.Rooms.GasLeaks = _injections;
        }

        /// <summary>Flow of a leak now, m³/s (the readout).</summary>
        public double LeakFlow(LeakState l)
        {
            FluidSolve? gas = _s.CommittedSnapshot?.Gas;
            return gas != null && gas.Compiled.LeakLinks.TryGetValue(l.Key, out int link) && gas.Result.LinkActive[link] ? Math.Max(0, gas.Result.Flow[link]) : 0;
        }

        // ---------- leaks ----------

        /// <summary>Gas fittings of what is built: the nodes a random leak can start at (bends, tees, inline parts).</summary>
        public static List<DuctNode> Fittings(DuctNetworkState net)
        {
            var list = new List<DuctNode>();
            foreach (DuctNode n in net.Nodes)
                if ((n.Kind == DuctNodeKind.Free && net.Degree(n.Id) >= 2) || n.Kind == DuctNodeKind.Inline) list.Add(n);
            return list;
        }

        /// <summary>Chance of a random fitting leak in the building over <paramref name="seconds"/> (Poisson at the flat rate per fitting).</summary>
        public double FittingLeakRatePerSecond(int fittings) => Data.Balancing.Failure.FittingLeakPerHour * fittings / Units.SecondsPerHour;

        void RollFittingLeak(double dt)
        {
            if (!_s.GasSupply.Connected) return;
            List<DuctNode> fittings = Fittings(_s.Committed.Gas);
            if (fittings.Count == 0) return;
            double p = 1 - Math.Exp(-FittingLeakRatePerSecond(fittings.Count) * dt);
            if (F.Roll() >= p) return;
            StartFittingLeak(fittings[Math.Min(fittings.Count - 1, (int)(F.Roll() * fittings.Count))]);
        }

        /// <summary>Starts a leak at a fitting: its area is a share of its largest pipe's cross-section.</summary>
        public LeakState? StartFittingLeak(DuctNode node)
        {
            DuctNetworkState net = _s.Committed.Gas;
            if (State.Leaks.Exists(l => l.Pos == node.Pos)) return null;
            double area = 0;
            foreach (DuctSegment seg in net.Segments)
                if (seg.NodeA == node.Id || seg.NodeB == node.Id) area = Math.Max(area, F.SegmentArea(seg));
            return F.AddLeak("fitting", "", node.Pos, area * G.FittingLeakAreaFraction, _s.Game.PlayTimeSeconds);
        }

        // ---------- alarm ----------

        /// <summary>Gas share at the cells of a tile, either layer.</summary>
        double GasAt(TilePos t)
        {
            RoomGridLayout grid = _s.Rooms.Grid;
            float[] gas = _s.Rooms.State.Gas;
            int n = grid.Cells, k = RoomGridBuilder.PerTile(Data), fi = t.F - _s.Layout.LowestFloor;
            if (fi < 0 || fi >= grid.Floors) return 0;
            double max = 0;
            for (int i = 0; i < k; i++)
                for (int j = 0; j < k; j++)
                {
                    int c = grid.Index(fi, t.X * k + i, t.Y * k + j);
                    max = Math.Max(max, Math.Max(gas[c], gas[n + c]));
                }
            return max;
        }

        /// <summary>Highest gas share in any cell layer of the building.</summary>
        public double MaxGas()
        {
            double max = 0;
            foreach (float v in _s.Rooms.State.Gas) if (v > max) max = v;
            return max;
        }

        void CheckAlarm(double dt)
        {
            if (!_s.Rooms.GasPresent) { E.Detecting = 0; return; }
            bool over = MaxGas() >= G.AlarmAnywhereFraction * Lfl;
            if (!over)
                foreach (SafetyDevice d in _s.Committed.Safety)
                    if (d.EquipmentId == GasSafetyIds.Detector && GasAt(d.Pos) >= G.AlarmDetectorFraction * Lfl) { over = true; break; }
            if (!over) { E.Detecting = 0; return; }
            E.Detecting += dt;
            if (E.Detecting >= G.DetectionSeconds * Progression.PrestigeEffects.RepairTimeFactor(Data, _s.Game)) RaiseAlarm();
        }

        /// <summary>
        /// The alarm stage: evacuation, automatic shutoffs close on the supply of every leak they can isolate, and unless that
        /// stops every leak the explosion timer starts.
        /// </summary>
        public void RaiseAlarm()
        {
            if (E.Alarm) return;
            E.Alarm = true;
            E.Detecting = 0;
            CloseAutoShutoffs();
            bool stopped = !LeakStillFed();
            E.TimerRunning = !stopped;
            E.TimerLeft = Data.Balancing.Failure.ExplosionTimerSeconds;
            _s.MarkDirty();
            if (!OfflineMode)
                F.RaiseAlert(stopped ? "Gas alarm · building evacuated · the automatic shutoff stopped the leak" : "Gas alarm · building evacuated · close the supply and send a crew");
            AlarmRaised?.Invoke();
        }

        /// <summary>Raised when the alarm sounds (the soft two-tone chime).</summary>
        public event Action? AlarmRaised;

        /// <summary>True when a leak is still reached by the supply through what is open.</summary>
        public bool LeakStillFed()
        {
            foreach (LeakState l in State.Leaks) if (Fed(l, null)) return true;
            return false;
        }

        /// <summary>The path from the gas connection to the leak's node over open parts, or null when it is cut off.</summary>
        List<DuctNode>? PathToLeak(LeakState leak, HashSet<TilePos>? closed)
        {
            DuctNetworkState net = _s.Committed.Gas;
            DuctNode? start = net.Nodes.Find(n => n.Kind == DuctNodeKind.Intake);
            int target = LeakNodeId(net, leak.Pos);
            if (start == null || target == 0) return null;
            var parent = new Dictionary<int, int> { [start.Id] = 0 };
            var queue = new Queue<int>();
            queue.Enqueue(start.Id);
            while (queue.Count > 0)
            {
                int u = queue.Dequeue();
                DuctNode node = net.FindNode(u)!;
                if (u != start.Id && IsClosed(node, closed)) continue;
                foreach (DuctSegment s in net.Segments)
                {
                    if (s.NodeA != u && s.NodeB != u) continue;
                    int v = s.Other(u);
                    if (parent.ContainsKey(v)) continue;
                    parent[v] = u;
                    queue.Enqueue(v);
                }
            }
            if (!parent.ContainsKey(target)) return null;
            var path = new List<DuctNode>();
            for (int at = target; at != 0; at = parent[at]) path.Add(net.FindNode(at)!);
            path.Reverse();
            return path;
        }

        bool Fed(LeakState leak, HashSet<TilePos>? extraClosed)
        {
            var closed = new HashSet<TilePos>(AutoClosedTiles());
            if (extraClosed != null) closed.UnionWith(extraClosed);
            return _s.GasSupply.Connected && PathToLeak(leak, closed) != null;
        }

        bool IsClosed(DuctNode n, HashSet<TilePos>? closed)
        {
            if (n.Kind != DuctNodeKind.Inline) return false;
            FittingDef? f = Data.FindFitting(n.PartId);
            if (f == null || f.Model != "valve" || f.MinOpening < 1) return false;
            return !n.On || (closed != null && closed.Contains(n.Pos));
        }

        /// <summary>The node a leak at a tile opens from (the duct node there, or the first node of the segment covering it), 0 when none.</summary>
        static int LeakNodeId(DuctNetworkState net, TilePos tile)
        {
            DuctNode? n = net.NodeAt(tile);
            if (n != null) return n.Id;
            foreach (DuctSegment s in net.Segments)
                foreach (TilePos t in DuctEditor.Tiles(net, s))
                    if (t == tile) return s.NodeA;
            return 0;
        }

        void CloseAutoShutoffs()
        {
            foreach (LeakState l in State.Leaks)
            {
                List<DuctNode>? path = PathToLeak(l, new HashSet<TilePos>(AutoClosedTiles()));
                if (path == null) continue;
                for (int i = path.Count - 1; i >= 0; i--)
                {
                    if (path[i].Kind != DuctNodeKind.Inline || Data.FindFitting(path[i].PartId)?.AutoShutoff != true) continue;
                    string key = TileKey(path[i].Pos);
                    if (!E.AutoClosed.Contains(key)) E.AutoClosed.Add(key);
                    break;
                }
            }
        }

        /// <summary>Tile key of a closed automatic shutoff.</summary>
        public static string TileKey(TilePos t) => $"{t.X},{t.Y},{t.F}";

        /// <summary>Automatic shutoffs closed now: those the alarm closed, plus all of them while the panel gives no power.</summary>
        public HashSet<TilePos> AutoClosedTiles()
        {
            var set = new HashSet<TilePos>();
            bool powerLoss = _s.PowerSpeed <= 0;
            foreach (DuctNode n in _s.Committed.Gas.Nodes)
            {
                if (n.Kind != DuctNodeKind.Inline || Data.FindFitting(n.PartId)?.AutoShutoff != true) continue;
                if (powerLoss || E.AutoClosed.Contains(TileKey(n.Pos))) set.Add(n.Pos);
            }
            return set;
        }

        // ---------- emergency ----------

        void UpdateEmergency(double dt)
        {
            CloseAutoShutoffs();
            bool fed = LeakStillFed();
            // A new leak the shutoffs cannot reach starts the timer again.
            if (fed && !E.TimerRunning)
            {
                E.TimerRunning = true;
                E.TimerLeft = Data.Balancing.Failure.ExplosionTimerSeconds;
            }
            if (State.Leaks.Count == 0 && MaxGas() < G.ClearFraction * Lfl)
            {
                ClearAlarm();
                return;
            }
            if (!E.TimerRunning || OfflineMode) return;
            E.TimerLeft -= dt;
            if (E.TimerLeft > 0) return;
            if (ExplosionPossible()) Explode();
            else E.TimerLeft = Data.Balancing.Failure.ExplosionTimerSeconds;
        }

        /// <summary>The offline timeline found the air clear with the leak repaired.</summary>
        public void ClearAfterAway() => ClearAlarm();

        void ClearAlarm()
        {
            E.Alarm = false;
            E.TimerRunning = false;
            E.TimerLeft = 0;
            E.AutoClosed.Clear();
            _s.MarkDirty();
            if (!OfflineMode) F.RaiseAlert("All clear · the air is safe and people are back");
        }

        /// <summary>Every tile holding an ignition source in what is built: the panel, electrical equipment that is not spark-free, stoves.</summary>
        public static HashSet<TilePos> IgnitionTiles(DataRepository data, BuildingLayout layout, BuildingState state)
        {
            var set = new HashSet<TilePos>();
            foreach (EquipmentState e in state.Equipment)
            {
                EquipmentDef? def = data.FindEquipment(e.EquipmentId);
                EntryOpeningDef? o = layout.FindOpening(e.OpeningId);
                if (def == null || o == null || !def.IsIgnitionSource || e.SparkFree) continue;
                List<TilePos>? tiles = EquipmentFits.Footprint(data, layout, state, o, def);
                if (tiles == null || tiles.Count == 0) set.Add(BuildingLayout.PosOf(o));
                else foreach (TilePos t in tiles) set.Add(t);
            }
            foreach (DuctNetworkState net in state.Networks)
                foreach (DuctNode n in net.Nodes)
                    if (n.Kind == DuctNodeKind.Inline && !n.SparkFree && data.FindEquipment(n.PartId)?.IsIgnitionSource == true) set.Add(n.Pos);
            foreach (OutletPlacementDef o in layout.Outlets)
                if (data.Outlet(o.Type).IsIgnitionSource) set.Add(BuildingLayout.PosOf(o));
            return set;
        }

        /// <summary>True when any cell layer within the ignition radius of an ignition source is between LFL and UFL.</summary>
        public bool ExplosionPossible()
        {
            RoomGridLayout grid = _s.Rooms.Grid;
            float[] gas = _s.Rooms.State.Gas;
            int n = grid.Cells, k = RoomGridBuilder.PerTile(Data);
            int reach = (int)Math.Ceiling(G.IgnitionRadiusMetres * k);
            float lfl = (float)Lfl, ufl = (float)Ufl;
            foreach (TilePos t in IgnitionTiles(Data, _s.Layout, _s.Committed))
            {
                int fi = t.F - _s.Layout.LowestFloor;
                if (fi < 0 || fi >= grid.Floors) continue;
                for (int cx = t.X * k - reach; cx < (t.X + 1) * k + reach; cx++)
                    for (int cy = t.Y * k - reach; cy < (t.Y + 1) * k + reach; cy++)
                    {
                        if (cx < 0 || cy < 0 || cx >= grid.NX || cy >= grid.NY) continue;
                        int c = grid.Index(fi, cx, cy);
                        if (grid.Room[c] < 0) continue;
                        float lo = gas[c], hi = gas[n + c];
                        if ((lo >= lfl && lo <= ufl) || (hi >= lfl && hi <= ufl)) return true;
                    }
            }
            return false;
        }

        /// <summary>
        /// Blast energy: Σ_cells (flammable gas volume in flammable range) · ρ_gas · LHV · yieldFactor, J (<c>08-failures.md</c>
        /// §Blast radius).
        /// </summary>
        public double CloudEnergyJoules()
        {
            RoomGridLayout grid = _s.Rooms.Grid;
            RoomGridParams p = _s.Rooms.Params;
            float[] gas = _s.Rooms.State.Gas;
            int n = grid.Cells;
            double cellArea = p.CellSize * p.CellSize, volume = 0;
            float lfl = (float)Lfl, ufl = (float)Ufl;
            for (int c = 0; c < n; c++)
            {
                if (grid.Room[c] < 0) continue;
                if (gas[c] >= lfl && gas[c] <= ufl) volume += gas[c] * cellArea * p.LowerHeight;
                if (gas[n + c] >= lfl && gas[n + c] <= ufl) volume += gas[n + c] * cellArea * p.UpperHeight;
            }
            double rho = Networks.NetworkCompiler.IdealGasDensity(Data, BuildingState.GasFluid, Units.CToK(Data.Balancing.Physics.DefaultAirTemperatureC));
            return Blast.EnergyJoules(volume * rho, Fluid.LowerHeatingValueMJPerKg * 1e6, Data.Balancing.Failure.YieldFactor);
        }

        void Explode()
        {
            E.TimerRunning = false;
            E.TimerLeft = 0;
            Exploded?.Invoke(CloudEnergyJoules());
        }

        // ---------- the gas safety certificate ----------

        /// <summary>The first certificate comes with the first gas pipe built.</summary>
        public void OnCommit()
        {
            GasCertificateState c = State.Certificate;
            if (c.Issued || _s.Committed.Gas.Segments.Count == 0) return;
            c.Issued = true;
            c.SecondsLeft = Data.Balancing.Inspection.GasCertHours * Units.SecondsPerHour;
            c.Reminded = false;
        }

        /// <summary>Time passes for the certificate (it counts while away too).</summary>
        public void TickCertificate(double dt)
        {
            GasCertificateState c = State.Certificate;
            if (!c.Issued) return;
            InspectionDef d = Data.Balancing.Inspection;
            bool wasValid = c.SecondsLeft > 0;
            c.SecondsLeft = Math.Max(0, c.SecondsLeft - dt);
            if (!c.Reminded && c.SecondsLeft > 0 && c.SecondsLeft <= d.GasCertReminderHours * Units.SecondsPerHour)
            {
                c.Reminded = true;
                if (!OfflineMode) F.RaiseAlert($"Gas safety certificate runs out in {Units.FormatDuration(c.SecondsLeft)}");
            }
            if (wasValid && c.SecondsLeft <= 0)
            {
                _s.MarkDirty();
                if (!OfflineMode) F.RaiseAlert("Gas safety certificate expired · pay is halved until it is renewed");
            }
            if (c.CheckLeft > 0)
            {
                c.CheckLeft -= dt;
                if (c.CheckLeft <= 0) FinishCheck();
            }
        }

        /// <summary>Renewal price, $.</summary>
        public double RenewalPrice => Data.Balancing.Inspection.GasCertRenewalPrice * Data.Balancing.Economy.TierFactor[_s.Layout.Tier - 1];

        /// <summary>Why the building would fail its renewal check now ("" = it would pass).</summary>
        public string RenewalProblem()
        {
            if (State.Leaks.Count > 0) return "a gas leak is not repaired";
            foreach (Codes.Violation v in Codes.CodeChecker.Check(Data, _s.Layout, _s.Committed))
                if (v.Fluid == BuildingState.GasFluid) return v.Reason;
            foreach (DuctSegment seg in _s.Committed.Gas.Segments)
                if (F.StressOf(BuildingState.GasFluid, seg.Id, _s.Committed) >= Data.Balancing.Failure.WeepThreshold) return "a gas pipe is weeping";
            return "";
        }

        /// <summary>Pays for a renewal and starts the check. False when it cannot be paid or a check is running.</summary>
        public bool RenewCertificate()
        {
            GasCertificateState c = State.Certificate;
            if (!c.Issued || c.CheckLeft > 0 || !_s.Game.CanAfford(RenewalPrice)) return false;
            _s.Game.Wallet -= RenewalPrice;
            c.CheckLeft = Data.Balancing.Inspection.GasCertCheckSeconds;
            c.LastFailure = "";
            _s.MarkDirty();
            return true;
        }

        void FinishCheck()
        {
            GasCertificateState c = State.Certificate;
            c.CheckLeft = 0;
            c.LastFailure = RenewalProblem();
            if (c.LastFailure.Length == 0)
            {
                c.SecondsLeft = Data.Balancing.Inspection.GasCertHours * Units.SecondsPerHour;
                c.Reminded = false;
                _s.MarkDirty();
                if (!OfflineMode) F.RaiseAlert("Gas safety certificate renewed");
            }
            else if (!OfflineMode) F.RaiseAlert("Gas check failed: " + c.LastFailure);
        }

        // ---------- away from the game: one well-mixed value per room (12 §Offline catch-up, step 4) ----------

        /// <summary>
        /// Well-mixed gas in a room's upper layer after <paramref name="seconds"/> with leaks feeding it: light gas collects in
        /// the upper layer, so its volume is what the leak fills. c∞ = Q_leak / (Q_leak + Q_fresh), τ = V / (Q_leak + Q_fresh).
        /// </summary>
        public static double WellMixed(double start, double leakFlow, double freshFlow, double volume, double seconds)
        {
            double q = leakFlow + freshFlow;
            if (q <= 0) return start;
            double target = leakFlow / q;
            return target + (start - target) * Math.Exp(-q * seconds / volume);
        }

        /// <summary>Seconds until a room reaches a gas share at the same flows, infinity when it never does.</summary>
        public static double TimeToReach(double start, double threshold, double leakFlow, double freshFlow, double volume)
        {
            if (start >= threshold) return 0;
            double q = leakFlow + freshFlow;
            if (q <= 0) return double.PositiveInfinity;
            double target = leakFlow / q;
            if (target <= threshold) return double.PositiveInfinity;
            return -volume / q * Math.Log((target - threshold) / (target - start));
        }
    }

    /// <summary>Equipment ids the gas rules look for.</summary>
    public static class GasSafetyIds
    {
        public const string Detector = "gas-detector";
        public const string PassiveVent = "passive-vent-opening";
    }

    /// <summary>Explosion physics (<c>08-failures.md</c> §Blast radius and chain reactions).</summary>
    public static class Blast
    {
        /// <summary>E = mass · LHV · yield, J.</summary>
        /// <param name="massKg">Flammable gas in range, kg.</param>
        /// <param name="lhvJPerKg">Lower heating value, J/kg.</param>
        public static double EnergyJoules(double massKg, double lhvJPerKg, double yieldFactor) => massKg * lhvJPerKg * yieldFactor;

        /// <summary>radius = k_r · E^(1/3), m.</summary>
        public static double RadiusMetres(double energyJoules, double kr) => kr * Math.Cbrt(Math.Max(0, energyJoules));

        /// <summary>damage(d) = clamp01(1 − d / radius).</summary>
        /// <param name="distanceMetres">From the blast centre to the other building's nearest edge.</param>
        public static double Damage(double distanceMetres, double radiusMetres)
        {
            if (radiusMetres <= 0) return 0;
            double v = 1 - distanceMetres / radiusMetres;
            return v < 0 ? 0 : v > 1 ? 1 : v;
        }
    }
}
