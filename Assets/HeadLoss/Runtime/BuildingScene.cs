using System.Collections.Generic;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.Failures;
using HeadLoss.Game.Session;
using HeadLoss.Unity.Interaction;
using HeadLoss.Unity.UI;
using HeadLoss.Unity.View;
using UnityEngine;
using UnityEngine.UIElements;

namespace HeadLoss.Unity
{
    /// <summary>
    /// Everything shown for the open building: blueprint of the floor on show, both networks with ghosts of the floors
    /// above and below, particles, equipment symbols, interaction and HUD. Created when a building opens and disposed
    /// when the player leaves it.
    /// </summary>
    public sealed class BuildingScene
    {
        public BuildingSession Session { get; }
        public BuildingInteraction Interaction { get; }
        public BuildingHud Hud { get; }
        public DuctRenderer Ducts { get; }
        /// <summary>Floor, layer and network on show.</summary>
        public BuildingViewState ViewState { get; }
        /// <summary>What the duct layer showed at its last rebuild.</summary>
        public DuctViewInput View { get; } = new DuctViewInput();
        /// <summary>Session solve revision the duct layer last rebuilt for.</summary>
        public int RenderedSolveRevision => _solveRev;

        readonly GameObject _root;
        readonly BuildingRenderer _building;
        readonly FlowParticles _particles;
        readonly EquipmentLayer _equipment;
        readonly RoomHeatmapRenderer _rooms;
        readonly ViewPalette _palette;
        readonly Sim.Rooms.IRoomGridStepper _previousStepper;
        readonly HeatmapState _heatmap;
        readonly BuildingInput _input;
        int _stateRev = -1, _solveRev = -1, _ixRev = -1, _hmRev = -1, _viewRev = -1, _shownFloor;

        /// <summary>HUD screen-fraction insets (left, right, top, bottom) at the 1920×1080 reference.</summary>
        public static readonly Vector4 HudInsets = new Vector4(310f / 1920f, 400f / 1920f, 190f / 1080f, 90f / 1080f);

        /// <param name="roomStepper">Room grid stepper for the open building (the Burst one), or null for the CPU reference.</param>
        public BuildingScene(BuildingSession session, Materials materials, ViewPalette palette, UiKit ui, HeatmapState heatmap,
            CameraController camera, BuildingInput input, VisualElement uiRoot, bool debugAvailable, Sim.Rooms.IRoomGridStepper? roomStepper = null)
        {
            Session = session;
            _heatmap = heatmap;
            _input = input;
            _palette = palette;
            _previousStepper = session.Rooms.Stepper;
            if (roomStepper != null) session.Rooms.Stepper = roomStepper;
            ViewState = new BuildingViewState(session.Layout, session.HasWater, session.HasGas)
            {
                WallCavityAvailable = session.Unlocked(session.Data.Balancing.Building.WallCavityTier) && session.Layout.Tier >= session.Data.Balancing.Building.WallCavityTier,
            };
            _shownFloor = ViewState.Floor;
            heatmap.SetFluid(ViewState.Fluid);
            _root = new GameObject("Building " + session.Layout.Id);
            _building = Child<BuildingRenderer>("Blueprint");
            _building.Build(session.Layout, palette, materials.Building, materials.Walls, materials.Overlay, ViewState.Floor);
            _rooms = Child<RoomHeatmapRenderer>("RoomHeatmap");
            _rooms.Init(materials.RoomHeat, session.Rooms.Grid, session.Layout.Width, session.Layout.Height);
            Ducts = Child<DuctRenderer>("Ducts");
            Ducts.Init(palette, heatmap, session.Data.Balancing.Drawing, session.Data.Balancing.Heatmap, materials.Ducts);
            _particles = Child<FlowParticles>("Particles");
            _particles.Init(session.Data.Balancing.Particles, materials.Particles);
            _equipment = Child<EquipmentLayer>("Equipment");
            _equipment.Init(session.Data, palette, materials.Equipment);

            Interaction = new BuildingInteraction(session, camera, input, View, ViewState);
            Hud = new BuildingHud(uiRoot, ui, session, Interaction, heatmap, camera) { DebugAvailable = debugAvailable };

            BuildingLayout l = session.Layout;
            camera.SetZoomLimits(session.Data.Balancing.Camera.ZoomMin, session.Data.Balancing.Camera.ZoomMax);
            camera.FrameInsets(new Rect(0, 0, l.Width, l.Height), HudInsets.x, HudInsets.y, HudInsets.z, HudInsets.w, 0.8f);
            session.SolveNow();
        }

