using System.Collections.Generic;
using HeadLoss.Core.Data;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.Failures;

namespace HeadLoss.Game.Networks
{
    /// <summary>
    /// The fans and pumps of one building state that are slowing down or starting up (<c>05-simulation.md</c> §2), each at its
    /// own rate from <c>equipment.json</c>. Runtime only, never saved: a loaded building's devices are steady. A device is known
    /// by its <see cref="DamageKeys"/> key. Devices newly placed are steady from the start (the design previews them that way).
    /// </summary>
    public sealed class DeviceSpin
    {
        sealed class Move
        {
            public double Share;
            public bool Rising;
            public double RunDownSeconds, SpinUpSeconds;
        }

        readonly Dictionary<string, Move> _moves = new Dictionary<string, Move>();
        readonly List<string> _keys = new List<string>();
        /// <summary>Every device at the last look: true = should be running.</summary>
        Dictionary<string, bool> _last = new Dictionary<string, bool>(), _now = new Dictionary<string, bool>();

        /// <summary>Some device is changing speed.</summary>
        public bool Active => _moves.Count > 0;

        /// <summary>A device's share of full speed while changing speed, 0–1 (0 when it is steady).</summary>
        public double Share(string key) => _moves.TryGetValue(key, out Move m) ? m.Share : 0;

        /// <summary>True when the device is starting up rather than slowing down.</summary>
        public bool Rising(string key) => _moves.TryGetValue(key, out Move m) && m.Rising;

        /// <summary>
        /// Compares the state with the last look. A device that went from on to off starts slowing, from full speed; one that
        /// went from off to on starts up, from a stop; one already changing speed turns round from where it is.
        /// </summary>
        /// <param name="powered">The panel gives power (a tripped panel stops every powered device).</param>
        /// <param name="start">False: devices just switched count as already there (those changing speed carry on).</param>
        public void Sync(BuildingState state, DataRepository data, bool powered, bool start = true)
        {
            _now.Clear();
            foreach (EquipmentState e in state.Equipment)
                if (IsSpinDevice(data.FindEquipment(e.EquipmentId), out EquipmentDef? def)) Visit(DamageKeys.Opening(e.OpeningId), e.On, def!, powered, start, data);
            foreach (DuctNetworkState net in state.Networks)
                foreach (DuctNode n in net.Nodes)
                    if (n.Kind == DuctNodeKind.Inline && IsSpinDevice(data.FindEquipment(n.PartId), out EquipmentDef? def)) Visit(DamageKeys.Inline(net.FluidId, n.Pos), n.On, def!, powered, start, data);
            (_last, _now) = (_now, _last);
            _keys.Clear();
            _keys.AddRange(_moves.Keys);
            foreach (string key in _keys) if (!_last.ContainsKey(key)) _moves.Remove(key);
        }

        /// <summary>Takes the state as it is: everything is steady.</summary>
        public void Settle(BuildingState state, DataRepository data, bool powered)
        {
            Sync(state, data, powered, false);
            _moves.Clear();
        }

        /// <summary>Moves every device changing speed.</summary>
        /// <param name="dt">Seconds.</param>
        /// <param name="stopBelow">Below this share a device has stopped; a device starts up from it.</param>
        /// <returns>The largest share of speed any device moved.</returns>
        public double Advance(double dt, double stopBelow)
        {
            if (_moves.Count == 0) return 0;
            double moved = 0;
            _keys.Clear();
            _keys.AddRange(_moves.Keys);
            foreach (string key in _keys)
            {
                Move m = _moves[key];
                double share = dt / (m.Rising ? m.SpinUpSeconds : m.RunDownSeconds);
                if (share > moved) moved = share;
                m.Share += m.Rising ? share : -share;
                if (m.Rising ? m.Share >= 1 : m.Share < stopBelow) _moves.Remove(key);
            }
            return moved;
        }

        /// <summary>A private copy for a solve that may run on a worker thread (null when everything is steady).</summary>
        public IReadOnlyDictionary<string, double>? Snapshot()
        {
            if (_moves.Count == 0) return null;
            var copy = new Dictionary<string, double>(_moves.Count);
            foreach (var kv in _moves) copy[kv.Key] = kv.Value.Share;
            return copy;
        }

        void Visit(string key, bool on, EquipmentDef def, bool powered, bool start, DataRepository data)
        {
            bool run = on && (powered || def.PowerModel == "none");
            _now[key] = run;
            if (_moves.TryGetValue(key, out Move m))
            {
                m.Rising = run;
                return;
            }
            if (!start || !_last.TryGetValue(key, out bool wasRunning) || wasRunning == run) return;
            _moves[key] = new Move
            {
                Share = run ? data.Balancing.Devices.SpinResolveStep : 1,
                Rising = run,
                RunDownSeconds = def.RunDownSeconds,
                SpinUpSeconds = def.SpinUpSeconds,
            };
        }

        /// <summary>Fans and pumps with a switch; the crank goes by its own decay.</summary>
        static bool IsSpinDevice(EquipmentDef? candidate, out EquipmentDef? def)
        {
            def = candidate;
            return def != null && def.Curve.Count > 0 && def.SpeedControl != "crank";
        }
    }
}
