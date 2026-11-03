using System;
using System.Collections.Generic;
using HeadLoss.Core;
using HeadLoss.Core.Data;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.Networks;
using HeadLoss.Game.Session;
using HeadLoss.Sim.Network;

namespace HeadLoss.Game.Failures
{
    /// <summary>The next burst drawn from the Weibull model (the offline event timeline).</summary>
    public sealed class OfflineBurstSample
    {
        public string Fluid = "";
        public int SegmentId;
        /// <summary>The tile edge that goes.</summary>
        public string Edge = "";
        /// <summary>Time until the burst, s, at the solve's stress rate.</summary>
        public double Seconds;
    }

    /// <summary>
    /// The burst model for one building (<c>08-failures.md</c>): pipes and ducts above their rating build stress, which
    /// raises a Weibull hazard; a roll on the building's failure stream bursts the segment. Stress is kept per tile edge,
    /// so it survives edits elsewhere and resets when that pipe is replaced or repaired. Water-hammer surges
    /// (<c>05-simulation.md</c> §6) add to the pressure for a short time. Repairs take one crew at a time.
    /// </summary>
    public sealed class BuildingFailures
    {
        readonly DataRepository _data;
        readonly BuildingLayout _layout;
        readonly BuildingRecord _record;
        readonly ulong _seed;
        readonly Dictionary<string, (double pa, double left)> _surges = new Dictionary<string, (double, double)>();
        double _sinceTick;

        public FailureState State => _record.Failures;
        /// <summary>Short calm messages (bursts, weeping joints found by leak sensors, repairs done).</summary>
        public event Action<string>? Alert;
        /// <summary>Bumped when a burst happens or a repair finishes (the building re-solves).</summary>
        public int Revision { get; private set; }

        /// <param name="seed">The building's seed (the failure stream derives from it).</param>
        public BuildingFailures(DataRepository data, BuildingLayout layout, BuildingRecord record, ulong seed)
        {
            _data = data;
            _layout = layout;
            _record = record;
            _seed = seed;
        }

        FailureDef F => _data.Balancing.Failure;

        // ---------- stress ----------

        /// <summary>Stress of a segment (the most stressed of its tile edges), 0 when none.</summary>
        public double StressOf(string fluid, int segmentId, BuildingState state)
        {
            DuctNetworkState net = state.Net(fluid);
            DuctSegment? s = net.FindSegment(segmentId);
            if (s == null) return 0;
            double max = 0;
            foreach (string e in Edges(fluid, net, s)) if (State.Stress.TryGetValue(e, out double v) && v > max) max = v;
            return max;
        }

        /// <summary>The tile edges of a segment as stress keys.</summary>
        public static List<string> Edges(string fluid, DuctNetworkState net, DuctSegment s)
        {
            List<TilePos> tiles = DuctEditor.Tiles(net, s);
            var list = new List<string>(tiles.Count);
            for (int i = 1; i < tiles.Count; i++) list.Add(FailureState.EdgeKey(fluid, tiles[i - 1], tiles[i]));
            return list;
        }

        /// <summary>Overpressure ratio x = p_max / p_rated of a solved segment (surge included), 0 when not solved.</summary>
        public double RatingRatio(FluidSolve solve, DuctNetworkState net, DuctSegment s, double surgePa)
        {
            if (!solve.Compiled.SegmentLink.TryGetValue(s.Id, out int link) || !solve.Result.LinkActive[link]) return 0;
            NetworkLink l = solve.Compiled.Graph.Links[link];
            double p = Math.Max(solve.Result.NodePressure[l.From], solve.Result.NodePressure[l.To]) + surgePa;
            return p / _data.Material(s.MaterialId).RatedPressure(s.DiameterMm);
        }

        /// <summary>Stress rate at an overpressure ratio, 1/s: k_s · max(0, x − 1)^a (+ optional ageing).</summary>
        public double StressRate(double x) => F.StressRate * Math.Pow(Math.Max(0, x - 1), F.StressExponent) + F.AgeingRate;

        /// <summary>Weibull hazard per unit stress, h(S) = (β/η)(S/η)^(β−1).</summary>
        public double Hazard(double s) => F.WeibullBeta / F.WeibullEta * Math.Pow(Math.Max(0, s) / F.WeibullEta, F.WeibullBeta - 1);

