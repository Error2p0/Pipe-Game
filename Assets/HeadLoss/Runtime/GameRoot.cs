using HeadLoss.Core.Data;
using HeadLoss.Game;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.Session;
using HeadLoss.Unity.Data;
using HeadLoss.Unity.Interaction;
using HeadLoss.Unity.UI;
using HeadLoss.Unity.View;
using UnityEngine;
using UnityEngine.UIElements;

namespace HeadLoss.Unity
{
    /// <summary>
    /// Main scene entry point. Loads data, opens the Phase 1 test house and owns the frame loop: input →
    /// commands, <see cref="GameLoop.Tick"/>, then rendering. No static state (Fast Enter Play Mode).
    /// </summary>
    public sealed class GameRoot : MonoBehaviour
    {
        [SerializeField] DataCatalog? catalog;
        [SerializeField] PanelSettings? panelSettings;
        [SerializeField] StyleSheet? styleSheet;
        [SerializeField] Shader? unlitShader;
        [SerializeField] string buildingId = "test-house";

        GameLoop? _loop;
        BuildingSession? _session;
        BuildingInput? _input;
        CameraController? _camera;
        BuildingRenderer? _building;
        DuctRenderer? _ducts;
        FlowParticles? _particles;
        FanView? _fan;
        BuildingInteraction? _interaction;
        BuildingHud? _hud;
        HeatmapState? _heatmap;
        readonly System.Collections.Generic.List<Material> _materials = new System.Collections.Generic.List<Material>();
        readonly DuctViewInput _view = new DuctViewInput();
        int _stateRev = -1, _solveRev = -1, _ixRev = -1, _hmRev = -1;

        public BuildingSession? Session => _session;
        public BuildingInteraction? Interaction => _interaction;
        public HeatmapState? Heatmap => _heatmap;
        public DuctRenderer? Ducts => _ducts;
        /// <summary>What the duct layer showed at its last rebuild.</summary>
        public DuctViewInput View => _view;
        /// <summary>Session solve revision the duct layer last rebuilt for.</summary>
        public int RenderedSolveRevision => _solveRev;

        void Awake()
        {
            // UI text is English-only for now; format every number the same way as Core.Units (invariant).
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
            System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        }

        void Start()
        {
            if (catalog == null || panelSettings == null || styleSheet == null || unlitShader == null)
            {
                Debug.LogError("GameRoot is missing references. Run Head Loss/Setup/Create Scenes.");
                enabled = false;
                return;
            }
            DataRepository data = catalog.Load();
            var errors = DataValidator.Validate(data);
            foreach (string e in errors) Debug.LogError("Data: " + e);

            var context = new GameContext(data, GameContext.NewGame(data));
            _loop = new GameLoop(context);
            _session = context.Open(buildingId);
            var palette = new ViewPalette(data);
            _heatmap = new HeatmapState(data.Balancing.Heatmap);

            Material Layer(int layer, string name)
            {
                var m = new Material(unlitShader) { name = name, renderQueue = RenderLayers.Queue(layer) };
                _materials.Add(m);
                return m;
            }
            Material particleMaterial = Layer(RenderLayers.Particles, "HeadLoss Particles");
            particleMaterial.enableInstancing = true;
            Color particleTint = palette.Paper;
            particleTint.a = 0.9f;
            particleMaterial.SetColor("_Tint", particleTint);

            _camera = new GameObject("Camera").AddComponent<CameraController>();
            _camera.Init(data.Balancing.Camera, palette.Paper);
            // Leave room for the toolbox (left), readout (right) and heatmap selector (top).
            _camera.Frame(new Rect(-4, -1, _session.Layout.Width + 8, _session.Layout.Height + 3.2f), 0.3f);

            _building = new GameObject("Building").AddComponent<BuildingRenderer>();
            _building.Build(_session.Layout, palette, Layer(RenderLayers.Building, "HeadLoss Building"), Layer(RenderLayers.Overlay, "HeadLoss Overlay"));
            _ducts = new GameObject("Ducts").AddComponent<DuctRenderer>();
            _ducts.Init(palette, _heatmap, data.Balancing.Drawing, Layer(RenderLayers.Ducts, "HeadLoss Ducts"));
            _particles = new GameObject("Particles").AddComponent<FlowParticles>();
            _particles.Init(data.Balancing.Particles, particleMaterial);
            Material equipment = Layer(RenderLayers.Equipment, "HeadLoss Equipment");
            foreach (var o in _session.Layout.EntryOpenings)
            {
                if (_session.State.EquipmentAt(o.Id) == null) continue;
                _fan = new GameObject("Fan").AddComponent<FanView>();
                _fan.Build(DuctGeometry.Centre(new TilePos(o.X, o.Y)), palette, equipment);
            }

            _input = new BuildingInput();
            _interaction = new BuildingInteraction(_session, _camera, _input, _ducts.Geometry);

            var doc = gameObject.AddComponent<UIDocument>();
            doc.panelSettings = panelSettings;
            _hud = new BuildingHud(doc.rootVisualElement, styleSheet, palette, _session, _interaction, _heatmap, _camera)
            {
                DebugAvailable = Debug.isDebugBuild,
            };

            _session.SolveNow();
        }