        T Child<T>(string name) where T : Component
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, false);
            return go.AddComponent<T>();
        }

        /// <summary>
        /// Dev only (F4): bursts a gas pipe on the floor in view, because normal play cannot force a leak before Phase 6's
        /// boosters and the gate has to judge one. Returns false when the building has no built gas pipe.
        /// </summary>
        public bool StartDebugLeak()
        {
            BuildingSession s = Session;
            DuctNetworkState net = s.Committed.Gas;
            if (net.Segments.Count == 0) return false;
            DuctSegment seg = net.Segments[net.Segments.Count - 1];
            foreach (DuctSegment x in net.Segments)
                if (BuildingFailures.MidTile(net, x).F == ViewState.Floor) { seg = x; break; }
            List<string> edges = BuildingFailures.Edges(BuildingState.GasFluid, net, seg);
            if (edges.Count == 0) return false;
            s.Failures.AddLeak("burst", edges[0], BuildingFailures.MidTile(net, seg), s.Failures.SegmentArea(seg) * s.Data.Balancing.Gas.BurstLeakAreaFraction, s.Game.PlayTimeSeconds);
            s.SolveNow();
            return true;
        }

        /// <summary>Input → interaction and hotkeys; then HUD and duct mesh refresh. Call after the game loop ticked.</summary>
        public void Update(float dt, float time)
        {
            BuildingSession s = Session;
            bool overUi = Hud.IsPointerOverUi(_input.PointerPosition);
            Interaction.Update(overUi, dt);

            if (_input.Plain.WasPressedThisFrame()) _heatmap.SetMode(HeatmapMode.Plain);
            for (int i = 0; i < _input.HeatmapKeys.Length; i++)
                if (_input.HeatmapKeys[i].WasPressedThisFrame()) _heatmap.SetMode((HeatmapMode)(i + 1));
            if (_input.DebugOverlay.WasPressedThisFrame()) Hud.ToggleDebug();
            if (Hud.DebugAvailable && _input.DebugLeak.WasPressedThisFrame()) StartDebugLeak();
            if (_input.RoomLayer.WasPressedThisFrame()) _heatmap.CycleLayer();
            if (_input.FloorUp.WasPressedThisFrame()) ViewState.StepFloor(1);
            if (_input.FloorDown.WasPressedThisFrame()) ViewState.StepFloor(-1);
            if (_input.Layer.WasPressedThisFrame()) ViewState.CycleLayer();
            if (_input.Undo.WasPressedThisFrame()) s.Undo();
            if (_input.Redo.WasPressedThisFrame()) s.Redo();
            if (_input.Commit.WasPressedThisFrame()) s.Execute(new Game.Commands.CommitDesignCommand());
            if (_input.Sheet.WasPressedThisFrame()) Hud.ToggleSheet();
            if (_input.Crank.WasPressedThisFrame()) s.Crank();
            s.CrankHeld = _input.Crank.IsPressed() || Hud.CrankHeld;

            _heatmap.SetFluid(ViewState.Fluid);
            if (ViewState.Floor != _shownFloor)
            {
                _shownFloor = ViewState.Floor;
                _building.ShowFloor(_shownFloor);
            }
            if (s.StateRevision != _stateRev) Interaction.Validate();
            _equipment.Sync(s.Layout, s.State, ViewState.Floor, ViewState.Fluid);
            UpdateHeatmapRange();
            _rooms.Refresh(s.Rooms, _heatmap, _palette, (float)s.Data.Balancing.Heatmap.RoomAlpha, ViewState.Floor - s.Layout.LowestFloor);
            RebuildDuctsIfNeeded();
            Hud.Update(time);
        }

        /// <summary>True once when the player asked to go back to the building list.</summary>
        public bool ConsumeBack()
        {
            bool back = Interaction.BackRequested || Hud.BackRequested;
            Interaction.BackRequested = Hud.BackRequested = false;
            return back;
        }

        /// <summary>Reduce motion (settings): steady particle speed.</summary>
        public bool ReduceMotion
        {
            get => _particles.ReduceMotion;
            set => _particles.ReduceMotion = value;
        }

        public void LateUpdate(float dt)
        {
            _particles.Draw(View, dt);
            _equipment.Animate(Session, Session.Data.Balancing.Particles.FanSpinDegreesPerM3h);
        }

        /// <summary>The scale follows the network in focus (and the rooms, for modes that colour them).</summary>
        void UpdateHeatmapRange()
        {
            NetworkSnapshot? snap = Session.Current;
            FluidSolve? solve = snap?.For(ViewState.Fluid);
            if (solve == null || _heatmap.Mode == HeatmapMode.Plain || _heatmap.Mode == HeatmapMode.Stress) return;
            double min = double.MaxValue, max = double.MinValue, roomMax = 0;
            foreach (var kv in solve.Compiled.SegmentLink)
            {
                double v = DuctRenderer.Value(_heatmap.Mode, solve, kv.Value);
                min = System.Math.Min(min, v);
                max = System.Math.Max(max, v);
            }
            if (_heatmap.RoomsColoured)
            {
                Game.Thermal.BuildingClimate rooms = Session.Rooms;
                int n = rooms.Grid.Cells;
                for (int c = 0; c < n; c++)
                {
                    if (rooms.Grid.Room[c] < 0) continue;
                    double v = RoomHeatmapRenderer.Value(rooms, _heatmap, c, n);
                    if (_heatmap.Mode == HeatmapMode.Velocity) roomMax = System.Math.Max(roomMax, v);
                    else
                    {
                        min = System.Math.Min(min, v);
                        max = System.Math.Max(max, v);
                    }
                }
            }
            if (min > max) return;
            _heatmap.AutoRange(min, max, roomMax);
        }

        void RebuildDuctsIfNeeded()
        {
            BuildingSession s = Session;
            BuildingInteraction ix = Interaction;
            if (s.StateRevision == _stateRev && s.SolveRevision == _solveRev && ix.Revision == _ixRev && _heatmap.Revision == _hmRev && ViewState.Revision == _viewRev) return;
            _stateRev = s.StateRevision;
            _solveRev = s.SolveRevision;
            _ixRev = ix.Revision;
            _hmRev = _heatmap.Revision;
            _viewRev = ViewState.Revision;

            View.Floor = ViewState.Floor;
            View.CeilingInFocus = ViewState.Layer != ViewLayer.Floor;
            View.InvalidPath = null;
            DrawPreview? pv = ix.Preview;
            bool preview = ix.IsDrawing && pv?.Snapshot != null;
            NetworkSnapshot? snap = preview ? pv!.Snapshot : s.Current;
            View.Snapshot = snap;
            BuildingState state = preview ? pv!.Snapshot!.State : s.State;
            foreach (NetworkView v in View.Views)
            {
                v.Network = state.Net(v.Fluid);
                v.Solve = snap?.For(v.Fluid);
                v.Active = v.Fluid == ViewState.Fluid;
                v.BurstSegments.Clear();
                foreach (int burst in s.Failures.BurstSegments(v.Fluid, v.Network)) v.BurstSegments.Add(burst);
                v.GhostSegments.Clear();
                if (preview)
                    foreach (DuctSegment seg in v.Network.Segments)
                        if (s.State.Net(v.Fluid).FindSegment(seg.Id) == null) v.GhostSegments.Add(seg.Id);
            }
            if (ix.IsDrawing && !preview && ix.Path.Count > 1) View.InvalidPath = new List<TilePos>(ix.Path);
            View.SelectionFluid = ix.Selected.Fluid ?? "air";
            View.HoverFluid = ix.Hover.Fluid ?? "air";
            View.SelectedSegments.Clear();
            foreach (int id in ix.SelectedSegments) View.SelectedSegments.Add(id);
            View.SelectedNode = ix.Selected.Kind == TargetKind.Node || ix.Selected.Kind == TargetKind.Inline ? ix.Selected.Id : 0;
            View.HoverSegment = ix.Hover.Kind == TargetKind.Segment ? ix.Hover.Id : 0;
            View.HoverNode = ix.Hover.Kind == TargetKind.Node || ix.Hover.Kind == TargetKind.Inline ? ix.Hover.Id : 0;
            View.SelectedTile = TileOf(ix.Selected);
            View.HoverTile = TileOf(ix.Hover);
            View.Box = ix.IsBoxSelecting ? ix.BoxWorld : (Rect?)null;
            View.Placing = ix.PlaceTool.Length > 0 && ix.PlaceHovering;
            View.PlaceTile = ix.PlaceTile;
            View.PlaceValid = ix.PlaceValid;
            View.Stress ??= (fluid, seg) => s.StressShown(fluid, seg);
            View.Surging ??= fluid => s.Failures.SurgePa(fluid) > 0;
            View.ViolationTiles.Clear();
            foreach (Game.Codes.Violation violation in s.DesignEvaluation.Violations) View.ViolationTiles.AddRange(violation.Tiles);
            View.LeakTiles.Clear();
            foreach (Game.Failures.LeakState leak in s.Record.Failures.Leaks) View.LeakTiles.Add(leak.Pos);
            Ducts.Rebuild(View);
        }

        /// <summary>The tile of an opening, outlet or inline part target on the floor on show (null for pipes and fittings).</summary>
        TilePos? TileOf(Target t)
        {
            TilePos? tile = t.Kind switch
            {
                TargetKind.Intake => Session.Layout.FindOpening(t.Anchor) is { } o ? BuildingLayout.PosOf(o) : (TilePos?)null,
                TargetKind.Outlet => Session.Layout.FindOutlet(t.Anchor) is { } v ? BuildingLayout.PosOf(v) : (TilePos?)null,
                TargetKind.Inline => Session.State.Net(t.Fluid ?? "air").FindNode(t.Id)?.Pos,
                TargetKind.Safety => t.Tile,
                _ => null,
            };
            return tile.HasValue && tile.Value.F == ViewState.Floor ? tile : null;
        }

        public void Dispose()
        {
            if (Session.Rooms.Stepper is Jobs.BurstRoomGridStepper burst) burst.Release(Session.Rooms.State);
            Session.Rooms.Stepper = _previousStepper;
            Session.Leave();
            Hud.Dispose();
            Object.Destroy(_root);
        }
    }

    /// <summary>The flat-layer materials shared by every building view.</summary>
    public sealed class Materials
    {
        public Material Building = null!, Walls = null!, RoomHeat = null!, Overlay = null!, Ducts = null!, Equipment = null!, Particles = null!;
    }
}
