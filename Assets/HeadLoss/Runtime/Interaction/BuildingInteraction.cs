using System.Collections.Generic;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.Commands;
using HeadLoss.Game.Session;
using HeadLoss.Unity.View;
using UnityEngine;

namespace HeadLoss.Unity.Interaction
{
    public enum TargetKind { None, Segment, Node, Intake, Outlet, Inline }

    /// <summary>Something under the pointer or selected.</summary>
    public struct Target
    {
        public TargetKind Kind;
        /// <summary>Segment or node id.</summary>
        public int Id;
        /// <summary>Opening id (intake) or outlet id.</summary>
        public string Anchor;
        /// <summary>Network of a segment, node or inline part (air, water).</summary>
        public string Fluid;

        public static Target None => new Target { Kind = TargetKind.None, Anchor = "", Fluid = "air" };
        public bool Same(Target o) => Kind == o.Kind && Id == o.Id && Anchor == o.Anchor && Fluid == o.Fluid;
    }

    /// <summary>
    /// Building-view pointer and keyboard logic: left-drag from the network draws an orthogonal run with a
    /// live preview solve, a click selects, a drag on empty space box-selects, right/middle drag pans, and with
    /// a part tool active a click on a straight duct places a damper or inline fan. Everything that changes the
    /// building goes through commands.
    /// </summary>
    public sealed class BuildingInteraction
    {
        enum Mode { Idle, Pressed, Drawing, BoxSelect }

        readonly BuildingSession _session;
        readonly CameraController _camera;
        readonly BuildingInput _input;
        readonly DuctViewInput _views;
        readonly DrawingDef _drawing;
        /// <summary>Floor, layer and network on show.</summary>
        public BuildingViewState View { get; }
        /// <summary>Size and material last chosen per network.</summary>
        readonly Dictionary<string, (double size, string material)> _drawChoice = new Dictionary<string, (double, string)>();

        Mode _mode = Mode.Idle;
        Vector2 _pressScreen;
        TilePos _pressTile;
        bool _pressOnNetwork;
        readonly List<TilePos> _path = new List<TilePos>();
        bool _panning;

        /// <summary>Size of the next run on the network in focus, mm.</summary>
        public double DrawDiameterMm
        {
            get => _drawChoice[View.Fluid].size;
            set => _drawChoice[View.Fluid] = (value, _drawChoice[View.Fluid].material);
        }

        /// <summary>Material of the next run on the network in focus.</summary>
        public string DrawMaterialId
        {
            get => _drawChoice[View.Fluid].material;
            set => _drawChoice[View.Fluid] = (_drawChoice[View.Fluid].size, value);
        }

        public bool DrawSmoothElbows;

        /// <summary>Part being placed (fitting or equipment id), or "" when no part tool is active.</summary>
        public string PlaceTool { get; private set; } = "";
        /// <summary>Place the part (an inline fan) pointing toward the intake instead of away from it (R).</summary>
        public bool PlaceReversed { get; private set; }
        /// <summary>Tile under the pointer while a part tool is active, and whether the part fits there.</summary>
        public TilePos PlaceTile { get; private set; }
        public bool PlaceValid { get; private set; }
        public bool PlaceHovering { get; private set; }

        /// <summary>Preview of the run being drawn (null when not drawing).</summary>
        public DrawPreview? Preview { get; private set; }
        public IReadOnlyList<TilePos> Path => _path;
        public bool IsDrawing => _mode == Mode.Drawing;
        public bool IsBoxSelecting => _mode == Mode.BoxSelect;
        public Rect BoxWorld { get; private set; }

        public Target Hover { get; private set; } = Target.None;
        public Target Selected { get; private set; } = Target.None;
        public readonly HashSet<int> SelectedSegments = new HashSet<int>();
        /// <summary>Tile where the selected segment was clicked ("add a part here").</summary>
        public TilePos SelectedTile { get; private set; }

        /// <summary>Esc with nothing to cancel: the player wants to leave the building.</summary>
        public bool BackRequested;