        /// <summary>
        /// Chance of a burst while stress grows from <paramref name="before"/> to <paramref name="after"/>:
        /// 1 − exp(−(H(after) − H(before))), the exact form of 1 − exp(−h(S)·dS), so the cumulative chance does not depend on
        /// the tick length (D87).
        /// </summary>
        public double BurstChance(double before, double after) => 1 - Math.Exp(-(CumulativeHazard(after) - CumulativeHazard(before)));

        /// <summary>Cumulative hazard H(S) = (S/η)^β.</summary>
        public double CumulativeHazard(double s) => Math.Pow(Math.Max(0, s) / F.WeibullEta, F.WeibullBeta);

        /// <summary>
        /// Seconds until the burst chance reaches 50% from stress <paramref name="s"/> at a constant stress rate (engineering
        /// software forecast). Infinity when the segment is not building stress.
        /// </summary>
        public double TimeToHalfChance(double s, double rate)
        {
            if (rate <= 0) return double.PositiveInfinity;
            double s50 = F.WeibullEta * Math.Pow(CumulativeHazard(s) + Math.Log(2), 1 / F.WeibullBeta);
            return Math.Max(0, (s50 - s) / rate);
        }

        /// <summary>Surge riding on a network now, Pa.</summary>
        public double SurgePa(string fluid) => _surges.TryGetValue(fluid, out var s) && s.left > 0 ? s.pa : 0;

        /// <summary>
        /// Advances stress and rolls bursts for what is built (<c>08-failures.md</c> §Burst model), at
        /// <c>failure.tickHz</c>. <paramref name="committed"/> must be the solve of <paramref name="state"/>.
        /// </summary>
        public void Tick(double dt, NetworkSnapshot? committed, BuildingState state, double now)
        {
            foreach (string fluid in new List<string>(_surges.Keys))
            {
                var s = _surges[fluid];
                _surges[fluid] = (s.pa, s.left - dt);
            }
            _sinceTick += dt;
            double step = 1.0 / Math.Max(0.01, F.TickHz);
            if (_sinceTick < step || committed == null) return;
            double elapsed = _sinceTick;
            _sinceTick = 0;
            foreach (FluidSolve fs in committed.All) Advance(fs, state.Net(fs.Fluid), elapsed, now);
        }

        void Advance(FluidSolve fs, DuctNetworkState net, double dt, double now)
        {
            double surge = SurgePa(fs.Fluid);
            HashSet<int> burst = BurstSegments(fs.Fluid, net);
            foreach (DuctSegment s in net.Segments)
            {
                if (burst.Contains(s.Id)) continue;
                double x = RatingRatio(fs, net, s, surge);
                double dS = StressRate(x) * dt;
                if (dS <= 0) continue;
                List<string> edges = Edges(fs.Fluid, net, s);
                double sMax = 0;
                string worst = edges.Count > 0 ? edges[0] : "";
                foreach (string e in edges)
                {
                    double v = (State.Stress.TryGetValue(e, out double old) ? old : 0) + dS;
                    State.Stress[e] = v;
                    if (v > sMax) { sMax = v; worst = e; }
                }
                // Weeping (pre-burst): leak sensors alert once per segment with the expected time to a burst.
                if (sMax >= F.WeepThreshold && State.LeakSensors.Contains(fs.Fluid) && !State.WeepAlerted.Contains(worst))
                {
                    State.WeepAlerted.Add(worst);
                    double t = TimeToHalfChance(sMax, dS / dt);
                    Alert?.Invoke($"Weeping joint on a {s.DiameterMm:0} mm {(fs.Fluid == "air" ? "duct" : "pipe")}, burst likely in ~{Units.FormatDuration(t)}");
                }
                double p = BurstChance(sMax - dS, sMax);
                if (p > 0 && Roll() < p) Burst(fs, net, s, worst, now);
            }
        }

        double Roll() => new Rng(Seeds.Derive(Seeds.Derive(_seed, "failure"), State.Counter++)).NextDouble();

        void Burst(FluidSolve fs, DuctNetworkState net, DuctSegment s, string edge, double now)
        {
            State.Bursts.Add(new BurstState { Fluid = fs.Fluid, Edge = edge, At = now });
            Revision++;
            if (OfflineMode)
            {
                if (State.MaintenanceContract) StartRepair(fs.Fluid, edge, State.PreferEmergency, "", 0, net, s, OfflinePay);
                return;
            }
            // The rupture is a sudden change in flow: a surge on the rest of the network.
            if (fs.Compiled.SegmentLink.TryGetValue(s.Id, out int link))
                AddSurge(fs, net, Math.Abs(fs.Result.Velocity[link]), DistanceToSource(fs, link), WaveSpeed(net, s), F.BurstSeconds);
            Alert?.Invoke($"A {s.DiameterMm:0} mm {(fs.Fluid == "air" ? "duct" : "pipe")} burst · repair it or close a shutoff upstream");
        }

