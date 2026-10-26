using System.Collections.Generic;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.Session;
using HeadLoss.Unity.Interaction;
using HeadLoss.Unity.UI;
using HeadLoss.Unity.View;
using UnityEngine;
using UnityEngine.UIElements;

namespace HeadLoss.Unity
{
    /// <summary>
    /// Everything shown for the open building: blueprint, ducts, particles, equipment symbols, interaction and
    /// HUD. Created when a building opens and disposed when the player leaves it.
    /// </summary>
    public sealed class BuildingScene
    {
        public BuildingSession Session { get; }
        public BuildingInteraction Interaction { get; }
        public BuildingHud Hud { get; }
        public DuctRenderer Ducts { get; }
        /// <summary>What the duct layer showed at its last rebuild.</summary>
        public DuctViewInput View { get; } = new DuctViewInput();
        /// <summary>Session solve revision the duct layer last rebuilt for.</summary>
        public int RenderedSolveRevision => _solveRev;

        readonly GameObject _root;
        readonly BuildingRenderer _building;
        readonly FlowParticles _particles;
        readonly EquipmentLayer _equipment;
        readonly HeatmapState _heatmap;
        readonly BuildingInput _input;
        int _stateRev = -1, _solveRev = -1, _ixRev = -1, _hmRev = -1;

        /// <summary>HUD screen-fraction insets (left, right, top, bottom) at the 1920×1080 reference.</summary>
        public static readonly Vector4 HudInsets = new Vector4(310f / 1920f, 400f / 1920f, 190f / 1080f, 90f / 1080f);

        public BuildingScene(BuildingSession session, Materials materials, ViewPalette palette, UiKit ui, HeatmapState heatmap,
            CameraController camera, BuildingInput input, VisualElement uiRoot, bool debugAvailable)
        {
            Session = session;
            _heatmap = heatmap;
            _input = input;
            _root = new GameObject("Building " + session.Layout.Id);
            _building = Child<BuildingRenderer>("Blueprint");
            _building.Build(session.Layout, palette, materials.Building, materials.Overlay);
            Ducts = Child<DuctRenderer>("Ducts");
            Ducts.Init(palette, heatmap, session.Data.Balancing.Drawing, materials.Ducts);
            _particles = Child<FlowParticles>("Particles");
            _particles.Init(session.Data.Balancing.Particles, materials.Particles);
            _equipment = Child<EquipmentLayer>("Equipment");
            _equipment.Init(session.Data, palette, materials.Equipment);

            Interaction = new BuildingInteraction(session, camera, input, Ducts.Geometry);
            Hud = new BuildingHud(uiRoot, ui, session, Interaction, heatmap, camera) { DebugAvailable = debugAvailable };

            BuildingLayout l = session.Layout;
            camera.FrameInsets(new Rect(0, 0, l.Width, l.Height), HudInsets.x, HudInsets.y, HudInsets.z, HudInsets.w, 0.8f);
            session.SolveNow();
        }

        T Child<T>(string name) where T : Component
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, false);
            return go.AddComponent<T>();
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
            if (_input.Undo.WasPressedThisFrame()) s.Undo();
            if (_input.Redo.WasPressedThisFrame()) s.Redo();
            if (_input.Commit.WasPressedThisFrame()) s.Execute(new Game.Commands.CommitDesignCommand());
            if (_input.Sheet.WasPressedThisFrame()) Hud.ToggleSheet();
            if (_input.Crank.WasPressedThisFrame()) s.Crank();
            s.CrankHeld = _input.Crank.IsPressed() || Hud.CrankHeld;

            if (s.StateRevision != _stateRev) Interaction.Validate();
            _equipment.Sync(s.Layout, s.State);
            UpdateHeatmapRange();
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

        public void LateUpdate(float dt)
        {
            _particles.Draw(Ducts.Geometry, View.Snapshot, dt);
            _equipment.Animate(Session, Session.Data.Balancing.Particles.FanSpinDegreesPerM3h);
        }

        void UpdateHeatmapRange()
        {
            NetworkSnapshot? snap = Session.Current;
            if (snap == null) return;
            double max = 0;
            foreach (var kv in snap.Compiled.SegmentLink) max = System.Math.Max(max, System.Math.Abs(snap.Result.Velocity[kv.Value]));
            _heatmap.AutoRange(max);
        }

        void RebuildDuctsIfNeeded()
        {
            BuildingSession s = Session;
            BuildingInteraction ix = Interaction;
            if (s.StateRevision == _stateRev && s.SolveRevision == _solveRev && ix.Revision == _ixRev && _heatmap.Revision == _hmRev) return;
            _stateRev = s.StateRevision;
            _solveRev = s.SolveRevision;
            _ixRev = ix.Revision;
            _hmRev = _heatmap.Revision;

            View.GhostSegments.Clear();
            View.InvalidPath = null;
            DrawPreview? pv = ix.Preview;
            if (ix.IsDrawing && pv?.Snapshot != null)
            {
                // Simulate before you buy: show the whole network as if the run were placed.
                View.Network = pv.Snapshot.State.Air;
                View.Snapshot = pv.Snapshot;
                foreach (DuctSegment seg in pv.Snapshot.State.Air.Segments)
                    if (s.State.Air.FindSegment(seg.Id) == null) View.GhostSegments.Add(seg.Id);
            }
            else
            {
                View.Network = s.State.Air;
                View.Snapshot = s.Current;
                if (ix.IsDrawing && ix.Path.Count > 1) View.InvalidPath = new List<TilePos>(ix.Path);
            }
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
            Ducts.Rebuild(View);
        }

        /// <summary>The tile of an intake, outlet or inline part target (null for ducts and fittings).</summary>
        TilePos? TileOf(Target t)
        {
            switch (t.Kind)
            {
                case TargetKind.Intake:
                    var o = Session.Layout.FindOpening(t.Anchor);
                    return o != null ? new TilePos(o.X, o.Y) : (TilePos?)null;
                case TargetKind.Outlet:
                    var v = Session.Layout.FindOutlet(t.Anchor);
                    return v != null ? new TilePos(v.X, v.Y) : (TilePos?)null;
                case TargetKind.Inline:
                    return Session.State.Air.FindNode(t.Id)?.Pos;
                default:
                    return null;
            }
        }

        public void Dispose()
        {
            Session.Leave();
            Hud.Dispose();
            Object.Destroy(_root);
        }
    }

    /// <summary>The flat-layer materials shared by every building view.</summary>
    public sealed class Materials
    {
        public Material Building = null!, Overlay = null!, Ducts = null!, Equipment = null!, Particles = null!;
    }
}
