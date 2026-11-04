using System.Collections.Generic;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.Progression;
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
        /// <summary>Raised after a command applied (sound, autosave hooks).</summary>
        public event System.Action<ICommand>? Executed;

        public bool Execute(ICommand command, BuildingSession session)
        {
            if (!command.Execute(session)) return false;
            Executed?.Invoke(command);
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
        readonly string _fluid;

        /// <param name="diameterMm">mm.</param>
        /// <param name="fluid">Network to draw on (air, water).</param>
        public PlaceRunCommand(IReadOnlyList<TilePos> path, double diameterMm, string materialId, bool smoothElbows, string fluid = "air")
        {
            _fluid = fluid;
            _path = new List<TilePos>(path);
            _diameterMm = diameterMm;
            _materialId = materialId;
            _smoothElbows = smoothElbows;
        }

        public override string Description => _fluid == "air" ? $"Draw duct ({_path.Count - 1} m)" : $"Draw pipe ({_path.Count - 1} m)";
        public DrawError LastError { get; private set; }

        protected override bool Apply(BuildingSession s)
        {
            LastError = s.ValidateRun(_fluid, _path, _diameterMm, _materialId, _smoothElbows);
            if (LastError != DrawError.None) return false;
            DuctEditor.PlaceRun(s.State.Net(_fluid), _path, _diameterMm, _materialId, _smoothElbows);
            return true;
        }
    }

    public sealed class DeleteSegmentCommand : DesignCommand
    {
        readonly List<int> _segmentIds;
        readonly string _fluid;

        public DeleteSegmentCommand(int segmentId, string fluid = "air")
        {
            _segmentIds = new List<int> { segmentId };
            _fluid = fluid;
        }

        public DeleteSegmentCommand(IEnumerable<int> segmentIds, string fluid = "air")
        {
            _segmentIds = new List<int>(segmentIds);
            _fluid = fluid;
        }

        public override string Description => _segmentIds.Count == 1 ? "Delete duct" : $"Delete {_segmentIds.Count} ducts";
        protected override bool Apply(BuildingSession s) => DuctEditor.DeleteSegments(s.State.Net(_fluid), _segmentIds);
    }

    public sealed class SetSegmentSizeCommand : DesignCommand
    {
        readonly List<int> _segmentIds;
        readonly double _diameterMm;
        readonly string _fluid;

        /// <param name="diameterMm">mm.</param>
        public SetSegmentSizeCommand(int segmentId, double diameterMm, string fluid = "air") : this(new[] { segmentId }, diameterMm, fluid) { }

        /// <param name="diameterMm">mm.</param>
        public SetSegmentSizeCommand(IEnumerable<int> segmentIds, double diameterMm, string fluid = "air")
        {
            _segmentIds = new List<int>(segmentIds);
            _diameterMm = diameterMm;
            _fluid = fluid;
        }

        /// <summary>Why the last attempt failed (ceiling space), or None.</summary>
        public DrawError LastError { get; private set; }

        public override string Description => $"Change duct size to {_diameterMm} mm";

        protected override bool Apply(BuildingSession s)
        {
            DuctNetworkState net = s.State.Net(_fluid);
            LastError = DrawError.None;
            foreach (int id in _segmentIds)
                if (net.FindSegment(id) is DuctSegment seg && !s.Unlocked(Tech.SizeTier(s.Data, seg.MaterialId, _diameterMm))) { LastError = DrawError.Locked; return false; }
            BuildingState before = s.State.Clone();
            if (!DuctEditor.SetSize(net, _segmentIds, _diameterMm)) return false;
            if (s.CeilingOverfull(s.State) && !s.CeilingOverfull(before))
            {
                s.Record.Design = before;
                LastError = DrawError.NoCeilingSpace;
                return false;
            }
            return true;
        }
    }

    /// <summary>Changes the material, sealing class or insulation (null = unchanged) of several segments.</summary>
    public sealed class SetSegmentSpecCommand : DesignCommand
    {
        readonly List<int> _segmentIds;
        readonly string? _material, _sealing, _insulation;
        readonly string _fluid;

        public SetSegmentSpecCommand(IEnumerable<int> segmentIds, string? materialId = null, string? sealing = null, string? insulation = null, string fluid = "air")
        {
            _fluid = fluid;
            _segmentIds = new List<int>(segmentIds);
            _material = materialId;
            _sealing = sealing;
            _insulation = insulation;
        }

        public override string Description => _material != null ? "Change duct material" : _sealing != null ? "Change duct sealing" : "Change insulation";

        protected override bool Apply(BuildingSession s)
        {
            foreach (string? id in new[] { _material, _sealing, _insulation })
                if (id != null && !s.ItemUnlocked(id)) return false;
            if (_material != null && s.Data.FindMaterial(_material)?.Fluids.Contains(_fluid) != true) return false;
            if (_sealing != null && (_fluid != "air" || _sealing.Length > 0 && s.Data.FindSealing(_sealing) == null)) return false;
            if (_insulation != null && _insulation.Length > 0 && s.Data.FindInsulation(_insulation) == null) return false;
            BuildingState before = s.State.Clone();
            if (!DuctEditor.SetSpec(s.State.Net(_fluid), _segmentIds, _material, _sealing, _insulation)) return false;
            if (s.CeilingOverfull(s.State) && !s.CeilingOverfull(before))
            {
                s.Record.Design = before;
                return false;
            }
            return true;
        }
    }

    /// <summary>Switches one fitting between sharp and smooth (long-radius elbow, smooth tee).</summary>
    public sealed class SetElbowCommand : DesignCommand
    {
        readonly int _nodeId;
        readonly bool _smooth;
        readonly string _fluid;

        public SetElbowCommand(int nodeId, bool smooth, string fluid = "air")
        {
            _nodeId = nodeId;
            _smooth = smooth;
            _fluid = fluid;
        }

        public override string Description => _smooth ? "Smooth fitting" : "Sharp fitting";

        protected override bool Apply(BuildingSession s)
        {
            DuctNode? n = s.State.Net(_fluid).FindNode(_nodeId);
            if (n == null || !(DuctEditor.IsElbow(s.State.Net(_fluid), n) || DuctEditor.IsTee(s.State.Net(_fluid), n)) || n.SmoothFittings == _smooth) return false;
            if (_smooth && !s.Unlocked(Tech.SmoothFittingsTier(s.Data))) return false;
            n.SmoothFittings = _smooth;
            return true;
        }
    }

    /// <summary>"Upgrade all bends": every elbow becomes long-radius and every tee smooth.</summary>
    public sealed class UpgradeAllBendsCommand : DesignCommand
    {
        readonly string _fluid;
        public UpgradeAllBendsCommand(string fluid = "air") => _fluid = fluid;
        public override string Description => "Upgrade all bends";

        protected override bool Apply(BuildingSession s)
        {
            if (!s.Unlocked(Tech.SmoothFittingsTier(s.Data))) return false;
            bool any = false;
            foreach (DuctNode n in s.State.Net(_fluid).Nodes)
            {
                if (n.SmoothFittings || !(DuctEditor.IsElbow(s.State.Net(_fluid), n) || DuctEditor.IsTee(s.State.Net(_fluid), n))) continue;
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
        /// <summary>Why the last attempt failed (floor space), or None.</summary>
        public DrawError LastError { get; private set; }

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
            if (def == null || def.Placement != "intake" || !s.ItemUnlocked(_equipmentId)) return false;
            if (!EquipmentFits.Opening(s.Data, s.Layout.FindOpening(_openingId)!, def)) return false;
            LastError = EquipmentFits.FloorSpace(s, _openingId, def, out _);
            if (LastError != DrawError.None) return false;
            if (e != null && e.EquipmentId == _equipmentId) return false;
            bool vsd = e != null && e.Vsd && def.SpeedControl == "on-off";
            if (e != null) s.State.Equipment.Remove(e);
            s.State.Equipment.Add(new EquipmentState { EquipmentId = _equipmentId, OpeningId = _openingId, On = true, Vsd = vsd });
            return true;
        }
    }

    /// <summary>Places an inline part (damper or inline fan) on a straight duct.</summary>
    public sealed class PlaceInlineCommand : DesignCommand
    {
        readonly TilePos _tile;
        readonly string _partId;
        readonly bool _reverse;
        readonly string _fluid;

        /// <param name="reverse">Point a fan against the default (away from the intake) direction.</param>
        public PlaceInlineCommand(TilePos tile, string partId, bool reverse = false, string fluid = "air")
        {
            _fluid = fluid;
            _tile = tile;
            _partId = partId;
            _reverse = reverse;
        }

        public override string Description => "Place " + _partId;
        public DrawError LastError { get; private set; }

        protected override bool Apply(BuildingSession s)
        {
            LastError = DuctEditor.ValidateInline(s.State.Net(_fluid), _tile);
            if (LastError == DrawError.None && !EquipmentFits.InlinePart(s.Data, _partId, _fluid)) LastError = DrawError.WrongNetwork;
            if (LastError == DrawError.None && !s.ItemUnlocked(_partId)) LastError = DrawError.Locked;
            if (LastError != DrawError.None) return false;
            (int dx, int dy) = DuctEditor.DefaultInlineDirection(s.State.Net(_fluid), _tile);
            if (_reverse) (dx, dy) = (-dx, -dy);
            DuctEditor.PlaceInline(s.State.Net(_fluid), _tile, _partId, dx, dy);
            return true;
        }
    }

    public sealed class RemoveInlineCommand : DesignCommand
    {
        readonly int _nodeId;
        readonly string _fluid;

        public RemoveInlineCommand(int nodeId, string fluid = "air")
        {
            _nodeId = nodeId;
            _fluid = fluid;
        }

        public override string Description => "Remove part";
        protected override bool Apply(BuildingSession s) => DuctEditor.RemoveInline(s.State.Net(_fluid), _nodeId);
    }

    /// <summary>Turns an inline fan around.</summary>
    public sealed class FlipInlineCommand : DesignCommand
    {
        readonly int _nodeId;
        readonly string _fluid;

        public FlipInlineCommand(int nodeId, string fluid = "air")
        {
            _nodeId = nodeId;
            _fluid = fluid;
        }

        public override string Description => "Turn fan around";

        protected override bool Apply(BuildingSession s)
        {
            DuctNode? n = s.State.Net(_fluid).FindNode(_nodeId);
            if (n == null || n.Kind != DuctNodeKind.Inline) return false;
            n.DirX = -n.DirX;
            n.DirY = -n.DirY;
            return true;
        }
    }

    /// <summary>
    /// Fits or removes a variable-speed drive on an on/off fan (<c>07-equipment.md</c>): at an intake
    /// (<paramref name="openingId"/>) or on an inline fan (<paramref name="tile"/>). Bought with the Commit.
    /// </summary>
    public sealed class SetVsdCommand : DesignCommand
    {
        readonly string _openingId;
        readonly TilePos _tile;
        readonly bool _fit;
        readonly string _fluid = "air";

        public SetVsdCommand(string openingId, bool fit)
        {
            _openingId = openingId;
            _fit = fit;
        }

        public SetVsdCommand(TilePos inlineTile, bool fit, string fluid = "air")
        {
            _openingId = "";
            _tile = inlineTile;
            _fit = fit;
            _fluid = fluid;
        }

        static EquipmentDef? DriveFor(BuildingSession s, string category)
        {
            foreach (EquipmentDef d in s.Data.Equipment) if (d.Placement == "addon" && d.FitsCategories.Contains(category)) return d;
            return null;
        }

        public override string Description => _fit ? "Fit variable-speed drive" : "Remove variable-speed drive";

        protected override bool Apply(BuildingSession s)
        {
            if (_openingId.Length > 0)
            {
                EquipmentState? e = s.State.EquipmentAt(_openingId);
                if (e == null || e.Vsd == _fit || s.Data.EquipmentItem(e.EquipmentId).SpeedControl != "on-off") return false;
                EquipmentDef? drive = DriveFor(s, s.Data.EquipmentItem(e.EquipmentId).Category);
                if (drive == null || (_fit && !s.Unlocked(drive.Tier))) return false;
                e.Vsd = _fit;
                e.Speed = 1;
                return true;
            }
            DuctNode? n = s.State.Net(_fluid).NodeAt(_tile);
            if (n == null || n.Kind != DuctNodeKind.Inline || n.Vsd == _fit || s.Data.FindEquipment(n.PartId)?.SpeedControl != "on-off") return false;
            EquipmentDef? d2 = DriveFor(s, s.Data.EquipmentItem(n.PartId).Category);
            if (d2 == null || (_fit && !s.Unlocked(d2.Tier))) return false;
            n.Vsd = _fit;
            n.Speed = 1;
            return true;
        }
    }

    /// <summary>
    /// Makes an electrical item spark-free, or back (<c>07-equipment.md</c> §Safety gear): at an opening or inline. Bought with
    /// the Commit for the difference; the item stops being an ignition source.
    /// </summary>
    public sealed class SetSparkFreeCommand : DesignCommand
    {
        readonly string _openingId;
        readonly TilePos _tile;
        readonly string _fluid;
        readonly bool _on;

        public SetSparkFreeCommand(string openingId, bool on)
        {
            _openingId = openingId;
            _fluid = "air";
            _on = on;
        }

        public SetSparkFreeCommand(TilePos inlineTile, string fluid, bool on)
        {
            _openingId = "";
            _tile = inlineTile;
            _fluid = fluid;
            _on = on;
        }

        public override string Description => _on ? "Spark-free variant" : "Standard variant";

        protected override bool Apply(BuildingSession s)
        {
            if (_on && !s.Unlocked(s.Data.Balancing.Gas.SparkFreeTier)) return false;
            if (_openingId.Length > 0)
            {
                EquipmentState? e = s.State.EquipmentAt(_openingId);
                if (e == null || e.SparkFree == _on || s.Data.FindEquipment(e.EquipmentId)?.IsIgnitionSource != true) return false;
                e.SparkFree = _on;
                return true;
            }
            DuctNode? n = s.State.Net(_fluid).NodeAt(_tile);
            if (n == null || n.Kind != DuctNodeKind.Inline || n.SparkFree == _on || s.Data.FindEquipment(n.PartId)?.IsIgnitionSource != true) return false;
            n.SparkFree = _on;
            return true;
        }
    }

    /// <summary>Places a gas detector on a room tile, or a passive vent on an exterior wall (<c>07-equipment.md</c> §Safety gear).</summary>
    public sealed class PlaceSafetyCommand : DesignCommand
    {
        readonly string _equipmentId;
        readonly TilePos _tile;
        readonly bool _high;

        /// <param name="high">Passive vents: high in the wall, else low.</param>
        public PlaceSafetyCommand(string equipmentId, TilePos tile, bool high = true)
        {
            _equipmentId = equipmentId;
            _tile = tile;
            _high = high;
        }

        public override string Description => "Place " + _equipmentId;
        public DrawError LastError { get; private set; }

        protected override bool Apply(BuildingSession s)
        {
            LastError = DrawError.None;
            EquipmentDef? def = s.Data.FindEquipment(_equipmentId);
            if (def == null || (def.Placement != "room" && def.Placement != "wall")) { LastError = DrawError.WrongNetwork; return false; }
            if (!s.ItemUnlocked(_equipmentId)) { LastError = DrawError.Locked; return false; }
            if (!s.Layout.IsInside(_tile)) { LastError = DrawError.OutsideBuilding; return false; }
            if (s.State.Safety.Exists(d => d.Pos == _tile && d.EquipmentId == _equipmentId)) { LastError = DrawError.Duplicate; return false; }
            string facing = "";
            if (def.Placement == "wall")
            {
                foreach (string side in new[] { "north", "south", "east", "west" })
                    if (s.Layout.HasOutsideWall(_tile, side)) { facing = side; break; }
                if (facing.Length == 0) { LastError = DrawError.NotOnOutsideWall; return false; }
            }
            s.State.Safety.Add(new SafetyDevice { EquipmentId = _equipmentId, X = _tile.X, Y = _tile.Y, Floor = _tile.F, Facing = facing, High = _high });
            return true;
        }
    }

    public sealed class RemoveSafetyCommand : DesignCommand
    {
        readonly string _equipmentId;
        readonly TilePos _tile;

        public RemoveSafetyCommand(string equipmentId, TilePos tile)
        {
            _equipmentId = equipmentId;
            _tile = tile;
        }

        public override string Description => "Remove " + _equipmentId;
        protected override bool Apply(BuildingSession s) => s.State.Safety.RemoveAll(d => d.Pos == _tile && d.EquipmentId == _equipmentId) > 0;
    }

    /// <summary>Moves a passive vent high or low in its wall.</summary>
    public sealed class SetVentHeightCommand : DesignCommand
    {
        readonly TilePos _tile;
        readonly bool _high;

        public SetVentHeightCommand(TilePos tile, bool high)
        {
            _tile = tile;
            _high = high;
        }

        public override string Description => _high ? "Vent high in the wall" : "Vent low in the wall";

        protected override bool Apply(BuildingSession s)
        {
            SafetyDevice? d = s.State.Safety.Find(x => x.Pos == _tile && x.EquipmentId == Failures.GasSafetyIds.PassiveVent);
            if (d == null || d.High == _high) return false;
            d.High = _high;
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
    /// A free operating setting (fan on/off, damper opening; fan speed with a variable-speed drive from Phase 3).
    /// It applies at once to the design and, when the same part is already built, to the committed state too (D25).
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

    /// <summary>
    /// Sets the speed of an intake fan with a variable-speed drive or built-in variable speed (0.2–1, 0.01 steps).
    /// Free and live like dampers (D25); consecutive moves merge into one undo step.
    /// </summary>
    public sealed class SetEquipmentSpeedCommand : OperationalCommand
    {
        readonly string _openingId;
        double _speed;
        double _wasDesign, _wasCommitted;
        bool _hadCommitted;

        public SetEquipmentSpeedCommand(string openingId, double speed)
        {
            _openingId = openingId;
            _speed = speed;
        }

        public override string Description => $"Speed {_speed:P0}";

        /// <summary>Clamps to 20–100% in 1% steps (<c>05-simulation.md</c> §2).</summary>
        public static double SpeedStep(double speed) => System.Math.Round(System.Math.Max(0.2, System.Math.Min(1.0, speed)) * 100) / 100;

        static bool Variable(BuildingSession s, EquipmentState e) => e.Vsd || s.Data.EquipmentItem(e.EquipmentId).SpeedControl == "variable";

        public override bool Execute(BuildingSession s)
        {
            EquipmentState? e = s.State.EquipmentAt(_openingId);
            double v = SpeedStep(_speed);
            if (e == null || e.Speed == v || !Variable(s, e)) return false;
            _speed = v;
            EquipmentState? c = s.CommittedTwin(e);
            if (c != null && !Variable(s, c)) c = null;  // the drive is not bought yet
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

        public override bool TryMerge(ICommand next)
        {
            if (!(next is SetEquipmentSpeedCommand d) || d._openingId != _openingId || d._hadCommitted != _hadCommitted) return false;
            _speed = d._speed;
            return true;
        }
    }

    /// <summary>Sets the speed of an inline fan with a variable-speed drive (live, free, merging like the intake version).</summary>
    public sealed class SetInlineSpeedCommand : OperationalCommand
    {
        readonly TilePos _tile;
        double _speed;
        double _wasDesign, _wasCommitted;
        bool _hadCommitted;

        readonly string _fluid;

        public SetInlineSpeedCommand(TilePos tile, double speed, string fluid = "air")
        {
            _fluid = fluid;
            _tile = tile;
            _speed = speed;
        }

        public override string Description => $"Speed {_speed:P0}";

        public override bool Execute(BuildingSession s)
        {
            DuctNode? n = s.State.Net(_fluid).NodeAt(_tile);
            double v = SetEquipmentSpeedCommand.SpeedStep(_speed);
            if (n == null || n.Kind != DuctNodeKind.Inline || !n.Vsd || n.Speed == v) return false;
            _speed = v;
            DuctNode? c = s.CommittedTwin(n, _fluid);
            if (c != null && !c.Vsd) c = null;
            _wasDesign = n.Speed;
            n.Speed = v;
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
            if (s.State.Net(_fluid).NodeAt(_tile) is DuctNode n) n.Speed = _wasDesign;
            if (_hadCommitted && s.Committed.Net(_fluid).NodeAt(_tile) is DuctNode c) c.Speed = _wasCommitted;
        }

        public override bool TryMerge(ICommand next)
        {
            if (!(next is SetInlineSpeedCommand d) || d._tile != _tile || d._hadCommitted != _hadCommitted) return false;
            _speed = d._speed;
            return true;
        }
    }

    /// <summary>Switches an inline fan on or off.</summary>
    public sealed class SetInlineOnCommand : OperationalCommand
    {
        readonly TilePos _tile;
        readonly bool _on;
        bool _wasDesign, _wasCommitted, _hadCommitted;

        readonly string _fluid;

        public SetInlineOnCommand(TilePos tile, bool on, string fluid = "air")
        {
            _fluid = fluid;
            _tile = tile;
            _on = on;
        }

        public override string Description => _on ? "Switch on" : "Switch off";

        /// <summary>The part changed in the built network (a valve closing or a pump stopping causes a surge).</summary>
        public bool ChangedBuilt => _hadCommitted;

        public override bool Execute(BuildingSession s)
        {
            DuctNode? n = s.State.Net(_fluid).NodeAt(_tile);
            if (n == null || n.Kind != DuctNodeKind.Inline || n.On == _on) return false;
            DuctNode? c = s.CommittedTwin(n, _fluid);
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
            if (s.State.Net(_fluid).NodeAt(_tile) is DuctNode n) n.On = _wasDesign;
            if (_hadCommitted && s.Committed.Net(_fluid).NodeAt(_tile) is DuctNode c) c.On = _wasCommitted;
        }
    }

    /// <summary>
    /// Sets a damper's opening θ. Free and live (D25): it applies at once to the design and, when the damper is
    /// already built, to the committed state too. Consecutive moves of the same damper merge into one undo step.
    /// </summary>
    public sealed class SetDamperOpeningCommand : OperationalCommand
    {
        readonly TilePos _tile;
        double _opening;
        double _wasDesign, _wasCommitted;
        bool _hadCommitted;

        readonly string _fluid;

        /// <param name="opening">θ, clamped to the damper's minimum opening–1.</param>
        public SetDamperOpeningCommand(TilePos tile, double opening, string fluid = "air")
        {
            _fluid = fluid;
            _tile = tile;
            _opening = opening;
        }

        public override string Description => $"Damper {_opening:P0} open";

        public override bool Execute(BuildingSession s)
        {
            DuctNode? n = s.State.Net(_fluid).NodeAt(_tile);
            if (n == null || n.Kind != DuctNodeKind.Inline) return false;
            FittingDef? f = s.Data.FindFitting(n.PartId);
            if (f == null || f.Model != "valve" || f.MinOpening >= 1) return false;
            double v = System.Math.Max(f.MinOpening, System.Math.Min(1.0, _opening));
            if (System.Math.Abs(n.Opening - v) < 1e-12) return false;
            _opening = v;
            DuctNode? c = s.CommittedTwin(n, _fluid);
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
            if (s.State.Net(_fluid).NodeAt(_tile) is DuctNode n) n.Opening = _wasDesign;
            if (_hadCommitted && s.Committed.Net(_fluid).NodeAt(_tile) is DuctNode c) c.Opening = _wasCommitted;
        }

        public override bool TryMerge(ICommand next)
        {
            if (!(next is SetDamperOpeningCommand d) || d._tile != _tile || d._hadCommitted != _hadCommitted) return false;
            _opening = d._opening;
            return true;
        }
    }

    /// <summary>
    /// Sets a regulator's downstream setpoint or a relief valve's opening pressure (<c>07-equipment.md</c> §Control parts:
    /// steps of 0.1 bar for water). Free and live like a damper; consecutive moves merge.
    /// </summary>
    public sealed class SetValveSettingCommand : OperationalCommand
    {
        readonly TilePos _tile;
        readonly string _fluid;
        double _setting;
        double _wasDesign, _wasCommitted;
        bool _hadCommitted;

        /// <param name="setting">Pa gauge; snapped to the fitting's step and range.</param>
        public SetValveSettingCommand(TilePos tile, double setting, string fluid = "water")
        {
            _tile = tile;
            _setting = setting;
            _fluid = fluid;
        }

        public override string Description => $"Set {_setting / 1e5:0.0} bar";

        public static double Snap(FittingDef f, double v)
        {
            v = System.Math.Max(f.SettingMin, System.Math.Min(f.SettingMax, v));
            return f.SettingStep > 0 ? System.Math.Round(v / f.SettingStep) * f.SettingStep : v;
        }

        public override bool Execute(BuildingSession s)
        {
            DuctNode? n = s.State.Net(_fluid).NodeAt(_tile);
            if (n == null || n.Kind != DuctNodeKind.Inline) return false;
            FittingDef? f = s.Data.FindFitting(n.PartId);
            if (f == null || (f.Model != "regulator" && f.Model != "relief")) return false;
            double v = Snap(f, _setting);
            double current = n.Setting > 0 ? n.Setting : f.DefaultSetting;
            if (System.Math.Abs(current - v) < 1e-9) return false;
            _setting = v;
            DuctNode? c = s.CommittedTwin(n, _fluid);
            _wasDesign = n.Setting;
            n.Setting = v;
            _hadCommitted = c != null;
            if (c != null)
            {
                _wasCommitted = c.Setting;
                c.Setting = v;
            }
            return true;
        }

        public override void Undo(BuildingSession s)
        {
            if (s.State.Net(_fluid).NodeAt(_tile) is DuctNode n) n.Setting = _wasDesign;
            if (_hadCommitted && s.Committed.Net(_fluid).NodeAt(_tile) is DuctNode c) c.Setting = _wasCommitted;
        }

        public override bool TryMerge(ICommand next)
        {
            if (!(next is SetValveSettingCommand d) || d._tile != _tile || d._fluid != _fluid || d._hadCommitted != _hadCommitted) return false;
            _setting = d._setting;
            return true;
        }
    }
}