        void Update()
        {
            if (_session == null || _input == null || _interaction == null || _hud == null || _heatmap == null || _loop == null) return;
            float dt = Time.deltaTime;
            bool overUi = _hud.IsPointerOverUi(_input.PointerPosition);
            _interaction.Update(overUi, dt);

            if (_input.Plain.WasPressedThisFrame()) _heatmap.SetMode(HeatmapMode.Plain);
            for (int i = 0; i < _input.HeatmapKeys.Length; i++)
                if (_input.HeatmapKeys[i].WasPressedThisFrame()) _heatmap.SetMode((HeatmapMode)(i + 1));
            if (_input.DebugOverlay.WasPressedThisFrame()) _hud.ToggleDebug();

            _loop.Tick(dt);

            if (_session.StateRevision != _stateRev) _interaction.Validate();
            UpdateHeatmapRange();
            RebuildDuctsIfNeeded();
            _hud.Update();
        }

        void LateUpdate()
        {
            if (_particles == null || _ducts == null) return;
            _particles.Draw(_ducts.Geometry, _view.Snapshot, Time.deltaTime);
            if (_fan != null && _session?.Current != null)
            {
                NetworkSnapshot snap = _session.Current;
                double q = 0;
                foreach (var kv in snap.Compiled.FanLink) q = snap.Result.Flow[kv.Value];
                _fan.Spin = (float)(HeadLoss.Core.Units.M3sToM3h(q) * _session.Data.Balancing.Particles.FanSpinDegreesPerM3h);
            }
        }

        void UpdateHeatmapRange()
        {
            NetworkSnapshot? snap = _session!.Current;
            if (snap == null) return;
            double max = 0;
            foreach (var kv in snap.Compiled.SegmentLink) max = System.Math.Max(max, System.Math.Abs(snap.Result.Velocity[kv.Value]));
            _heatmap!.AutoRange(max);
        }

        void RebuildDuctsIfNeeded()
        {
            BuildingSession s = _session!;
            BuildingInteraction ix = _interaction!;
            if (s.StateRevision == _stateRev && s.SolveRevision == _solveRev && ix.Revision == _ixRev && _heatmap!.Revision == _hmRev) return;
            _stateRev = s.StateRevision;
            _solveRev = s.SolveRevision;
            _ixRev = ix.Revision;
            _hmRev = _heatmap!.Revision;

            _view.GhostSegments.Clear();
            _view.InvalidPath = null;
            DrawPreview? pv = ix.Preview;
            if (ix.IsDrawing && pv?.Snapshot != null)
            {
                // Simulate before you buy: show the whole network as if the run were placed.
                _view.Network = pv.Snapshot.State.Air;
                _view.Snapshot = pv.Snapshot;
                foreach (DuctSegment seg in pv.Snapshot.State.Air.Segments)
                    if (s.State.Air.FindSegment(seg.Id) == null) _view.GhostSegments.Add(seg.Id);
            }
            else
            {
                _view.Network = s.State.Air;
                _view.Snapshot = s.Current;
                if (ix.IsDrawing && ix.Path.Count > 1) _view.InvalidPath = new System.Collections.Generic.List<TilePos>(ix.Path);
            }
            _view.SelectedSegments.Clear();
            foreach (int id in ix.SelectedSegments) _view.SelectedSegments.Add(id);
            _view.SelectedNode = ix.Selected.Kind == TargetKind.Node ? ix.Selected.Id : 0;
            _view.HoverSegment = ix.Hover.Kind == TargetKind.Segment ? ix.Hover.Id : 0;
            _view.HoverNode = ix.Hover.Kind == TargetKind.Node ? ix.Hover.Id : 0;
            _ducts!.Rebuild(_view);
        }

        void OnDestroy()
        {
            _input?.Dispose();
            foreach (Material m in _materials) Destroy(m);
        }
    }
}