        /// <summary>Set while the offline catch-up runs: maintenance contracts dispatch crews, no alerts (the report lists it all).</summary>
        public bool OfflineMode { get; set; }
        /// <summary>Pays a contract crew's bill during the catch-up.</summary>
        public Func<double, bool>? OfflinePay { get; set; }

        // ---------- the offline timeline (12 §Offline catch-up) ----------

        /// <summary>
        /// Draws the next burst for a solved state: for each segment building stress, burst stress S_b from the inverse
        /// Weibull CDF given its current stress (H(S_b) = H(S) − ln U), then the time to reach it at the current rate. The
        /// hazard is memoryless in H, so drawing again at every stop on the timeline stays correct.
        /// </summary>
        /// <param name="onlySegment">Consider only this segment (0 = all).</param>
        public OfflineBurstSample? SampleNextBurst(NetworkSnapshot snap, BuildingState state, int onlySegment = 0)
        {
            OfflineBurstSample? best = null;
            foreach (FluidSolve fs in snap.All)
            {
                DuctNetworkState net = state.Net(fs.Fluid);
                HashSet<int>? burst = null;
                foreach (DuctSegment s in net.Segments)
                {
                    if (onlySegment != 0 && s.Id != onlySegment) continue;
                    double rate = StressRate(RatingRatio(fs, net, s, 0));
                    if (rate <= 0) continue;
                    burst ??= BurstSegments(fs.Fluid, net);
                    if (burst.Contains(s.Id)) continue;
                    string worst = "";
                    double sNow = -1;
                    foreach (string e in Edges(fs.Fluid, net, s))
                    {
                        double v = State.Stress.TryGetValue(e, out double old) ? old : 0;
                        if (v > sNow) { sNow = v; worst = e; }
                    }
                    double u = Math.Max(1e-12, Roll());
                    double sBurst = F.WeibullEta * Math.Pow(CumulativeHazard(sNow) - Math.Log(u), 1 / F.WeibullBeta);
                    double t = Math.Max(0, (sBurst - sNow) / rate);
                    if (best == null || t < best.Seconds)
                        best = new OfflineBurstSample { Fluid = fs.Fluid, SegmentId = s.Id, Edge = worst, Seconds = t };
                }
            }
            return best;
        }

        /// <summary>Adds the stress of <paramref name="dt"/> seconds at the solve's rates (no rolls: the timeline drew the bursts).</summary>
        public void AdvanceStress(NetworkSnapshot snap, BuildingState state, double dt)
        {
            if (dt <= 0) return;
            foreach (FluidSolve fs in snap.All)
            {
                DuctNetworkState net = state.Net(fs.Fluid);
                HashSet<int>? burst = null;
                foreach (DuctSegment s in net.Segments)
                {
                    double dS = StressRate(RatingRatio(fs, net, s, 0)) * dt;
                    if (dS <= 0) continue;
                    burst ??= BurstSegments(fs.Fluid, net);
                    if (burst.Contains(s.Id)) continue;
                    foreach (string e in Edges(fs.Fluid, net, s)) State.Stress[e] = (State.Stress.TryGetValue(e, out double old) ? old : 0) + dS;
                }
            }
        }

        /// <summary>The burst the timeline drew happens now.</summary>
        public void BurstSampled(NetworkSnapshot snap, BuildingState state, OfflineBurstSample b, double now)
        {
            FluidSolve? fs = snap.For(b.Fluid);
            DuctNetworkState net = state.Net(b.Fluid);
            DuctSegment? s = net.FindSegment(b.SegmentId);
            if (fs != null && s != null) Burst(fs, net, s, b.Edge, now);
        }

        /// <summary>Bursts already waiting when the player left: a contract sends crews for them too.</summary>
        public void DispatchContract(BuildingState state)
        {
            if (!State.MaintenanceContract) return;
            foreach (BurstState b in new List<BurstState>(State.Bursts))
            {
                DuctNetworkState net = state.Net(b.Fluid);
                DuctSegment? s = net.Segments.Find(x => Edges(b.Fluid, net, x).Contains(b.Edge));
                if (s != null) StartRepair(b.Fluid, b.Edge, State.PreferEmergency, "", 0, net, s, OfflinePay);
            }
        }

        // ---------- bursts and isolation ----------

