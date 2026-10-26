using System.Collections.Generic;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.Session;

namespace HeadLoss.Game.Commands
{
    /// <summary>A player action. Only commands mutate game state (<c>01-architecture.md</c> §Layers).</summary>
    public interface ICommand
    {
        string Description { get; }
        /// <summary>False for actions that spend money (Commit): they run but never enter the undo history.</summary>
        bool Undoable { get; }
        /// <returns>False when the command did not apply (nothing changed).</returns>
        bool Execute(BuildingSession session);
        /// <summary>Reverts a successful <see cref="Execute"/>.</summary>
        void Undo(BuildingSession session);
        /// <summary>
        /// Folds <paramref name="next"/> (already executed) into this command when both are steps of one gesture
        /// (a slider drag), so one undo reverts the whole gesture.
        /// </summary>
        bool TryMerge(ICommand next);
    }

    /// <summary>
    /// Runs commands and keeps the undo/redo history (<c>11-ui-visuals-audio.md</c>: unlimited in design mode,
    /// cleared when leaving the building).
    /// </summary>
    public sealed class CommandStack
    {
        readonly List<ICommand> _done = new List<ICommand>();
        readonly List<ICommand> _undone = new List<ICommand>();
        public IReadOnlyList<ICommand> History => _done;
        public bool CanUndo => _done.Count > 0;
        public bool CanRedo => _undone.Count > 0;
        public string UndoDescription => _done.Count > 0 ? _done[_done.Count - 1].Description : "";
        public string RedoDescription => _undone.Count > 0 ? _undone[_undone.Count - 1].Description : "";

        public bool Execute(ICommand command, BuildingSession session)
        {
            if (!command.Execute(session)) return false;
            if (command.Undoable)
            {
                if (_done.Count == 0 || !_done[_done.Count - 1].TryMerge(command)) _done.Add(command);
                _undone.Clear();
            }
            session.MarkDirty();
            return true;
        }

        public bool Undo(BuildingSession session)
        {
            if (_done.Count == 0) return false;
            ICommand c = _done[_done.Count - 1];
            _done.RemoveAt(_done.Count - 1);
            c.Undo(session);
            _undone.Add(c);
            session.MarkDirty();
            return true;
        }

        public bool Redo(BuildingSession session)
        {
            if (_undone.Count == 0) return false;
            ICommand c = _undone[_undone.Count - 1];
            _undone.RemoveAt(_undone.Count - 1);
            if (c.Execute(session)) _done.Add(c);
            session.MarkDirty();
            return true;
        }

        public void Clear()
        {
            _done.Clear();
            _undone.Clear();
        }
    }

    /// <summary>
    /// A change to the design draft. Undo restores a snapshot of the draft taken before the change (states are
    /// small), so every design edit undoes exactly, whatever it touched.
    /// </summary>
    public abstract class DesignCommand : ICommand
    {
        BuildingState? _before;

        public abstract string Description { get; }
        public bool Undoable => true;
        protected abstract bool Apply(BuildingSession s);

        public bool Execute(BuildingSession s)
        {
            BuildingState before = s.State.Clone();
            if (!Apply(s)) return false;
            _before = before;
            return true;
        }

        public void Undo(BuildingSession s)
        {
            if (_before != null) s.Record.Design = _before.Clone();
        }

        public virtual bool TryMerge(ICommand next) => false;
    }

    public sealed class PlaceRunCommand : DesignCommand
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

        public override string Description => $"Draw duct ({_path.Count - 1} m)";
        public DrawError LastError { get; private set; }

