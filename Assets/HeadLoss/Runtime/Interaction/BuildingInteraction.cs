using System.Collections.Generic;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.Commands;
using HeadLoss.Game.Session;
using HeadLoss.Unity.View;
using UnityEngine;

namespace HeadLoss.Unity.Interaction
{
    public enum TargetKind { None, Segment, Node, Fan, Outlet }

    /// <summary>Something under the pointer or selected.</summary>
    public struct Target
    {
        public TargetKind Kind;
        /// <summary>Segment or node id.</summary>
        public int Id;
        /// <summary>Opening id (fan) or outlet id.</summary>
        public string Anchor;

        public static Target None => new Target { Kind = TargetKind.None, Anchor = "" };
        public bool Same(Target o) => Kind == o.Kind && Id == o.Id && Anchor == o.Anchor;
    }

    /// <summary>
    /// Building-view pointer and keyboard logic: left-drag from the network draws an orthogonal run with a
    /// live preview solve, a click selects, a drag on empty space box-selects, right/middle drag pans.
    /// Everything that changes the building goes through commands.
    /// </summary>
    public sealed class BuildingInteraction
    {
        enum Mode { Idle, Pressed, Drawing, BoxSelect }

        readonly BuildingSession _session;
        readonly CameraController _camera;
        readonly BuildingInput _input;
        readonly DuctGeometry _geometry;
        readonly DrawingDef _drawing;

        Mode _mode = Mode.Idle;
        Vector2 _pressScreen;
        TilePos _pressTile;
        bool _pressOnNetwork;
        readonly List<TilePos> _path = new List<TilePos>();
        bool _panning;

        public double DrawDiameterMm;
        public string DrawMaterialId;
        public bool DrawSmoothElbows;

        /// <summary>Preview of the run being drawn (null when not drawing).</summary>
        public DrawPreview? Preview { get; private set; }
        public IReadOnlyList<TilePos> Path => _path;
        public bool IsDrawing => _mode == Mode.Drawing;
        public bool IsBoxSelecting => _mode == Mode.BoxSelect;
        public Rect BoxWorld { get; private set; }

        public Target Hover { get; private set; } = Target.None;
        public Target Selected { get; private set; } = Target.None;
        public readonly HashSet<int> SelectedSegments = new HashSet<int>();

        /// <summary>Bumped when hover, selection, the path or the preview change (the duct mesh rebuilds).</summary>
        public int Revision { get; private set; }

        /// <summary>A short calm message for the hint line (empty = none).</summary>
        public string Hint { get; private set; } = "";

        public BuildingInteraction(BuildingSession session, CameraController camera, BuildingInput input, DuctGeometry geometry)
        {
            _session = session;
            _camera = camera;
            _input = input;
            _geometry = geometry;
            _drawing = session.Data.Balancing.Drawing;
            DrawDiameterMm = _drawing.DefaultDuctSizeMm;
            DrawMaterialId = _drawing.DefaultMaterial;
        }

        static TilePos TileAt(Vector2 world) => new TilePos(Mathf.FloorToInt(world.x), Mathf.FloorToInt(world.y));