        /// <summary>Bumped when hover, selection, the path, the tool or the preview change (the duct mesh rebuilds).</summary>
        public int Revision { get; private set; }
        /// <summary>Tile under the pointer (the room readout uses it when nothing else is hovered).</summary>
        public TilePos PointerTile { get; private set; }
        /// <summary>The pointer is over the HUD.</summary>
        public bool PointerOverUi { get; private set; }

        /// <summary>A short calm message for the hint line (empty = none).</summary>
        public string Hint { get; private set; } = "";

        public BuildingInteraction(BuildingSession session, CameraController camera, BuildingInput input, DuctViewInput views, BuildingViewState view)
        {
            _session = session;
            _camera = camera;
            _input = input;
            _views = views;
            View = view;
            _drawing = session.Data.Balancing.Drawing;
            _drawChoice["air"] = (_drawing.DefaultDuctSizeMm, _drawing.DefaultMaterial);
            _drawChoice["water"] = (22, "pvc");
        }

        TilePos TileAt(Vector2 world) => new TilePos(Mathf.FloorToInt(world.x), Mathf.FloorToInt(world.y), View.Floor);

        DuctNetworkState Net => _session.State.Net(View.Fluid);

        /// <summary>Pipes are drawn and picked in the ceiling layer (the floor layer is for equipment and outlets).</summary>
        bool PipesInFocus => View.Layer != ViewLayer.Floor;

        /// <summary>Shows a short calm message on the hint line.</summary>
        public void SetHint(string text)
        {
            Hint = text;
            Revision++;
        }

        /// <summary>Puts a network in focus (the toolbox tabs, or selecting one of its pipes).</summary>
        public void SetFluid(string fluid)
        {
            if (fluid == View.Fluid) return;
            CancelGesture();
            if (PlaceTool.Length > 0) SetPlaceTool(PlaceTool);
            View.SetFluid(fluid);
            Revision++;
        }

        /// <summary>Starts (or with the same id, stops) placing a part.</summary>
        public void SetPlaceTool(string partId)
        {
            CancelGesture();
            PlaceTool = PlaceTool == partId ? "" : partId;
            PlaceReversed = false;
            PlaceHovering = false;
            if (PlaceTool.Length > 0) ClearSelection();
            Hint = "";
            Revision++;
        }

        /// <summary>Per-frame update. <paramref name="pointerOverUi"/> blocks world interaction.</summary>
        public void Update(bool pointerOverUi, float dt)
        {
            Vector2 screen = _input.PointerPosition;
            Vector2 world = _camera.ScreenToWorld(screen);
            TilePos pointer = TileAt(world);
            if (pointer != PointerTile || pointerOverUi != PointerOverUi)
            {
                PointerTile = pointer;
                PointerOverUi = pointerOverUi;
                Revision++;
            }

            // Camera.
            float scroll = _input.Scroll.ReadValue<Vector2>().y;
            if (!pointerOverUi && Mathf.Abs(scroll) > 0.01f) _camera.Zoom(Mathf.Sign(scroll), screen);
            _camera.KeyPan(_input.Pan.ReadValue<Vector2>(), dt);
            bool secondaryCancels = _mode == Mode.Drawing || PlaceTool.Length > 0;
            bool panPress = (_input.Secondary.WasPressedThisFrame() && !secondaryCancels) || _input.Middle.WasPressedThisFrame();
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

            // Cancel chain: gesture → part tool → selection → leave the building.
            if (_input.Cancel.WasPressedThisFrame())
            {
                if (_mode == Mode.Drawing || _mode == Mode.BoxSelect) CancelGesture();
                else if (PlaceTool.Length > 0) SetPlaceTool(PlaceTool);
                else if (Selected.Kind != TargetKind.None) ClearSelection();
                else BackRequested = true;
            }
            if (_input.Secondary.WasPressedThisFrame())
            {
                if (_mode == Mode.Drawing) CancelGesture();
                else if (PlaceTool.Length > 0) SetPlaceTool(PlaceTool);
            }
            if (_input.Delete.WasPressedThisFrame()) DeleteSelection();
            if (_input.Rotate.WasPressedThisFrame()) Rotate();

            if (PlaceTool.Length > 0)
            {
                UpdatePlacing(pointerOverUi, world);
                return;
            }

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
                        if (!PipesInFocus && DuctEditor.Occupancy(Net).ContainsKey(_pressTile))
                        {
                            Hint = "Pipes and ducts run in the ceiling layer · press L";
                            Revision++;
                        }
                    }
                    break;