        /// <summary>Segments of a network that contain a burst edge.</summary>
        public HashSet<int> BurstSegments(string fluid, DuctNetworkState net)
        {
            var set = new HashSet<int>();
            if (State.Bursts.Count == 0) return set;
            var edges = new HashSet<string>();
            foreach (BurstState b in State.Bursts) if (b.Fluid == fluid) edges.Add(b.Edge);
            if (edges.Count == 0) return set;
            foreach (DuctSegment s in net.Segments)
                foreach (string e in Edges(fluid, net, s))
                    if (edges.Contains(e)) { set.Add(s.Id); break; }
            return set;
        }

        /// <summary>Every burst segment per network of a state (compile options).</summary>
        public Dictionary<string, HashSet<int>> AllBurstSegments(BuildingState state)
        {
            var d = new Dictionary<string, HashSet<int>>();
            foreach (string f in BuildingState.FluidIds) d[f] = BurstSegments(f, state.Net(f));
            return d;
        }

        /// <summary>
        /// True when a burst cuts the building's income: a burst on a water or air line that a closed shutoff has not
        /// isolated (<c>07-equipment.md</c> §Control parts).
        /// </summary>
        public bool HasActiveBurst(NetworkSnapshot? committed, BuildingState state)
        {
            if (State.Bursts.Count == 0) return false;
            foreach (BurstState b in State.Bursts)
            {
                FluidSolve? fs = committed?.For(b.Fluid);
                DuctNetworkState net = state.Net(b.Fluid);
                bool isolated = false;
                if (fs != null)
                    foreach (int seg in BurstSegments(b.Fluid, net))
                        if (fs.Compiled.IsolatedSegments.Contains(seg)) isolated = true;
                if (!isolated) return true;
            }
            return false;
        }

        // ---------- water hammer ----------

        /// <summary>Wave speed of a segment's material, m/s (0 for ducts: no hammer in air).</summary>
        double WaveSpeed(DuctNetworkState net, DuctSegment s) => net.FluidId == "air" ? 0 : _data.Material(s.MaterialId).WaveSpeed;

        /// <summary>
        /// A Joukowsky surge Δp = ρ a Δv, scaled by (2L/a)/t when the change is slower than the wave's round trip, and by
        /// the accumulator factor when the network has a pressure tank. It rides on the network for
        /// <c>hammer.durationSeconds</c>.
        /// </summary>
        public double AddSurge(FluidSolve fs, DuctNetworkState net, double dv, double distance, double waveSpeed, double changeSeconds)
        {
            if (waveSpeed <= 0 || dv <= 0) return 0;
            double rho = fs.Result.Density.Length > 0 ? fs.Result.Density[0] : 1000;
            double dp = Hammer.Surge(rho, waveSpeed, dv, distance, changeSeconds);
            foreach (DuctNode n in net.Nodes)
                if (n.Kind == DuctNodeKind.Inline && _data.FindEquipment(n.PartId)?.Category == "water-accessory") { dp *= F.AccumulatorSurgeFactor; break; }
            double current = SurgePa(fs.Fluid);
            _surges[fs.Fluid] = (Math.Max(current, dp), F.HammerDurationSeconds);
            LastSurgePa = dp;
            return dp;
        }

        /// <summary>The last surge raised, Pa (the hover note "Surge: +3.1 bar").</summary>
        public double LastSurgePa { get; private set; }

        /// <summary>Pipe length from a link back to the supply (the nearest reflecting boundary), m.</summary>
        public static double DistanceToSource(FluidSolve fs, int link)
        {
            NetworkGraph g = fs.Compiled.Graph;
            int start = g.Links[link].From;
            var dist = new Dictionary<int, double> { [start] = 0 };
            var queue = new List<int> { start };
            double best = double.PositiveInfinity;
            for (int qi = 0; qi < queue.Count; qi++)
            {
                int u = queue[qi];
                if (g.Nodes[u].Kind == NodeKind.FixedPressure) best = Math.Min(best, dist[u]);
                for (int k = 0; k < g.Links.Count; k++)
                {
                    NetworkLink l = g.Links[k];
                    int v = l.From == u ? l.To : l.To == u ? l.From : -1;
                    if (v < 0 || dist.ContainsKey(v)) continue;
                    dist[v] = dist[u] + (l.Kind == LinkKind.Pipe ? l.Length : 0);
                    queue.Add(v);
                }
            }
            return double.IsInfinity(best) ? 0 : best;
        }

