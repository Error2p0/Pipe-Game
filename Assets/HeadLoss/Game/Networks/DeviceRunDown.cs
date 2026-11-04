using System.Collections.Generic;
using HeadLoss.Core.Data;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.Failures;

namespace HeadLoss.Game.Networks
{
    /// <summary>
    /// The fans and pumps of one building state that were switched off and have not stopped yet (<c>05-simulation.md</c> §2).
    /// Runtime only, never saved: a loaded building's switched-off devices have long stopped. A device is known by its
    /// <see cref="DamageKeys"/> key. Switching back on is immediate.
    /// </summary>
    public sealed class DeviceRunDown
    {
        readonly Dictionary<string, double> _left = new Dictionary<string, double>();
        readonly List<string> _keys = new List<string>();
        HashSet<string> _running = new HashSet<string>();
        HashSet<string> _runningNow = new HashSet<string>();
        readonly HashSet<string> _present = new HashSet<string>();

        /// <summary>Some device is still slowing down.</summary>
        public bool Active => _left.Count > 0;

        /// <summary>Share of its speed a device has left, 0–1 (0 when it is not running down).</summary>
        public double Left(string key) => _left.TryGetValue(key, out double f) ? f : 0;

        /// <summary>Compares the state with the last look: devices that went from on to off start running down, from their full share.</summary>
        /// <param name="start">False: devices that went off count as already stopped (those running down carry on).</param>
        public void Sync(BuildingState state, DataRepository data, bool start = true)
        {
            _runningNow.Clear();
            _present.Clear();
            foreach (EquipmentState e in state.Equipment)
                if (IsRunDownDevice(data.FindEquipment(e.EquipmentId))) Visit(DamageKeys.Opening(e.OpeningId), e.On, start);
            foreach (DuctNetworkState net in state.Networks)
                foreach (DuctNode n in net.Nodes)
                    if (n.Kind == DuctNodeKind.Inline && IsRunDownDevice(data.FindEquipment(n.PartId))) Visit(DamageKeys.Inline(net.FluidId, n.Pos), n.On, start);
            (_running, _runningNow) = (_runningNow, _running);
            _keys.Clear();
            _keys.AddRange(_left.Keys);
            foreach (string key in _keys) if (!_present.Contains(key)) _left.Remove(key);
        }

        /// <summary>Takes the state as it is: nothing is running down.</summary>
        public void Settle(BuildingState state, DataRepository data)
        {
            Sync(state, data, false);
            _left.Clear();
        }

        /// <summary>Slows every running-down device.</summary>
        /// <param name="share">Share of full speed to take off.</param>
        /// <param name="stopBelow">Below this share a device has stopped.</param>
        public void Advance(double share, double stopBelow)
        {
            if (_left.Count == 0) return;
            _keys.Clear();
            _keys.AddRange(_left.Keys);
            foreach (string key in _keys)
            {
                double f = _left[key] - share;
                if (f < stopBelow) _left.Remove(key);
                else _left[key] = f;
            }
        }

        /// <summary>A private copy for a solve that may run on a worker thread (null when nothing is running down).</summary>
        public IReadOnlyDictionary<string, double>? Snapshot() => _left.Count > 0 ? new Dictionary<string, double>(_left) : null;

        void Visit(string key, bool on, bool start)
        {
            _present.Add(key);
            if (on)
            {
                _runningNow.Add(key);
                _left.Remove(key);
            }
            else if (start && _running.Contains(key)) _left[key] = 1;
        }

        /// <summary>Fans and pumps with a switch; the crank slows by its own decay.</summary>
        static bool IsRunDownDevice(EquipmentDef? def) => def != null && def.Curve.Count > 0 && def.SpeedControl != "crank";
    }
}