                case Mode.Pressed:
                    if (!_input.Primary.IsPressed())
                    {
                        Select(Pick(world));
                        SelectedTile = TileAt(world);
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
                        _mode = Mode.Idle;
                        Revision++;
                    }
                    break;
            }
        }

        void UpdatePlacing(bool pointerOverUi, Vector2 world)
        {
            SetHover(Target.None);
            TilePos tile = TileAt(world);
            bool hovering = !pointerOverUi && _session.Layout.InFootprint(tile);
            DrawError err = DuctEditor.ValidateInline(Net, tile);
            if (err == DrawError.None && !EquipmentFits.InlinePart(_session.Data, PlaceTool, View.Fluid)) err = DrawError.WrongNetwork;
            bool valid = hovering && err == DrawError.None;
            if (hovering != PlaceHovering || tile != PlaceTile || valid != PlaceValid)
            {
                PlaceHovering = hovering;
                PlaceTile = tile;
                PlaceValid = valid;
                Revision++;
            }
            string name = PartName(PlaceTool);
            string what = View.Fluid == "air" ? "duct" : "pipe";
            Hint = hovering && !valid ? DuctEditor.Describe(err)
                : _session.Data.FindEquipment(PlaceTool)?.Curve.Count > 0 ? $"Click a straight {what} to place the {name} · R turns it · Esc to stop"
                : $"Click a straight {what} to place a {name} · Esc to stop";
            if (hovering && _input.Primary.WasPressedThisFrame())
            {
                var cmd = new PlaceInlineCommand(tile, PlaceTool, PlaceReversed, View.Fluid);
                if (_session.Execute(cmd)) Revision++;
            }
        }

        string PartName(string id) =>
            (_session.Data.FindEquipment(id)?.DisplayName ?? _session.Data.FindFitting(id)?.DisplayName ?? id).ToLowerInvariant();

        void Rotate()
        {
            if (PlaceTool.Length > 0)
            {
                PlaceReversed = !PlaceReversed;
                Revision++;
                return;
            }
            if (Selected.Kind == TargetKind.Inline && _session.State.Net(Selected.Fluid).FindNode(Selected.Id) is DuctNode n
                && (_session.Data.FindEquipment(n.PartId) != null || _session.Data.FindFitting(n.PartId)?.Model == "check"))
                _session.Execute(new FlipInlineCommand(n.Id, Selected.Fluid));
        }

        bool StartsOnNetwork(TilePos t) => PipesInFocus && DuctEditor.Occupancy(Net).TryGetValue(t, out TileOccupant o) && !o.IsEmpty;

        /// <summary>
        /// Runs a riser one floor up or down from the selected pipe node or pipe tile, which must be in a shaft
        /// (<c>04-buildings.md</c> §Floors and risers). Uses the size and material of the pipe it starts from. The view
        /// follows to the new floor.
        /// </summary>
        public DrawError AddRiser(int direction)
        {
            TilePos? start = RiserStart();
            if (start == null) return DrawError.NotInShaft;
            DuctNetworkState net = _session.State.Net(Selected.Fluid);
            DuctSegment? seg = null;
            DuctNode? node = net.NodeAt(start.Value);
            foreach (DuctSegment s in net.Segments)
                if (node != null ? (s.NodeA == node.Id || s.NodeB == node.Id) : DuctEditor.Tiles(net, s).Contains(start.Value)) { seg = s; break; }
            double size = seg?.DiameterMm ?? DrawDiameterMm;
            string material = seg?.MaterialId ?? DrawMaterialId;
            var path = new List<TilePos> { start.Value, start.Value.OnFloor(start.Value.F + direction) };
            var cmd = new PlaceRunCommand(path, size, material, false, Selected.Fluid);
            if (!_session.Execute(cmd))
            {
                Hint = DuctEditor.Describe(cmd.LastError);
                Revision++;
                return cmd.LastError;
            }
            View.SetFloor(start.Value.F + direction);
            ClearSelection();
            Revision++;
            return DrawError.None;
        }

        /// <summary>The shaft tile a riser would start from: the selected pipe node, or the clicked tile of a selected pipe.</summary>
        public TilePos? RiserStart()
        {
            DuctNetworkState net = _session.State.Net(Selected.Fluid);
            TilePos? t = Selected.Kind == TargetKind.Node && net.FindNode(Selected.Id) is DuctNode n ? n.Pos
                : Selected.Kind == TargetKind.Segment && SelectedSegments.Count == 1 ? SelectedTile : (TilePos?)null;
            return t.HasValue && _session.Layout.IsShaft(t.Value) ? t : null;
        }

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
                if (cx != 0 && System.Math.Sign(dx) == cx) next = new TilePos(last.X + cx, last.Y, last.F);
                else if (cy != 0 && System.Math.Sign(dy) == cy) next = new TilePos(last.X, last.Y + cy, last.F);
                else if (System.Math.Abs(dx) >= System.Math.Abs(dy)) next = new TilePos(last.X + System.Math.Sign(dx), last.Y, last.F);
                else next = new TilePos(last.X, last.Y + System.Math.Sign(dy), last.F);
                if (_path.Count >= 2 && next == _path[_path.Count - 2]) _path.RemoveAt(_path.Count - 1);
                else _path.Add(next);
            }
        }

        void RefreshPreview()
        {
            Preview = _path.Count >= 2 ? _session.Preview(_path, DrawDiameterMm, DrawMaterialId, DrawSmoothElbows, View.Fluid) : null;
            Hint = Preview != null && Preview.Error != DrawError.None ? DuctEditor.Describe(Preview.Error) : "";
            Revision++;
        }

        void CommitRun()
        {
            if (Preview != null && Preview.Error == DrawError.None)
                _session.Execute(new PlaceRunCommand(_path, DrawDiameterMm, DrawMaterialId, DrawSmoothElbows, View.Fluid));
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

        /// <summary>Updates the box and selects what it covers as it is dragged (live).</summary>
        void UpdateBox(Vector2 world)
        {
            Vector2 a = _camera.ScreenToWorld(_pressScreen);
            BoxWorld = Rect.MinMaxRect(Mathf.Min(a.x, world.x), Mathf.Min(a.y, world.y), Mathf.Max(a.x, world.x), Mathf.Max(a.y, world.y));
            SelectBox();
            Revision++;
        }

        void SelectBox()
        {
            ClearSelection();
            Rect r = BoxWorld;
            foreach (DuctGeometry.Segment s in _views.View(View.Fluid).Geometry.Segments)
                if (r.Contains(s.A) || r.Contains(s.B) || r.Contains((s.A + s.B) * 0.5f)) SelectedSegments.Add(s.Id);
            if (SelectedSegments.Count > 0) Selected = new Target { Kind = TargetKind.Segment, Id = 0, Anchor = "", Fluid = View.Fluid };
        }

        void SetHover(Target t)
        {
            if (t.Same(Hover)) return;
            Hover = t;
            Revision++;
        }

        public void Select(Target t)
        {
            if (t.Fluid == null) t.Fluid = View.Fluid;
            SelectedSegments.Clear();
            Selected = t;
            if (t.Kind == TargetKind.Segment) SelectedSegments.Add(t.Id);
            // Selecting a pipe of the other network brings that network into focus.
            if (t.Kind != TargetKind.None && t.Fluid != View.Fluid) View.SetFluid(t.Fluid);
            Revision++;
        }

        public void ClearSelection() => Select(Target.None);

        public void DeleteSelection()
        {
            if (Selected.Kind == TargetKind.Inline)
            {
                _session.Execute(new RemoveInlineCommand(Selected.Id, Selected.Fluid));
                ClearSelection();
                return;
            }
            if (SelectedSegments.Count == 0) return;
            _session.Execute(new DeleteSegmentCommand(SelectedSegments, Selected.Fluid));
            ClearSelection();
        }

        /// <summary>Drops selection and hover that no longer exist after an edit (including undo).</summary>
        public void Validate()
        {
            DuctNetworkState net = _session.State.Net(Selected.Fluid ?? "air");
            int before = SelectedSegments.Count;
            SelectedSegments.RemoveWhere(id => net.FindSegment(id) == null);
            if (Selected.Kind == TargetKind.Segment && SelectedSegments.Count == 0 && before > 0) Selected = Target.None;
            if ((Selected.Kind == TargetKind.Node || Selected.Kind == TargetKind.Inline) && !NodeIs(net, Selected)) Selected = Target.None;
            DuctNetworkState hnet = _session.State.Net(Hover.Fluid ?? "air");
            if (Hover.Kind == TargetKind.Segment && hnet.FindSegment(Hover.Id) == null) Hover = Target.None;
            if ((Hover.Kind == TargetKind.Node || Hover.Kind == TargetKind.Inline) && !NodeIs(hnet, Hover)) Hover = Target.None;
            Revision++;
        }

        static bool NodeIs(DuctNetworkState net, Target t)
        {
            DuctNode? n = net.FindNode(t.Id);
            return n != null && (t.Kind == TargetKind.Inline) == (n.Kind == DuctNodeKind.Inline);
        }

        /// <summary>
        /// What is under a world point on the floor on show: outlet or opening tile, then (in the ceiling layer) inline
        /// parts, fittings, risers and pipes, of the network in focus first and then the other one.
        /// </summary>
        public Target Pick(Vector2 world)
        {
            TilePos tile = TileAt(world);
            foreach (OutletPlacementDef o in _session.Layout.Outlets)
                if (BuildingLayout.PosOf(o) == tile && Vector2.Distance(world, DuctGeometry.Centre(tile)) < 0.4f)
                    return new Target { Kind = TargetKind.Outlet, Anchor = o.Id, Fluid = BuildingState.OutletFluid(o.Type) };
            foreach (EntryOpeningDef o in _session.Layout.EntryOpenings)
                if (BuildingLayout.PosOf(o) == tile && Vector2.Distance(world, DuctGeometry.Centre(tile)) < 0.45f)
                    return new Target { Kind = TargetKind.Intake, Anchor = o.Id, Fluid = o.Utility == "power" ? View.Fluid : o.Utility };
            if (!PipesInFocus) return Target.None;
            Target t = PickIn(View.Fluid, world);
            if (t.Kind != TargetKind.None) return t;
            foreach (string f in View.Fluids)
                if (f != View.Fluid && (t = PickIn(f, world)).Kind != TargetKind.None) return t;
            return Target.None;
        }

        Target PickIn(string fluid, Vector2 world)
        {
            DuctNetworkState net = _session.State.Net(fluid);
            foreach (DuctNode n in net.Nodes)
            {
                if (n.Kind != DuctNodeKind.Inline || n.Floor != View.Floor) continue;
                if (Vector2.Distance(world, DuctGeometry.Centre(n.Pos)) < 0.38f) return new Target { Kind = TargetKind.Inline, Id = n.Id, Anchor = "", Fluid = fluid };
            }
            foreach (DuctNode n in net.Nodes)
            {
                if (n.Kind != DuctNodeKind.Free || n.Floor != View.Floor || net.Degree(n.Id) < 1) continue;
                if (Vector2.Distance(world, DuctGeometry.Centre(n.Pos)) < 0.3f) return new Target { Kind = TargetKind.Node, Id = n.Id, Anchor = "", Fluid = fluid };
            }
            DuctGeometry geometry = _views.View(fluid).Geometry;
            foreach (DuctGeometry.Riser r in geometry.Risers)
                if (Vector2.Distance(world, r.Centre) < 0.35f) return new Target { Kind = TargetKind.Segment, Id = r.SegmentId, Anchor = "", Fluid = fluid };
            int best = 0;
            float bestDist = float.MaxValue;
            foreach (DuctGeometry.Segment s in geometry.Segments)
            {
                float d = DuctGeometry.DistanceToSegment(world, s.A, s.B);
                if (d < Mathf.Max(s.Width * 0.5f + 0.08f, 0.25f) && d < bestDist)
                {
                    bestDist = d;
                    best = s.Id;
                }
            }
            return best != 0 ? new Target { Kind = TargetKind.Segment, Id = best, Anchor = "", Fluid = fluid } : Target.None;
        }
    }
}