        /// <summary>
        /// Compares two solves of what is built and raises a surge for each sudden change (<c>05-simulation.md</c> §6): a
        /// shutoff closing, a pump starting or stopping, a check valve slamming shut.
        /// </summary>
        public void DetectSurges(NetworkSnapshot? before, NetworkSnapshot after)
        {
            if (before == null) return;
            foreach (FluidSolve fs in after.All)
            {
                if (fs.Fluid == "air") continue;
                FluidSolve? old = before.For(fs.Fluid);
                if (old == null) continue;
                DuctNetworkState net = after.State.Net(fs.Fluid);
                var byLabel = new Dictionary<string, int>();
                for (int k = 0; k < old.Compiled.Graph.Links.Count; k++) byLabel[old.Compiled.Graph.Links[k].Label] = k;
                for (int k = 0; k < fs.Compiled.Graph.Links.Count; k++)
                {
                    NetworkLink l = fs.Compiled.Graph.Links[k];
                    if (!byLabel.TryGetValue(l.Label, out int ok)) continue;
                    NetworkLink lo = old.Compiled.Graph.Links[ok];
                    double change;
                    if (l.Kind == LinkKind.Valve && l.Closed && !lo.Closed) change = F.ValveCloseSeconds;
                    else if (l.Kind == LinkKind.Fan && (l.Speed > 0) != (lo.Speed > 0)) change = F.PumpChangeSeconds;
                    else if (l.Kind == LinkKind.Check && fs.Result.Status[k] == LinkStatus.Closed && old.Result.Status[ok] != LinkStatus.Closed) change = F.CheckValveSeconds;
                    else continue;
                    double dv = Math.Abs(old.Result.Velocity[ok] - (fs.Result.LinkActive[k] ? fs.Result.Velocity[k] : 0));
                    double a = 0;
                    foreach (DuctSegment s in net.Segments) a = Math.Max(a, WaveSpeed(net, s));
                    AddSurge(fs, net, dv, DistanceToSource(old, ok), a, change);
                }
            }
        }

        // ---------- repairs ----------

        /// <summary>Standard repair price of a segment, $: per metre × (D/D_ref) × tier factor, at least the minimum × tier factor.</summary>
        public double RepairPrice(DuctSegment s, double metres, bool emergency)
        {
            PricesDef pr = _data.Balancing.Prices;
            bool duct = _data.Material(s.MaterialId).Kind == "duct";
            double dRef = duct ? pr.DuctReferenceDiameterMm : pr.PipeReferenceDiameterMm;
            double tier = _data.Balancing.Economy.TierFactor[_layout.Tier - 1];
            double standard = Math.Max(F.RepairPricePerMetre * metres * (s.DiameterMm / dRef) * tier, F.RepairMinPrice * tier);
            return emergency ? standard * F.RepairEmergencyFactor : standard;
        }

        /// <summary>Repair time for a crew, s (the rapid-response prestige bonus arrives in Phase 5).</summary>
        public double RepairSeconds(bool emergency) => emergency ? F.RepairEmergencySeconds : F.RepairStandardSeconds;

        /// <summary>The repair job for a burst edge, if one is booked.</summary>
        public RepairJob? JobFor(string fluid, string edge) => State.Repairs.Find(r => r.Fluid == fluid && r.Edge == edge);

        /// <summary>
        /// Books a crew for a burst segment, optionally fitting a new material or size in the same visit. The price is paid
        /// by the caller (<paramref name="pay"/>); emergency crews jump the queue.
        /// </summary>
        public bool StartRepair(string fluid, string edge, bool emergency, string upgradeMaterial, double upgradeDiameterMm,
            DuctNetworkState net, DuctSegment segment, Func<double, bool>? pay)
        {
            if (!State.Bursts.Exists(b => b.Fluid == fluid && b.Edge == edge) || JobFor(fluid, edge) != null) return false;
            double metres = DuctEditor.Tiles(net, segment).Count - 1;
            if (DuctEditor.IsRiser(net, segment)) metres *= _data.Balancing.Building.FloorHeight;
            double price = RepairPrice(segment, metres, emergency);
            if (pay != null && !pay(price)) return false;
            var job = new RepairJob
            {
                Fluid = fluid, Edge = edge, Emergency = emergency, Remaining = RepairSeconds(emergency), Total = RepairSeconds(emergency),
                UpgradeMaterial = upgradeMaterial, UpgradeDiameterMm = upgradeDiameterMm,
            };
            if (emergency)
            {
                int at = State.Repairs.FindIndex(r => !r.Emergency && !r.Started);
                if (at < 0) State.Repairs.Add(job); else State.Repairs.Insert(at, job);
            }
            else State.Repairs.Add(job);
            return true;
        }

