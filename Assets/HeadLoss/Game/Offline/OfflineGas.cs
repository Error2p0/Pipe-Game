using System;
using System.Collections.Generic;
using HeadLoss.Core;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.Codes;
using HeadLoss.Game.Failures;
using HeadLoss.Game.Session;
using HeadLoss.Sim.Network;

namespace HeadLoss.Game.Offline
{
    /// <summary>What stops the offline timeline for a building's gas.</summary>
    public enum OfflineGasStopKind { None, FittingLeak, Alarm, Clear, CertificateExpires, CertificateCheck }

    public readonly struct OfflineGasStop
    {
        public readonly OfflineGasStopKind Kind;
        /// <summary>Time to the stop, s.</summary>
        public readonly double Seconds;

        public OfflineGasStop(OfflineGasStopKind kind, double seconds)
        {
            Kind = kind;
            Seconds = seconds;
        }
    }

    /// <summary>
    /// A building's gas while the player is away (<c>12-save-offline.md</c> §Offline catch-up, step 4): the room grid is not
    /// stepped; each leaking room's upper layer is one well-mixed value (light gas collects there), filled by its leaks and
    /// cleared by its vents and passive vents. That is enough to reach the alarm stage. The explosion timer stays paused;
    /// on return the estimate is carried into the grid.
    /// </summary>
    public sealed class OfflineGas
    {
        readonly BuildingSession _s;
        readonly Dictionary<int, double> _rooms = new Dictionary<int, double>();
        readonly GasDef _g;
        readonly double _lfl;

        public OfflineGas(BuildingSession session)
        {
            _s = session;
            _g = session.Data.Balancing.Gas;
            _lfl = session.GasSafety.Lfl;
            // Rooms that already hold gas keep it.
            for (int r = 0; r < session.Rooms.Rooms.Length; r++)
                if (session.Rooms.Rooms[r].GasMax > 0) _rooms[r] = session.Rooms.Rooms[r].GasMax;
        }

        double RoomVolume(int room)
        {
            RoomDef r = _s.Layout.Rooms[room];
            return r.Width * r.Height * (_s.Rooms.Params.UpperHeight);
        }

        /// <summary>Fresh air into a room, m³/s: its vents' flow and its passive vents at wind factor 1.</summary>
        double FreshFlow(NetworkSnapshot snap, int room)
        {
            double ach = CodeChecker.AirChanges(_s.Data, _s.Layout, _s.Committed, snap, room);
            RoomDef r = _s.Layout.Rooms[room];
            return ach * (r.Width * r.Height * _s.Data.Balancing.RoomGrid.RoomHeight) / 3600;
        }

        /// <summary>Gas flowing into each room now, m³/s (only leaks the supply still reaches).</summary>
        Dictionary<int, double> LeakFlows(NetworkSnapshot snap)
        {
            var flows = new Dictionary<int, double>();
            FluidSolve? gas = snap.Gas;
            if (gas == null) return flows;
            foreach (LeakState l in _s.Record.Failures.Leaks)
            {
                if (!gas.Compiled.LeakLinks.TryGetValue(l.Key, out int link) || !gas.Result.LinkActive[link]) continue;
                int room = _s.Layout.RoomIndexAt(l.Pos);
                if (room < 0) continue;
                flows[room] = (flows.TryGetValue(room, out double q) ? q : 0) + Math.Max(0, gas.Result.Flow[link]);
                if (!_rooms.ContainsKey(room)) _rooms[room] = 0;
            }
            return flows;
        }

        /// <summary>The alarm threshold in a room: 20% of LFL with a detector there, else 50%.</summary>
        double Threshold(int room)
        {
            foreach (SafetyDevice d in _s.Committed.Safety)
                if (d.EquipmentId == GasSafetyIds.Detector && _s.Layout.RoomIndexAt(d.Pos) == room) return _g.AlarmDetectorFraction * _lfl;
            return _g.AlarmAnywhereFraction * _lfl;
        }