        /// <summary>Per-frame update. <paramref name="pointerOverUi"/> blocks world interaction.</summary>
        public void Update(bool pointerOverUi, float dt)
        {
            Vector2 screen = _input.PointerPosition;
            Vector2 world = _camera.ScreenToWorld(screen);

            // Camera.
            float scroll = _input.Scroll.ReadValue<Vector2>().y;
            if (!pointerOverUi && Mathf.Abs(scroll) > 0.01f) _camera.Zoom(Mathf.Sign(scroll), screen);
            _camera.KeyPan(_input.Pan.ReadValue<Vector2>(), dt);
            bool panPress = (_input.Secondary.WasPressedThisFrame() && _mode != Mode.Drawing) || _input.Middle.WasPressedThisFrame();
            if (panPress && !pointerOverUi)
            {
                _panning = true;
                _camera.BeginDrag(screen);
            }
            if (_panning)
            {
                _camera.UpdateDrag(screen);
                if (!_input.Secondary.IsPressed() && !_input.Middle.IsPressed())
                {
                    _panning = false;
                    _camera.EndDrag();
                }
            }

            // Cancel / delete.
            if (_input.Cancel.WasPressedThisFrame())
            {
                if (_mode == Mode.Drawing || _mode == Mode.BoxSelect) CancelGesture();
                else ClearSelection();
            }
            if (_mode == Mode.Drawing && _input.Secondary.WasPressedThisFrame()) CancelGesture();
            if (_input.Delete.WasPressedThisFrame()) DeleteSelection();

            switch (_mode)
            {
                case Mode.Idle:
                    SetHover(pointerOverUi || _panning ? Target.None : Pick(world));
                    if (!pointerOverUi && _input.Primary.WasPressedThisFrame())
                    {
                        _mode = Mode.Pressed;
                        _pressScreen = screen;
                        _pressTile = TileAt(world);
                        _pressOnNetwork = StartsOnNetwork(_pressTile);
                    }
                    break;

                case Mode.Pressed:
                    if (!_input.Primary.IsPressed())
                    {
                        Select(Pick(world));
                        _mode = Mode.Idle;
                        break;
                    }
                    TilePos tile = TileAt(world);
                    if (tile != _pressTile && _pressOnNetwork)
                    {
                        _mode = Mode.Drawing;
                        _path.Clear();
                        _path.Add(_pressTile);
                        SetHover(Target.None);
                        ExtendPathTo(tile);
                        RefreshPreview();
                    }
                    else if ((screen - _pressScreen).magnitude > 8 && !_pressOnNetwork)
                    {
                        _mode = Mode.BoxSelect;
                        UpdateBox(world);
                    }
                    break;

                case Mode.Drawing:
                    TilePos t = TileAt(world);
                    if (t != _path[_path.Count - 1] && _session.Layout.InFootprint(t))
                    {
                        ExtendPathTo(t);
                        RefreshPreview();
                    }
                    if (!_input.Primary.IsPressed()) CommitRun();
                    break;

                case Mode.BoxSelect:
                    UpdateBox(world);
                    if (!_input.Primary.IsPressed())
                    {
                        SelectBox();
                        _mode = Mode.Idle;
                        Revision++;
                    }
                    break;
            }
        }

        bool StartsOnNetwork(TilePos t) => DuctEditor.Occupancy(_session.State.Air).TryGetValue(t, out TileOccupant o) && !o.IsEmpty;

        /// <summary>Extends the path one orthogonal step at a time toward the pointer tile, keeping direction and backtracking.</summary>
        void ExtendPathTo(TilePos target)
        {
            for (int guard = 0; guard < 400 && _path[_path.Count - 1] != target; guard++)
            {
                int idx = _path.IndexOf(target);
                if (idx >= 0)
                {
                    _path.RemoveRange(idx + 1, _path.Count - idx - 1);
                    break;
                }
                TilePos last = _path[_path.Count - 1];
                int dx = target.X - last.X, dy = target.Y - last.Y;
                (int cx, int cy) = _path.Count >= 2 ? _path[_path.Count - 2].DirectionTo(last) : (0, 0);
                TilePos next;
                if (cx != 0 && System.Math.Sign(dx) == cx) next = new TilePos(last.X + cx, last.Y);
                else if (cy != 0 && System.Math.Sign(dy) == cy) next = new TilePos(last.X, last.Y + cy);
                else if (System.Math.Abs(dx) >= System.Math.Abs(dy)) next = new TilePos(last.X + System.Math.Sign(dx), last.Y);
                else next = new TilePos(last.X, last.Y + System.Math.Sign(dy));
                if (_path.Count >= 2 && next == _path[_path.Count - 2]) _path.RemoveAt(_path.Count - 1);
                else _path.Add(next);
            }
        }

        void RefreshPreview()
        {
            Preview = _path.Count >= 2 ? _session.Preview(_path, DrawDiameterMm, DrawMaterialId, DrawSmoothElbows) : null;
            Hint = Preview != null && Preview.Error != DrawError.None ? DuctEditor.Describe(Preview.Error) : "";
            Revision++;
        }

        void CommitRun()
        {
            if (Preview != null && Preview.Error == DrawError.None)
                _session.Execute(new PlaceRunCommand(_path, DrawDiameterMm, DrawMaterialId, DrawSmoothElbows));
            CancelGesture();
        }

