using System.Collections.Generic;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.Session;

namespace HeadLoss.Game.Commands
{
    /// <summary>A player action. Only commands mutate game state (<c>01-architecture.md</c> §Layers).</summary>
    public interface ICommand
    {
        string Description { get; }
        /// <returns>False when the command did not apply (nothing changed).</returns>
        bool Execute(BuildingSession session);
    }

    /// <summary>
    /// Runs commands and keeps their history. Undo/redo is Phase 2 (<c>14-roadmap.md</c>); history is
    /// cleared when leaving the building.
    /// </summary>
    public sealed class CommandStack
    {
        readonly List<ICommand> _done = new List<ICommand>();
        public IReadOnlyList<ICommand> History => _done;

        public bool Execute(ICommand command, BuildingSession session)
        {
            if (!command.Execute(session)) return false;
            _done.Add(command);
            session.MarkDirty();
            return true;
        }

        public void Clear() => _done.Clear();
    }

    public sealed class PlaceRunCommand : ICommand
    {
        readonly List<TilePos> _path;
        readonly double _diameterMm;
        readonly string _materialId;
        readonly bool _smoothElbows;

        /// <param name="diameterMm">mm.</param>
        public PlaceRunCommand(IReadOnlyList<TilePos> path, double diameterMm, string materialId, bool smoothElbows)
        {
            _path = new List<TilePos>(path);
            _diameterMm = diameterMm;
            _materialId = materialId;
            _smoothElbows = smoothElbows;
        }

        public string Description => $"Draw duct ({_path.Count - 1} m)";
        public DrawError LastError { get; private set; }

        public bool Execute(BuildingSession s)
        {
            LastError = DuctEditor.Validate(s.State.Air, s.Layout, _path, s.Data.Balancing.Drawing.MaxNodeDegree);
            if (LastError != DrawError.None) return false;
            DuctEditor.PlaceRun(s.State.Air, _path, _diameterMm, _materialId, _smoothElbows);
            return true;
        }
    }

    public sealed class DeleteSegmentCommand : ICommand
    {
        readonly List<int> _segmentIds;
        public DeleteSegmentCommand(int segmentId) => _segmentIds = new List<int> { segmentId };
        public DeleteSegmentCommand(IEnumerable<int> segmentIds) => _segmentIds = new List<int>(segmentIds);
        public string Description => _segmentIds.Count == 1 ? "Delete duct" : $"Delete {_segmentIds.Count} ducts";
        public bool Execute(BuildingSession s) => DuctEditor.DeleteSegments(s.State.Air, _segmentIds);
    }

    public sealed class SetSegmentSizeCommand : ICommand
    {
        readonly List<int> _segmentIds;
        readonly double _diameterMm;

        /// <param name="diameterMm">mm.</param>
        public SetSegmentSizeCommand(int segmentId, double diameterMm) : this(new[] { segmentId }, diameterMm) { }

        /// <param name="diameterMm">mm.</param>
        public SetSegmentSizeCommand(IEnumerable<int> segmentIds, double diameterMm)
        {
            _segmentIds = new List<int>(segmentIds);
            _diameterMm = diameterMm;
        }

        public string Description => $"Change duct size to {_diameterMm} mm";
        public bool Execute(BuildingSession s) => DuctEditor.SetSize(s.State.Air, _segmentIds, _diameterMm);
    }

    /// <summary>Switches one elbow between sharp and long-radius.</summary>
    public sealed class SetElbowCommand : ICommand
    {
        readonly int _nodeId;
        readonly bool _smooth;

        public SetElbowCommand(int nodeId, bool smooth)
        {
            _nodeId = nodeId;
            _smooth = smooth;
        }

        public string Description => _smooth ? "Long-radius elbow" : "Sharp elbow";

        public bool Execute(BuildingSession s)
        {
            DuctNode? n = s.State.Air.FindNode(_nodeId);
            if (n == null || !DuctEditor.IsElbow(s.State.Air, n) || n.SmoothFittings == _smooth) return false;
            n.SmoothFittings = _smooth;
            return true;
        }
    }

    /// <summary>"Upgrade all bends": every elbow of the network becomes long-radius.</summary>
    public sealed class UpgradeAllBendsCommand : ICommand
    {
        public string Description => "Upgrade all bends";

        public bool Execute(BuildingSession s)
        {
            bool any = false;
            foreach (DuctNode n in s.State.Air.Nodes)
            {
                if (n.SmoothFittings || !DuctEditor.IsElbow(s.State.Air, n)) continue;
                n.SmoothFittings = true;
                any = true;
            }
            return any;
        }
    }

    public sealed class SetEquipmentOnCommand : ICommand
    {
        readonly string _openingId;
        readonly bool _on;

        public SetEquipmentOnCommand(string openingId, bool on)
        {
            _openingId = openingId;
            _on = on;
        }

        public string Description => _on ? "Switch on" : "Switch off";

        public bool Execute(BuildingSession s)
        {
            EquipmentState? e = s.State.EquipmentAt(_openingId);
            if (e == null || e.On == _on) return false;
            e.On = _on;
            return true;
        }
    }

    /// <summary>Sets a device speed fraction (dev overlay in Phase 1; VSDs later).</summary>
    public sealed class SetEquipmentSpeedCommand : ICommand
    {
        readonly string _openingId;
        readonly double _speed;

        public SetEquipmentSpeedCommand(string openingId, double speed)
        {
            _openingId = openingId;
            _speed = speed;
        }

        public string Description => $"Speed {_speed:P0}";

        public bool Execute(BuildingSession s)
        {
            EquipmentState? e = s.State.EquipmentAt(_openingId);
            double v = System.Math.Max(0.2, System.Math.Min(1.0, _speed));
            if (e == null || e.Speed == v) return false;
            e.Speed = v;
            return true;
        }
    }
}