        protected override bool Apply(BuildingSession s)
        {
            LastError = DuctEditor.Validate(s.State.Air, s.Layout, _path, s.Data.Balancing.Drawing.MaxNodeDegree);
            if (LastError != DrawError.None) return false;
            DuctEditor.PlaceRun(s.State.Air, _path, _diameterMm, _materialId, _smoothElbows);
            return true;
        }
    }

    public sealed class DeleteSegmentCommand : DesignCommand
    {
        readonly List<int> _segmentIds;
        public DeleteSegmentCommand(int segmentId) => _segmentIds = new List<int> { segmentId };
        public DeleteSegmentCommand(IEnumerable<int> segmentIds) => _segmentIds = new List<int>(segmentIds);
        public override string Description => _segmentIds.Count == 1 ? "Delete duct" : $"Delete {_segmentIds.Count} ducts";
        protected override bool Apply(BuildingSession s) => DuctEditor.DeleteSegments(s.State.Air, _segmentIds);
    }

    public sealed class SetSegmentSizeCommand : DesignCommand
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

        public override string Description => $"Change duct size to {_diameterMm} mm";
        protected override bool Apply(BuildingSession s) => DuctEditor.SetSize(s.State.Air, _segmentIds, _diameterMm);
    }

    /// <summary>Switches one elbow between sharp and long-radius.</summary>
    public sealed class SetElbowCommand : DesignCommand
    {
        readonly int _nodeId;
        readonly bool _smooth;

        public SetElbowCommand(int nodeId, bool smooth)
        {
            _nodeId = nodeId;
            _smooth = smooth;
        }

        public override string Description => _smooth ? "Long-radius elbow" : "Sharp elbow";

        protected override bool Apply(BuildingSession s)
        {
            DuctNode? n = s.State.Air.FindNode(_nodeId);
            if (n == null || !DuctEditor.IsElbow(s.State.Air, n) || n.SmoothFittings == _smooth) return false;
            n.SmoothFittings = _smooth;
            return true;
        }
    }

    /// <summary>"Upgrade all bends": every elbow of the network becomes long-radius.</summary>
    public sealed class UpgradeAllBendsCommand : DesignCommand
    {
        public override string Description => "Upgrade all bends";

        protected override bool Apply(BuildingSession s)
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

    /// <summary>Places, replaces or removes (empty id) the source equipment at an entry opening.</summary>
    public sealed class SetIntakeEquipmentCommand : DesignCommand
    {
        readonly string _openingId;
        readonly string _equipmentId;

        public SetIntakeEquipmentCommand(string openingId, string equipmentId)
        {
            _openingId = openingId;
            _equipmentId = equipmentId;
        }

        public override string Description => _equipmentId.Length == 0 ? "Remove equipment" : "Place equipment";

        protected override bool Apply(BuildingSession s)
        {
            if (s.Layout.FindOpening(_openingId) == null) return false;
            EquipmentState? e = s.State.EquipmentAt(_openingId);
            if (_equipmentId.Length == 0)
            {
                if (e == null) return false;
                s.State.Equipment.Remove(e);
                return true;
            }
            EquipmentDef? def = s.Data.FindEquipment(_equipmentId);
            if (def == null || def.Placement != "intake") return false;
            if (e != null && e.EquipmentId == _equipmentId) return false;
            if (e != null) s.State.Equipment.Remove(e);
            s.State.Equipment.Add(new EquipmentState { EquipmentId = _equipmentId, OpeningId = _openingId, On = true });
            return true;
        }
    }

    /// <summary>Places an inline part (damper or inline fan) on a straight duct.</summary>
    public sealed class PlaceInlineCommand : DesignCommand
    {
        readonly TilePos _tile;
        readonly string _partId;
        readonly bool _reverse;

        /// <param name="reverse">Point a fan against the default (away from the intake) direction.</param>
        public PlaceInlineCommand(TilePos tile, string partId, bool reverse = false)
        {
            _tile = tile;
            _partId = partId;
            _reverse = reverse;
        }

        public override string Description => "Place " + _partId;
        public DrawError LastError { get; private set; }

        protected override bool Apply(BuildingSession s)
        {
            LastError = DuctEditor.ValidateInline(s.State.Air, _tile);
            if (LastError != DrawError.None) return false;
            (int dx, int dy) = DuctEditor.DefaultInlineDirection(s.State.Air, _tile);
            if (_reverse) (dx, dy) = (-dx, -dy);
            DuctEditor.PlaceInline(s.State.Air, _tile, _partId, dx, dy);
            return true;
        }
    }

    public sealed class RemoveInlineCommand : DesignCommand
    {
        readonly int _nodeId;
        public RemoveInlineCommand(int nodeId) => _nodeId = nodeId;
        public override string Description => "Remove part";
        protected override bool Apply(BuildingSession s) => DuctEditor.RemoveInline(s.State.Air, _nodeId);
    }

    /// <summary>Turns an inline fan around.</summary>
    public sealed class FlipInlineCommand : DesignCommand
    {
        readonly int _nodeId;
        public FlipInlineCommand(int nodeId) => _nodeId = nodeId;
        public override string Description => "Turn fan around";

        protected override bool Apply(BuildingSession s)
        {
            DuctNode? n = s.State.Air.FindNode(_nodeId);
            if (n == null || n.Kind != DuctNodeKind.Inline) return false;
            n.DirX = -n.DirX;
            n.DirY = -n.DirY;
            return true;
        }
    }

    /// <summary>Puts the design back to the committed state (undoable, so a Discard can be taken back).</summary>
    public sealed class DiscardDesignCommand : DesignCommand
    {
        public override string Description => "Discard design";

        protected override bool Apply(BuildingSession s)
        {
            if (!s.HasPendingDesign) return false;
            s.Record.Design = s.Record.Committed.Clone();
            return true;
        }
    }

    /// <summary>Pays for the design and makes it the committed, earning state. Not undoable (money changes hands).</summary>
    public sealed class CommitDesignCommand : ICommand
    {
        public string Description => "Commit design";
        public bool Undoable => false;
        public bool Execute(BuildingSession s) => s.Commit();
        public void Undo(BuildingSession s) { }
        public bool TryMerge(ICommand next) => false;
    }

    /// <summary>
    /// A free operating setting (on/off, damper position). It applies at once to the design and, when the same
    /// part is already built, to the committed state too, so income responds live while balancing.
    /// </summary>
    public abstract class OperationalCommand : ICommand
    {
        public abstract string Description { get; }
        public bool Undoable => true;
        public abstract bool Execute(BuildingSession session);
        public abstract void Undo(BuildingSession session);
        public virtual bool TryMerge(ICommand next) => false;
    }

    public sealed class SetEquipmentOnCommand : OperationalCommand
    {
        readonly string _openingId;
        readonly bool _on;
        bool _wasDesign, _wasCommitted;
        bool _hadCommitted;

        public SetEquipmentOnCommand(string openingId, bool on)
        {
            _openingId = openingId;
            _on = on;
        }

        public override string Description => _on ? "Switch on" : "Switch off";

        public override bool Execute(BuildingSession s)
        {
            EquipmentState? e = s.State.EquipmentAt(_openingId);
            if (e == null || e.On == _on) return false;
            EquipmentState? c = s.CommittedTwin(e);
            _wasDesign = e.On;
            e.On = _on;
            _hadCommitted = c != null;
            if (c != null)
            {
                _wasCommitted = c.On;
                c.On = _on;
            }
            return true;
        }

        public override void Undo(BuildingSession s)
        {
            EquipmentState? e = s.State.EquipmentAt(_openingId);
            if (e != null) e.On = _wasDesign;
            if (_hadCommitted && s.Committed.EquipmentAt(_openingId) is EquipmentState c) c.On = _wasCommitted;
        }
    }

    /// <summary>Sets a device speed fraction (dev overlay; variable-speed drives arrive in Phase 3).</summary>
    public sealed class SetEquipmentSpeedCommand : OperationalCommand
    {
        readonly string _openingId;
        readonly double _speed;
        double _wasDesign, _wasCommitted;
        bool _hadCommitted;

        public SetEquipmentSpeedCommand(string openingId, double speed)
        {
            _openingId = openingId;
            _speed = speed;
        }

        public override string Description => $"Speed {_speed:P0}";

        public override bool Execute(BuildingSession s)
        {
            EquipmentState? e = s.State.EquipmentAt(_openingId);
            double v = System.Math.Max(0.2, System.Math.Min(1.0, _speed));
            if (e == null || e.Speed == v) return false;
            EquipmentState? c = s.CommittedTwin(e);
            _wasDesign = e.Speed;
            e.Speed = v;
            _hadCommitted = c != null;
            if (c != null)
            {
                _wasCommitted = c.Speed;
                c.Speed = v;
            }
            return true;
        }

        public override void Undo(BuildingSession s)
        {
            EquipmentState? e = s.State.EquipmentAt(_openingId);
            if (e != null) e.Speed = _wasDesign;
            if (_hadCommitted && s.Committed.EquipmentAt(_openingId) is EquipmentState c) c.Speed = _wasCommitted;
        }
    }

    /// <summary>Switches an inline fan on or off.</summary>
    public sealed class SetInlineOnCommand : OperationalCommand
    {
        readonly TilePos _tile;
        readonly bool _on;
        bool _wasDesign, _wasCommitted, _hadCommitted;

        public SetInlineOnCommand(TilePos tile, bool on)
        {
            _tile = tile;
            _on = on;
        }

        public override string Description => _on ? "Switch on" : "Switch off";

        public override bool Execute(BuildingSession s)
        {
            DuctNode? n = s.State.Air.NodeAt(_tile);
            if (n == null || n.Kind != DuctNodeKind.Inline || n.On == _on) return false;
            DuctNode? c = s.CommittedTwin(n);
            _wasDesign = n.On;
            n.On = _on;
            _hadCommitted = c != null;
            if (c != null)
            {
                _wasCommitted = c.On;
                c.On = _on;
            }
            return true;
        }

        public override void Undo(BuildingSession s)
        {
            if (s.State.Air.NodeAt(_tile) is DuctNode n) n.On = _wasDesign;
            if (_hadCommitted && s.Committed.Air.NodeAt(_tile) is DuctNode c) c.On = _wasCommitted;
        }
    }

    /// <summary>Sets a damper's opening θ. Consecutive moves of the same damper merge into one undo step.</summary>
    public sealed class SetDamperOpeningCommand : OperationalCommand
    {
        readonly TilePos _tile;
        double _opening;
        double _wasDesign, _wasCommitted;
        bool _hadCommitted;

        /// <param name="opening">θ, clamped to the damper's minimum opening–1.</param>
        public SetDamperOpeningCommand(TilePos tile, double opening)
        {
            _tile = tile;
            _opening = opening;
        }

        public override string Description => $"Damper {_opening:P0} open";

        public override bool Execute(BuildingSession s)
        {
            DuctNode? n = s.State.Air.NodeAt(_tile);
            if (n == null || n.Kind != DuctNodeKind.Inline) return false;
            FittingDef? f = s.Data.FindFitting(n.PartId);
            if (f == null || f.Model != "valve") return false;
            double v = System.Math.Max(f.MinOpening, System.Math.Min(1.0, _opening));
            if (System.Math.Abs(n.Opening - v) < 1e-12) return false;
            _opening = v;
            DuctNode? c = s.CommittedTwin(n);
            _wasDesign = n.Opening;
            n.Opening = v;
            _hadCommitted = c != null;
            if (c != null)
            {
                _wasCommitted = c.Opening;
                c.Opening = v;
            }
            return true;
        }

        public override void Undo(BuildingSession s)
        {
            if (s.State.Air.NodeAt(_tile) is DuctNode n) n.Opening = _wasDesign;
            if (_hadCommitted && s.Committed.Air.NodeAt(_tile) is DuctNode c) c.Opening = _wasCommitted;
        }

        public override bool TryMerge(ICommand next)
        {
            if (!(next is SetDamperOpeningCommand d) || d._tile != _tile || d._hadCommitted != _hadCommitted) return false;
            _opening = d._opening;
            return true;
        }
    }
}