        void CancelGesture()
        {
            _mode = Mode.Idle;
            _path.Clear();
            Preview = null;
            Hint = "";
            Revision++;
        }

        void UpdateBox(Vector2 world)
        {
            Vector2 a = _camera.ScreenToWorld(_pressScreen);
            BoxWorld = Rect.MinMaxRect(Mathf.Min(a.x, world.x), Mathf.Min(a.y, world.y), Mathf.Max(a.x, world.x), Mathf.Max(a.y, world.y));
            Revision++;
        }

        void SelectBox()
        {
            ClearSelection();
            Rect r = BoxWorld;
            foreach (DuctGeometry.Segment s in _geometry.Segments)
                if (r.Contains(s.A) || r.Contains(s.B) || r.Contains((s.A + s.B) * 0.5f)) SelectedSegments.Add(s.Id);
            if (SelectedSegments.Count > 0) Selected = new Target { Kind = TargetKind.Segment, Id = 0, Anchor = "" };
        }

        void SetHover(Target t)
        {
            if (t.Same(Hover)) return;
            Hover = t;
            Revision++;
        }

        public void Select(Target t)
        {
            SelectedSegments.Clear();
            Selected = t;
            if (t.Kind == TargetKind.Segment) SelectedSegments.Add(t.Id);
            Revision++;
        }

        public void ClearSelection() => Select(Target.None);

        public void DeleteSelection()
        {
            if (SelectedSegments.Count == 0) return;
            _session.Execute(new DeleteSegmentCommand(SelectedSegments));
            ClearSelection();
        }

        /// <summary>Drops selection and hover that no longer exist after an edit.</summary>
        public void Validate()
        {
            DuctNetworkState net = _session.State.Air;
            int before = SelectedSegments.Count;
            SelectedSegments.RemoveWhere(id => net.FindSegment(id) == null);
            if (Selected.Kind == TargetKind.Segment && SelectedSegments.Count == 0 && before > 0) Selected = Target.None;
            if (Selected.Kind == TargetKind.Node && net.FindNode(Selected.Id) == null) Selected = Target.None;
            if (Hover.Kind == TargetKind.Segment && net.FindSegment(Hover.Id) == null) Hover = Target.None;
            if (Hover.Kind == TargetKind.Node && net.FindNode(Hover.Id) == null) Hover = Target.None;
            Revision++;
        }

        /// <summary>What is under a world point: outlet or intake tile, then fittings, then ducts.</summary>
        public Target Pick(Vector2 world)
        {
            TilePos tile = TileAt(world);
            foreach (OutletPlacementDef o in _session.Layout.Outlets)
                if (o.X == tile.X && o.Y == tile.Y && Vector2.Distance(world, DuctGeometry.Centre(tile)) < 0.4f)
                    return new Target { Kind = TargetKind.Outlet, Anchor = o.Id };
            foreach (EntryOpeningDef o in _session.Layout.EntryOpenings)
                if (o.X == tile.X && o.Y == tile.Y && _session.State.EquipmentAt(o.Id) != null && Vector2.Distance(world, DuctGeometry.Centre(tile)) < 0.45f)
                    return new Target { Kind = TargetKind.Fan, Anchor = o.Id };

            DuctNetworkState net = _session.State.Air;
            foreach (DuctNode n in net.Nodes)
            {
                if (n.Kind != DuctNodeKind.Free || net.Degree(n.Id) < 2) continue;
                if (Vector2.Distance(world, DuctGeometry.Centre(n.Pos)) < 0.3f) return new Target { Kind = TargetKind.Node, Id = n.Id, Anchor = "" };
            }
            int best = 0;
            float bestDist = float.MaxValue;
            foreach (DuctGeometry.Segment s in _geometry.Segments)
            {
                float d = DuctGeometry.DistanceToSegment(world, s.A, s.B);
                if (d < Mathf.Max(s.Width * 0.5f + 0.08f, 0.25f) && d < bestDist)
                {
                    bestDist = d;
                    best = s.Id;
                }
            }
            return best != 0 ? new Target { Kind = TargetKind.Segment, Id = best, Anchor = "" } : Target.None;
        }
    }
}