        /// <summary>
        /// Works the crew queue (one crew per building; standard crews keep working offline). A finished repair removes the
        /// burst, resets the segment's stress and fits the upgrade in both the built state and the design. True when one
        /// finished.
        /// </summary>
        public bool TickRepairs(double dt, BuildingRecord record)
        {
            if (State.Repairs.Count == 0) return false;
            RepairJob job = State.Repairs[0];
            job.Started = true;
            job.Remaining -= dt;
            if (job.Remaining > 0) return false;
            State.Repairs.RemoveAt(0);
            State.Bursts.RemoveAll(b => b.Fluid == job.Fluid && b.Edge == job.Edge);
            foreach (BuildingState st in new[] { record.Committed, record.Design })
            {
                DuctNetworkState net = st.Net(job.Fluid);
                foreach (DuctSegment s in net.Segments)
                {
                    List<string> edges = Edges(job.Fluid, net, s);
                    if (!edges.Contains(job.Edge)) continue;
                    foreach (string e in edges)
                    {
                        State.Stress.Remove(e);
                        State.WeepAlerted.Remove(e);
                    }
                    if (job.UpgradeMaterial.Length > 0) s.MaterialId = job.UpgradeMaterial;
                    if (job.UpgradeDiameterMm > 0) s.DiameterMm = job.UpgradeDiameterMm;
                    break;
                }
                DuctEditor.Normalize(net);
            }
            Revision++;
            if (!OfflineMode) Alert?.Invoke(job.Emergency ? "Emergency crew finished the repair" : "Repair finished");
            return true;
        }

        /// <summary>
        /// After a Commit: tile edges whose pipe was replaced or newly laid start with no stress (<c>08-failures.md</c>:
        /// repairing or replacing a segment resets it to 0); edges no longer built are forgotten.
        /// </summary>
        public void OnCommit(BuildingState before, BuildingState after)
        {
            var oldSpecs = EdgeSpecs(before);
            var newSpecs = EdgeSpecs(after);
            foreach (string e in new List<string>(State.Stress.Keys))
                if (!newSpecs.TryGetValue(e, out string spec) || !oldSpecs.TryGetValue(e, out string was) || was != spec)
                {
                    State.Stress.Remove(e);
                    State.WeepAlerted.Remove(e);
                }
            // A burst whose pipe was redrawn or replaced is gone with it.
            State.Bursts.RemoveAll(b => !newSpecs.TryGetValue(b.Edge, out string spec) || !oldSpecs.TryGetValue(b.Edge, out string was) || was != spec);
            State.Repairs.RemoveAll(r => !State.Bursts.Exists(b => b.Fluid == r.Fluid && b.Edge == r.Edge));
            Revision++;
        }

        static Dictionary<string, string> EdgeSpecs(BuildingState s)
        {
            var d = new Dictionary<string, string>();
            foreach (DuctNetworkState net in s.Networks)
                foreach (DuctSegment seg in net.Segments)
                {
                    string spec = $"{seg.MaterialId}/{seg.DiameterMm}";
                    foreach (string e in Edges(net.FluidId, net, seg)) d[e] = spec;
                }
            return d;
        }
    }

    /// <summary>Water hammer (<c>05-simulation.md</c> §6).</summary>
    public static class Hammer
    {
        /// <summary>Joukowsky surge Δp = ρ a Δv, Pa.</summary>
        /// <param name="density">kg/m³.</param>
        /// <param name="waveSpeed">a, m/s.</param>
        /// <param name="dv">Velocity change, m/s.</param>
        public static double Joukowsky(double density, double waveSpeed, double dv) => density * waveSpeed * Math.Abs(dv);

        /// <summary>
        /// Surge for a change over <paramref name="changeSeconds"/>: the full Joukowsky value when faster than the wave's
        /// round trip 2L/a, else scaled by (2L/a)/t.
        /// </summary>
        /// <param name="distance">L, distance to the nearest reflecting boundary, m.</param>
        public static double Surge(double density, double waveSpeed, double dv, double distance, double changeSeconds)
        {
            double full = Joukowsky(density, waveSpeed, dv);
            double roundTrip = 2 * distance / waveSpeed;
            return changeSeconds > roundTrip && changeSeconds > 0 ? full * roundTrip / changeSeconds : full;
        }
    }
}