        /// <summary>The next gas event within <paramref name="horizon"/> seconds, if any.</summary>
        public OfflineGasStop NextStop(NetworkSnapshot snap, double horizon, BuildingFailures f)
        {
            var best = new OfflineGasStop(OfflineGasStopKind.None, horizon);
            void Consider(OfflineGasStopKind kind, double seconds)
            {
                if (seconds < best.Seconds) best = new OfflineGasStop(kind, Math.Max(0, seconds));
            }
            GasCertificateState cert = _s.Record.Failures.Certificate;
            if (cert.Issued && cert.SecondsLeft > 0) Consider(OfflineGasStopKind.CertificateExpires, cert.SecondsLeft);
            if (cert.CheckLeft > 0) Consider(OfflineGasStopKind.CertificateCheck, cert.CheckLeft);

            // A random fitting leak: the Poisson process is memoryless, so drawing again at every stop stays correct.
            if (_s.GasSupply.Connected)
            {
                int fittings = GasSafety.Fittings(_s.Committed.Gas).Count;
                double rate = _s.GasSafety.FittingLeakRatePerSecond(fittings);
                if (rate > 0) Consider(OfflineGasStopKind.FittingLeak, -Math.Log(Math.Max(1e-12, f.Roll())) / rate);
            }

            Dictionary<int, double> flows = LeakFlows(snap);
            if (!_s.GasSafety.Evacuated)
            {
                foreach (var kv in _rooms)
                {
                    double leak = flows.TryGetValue(kv.Key, out double q) ? q : 0;
                    double t = GasSafety.TimeToReach(kv.Value, Threshold(kv.Key), leak, FreshFlow(snap, kv.Key), RoomVolume(kv.Key));
                    if (!double.IsInfinity(t)) Consider(OfflineGasStopKind.Alarm, t);
                }
            }
            else if (_s.Record.Failures.Leaks.Count == 0)
            {
                // Everything repaired: the alarm ends when the rooms have cleared.
                double target = _g.ClearFraction * _lfl, longest = 0;
                foreach (var kv in _rooms)
                {
                    if (kv.Value <= target) continue;
                    double q = FreshFlow(snap, kv.Key);
                    longest = q > 0 ? Math.Max(longest, RoomVolume(kv.Key) / q * Math.Log(kv.Value / target)) : double.PositiveInfinity;
                }
                if (!double.IsInfinity(longest)) Consider(OfflineGasStopKind.Clear, longest);
            }
            return best;
        }

        /// <summary>Advances the rooms and acts on the stop reached. True when the building's state changed (re-solve it).</summary>
        public bool Advance(NetworkSnapshot snap, double seconds, OfflineGasStop stop, double now, OfflineBuildingLine line, OfflineReport report)
        {
            Dictionary<int, double> flows = LeakFlows(snap);
            var keys = new List<int>(_rooms.Keys);
            foreach (int room in keys)
                _rooms[room] = GasSafety.WellMixed(_rooms[room], flows.TryGetValue(room, out double q) ? q : 0, FreshFlow(snap, room), RoomVolume(room), seconds);
            bool wasExpired = _s.GasSafety.CertificateExpired;
            _s.GasSafety.TickCertificate(seconds);
            bool changed = wasExpired != _s.GasSafety.CertificateExpired;
            if (changed && !wasExpired) report.CertificatesExpired++;
            switch (stop.Kind)
            {
                case OfflineGasStopKind.FittingLeak:
                    List<DuctNode> fittings = GasSafety.Fittings(_s.Committed.Gas);
                    if (fittings.Count > 0)
                    {
                        DuctNode node = fittings[Math.Min(fittings.Count - 1, (int)(_s.Failures.Roll() * fittings.Count))];
                        if (_s.GasSafety.StartFittingLeak(node) != null)
                        {
                            line.Leaks++;
                            report.Leaks++;
                            changed = true;
                        }
                    }
                    break;
                case OfflineGasStopKind.Alarm:
                    _s.GasSafety.RaiseAlarm();
                    line.Alarms++;
                    report.Alarms++;
                    changed = true;
                    break;
                case OfflineGasStopKind.Clear:
                    _s.GasSafety.ClearAfterAway();
                    foreach (int room in keys) _rooms[room] = 0;
                    changed = true;
                    break;
            }
            return changed;
        }

        /// <summary>Back in game: the well-mixed estimate becomes the grid's upper layers, and the full grid takes over.</summary>
        public void CarryIntoGrid()
        {
            foreach (var kv in _rooms) _s.Rooms.SeedRoomGas(kv.Key, kv.Value);
        }
    }
}
