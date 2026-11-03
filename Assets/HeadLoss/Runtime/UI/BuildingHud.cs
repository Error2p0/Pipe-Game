using System.Collections.Generic;
using System.Text;
using HeadLoss.Core;
using HeadLoss.Core.Data;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.Codes;
using HeadLoss.Game.Commands;
using HeadLoss.Game.Economy;
using HeadLoss.Game.Failures;
using HeadLoss.Game.Progression;
using HeadLoss.Game.Networks;
using HeadLoss.Game.Session;
using HeadLoss.Sim.Network;
using HeadLoss.Unity.Interaction;
using HeadLoss.Unity.View;
using UnityEngine;
using UnityEngine.UIElements;

namespace HeadLoss.Unity.UI
{
    /// <summary>
    /// Building-view HUD (UI Toolkit): title and status line, design bar (Commit / Discard, undo / redo),
    /// toolbox (duct size, elbows, parts, hand crank), outlet list with live zones, requirement sheet, heatmap
    /// selector and legend, readout/selection panel with actions, world labels (vent deliveries, open ends,
    /// fitting K and Δp while drawing), hint line and the dev debug overlay. Text is rebuilt only when its
    /// inputs change; label positions follow the camera every frame.
    /// </summary>
    public sealed class BuildingHud
    {
        const int ReadoutLines = 14;

        readonly VisualElement _root, _layer;
        readonly UiKit _ui;
        readonly ViewPalette _p;
        readonly BuildingSession _s;
        readonly BuildingInteraction _ix;
        readonly HeatmapState _hm;
        readonly CameraController _cam;
        readonly DataRepository _data;
        readonly string _symbol;

        Label _title = null!, _status = null!, _hint = null!;
        Label _designText = null!, _designReason = null!;
        Button _commit = null!, _discard = null!, _undo = null!, _redo = null!;
        DropdownField _size = null!, _material = null!;
        Label _lockNote = null!;
        Button _sharp = null!, _smooth = null!, _upgrade = null!;
        int _shownTier = -1;
        readonly List<string> _materials = new List<string>();
        Slider? _speedSlider, _settingSlider;
        readonly List<(string id, Button button)> _partButtons = new List<(string, Button)>();
        readonly List<(string fluid, Button button)> _fluidButtons = new List<(string, Button)>();
        readonly List<(int floor, Button button)> _floorButtons = new List<(int, Button)>();
        Button _layerFloor = null!, _layerCeiling = null!, _layerCavity = null!;
        Label _ductHeader = null!, _partsHeader = null!, _protectHeader = null!;
        Button _sensors = null!, _contract = null!;
        string _shownFluid = "";
        int _viewRev = -1;
        VisualElement _violations = null!, _violationRows = null!, _stressTrack = null!, _stressFill = null!;
        Label _violationHead = null!;
        string _shownViolations = "";
        BuildingViewState View => _ix.View;
        VisualElement _crankBox = null!, _crankFill = null!, _crankButton = null!;
        readonly Button[] _modeButtons = new Button[8];
        Label _legendTitle = null!, _legendMin = null!, _legendMid = null!, _legendMax = null!;
        VisualElement _legendBar = null!, _legendTicks = null!;
        Button _lock = null!;
        Texture2D? _legendTexture;
        string _legendPalette = "";
        VisualElement _readout = null!, _readoutActions = null!;
        Label _readoutTitle = null!;
        readonly Label[] _lines = new Label[ReadoutLines];
        OutletList _outlets = null!;
        RequirementSheet _sheet = null!;
        VisualElement _labelLayer = null!;
        readonly List<Label> _ventLabels = new List<Label>();
        readonly List<Label> _openLabels = new List<Label>();
        readonly List<Vector2> _openLabelWorld = new List<Vector2>();
        int _openLabelCount;
        readonly List<Label> _fittingLabels = new List<Label>();
        readonly List<Vector2> _fittingLabelWorld = new List<Vector2>();
        int _fittingLabelCount;
        VisualElement _debug = null!;
        Label _debugText = null!;
        VisualElement _debugRows = null!;
        readonly List<Label[]> _debugCells = new List<Label[]>();
        static readonly string[] DebugHeader = { "link", "kind", "Q m³/h", "v m/s", "Re", "f", "ΣK", "Δp fr Pa", "Δp fit Pa", "p from Pa", "p to Pa" };
        static readonly float[] DebugWidths = { 150, 50, 70, 60, 70, 64, 50, 76, 76, 80, 76 };
        public bool DebugAvailable { get; set; }
        bool _debugVisible;

        int _solveRev = -1, _ixRev = -1, _hmRev = -1, _stateRev = -1, _roomsRev = -1;
        float _nextMoneyRefresh;
        string _shownActionsKey = "";
        Slider? _damperSlider;
        readonly List<double> _sizes = new List<double>();
        readonly StringBuilder _sb = new StringBuilder(4096);

        /// <summary>The on-screen crank button is held down.</summary>
        public bool CrankHeld { get; private set; }
        /// <summary>The Buildings (back) button was pressed.</summary>
        public bool BackRequested;
        /// <summary>The Shop button was pressed.</summary>
        public bool ShopRequested;
        /// <summary>The unlock condition of a tier with progress (set by the game root, which sees every building).</summary>
        public System.Func<int, string>? UnlockCondition;

        public BuildingHud(VisualElement root, UiKit ui, BuildingSession session, BuildingInteraction interaction,
            HeatmapState heatmap, CameraController camera)
        {
            _root = root;
            _ui = ui;
            _p = ui.P;
            _s = session;
            _ix = interaction;
            _hm = heatmap;
            _cam = camera;
            _data = session.Data;
            _symbol = _data.Game.CurrencySymbol;
            _layer = new VisualElement { pickingMode = PickingMode.Ignore };
            _layer.AddToClassList("hl-root");
            root.Add(_layer);
            Build();
            if (!session.Record.SheetSeen) _sheet.Show(true);
        }

        /// <summary>Removes the HUD (leaving the building).</summary>
        public void Dispose()
        {
            _layer.RemoveFromHierarchy();
            if (_legendTexture != null) Object.Destroy(_legendTexture);
        }

        // ---------- construction ----------

        void Build()
        {
            _labelLayer = new VisualElement { pickingMode = PickingMode.Ignore };
            _labelLayer.style.position = Position.Absolute;
            _labelLayer.style.left = _labelLayer.style.top = _labelLayer.style.right = _labelLayer.style.bottom = 0;
            _layer.Add(_labelLayer);

            var left = new VisualElement { pickingMode = PickingMode.Ignore };
            left.AddToClassList("hl-leftcolumn");
            _layer.Add(left);
            VisualElement card = _ui.Panel(left, "hl-titlecard");
            _title = UiKit.Text(card, _s.Layout.DisplayName, "hl-title");
            _status = UiKit.Text(card, "", "hl-subtitle");
            _status.style.whiteSpace = WhiteSpace.Normal;
            VisualElement cardRow = UiKit.Row(card);
            _ui.Button(cardRow, "Requirements (Tab)", () => _sheet.Toggle());
            _ui.Button(cardRow, _s.Game.City != null ? "City (Esc)" : "Buildings (Esc)", () => BackRequested = true);
            _ui.Button(cardRow, "Shop", () => ShopRequested = true);

            BuildViewBar(left);

            VisualElement design = _ui.Panel(_layer, "hl-designbar");
            _designText = UiKit.Text(design, "", "hl-design-text");
            VisualElement designRow = UiKit.Row(design);
            designRow.style.justifyContent = Justify.Center;
            _commit = _ui.Button(designRow, "Commit (Enter)", () => _s.Execute(new CommitDesignCommand()));
            _discard = _ui.Button(designRow, "Discard", () => _s.Execute(new DiscardDesignCommand()));
            _undo = _ui.Button(designRow, "Undo", () => _s.Undo());
            _redo = _ui.Button(designRow, "Redo", () => _s.Redo());
            _designReason = UiKit.Text(design, "", "hl-design-reason");
            _designReason.style.color = _p.Warning;

            BuildToolbox(left);

            VisualElement modes = new VisualElement();
            modes.AddToClassList("hl-heatmaps");
            _layer.Add(modes);
            for (int i = 0; i < 8; i++)
            {
                var mode = (HeatmapMode)i;
                Button b = _ui.Button(modes, $"{i} {HeatmapState.DisplayName(mode)}", () => _hm.SetMode(mode));
                b.SetEnabled(HeatmapState.IsAvailable(mode));
                _modeButtons[i] = b;
            }

            VisualElement legend = _ui.Panel(_layer, "hl-legend");
            var legendHead = UiKit.Row(legend);
            legendHead.style.justifyContent = Justify.SpaceBetween;
            legendHead.style.alignItems = Align.Center;
            _legendTitle = UiKit.Text(legendHead, "", "hl-section");
            _lock = _ui.Button(legendHead, "Lock range", () => _hm.SetLocked(!_hm.Locked));
            _legendBar = new VisualElement();
            _legendBar.AddToClassList("hl-legend-bar");
            legend.Add(_legendBar);
            _legendTicks = new VisualElement();
            _legendTicks.AddToClassList("hl-legend-ticks");
            _legendMin = UiKit.Text(_legendTicks, "", "hl-subtitle");
            _legendMid = UiKit.Text(_legendTicks, "", "hl-subtitle");
            _legendMax = UiKit.Text(_legendTicks, "", "hl-subtitle");
            legend.Add(_legendTicks);

            var right = new VisualElement { pickingMode = PickingMode.Ignore };
            right.AddToClassList("hl-rightcolumn");
            _layer.Add(right);
            _outlets = new OutletList(right, _ui, _s);
            _violations = _ui.Panel(right, "hl-violations");
            _violationHead = UiKit.Text(_violations, "", "hl-section");
            _violationHead.style.color = _p.Danger;
            _violationRows = new VisualElement();
            _violations.Add(_violationRows);
            _violations.style.display = DisplayStyle.None;
            _readout = _ui.Panel(right, "hl-readout");
            _readoutTitle = UiKit.Text(_readout, "", "hl-readout-title");
            for (int i = 0; i < ReadoutLines; i++) _lines[i] = UiKit.Text(_readout, "", "hl-readout-line");
            _stressTrack = new VisualElement();
            _stressTrack.AddToClassList("hl-stress-track");
            _stressTrack.style.backgroundColor = _p.GridLines;
            _stressFill = new VisualElement { pickingMode = PickingMode.Ignore };
            _stressFill.AddToClassList("hl-stress-fill");
            _stressTrack.Add(_stressFill);
            _readout.Add(_stressTrack);
            _stressTrack.style.display = DisplayStyle.None;
            _readoutActions = UiKit.Row(_readout);
            _readoutActions.style.marginTop = 8;

            _hint = UiKit.Text(_layer, "", "hl-hint");
            _hint.pickingMode = PickingMode.Ignore;

            _sheet = new RequirementSheet(_layer, _ui, _s);

            _debug = _ui.Panel(_layer, "hl-debug");
            _debugText = new Label();
            _debugText.AddToClassList("hl-debug-text");
            _debug.Add(_debugText);
            var scroll = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
            scroll.style.flexGrow = 1;
            scroll.style.marginTop = 6;
            _debugRows = scroll;
            _debug.Add(scroll);
            _debug.style.display = DisplayStyle.None;

            foreach (OutletPlacementDef _ in _s.Layout.Outlets) _ventLabels.Add(WorldLabel());
        }

        /// <summary>Network tabs, floor tabs and the layer switcher, always visible (<c>11-ui-visuals-audio.md</c> §Building controls).</summary>
        void BuildViewBar(VisualElement parent)
        {
            VisualElement bar = _ui.Panel(parent, "hl-viewbar");
            if (View.Fluids.Count > 1)
            {
                VisualElement row = UiKit.Row(bar);
                foreach (string f in View.Fluids)
                {
                    string fluid = f;
                    _fluidButtons.Add((f, _ui.Button(row, _data.Fluid(f).DisplayName, () => _ix.SetFluid(fluid))));
                }
            }
            if (_s.Layout.Floors > 1)
            {
                VisualElement row = UiKit.Row(bar);
                for (int f = _s.Layout.TopFloor; f >= _s.Layout.LowestFloor; f--)
                {
                    int floor = f;
                    _floorButtons.Add((f, _ui.Button(row, f == 0 ? "G" : f < 0 ? "B" : f.ToString(), () => View.SetFloor(floor))));
                }
                UiKit.Text(bar, "PageUp / PageDown", "hl-subtitle");
            }
            VisualElement layers = UiKit.Row(bar);
            _layerFloor = _ui.Button(layers, "Floor", () => View.SetLayer(ViewLayer.Floor));
            _layerCeiling = _ui.Button(layers, "Ceiling", () => View.SetLayer(ViewLayer.Ceiling));
            _layerCavity = _ui.Button(layers, "Wall", () => View.SetLayer(ViewLayer.WallCavity));
            _layerCavity.SetEnabled(View.WallCavityAvailable);
        }

        void BuildToolbox(VisualElement parent)
        {
            VisualElement toolPanel = _ui.Panel(parent, "hl-toolbox");
            var tools = new ScrollView(ScrollViewMode.Vertical);
            tools.style.flexShrink = 1;
            toolPanel.Add(tools);
            _ductHeader = UiKit.Text(tools, "Duct", "hl-section");
            _size = new DropdownField();
            _size.AddToClassList("hl-dropdown");
            _ui.StyleField(_size);
            _size.RegisterValueChangedCallback(_ => { if (_size.index >= 0) _ix.DrawDiameterMm = _sizes[_size.index]; });
            tools.Add(_size);
            _material = new DropdownField();
            _material.AddToClassList("hl-dropdown");
            _ui.StyleField(_material);
            _material.RegisterValueChangedCallback(_ =>
            {
                if (_material.index < 0) return;
                _ix.DrawMaterialId = _materials[_material.index];
                RebuildDrawChoices();
            });
            tools.Add(_material);

            UiKit.Text(tools, "Fittings (elbows and tees)", "hl-section");
            VisualElement row = UiKit.Row(tools);
            _sharp = _ui.Button(row, "Sharp", () => SetSmooth(false));
            _smooth = _ui.Button(row, "Smooth", () => SetSmooth(true));
            _upgrade = _ui.Button(tools, "Upgrade all fittings", () => _s.Execute(new UpgradeAllBendsCommand(View.Fluid)));
            _lockNote = UiKit.Text(tools, "", "hl-subtitle");

            _partsHeader = UiKit.Text(tools, "Parts (click a straight duct)", "hl-section");
            foreach (string id in _data.Game.Toolbox.Parts)
            {
                string label = PartLabel(id);
                string part = id;
                Button b = _ui.Button(tools, label, () => _ix.SetPlaceTool(part));
                _partButtons.Add((id, b));
            }

            _protectHeader = UiKit.Text(tools, "Protection", "hl-section");
            _sensors = _ui.Button(tools, "", () => _s.BuyLeakSensors(View.Fluid));
            _contract = _ui.Button(tools, "", () => _s.SetMaintenanceContract(!_s.Record.Failures.MaintenanceContract, _s.Record.Failures.PreferEmergency));

            _crankBox = new VisualElement();
            tools.Add(_crankBox);
            UiKit.Text(_crankBox, "Hand crank", "hl-section");
            _crankButton = new VisualElement();
            _crankButton.AddToClassList("hl-button");
            _crankButton.AddToClassList("hl-crank");
            _crankButton.style.backgroundColor = _p.Paper;
            UiKit.Border(_crankButton, _p.Walls);
            _crankFill = new VisualElement { pickingMode = PickingMode.Ignore };
            _crankFill.AddToClassList("hl-crank-fill");
            Color fill = _p.Good;
            fill.a = 0.35f;
            _crankFill.style.backgroundColor = fill;
            _crankButton.Add(_crankFill);
            var crankLabel = new Label("Crank (hold C)") { pickingMode = PickingMode.Ignore };
            crankLabel.AddToClassList("hl-crank-label");
            _crankButton.Add(crankLabel);
            _crankButton.RegisterCallback<PointerDownEvent>(e =>
            {
                _s.Crank();
                CrankHeld = true;
                _crankButton.CapturePointer(e.pointerId);
            });
            _crankButton.RegisterCallback<PointerUpEvent>(e =>
            {
                CrankHeld = false;
                _crankButton.ReleasePointer(e.pointerId);
            });
            _crankButton.RegisterCallback<PointerCaptureOutEvent>(_ => CrankHeld = false);
            _crankBox.Add(_crankButton);
        }

        string PartLabel(string id)
        {
            EquipmentDef? eq = _data.FindEquipment(id);
            if (eq != null) return $"{eq.DisplayName} · {Units.FormatMoney(eq.Price, _symbol)}";
            FittingDef f = _data.Fitting(id);
            return $"{f.DisplayName} · from {Units.FormatMoney(f.Price, _symbol)}";
        }

        Label WorldLabel()
        {
            var l = new Label { pickingMode = PickingMode.Ignore };
            l.AddToClassList("hl-world-label");
            Color bg = _p.Paper;
            bg.a = 0.9f;
            l.style.backgroundColor = bg;
            l.style.color = _p.Text;
            _labelLayer.Add(l);
            return l;
        }

        void SetSmooth(bool smooth)
        {
            if (smooth && !_s.Unlocked(Tech.SmoothFittingsTier(_data))) return;
            _ix.DrawSmoothElbows = smooth;
            _hmRev = -1;
        }

        /// <summary>The duct sizes and materials on offer: only unlocked ones (<c>03-city.md</c> §Progression).</summary>
        void RebuildDrawChoices()
        {
            string fluid = View.Fluid;
            _materials.Clear();
            var matChoices = new List<string>();
            foreach (MaterialDef m in _data.Materials)
            {
                if (!m.Fluids.Contains(fluid) || !_s.Unlocked(m.Tier)) continue;
                _materials.Add(m.Id);
                matChoices.Add(m.DisplayName);
            }
            _material.choices = matChoices;
            if (_materials.Count == 0)
            {
                // The fluid is not unlocked yet: the building can be inspected but not plumbed (03 §Progression).
                _size.choices = new List<string>();
                _size.SetValueWithoutNotify("");
                _material.SetValueWithoutNotify("");
                _sizes.Clear();
                return;
            }
            if (!_materials.Contains(_ix.DrawMaterialId)) _ix.DrawMaterialId = _materials[0];
            _material.SetValueWithoutNotify(matChoices[_materials.IndexOf(_ix.DrawMaterialId)]);

            _sizes.Clear();
            var sizeChoices = new List<string>();
            PipeFamilyDef family = _data.PipeFamily(fluid == "air" ? _data.Balancing.Drawing.DefaultDuctFamily : "pipe");
            MaterialDef mat = _data.Material(_ix.DrawMaterialId);
            foreach (PipeSizeDef sz in family.Sizes)
            {
                if (!_s.Unlocked(sz.Tier)) continue;
                _sizes.Add(sz.DiameterMm);
                string rating = fluid == "air" ? "" : $" · rated {Units.FormatBar(mat.RatedPressure(sz.DiameterMm))}";
                sizeChoices.Add($"{sz.DiameterMm:0} mm · {Units.FormatMoney(Pricing.PerMetre(_data, _ix.DrawMaterialId, sz.DiameterMm), _symbol)}/m{rating}");
            }
            if (_sizes.Count == 0) return;
            if (!_sizes.Contains(_ix.DrawDiameterMm)) _ix.DrawDiameterMm = fluid == "air" && _sizes.Contains(_data.Balancing.Drawing.DefaultDuctSizeMm) ? _data.Balancing.Drawing.DefaultDuctSizeMm : _sizes[0];
            _size.choices = sizeChoices;
            _size.SetValueWithoutNotify(sizeChoices[_sizes.IndexOf(_ix.DrawDiameterMm)]);
        }

        // ---------- per frame ----------

        /// <summary>True when the pointer (Input System screen coordinates, origin bottom-left) is over a HUD element.</summary>
        public bool IsPointerOverUi(Vector2 screen)
        {
            IPanel? panel = _root.panel;
            if (panel == null) return false;
            Vector2 pos = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screen.x, Screen.height - screen.y));
            VisualElement picked = panel.Pick(pos);
            return picked != null;
        }

        public void ToggleDebug()
        {
            if (!DebugAvailable) return;
            _debugVisible = !_debugVisible;
            _debug.style.display = _debugVisible ? DisplayStyle.Flex : DisplayStyle.None;
            _solveRev = -1;
        }

        public void ToggleSheet() => _sheet.Toggle();
        public bool SheetVisible => _sheet.Visible;

        public void Update(float time)
        {
            bool solveChanged = _s.SolveRevision != _solveRev;
            bool ixChanged = _ix.Revision != _ixRev;
            bool hmChanged = _hm.Revision != _hmRev;
            bool stateChanged = _s.StateRevision != _stateRev;
            bool viewChanged = View.Revision != _viewRev;
            _viewRev = View.Revision;
            if (viewChanged)
            {
                ixChanged = true;
                solveChanged = true;
            }
            bool roomsChanged = _s.Rooms.Revision != _roomsRev;
            _roomsRev = _s.Rooms.Revision;
            _solveRev = _s.SolveRevision;
            _ixRev = _ix.Revision;
            _hmRev = _hm.Revision;
            _stateRev = _s.StateRevision;

            if (_outlets.Clicked.Length > 0)
            {
                if (_s.Layout.FindOutlet(_outlets.Clicked) is OutletPlacementDef clicked)
                {
                    View.SetFloor(clicked.Floor);
                    _ix.Select(new Target { Kind = TargetKind.Outlet, Anchor = clicked.Id, Fluid = BuildingState.OutletFluid(clicked.Type) });
                }
                _outlets.Clicked = "";
            }
            if (solveChanged || stateChanged || hmChanged || ixChanged) RefreshTools();
            if (solveChanged || hmChanged) RefreshLegend();
            if (solveChanged || ixChanged || stateChanged || (roomsChanged && _ix.Hover.Kind == TargetKind.None)) RefreshReadout(stateChanged);
            if (solveChanged || ixChanged) RefreshFittingLabels();
            if (solveChanged || ixChanged || stateChanged) RefreshHint();
            if (solveChanged)
            {
                RefreshVentLabels();
                RefreshViolations();
                _outlets.Refresh();
            }
            if (solveChanged && _debugVisible) RefreshDebug();
            if (stateChanged || solveChanged || time >= _nextMoneyRefresh)
            {
                _nextMoneyRefresh = time + 0.1f;
                RefreshDesignBar();
                RefreshStatus();
            }
            _crankFill.style.width = Length.Percent((float)(_s.CrankSpeed * 100));
            PositionLabels();
        }

        void RefreshTools()
        {
            int tier = Tech.UnlockedTier(_s.Game);
            if (tier != _shownTier || _shownFluid != View.Fluid)
            {
                _shownTier = tier;
                _shownFluid = View.Fluid;
                bool air = View.Fluid == "air";
                RebuildDrawChoices();
                bool smoothOk = _s.Unlocked(Tech.SmoothFittingsTier(_data));
                _smooth.SetEnabled(smoothOk);
                _upgrade.SetEnabled(smoothOk);
                if (!smoothOk) _ix.DrawSmoothElbows = false;
                _ductHeader.text = air ? "Duct" : "Pipe";
                _partsHeader.text = air ? "Parts (click a straight duct)" : "Parts (click a straight pipe)";
                foreach ((string id, Button b) in _partButtons)
                {
                    int t = Tech.ItemTier(_data, id);
                    bool here = EquipmentFits.InlinePart(_data, id, View.Fluid) && t <= tier + 1;
                    b.style.display = here ? DisplayStyle.Flex : DisplayStyle.None;
                    b.SetEnabled(_s.Unlocked(t));
                    b.text = _s.Unlocked(t) ? PartLabel(id) : $"{PartLabel(id)} · tier {t}";
                }
                _shownActionsKey = "";
            }
            MetaUpgradeDef? sensors = _data.Upgrades.Meta.Find(m => m.Id == "leak-sensors"), contract = _data.Upgrades.Meta.Find(m => m.Id == "maintenance-contract");
            bool hasSensors = _s.Record.Failures.LeakSensors.Contains(View.Fluid);
            if (sensors != null)
            {
                bool ok = _s.Unlocked(sensors.Tier);
                _sensors.text = hasSensors ? "Leak sensors fitted" : ok ? $"Leak sensors · {Units.FormatMoney(sensors.Price, _symbol)}" : $"Leak sensors · tier {sensors.Tier}";
                _sensors.SetEnabled(ok && !hasSensors);
            }
            if (contract != null)
            {
                bool ok = _s.Unlocked(contract.Tier);
                _contract.text = _s.Record.Failures.MaintenanceContract ? $"Maintenance contract on · {contract.PremiumFraction * 100:0}% of gross" : ok ? $"Maintenance contract · {contract.PremiumFraction * 100:0}% of gross" : $"Maintenance contract · tier {contract.Tier}";
                _contract.SetEnabled(ok);
                StyleButton(_contract, _s.Record.Failures.MaintenanceContract);
            }
            foreach ((string f, Button b) in _fluidButtons) StyleButton(b, f == View.Fluid);
            foreach ((int f, Button b) in _floorButtons) StyleButton(b, f == View.Floor);
            StyleButton(_layerFloor, View.Layer == ViewLayer.Floor);
            StyleButton(_layerCeiling, View.Layer == ViewLayer.Ceiling);
            StyleButton(_layerCavity, View.Layer == ViewLayer.WallCavity);
            string note = tier < Tech.MaxTier && UnlockCondition != null ? $"Tier {tier + 1} (more sizes, materials and parts): {UnlockCondition(tier + 1).ToLowerInvariant()}" : "";
            if (_lockNote.text != note) _lockNote.text = note;
            _lockNote.style.display = note.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            StyleButton(_sharp, !_ix.DrawSmoothElbows);
            StyleButton(_smooth, _ix.DrawSmoothElbows);
            foreach ((string id, Button b) in _partButtons) StyleButton(b, _ix.PlaceTool == id);
            for (int i = 0; i < 8; i++) StyleButton(_modeButtons[i], (int)_hm.Mode == i);
            _crankBox.style.display = _s.HasCrank ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void StyleButton(Button b, bool active) => _ui.Style(b, active);

        void RefreshStatus()
        {
            BuildingEvaluation ev = _s.CommittedEvaluation;
            bool anyBuilt = _s.Committed.Air.Segments.Count > 0 || _s.Committed.Water.Segments.Count > 0;
            string settle = _s.Settling ? " · network still settling" : "";
            if (_s.Record.PanelTripped)
            {
                _status.text = $"Offline · panel tripped: {Units.FormatPower(_s.RequestedPowerW)} of {_s.PanelLimitKW:0.#} kW · reduce the load and reset it at the power connection";
                _status.style.color = _p.Warning;
            }
            else if (ev.Burst)
            {
                _status.text = $"Burst · income cut to {Units.FormatMoneyRate(ev.Gross, _symbol)} · repair it, or close a shutoff upstream{settle}";
                _status.style.color = _p.Danger;
            }
            else if (ev.Online)
            {
                string over = ev.AboveCodeMax > 0 ? $" · {ev.AboveCodeMax} above code max" : "";
                _status.text = $"Online · earning {Units.FormatMoneyRate(ev.Net, _symbol)}{over}{settle}";
                _status.style.color = _p.Good;
            }
            else if (!anyBuilt && !_s.Record.EverOnline)
            {
                _status.text = _s.HasWater ? "Not built yet · ducts to every vent, pipes to every tap, then Commit" : "Not built yet · draw ducts from the intake to every vent, then Commit";
                _status.style.color = _p.Text;
            }
            else if (ev.BlockingViolations > 0 && ev.BelowMinimum == 0)
            {
                _status.text = $"Blocked by {ev.BlockingViolations} code violation{(ev.BlockingViolations == 1 ? "" : "s")}{settle}";
                _status.style.color = _p.Warning;
            }
            else if (_s.HasWater && !_s.Supply.Connected && _s.Game.City != null)
            {
                _status.text = "Offline · no city water: route a water main to this plot in the city view";
                _status.style.color = _p.Warning;
            }
            else
            {
                string power = ev.PowerW > 0 ? $" · equipment still drawing {Units.FormatPower(ev.PowerW)}" : "";
                string crank = _s.HasCrank && _s.CrankSpeed < 0.05 ? " · crank to move air" : "";
                _status.text = $"Offline · {ev.BelowMinimum} of {ev.Outlets.Count} outlets below minimum{power}{crank}{settle}";
                _status.style.color = _p.Warning;
            }
        }

        void RefreshDesignBar()
        {
            BuildingEvaluation projected = _s.DesignEvaluation;
            if (_s.HasPendingDesign)
            {
                double net = _s.PendingCost.Net;
                string cost = net >= 0 ? $"cost {Units.FormatMoney(net, _symbol)}" : $"refund {Units.FormatMoney(-net, _symbol)}";
                string online = projected.Online ? "" : " (offline)";
                _designText.text = $"Design · not committed · {cost} · projected income {Units.FormatMoneyRate(projected.Net, _symbol)}{online}";
            }
            else
            {
                _designText.text = $"Design matches what is built · income {Units.FormatMoneyRate(_s.CommittedEvaluation.Net, _symbol)}";
            }
            CommitCheck check = _s.CheckCommit();
            _commit.SetEnabled(check.Allowed);
            _discard.SetEnabled(_s.HasPendingDesign);
            _undo.SetEnabled(_s.Commands.CanUndo);
            _redo.SetEnabled(_s.Commands.CanRedo);
            _designReason.text = _s.HasPendingDesign && !check.Allowed ? check.Reason : "";
            _designReason.style.display = _designReason.text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void RefreshLegend()
        {
            if (_hm.Mode == HeatmapMode.Plain)
            {
                FluidDef shown = _data.Fluid(View.Fluid);
                _legendTitle.text = $"Plain · {shown.DisplayName} ({shown.LinePattern} line)";
                _legendBar.style.backgroundImage = StyleKeyword.None;
                _legendBar.style.backgroundColor = _p.Fluid(View.Fluid);
                _legendTicks.style.display = DisplayStyle.None;
                _lock.style.display = DisplayStyle.None;
                return;
            }
            if (_legendTexture == null || _legendPalette != _hm.Palette)
            {
                _legendTexture ??= new Texture2D(256, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int i = 0; i < 256; i++) _legendTexture.SetPixel(i, 0, _p.Ramp(_hm.Palette, i / 255f));
                _legendTexture.Apply();
                _legendPalette = _hm.Palette;
            }
            string unit = HeatmapState.Unit(_hm.Mode, View.Fluid);
            string pipes = View.Fluid == "air" ? "ducts" : "pipes";
            string layer = _hm.RoomsColoured && _hm.Mode != HeatmapMode.Velocity
                ? $" · rooms: {(_hm.Layer == RoomLayer.Max ? "warmer layer" : _hm.Layer == RoomLayer.Upper ? "upper layer" : "lower layer")} (H)" : "";
            _legendTitle.text = _hm.Mode switch
            {
                HeatmapMode.Velocity => _hm.RoomsColoured ? $"Velocity (m/s) · rooms 0–{Units.Number(_hm.RoomMax, 1)} m/s" : "Velocity in pipes (m/s)",
                HeatmapMode.Pressure => $"Pressure in {pipes} ({unit})",
                HeatmapMode.Flow => $"Flow rate in {pipes} ({unit})",
                HeatmapMode.Stress => _s.Game.City?.Utilities.EngineeringSoftware == true && _s.HasPendingDesign ? "Stress forecast after 24 h (burst chance rises from 0.6)" : "Stress (1 and above in the warning colour)",
                _ => $"{HeatmapState.DisplayName(_hm.Mode)} ({unit}){layer}",
            };
            _legendBar.style.backgroundColor = StyleKeyword.None;
            _legendBar.style.backgroundImage = new StyleBackground(_legendTexture);
            _legendTicks.style.display = DisplayStyle.Flex;
            _lock.style.display = DisplayStyle.Flex;
            int digits = _hm.Mode == HeatmapMode.Density ? 3 : _hm.Mode == HeatmapMode.Stress ? 1 : (_hm.Mode == HeatmapMode.Flow || _hm.Mode == HeatmapMode.Pressure) && View.Fluid == "air" ? 0 : 1;
            _legendMin.text = Units.Number(_hm.RangeMin, digits);
            _legendMid.text = Units.Number((_hm.RangeMin + _hm.RangeMax) / 2, digits);
            _legendMax.text = Units.Number(_hm.RangeMax, digits) + " " + unit;
            _lock.text = _hm.Locked ? "Unlock range" : "Lock range";
            StyleButton(_lock, _hm.Locked);
        }

        void RefreshHint()
        {
            string hint = _ix.Hint;
            bool warn = hint.Length > 0 && _ix.PlaceTool.Length == 0;
            if (_ix.PlaceTool.Length > 0 && _ix.PlaceHovering && !_ix.PlaceValid) warn = true;
            if (hint.Length == 0 && View.Layer == ViewLayer.Floor) hint = "Floor layer: equipment and outlets · press L for the ceiling, where the pipes run";
            if (hint.Length == 0 && View.Fluid == "water" && !_s.Unlocked(_data.Material("pvc").Tier))
            {
                hint = $"Requires water (tier {_data.Material("pvc").Tier})";
                warn = true;
            }
            if (hint.Length == 0 && View.Fluid == "water" && _s.State.Water.Segments.Count == 0)
                hint = _s.Layout.Floors > 1 ? "Drag from the water connection to a tap · a riser goes between floors in a shaft" : "Drag from the water connection to a tap to draw a pipe";
            bool airEmpty = View.Fluid == "air" && _s.State.Air.Segments.Count == 0;
            bool intakeHasFan = _s.Layout.OpeningFor("air") is EntryOpeningDef intake && _s.State.EquipmentAt(intake.Id) != null;
            if (hint.Length == 0 && airEmpty)
                hint = !intakeHasFan
                    ? "Click the intake to place a fan, then drag from it to a vent to draw a duct"
                    : "Drag from the intake to a vent to draw a duct";
            _hint.text = hint;
            _hint.style.color = warn ? _p.Warning : _p.Text;
        }

        // ---------- readout ----------

        void RefreshReadout(bool stateChanged)
        {
            // A selection keeps its panel and actions while the pointer moves (also over other things); with
            // nothing selected the panel shows what is under the pointer, read-only.
            bool selected = _ix.Selected.Kind != TargetKind.None;
            Target t = selected ? _ix.Selected : _ix.Hover;
            bool actions = selected;
            int lines = 0;
            NetworkSnapshot? snap = _s.Current;
            _stressTrack.style.display = DisplayStyle.None;
            string fluid = t.Fluid ?? View.Fluid;
            switch (t.Kind)
            {
                case TargetKind.Segment:
                    if (t.Id == 0 || _ix.SelectedSegments.Count > 1 && selected)
                    {
                        _readoutTitle.text = $"{_ix.SelectedSegments.Count} {(fluid == "air" ? "ducts" : "pipes")} selected";
                        lines = Line(lines, "Change their size or delete them.");
                    }
                    else lines = SegmentReadout(fluid, t.Id, snap, lines);
                    break;
                case TargetKind.Node: lines = NodeReadout(fluid, t.Id, snap, lines); break;
                case TargetKind.Intake: lines = OpeningReadout(t.Anchor, snap, lines); break;
                case TargetKind.Outlet: lines = OutletReadout(t.Anchor, lines); break;
                case TargetKind.Inline: lines = InlineReadout(fluid, t.Id, snap, lines); break;
            }
            bool room = false;
            if (t.Kind == TargetKind.None && !_ix.PointerOverUi && _s.Layout.RoomOf(_ix.PointerTile) is RoomDef r)
            {
                room = true;
                lines = RoomReadout(r, lines);
            }
            for (int i = lines; i < ReadoutLines; i++) _lines[i].style.display = DisplayStyle.None;
            _readout.style.display = t.Kind == TargetKind.None && !room ? DisplayStyle.None : DisplayStyle.Flex;

            string key = actions ? ActionsKey(t) : "";
            if (key != _shownActionsKey)
            {
                _shownActionsKey = key;
                BuildActions(actions ? t : Target.None);
            }
            else if (t.Kind == TargetKind.Inline && _s.State.Net(fluid).FindNode(t.Id) is DuctNode d)
            {
                _damperSlider?.SetValueWithoutNotify((float)d.Opening * 100);
                _speedSlider?.SetValueWithoutNotify((float)d.Speed * 100);
                if (_settingSlider != null && _data.FindFitting(d.PartId) is FittingDef fd)
                    _settingSlider.SetValueWithoutNotify((float)((d.Setting > 0 ? d.Setting : fd.DefaultSetting) / Units.PaPerBar));
            }
            else if (_speedSlider != null && t.Kind == TargetKind.Intake && _s.State.EquipmentAt(t.Anchor) is EquipmentState se)
                _speedSlider.SetValueWithoutNotify((float)se.Speed * 100);
        }

        /// <summary>What the action buttons depend on; they are rebuilt only when it changes (a slider keeps its drag).</summary>
        string ActionsKey(Target t)
        {
            _sb.Clear();
            string fluid = t.Fluid ?? View.Fluid;
            _sb.Append((int)t.Kind).Append('/').Append(t.Id).Append('/').Append(t.Anchor).Append('/').Append(fluid).Append('/');
            DuctNetworkState net = _s.State.Net(fluid);
            switch (t.Kind)
            {
                case TargetKind.Segment:
                    foreach (int id in _ix.SelectedSegments) _sb.Append(id).Append(',');
                    _sb.Append(_ix.SelectedTile.X).Append(',').Append(_ix.SelectedTile.Y).Append(',').Append(_ix.SelectedTile.F).Append('/').Append(_s.StateRevision);
                    _sb.Append('/').Append(_s.Record.Failures.Bursts.Count).Append('/').Append(_s.Record.Failures.Repairs.Count);
                    break;
                case TargetKind.Node:
                    if (net.FindNode(t.Id) is DuctNode n) _sb.Append(n.SmoothFittings).Append(net.Degree(n.Id));
                    break;
                case TargetKind.Intake:
                    if (_s.State.EquipmentAt(t.Anchor) is EquipmentState e) _sb.Append(e.EquipmentId).Append(e.On).Append(e.Vsd);
                    _sb.Append(_s.Record.PanelTripped);
                    break;
                case TargetKind.Inline:
                    if (net.FindNode(t.Id) is DuctNode p) _sb.Append(p.PartId).Append(p.On).Append(p.Vsd);
                    break;
            }
            _sb.Append('/').Append(_shownTier);
            return _sb.ToString();
        }

        int Line(int i, string text)
        {
            if (i >= ReadoutLines) return i;
            _lines[i].text = text;
            _lines[i].style.display = DisplayStyle.Flex;
            return i + 1;
        }

        /// <summary>The burst edge on a design segment, if it has burst.</summary>
        string? BurstEdge(string fluid, DuctNetworkState net, DuctSegment seg)
        {
            if (_s.Record.Failures.Bursts.Count == 0) return null;
            List<string> edges = BuildingFailures.Edges(fluid, net, seg);
            foreach (BurstState b in _s.Record.Failures.Bursts)
                if (b.Fluid == fluid && edges.Contains(b.Edge)) return b.Edge;
            return null;
        }

        int SegmentReadout(string fluid, int id, NetworkSnapshot? snap, int i)
        {
            DuctNetworkState net = _s.State.Net(fluid);
            DuctSegment? seg = net.FindSegment(id);
            if (seg == null) return i;
            MaterialDef mat = _data.Material(seg.MaterialId);
            bool air = fluid == "air", riser = DuctEditor.IsRiser(net, seg);
            string word = riser ? "Riser" : air ? "Duct" : "Pipe";
            _readoutTitle.text = $"{word} · {seg.DiameterMm:0} mm {mat.DisplayName.ToLowerInvariant()}";
            string? burst = BurstEdge(fluid, net, seg);
            if (burst != null)
            {
                RepairJob? job = _s.Failures.JobFor(fluid, burst);
                i = Line(i, job == null ? "Burst · it spills until it is repaired or a shutoff upstream is closed"
                    : job.Started ? $"Burst · crew on site, {Units.FormatDuration(job.Remaining)} left" : "Burst · a crew is booked and waiting for the one ahead");
                return i;
            }
            FluidSolve? fs = snap?.For(fluid);
            if (fs == null || !fs.Compiled.SegmentLink.TryGetValue(id, out int k)) return Line(i, "Solving…");
            SolveResult r = fs.Result;
            NetworkLink l = fs.Compiled.Graph.Links[k];
            i = Line(i, $"Flow  {Units.FormatFlow(System.Math.Abs(r.Flow[k]), fluid)}");
            i = Line(i, $"Velocity  {Units.FormatVelocity(System.Math.Abs(r.Velocity[k]))}");
            double pIn = r.Flow[k] >= 0 ? r.PressureFrom(fs.Compiled.Graph, k) : r.PressureTo(fs.Compiled.Graph, k);
            double pOut = r.Flow[k] >= 0 ? r.PressureTo(fs.Compiled.Graph, k) : r.PressureFrom(fs.Compiled.Graph, k);
            i = Line(i, $"Pressure  {Units.FormatPressure(pIn, fluid)} → {Units.FormatPressure(pOut, fluid)}");
            i = Line(i, $"Loss  friction {Units.FormatPressure(System.Math.Abs(r.DpFriction[k]), fluid)} · fittings {Units.FormatPressure(System.Math.Abs(r.DpFittings[k]), fluid)} · elevation {Units.FormatPressure(r.DpElevation[k], fluid)}");
            string regime = r.Reynolds[k] <= _data.Balancing.Physics.LaminarReynolds ? "laminar" : r.Reynolds[k] >= _data.Balancing.Physics.TurbulentReynolds ? "turbulent" : "transitional";
            i = Line(i, $"Re  {r.Reynolds[k]:0} ({regime}) · f {r.FrictionFactor[k]:0.0000}");
            i = Line(i, $"Length  {l.Length:0} m · ΣK {l.SumK:0.00} · {Units.FormatMoney(Pricing.SegmentPerMetre(_data, seg), _symbol)}/m");
            if (fs.Compiled.SegmentLeaks.TryGetValue(id, out var leaks))
            {
                string ins = seg.Insulation.Length > 0 ? " · " + _data.FindInsulation(seg.Insulation)!.DisplayName.ToLowerInvariant() : "";
                i = Line(i, $"{_data.Sealing(seg.Sealing).DisplayName} · leaking {Units.FormatAirFlow(r.Flow[leaks.a] + r.Flow[leaks.b])}{ins}");
            }
            i = Line(i, $"Temperature  {Units.FormatTemperatureK(l.Temperature)}");
            return StressLines(fluid, net, seg, fs, i);
        }

        /// <summary>The stress bar, the overpressure and (with the engineering software) the time to a 50% burst chance.</summary>
        int StressLines(string fluid, DuctNetworkState net, DuctSegment seg, FluidSolve fs, int i)
        {
            BuildingFailures f = _s.Failures;
            double surge = f.SurgePa(fluid);
            double stress = f.StressOf(fluid, seg.Id, _s.State);
            double x = f.RatingRatio(fs, net, seg, 0);
            double rated = _data.Material(seg.MaterialId).RatedPressure(seg.DiameterMm);
            string weep = stress >= _data.Balancing.Failure.WeepThreshold ? " · weeping" : "";
            i = Line(i, $"Stress  {stress:0.00}{weep} · {x:0.00} × rating ({Units.FormatPressure(rated, fluid)})");
            _stressTrack.style.display = DisplayStyle.Flex;
            _stressFill.style.width = Length.Percent(Mathf.Clamp01((float)stress) * 100);
            _stressFill.style.backgroundColor = stress >= _data.Balancing.Failure.WeepThreshold ? _p.Warning : _p.Good;
            if (surge > 0) i = Line(i, $"Surge: +{Units.FormatPressure(surge, fluid)}");
            if (_s.Game.City?.Utilities.EngineeringSoftware == true)
            {
                double rate = f.StressRate(x);
                i = Line(i, rate > 0
                    ? $"Forecast  50% burst chance in {Units.FormatDuration(f.TimeToHalfChance(stress, rate))} · stress {stress + rate * 86400:0.00} after 24 h"
                    : "Forecast  no stress building at this pressure");
            }
            return i;
        }

        int NodeReadout(string fluid, int id, NetworkSnapshot? snap, int i)
        {
            DuctNetworkState net = _s.State.Net(fluid);
            DuctNode? n = net.FindNode(id);
            if (n == null) return i;
            bool elbow = DuctEditor.IsElbow(net, n);
            int degree = net.Degree(id);
            bool air = fluid == "air";
            _readoutTitle.text = elbow
                ? _data.Fitting(n.SmoothFittings ? "elbow-90-long-radius" : "elbow-90-sharp").DisplayName
                : degree >= 4 ? _data.Fitting("cross-sharp").DisplayName
                : degree == 3 ? _data.Fitting(n.SmoothFittings ? "tee-smooth" : "tee-sharp").DisplayName
                : degree == 1 ? (air ? _data.Fitting("open-end").DisplayName : "Capped pipe end")
                : _data.Fitting("reducer").DisplayName;
            FluidSolve? fs = snap?.For(fluid);
            if (fs == null) return Line(i, "Solving…");
            if (degree == 1 && !air) return Line(i, "A capped end carries no flow. Extend it to a tap, run a riser from it in a shaft, or remove it.");
            if (degree == 1)
            {
                if (!fs.Compiled.OpenEndLink.TryGetValue(id, out int k)) return Line(i, "Solving…");
                double q = fs.Result.Flow[k];
                i = Line(i, $"Air escaping  {Units.FormatAirFlow(q)}");
                i = Line(i, $"Exit loss  K {_data.Fitting("open-end").K:0.0} · {Units.FormatPa(System.Math.Abs(fs.Result.DpFittings[k]))}");
                return Line(i, "This air reaches no outlet. Extend the duct to a vent or remove it.");
            }
            bool any = false;
            foreach (CompiledFitting f in fs.Compiled.Fittings)
            {
                if (f.NodeId != id) continue;
                any = true;
                double dp = FittingLoss(fs, f);
                string part = f.Kind == FittingKind.TeeRun ? " (run)" : f.Kind == FittingKind.TeeBranch ? " (branch)" : "";
                i = Line(i, $"{_data.Fitting(f.FittingId).DisplayName}{part}  K {f.K:0.00} · loses {Units.FormatPressure(dp, fluid)}");
            }
            if (!any) i = Line(i, "No loss here");
            if (elbow) i = Line(i, n.SmoothFittings ? "Long-radius: gentle turn, low loss." : "Sharp: abrupt turn, high loss.");
            if (degree == 3) i = Line(i, n.SmoothFittings ? "Smooth tee: the branch turns gently." : "Sharp tee: the branch turns abruptly.");
            return i;
        }

        static double FittingLoss(FluidSolve fs, CompiledFitting f)
        {
            double v = fs.Result.Velocity[f.LinkIndex];
            return f.K * 0.5 * fs.Result.Density[f.LinkIndex] * v * v;
        }

        /// <summary>The panel of an opening by utility: air intake, water connection or power connection.</summary>
        int OpeningReadout(string openingId, NetworkSnapshot? snap, int i)
        {
            EntryOpeningDef? o = _s.Layout.FindOpening(openingId);
            if (o == null) return i;
            if (o.Utility == "water") return WaterConnectionReadout(openingId, snap, i);
            if (o.Utility == "power") return PowerConnectionReadout(openingId, i);
            EquipmentState? e = _s.State.EquipmentAt(openingId);
            if (e == null)
            {
                _readoutTitle.text = "Air intake · empty";
                return Line(i, "An open louvre. Place a fan here to move air, or put an inline fan on the duct.");
            }
            EquipmentDef def = _data.EquipmentItem(e.EquipmentId);
            _readoutTitle.text = def.DisplayName;
            bool crank = def.SpeedControl == "crank";
            if (!crank && !e.On) return Line(i, "Switched off");
            if (crank) i = Line(i, $"Crank speed  {_s.CrankSpeed * 100:0} % · click or hold C (no electricity, nothing offline)");
            else if (e.Vsd || def.SpeedControl == "variable") i = Line(i, $"Speed  {e.Speed * 100:0} %{(e.Vsd ? " · variable-speed drive" : "")}");
            FluidSolve? fs = snap?.Air;
            if (fs == null || !fs.Compiled.FanLink.TryGetValue(openingId, out int k)) return Line(i, "Solving…");
            return DeviceLines(fs, k, def, i);
        }

        int WaterConnectionReadout(string openingId, NetworkSnapshot? snap, int i)
        {
            EquipmentState? e = _s.State.EquipmentAt(openingId);
            EquipmentDef? def = e != null ? _data.FindEquipment(e.EquipmentId) : null;
            bool pump = def != null && def.Curve.Count > 0;
            _readoutTitle.text = pump ? def!.DisplayName : "Water connection · mains pressure only";
            i = Line(i, _s.Supply.Connected
                ? $"City water  {Units.FormatBar(_s.Supply.PressurePa)} at the connection"
                : _s.Game.City != null ? "No city water yet · route a water main to this plot in the city view" : "No city water");
            FluidSolve? fs = snap?.Water;
            if (fs != null && fs.Compiled.SupplyNode >= 0) i = Line(i, $"Drawing  {Units.FormatWaterFlow(fs.Compiled.SupplyFlow(fs.Result))} from the main");
            if (!pump) return Line(i, "Free and fixed; weak on upper floors. Place a pump here for more head.");
            if (!e!.On) return Line(i, "Switched off (closed to water)");
            if (e.Vsd || def!.SpeedControl == "variable") i = Line(i, $"Speed  {e.Speed * 100:0} %{(e.Vsd ? " · variable-speed drive" : "")}");
            if (fs == null || !fs.Compiled.FanLink.TryGetValue(openingId, out int k)) return Line(i, "Solving…");
            return DeviceLines(fs, k, def!, i);
        }

        int PowerConnectionReadout(string openingId, int i)
        {
            EquipmentState? e = _s.State.EquipmentAt(openingId);
            EquipmentDef? def = e != null ? _data.FindEquipment(e.EquipmentId) : null;
            _readoutTitle.text = def?.DisplayName ?? "Power connection";
            i = Line(i, $"Draw  {Units.FormatPower(_s.RequestedPowerW)} · panel limit {_s.PanelLimitKW:0.#} kW");
            i = Line(i, _s.Power.Cabled ? $"Cable  {_s.ConnectionKW:0.#} kW · the city gives {_s.Power.AllocatedKW:0.#} kW now" : $"Basic connection  {_s.ConnectionKW:0.#} kW (no cable routed)");
            if (_s.Record.PanelTripped) i = Line(i, $"Panel tripped: {Units.FormatPower(_s.RequestedPowerW)} of {_s.PanelLimitKW:0.#} kW. Reduce the load or upgrade the panel, then reset it.");
            else if (_s.PowerSpeed < 0.999) i = Line(i, $"Brownout: fans and pumps run at {_s.PowerSpeed * 100:0} % speed");
            return i;
        }

        int DeviceLines(FluidSolve fs, int k, EquipmentDef def, int i)
        {
            SolveResult r = fs.Result;
            bool pump = def.Category == "water-mover";
            i = Line(i, $"Flow  {Units.FormatFlow(r.Flow[k], fs.Fluid)}");
            if (pump)
            {
                double rhoG = r.Density[k] * _data.Balancing.Physics.Gravity;
                i = Line(i, $"Head  {r.DpDevice[k] / rhoG:0.0} m ({Units.FormatBar(r.DpDevice[k])}) · shutoff {def.Curve[0].PressurePa / rhoG:0} m");
            }
            else i = Line(i, $"Pressure rise  {Units.FormatPa(r.DpDevice[k])} (shutoff {def.Curve[0].PressurePa:0} Pa)");
            if (def.PowerModel != "none")
            {
                double cost = r.Power[k] / 1000 * _data.Balancing.Economy.ElectricityPricePerKWs * _data.Balancing.Economy.ElectricityTierFactor[_s.Layout.Tier - 1];
                i = Line(i, $"Efficiency  {r.Efficiency[k] * 100:0} %");
                i = Line(i, $"Power  {Units.FormatPower(r.Power[k])} · electricity {Units.FormatMoneyRate(cost, _symbol)}");
            }
            return i;
        }

        int InlineReadout(string fluid, int nodeId, NetworkSnapshot? snap, int i)
        {
            DuctNode? n = _s.State.Net(fluid).FindNode(nodeId);
            if (n == null) return i;
            FluidSolve? fs = snap?.For(fluid);
            int k = -1;
            bool solved = fs != null && fs.Compiled.InlineLink.TryGetValue(nodeId, out k);
            EquipmentDef? eq = _data.FindEquipment(n.PartId);
            if (eq != null)
            {
                _readoutTitle.text = eq.DisplayName;
                if (eq.Curve.Count == 0) return Line(i, $"Surges on this network × {_data.Balancing.Failure.AccumulatorSurgeFactor:0.0#}: pipes build less stress when valves close or pumps stop.");
                if (!n.On) return Line(i, $"Switched off (closed to {(fluid == "air" ? "air" : "water")})");
                if (!solved) return Line(i, "Solving…");
                return DeviceLines(fs!, k, eq, i);
            }
            FittingDef valve = _data.Fitting(n.PartId);
            _readoutTitle.text = valve.DisplayName;
            switch (valve.Model)
            {
                case "regulator":
                {
                    double set = n.Setting > 0 ? n.Setting : valve.DefaultSetting;
                    i = Line(i, $"Setpoint  {Units.FormatBar(set)} downstream");
                    if (!solved) return Line(i, "Solving…");
                    LinkStatus st = fs!.Result.Status[k];
                    NetworkLink l = fs.Compiled.Graph.Links[k];
                    i = Line(i, st == LinkStatus.Active ? "Holding the setpoint" : st == LinkStatus.Open ? "Fully open: upstream is below the setpoint" : "Closed: no flow forward");
                    i = Line(i, $"Pressure  {Units.FormatBar(fs.Result.NodePressure[l.From])} → {Units.FormatBar(fs.Result.NodePressure[l.To])}");
                    return Line(i, $"Flow  {Units.FormatWaterFlow(System.Math.Abs(fs.Result.Flow[k]))}");
                }
                case "relief":
                {
                    double set = n.Setting > 0 ? n.Setting : valve.DefaultSetting;
                    i = Line(i, $"Opens at  {Units.FormatBar(set)}");
                    if (fs == null || !fs.Compiled.ReliefLink.TryGetValue(nodeId, out int rl)) return Line(i, "Solving…");
                    bool open = fs.Result.Status[rl] == LinkStatus.Open;
                    i = Line(i, open ? $"Venting {Units.FormatWaterFlow(fs.Result.Flow[rl])} to the drain (vented water costs nothing)" : "Closed: the pressure here is below its setting");
                    return Line(i, $"Pressure here  {Units.FormatBar(fs.Result.NodePressure[fs.Compiled.Graph.Links[rl].From])}");
                }
                case "check":
                    i = Line(i, "Lets water through one way only");
                    if (!solved) return Line(i, "Solving…");
                    i = Line(i, fs!.Result.Status[k] == LinkStatus.Closed ? "Closed against reverse flow" : $"Flow  {Units.FormatWaterFlow(System.Math.Abs(fs.Result.Flow[k]))}");
                    return i;
            }
            if (valve.Model == "fixed") i = Line(i, $"K {valve.K:0.0#}");
            else if (valve.MinOpening >= 1)
            {
                i = Line(i, n.On ? $"Open · K {valve.K:0.0#}" : "Closed");
                if (!n.On) return Line(i, "The branch behind it is isolated: its outlets pay nothing but do not keep the building offline, and a burst there no longer cuts the income.");
            }
            else i = Line(i, $"Open  {n.Opening * 100:0} % · K {valve.K / (n.Opening * n.Opening):0.0#}");
            if (!solved) return Line(i, "Solving…");
            SolveResult r = fs!.Result;
            i = Line(i, $"Flow  {Units.FormatFlow(System.Math.Abs(r.Flow[k]), fluid)}");
            i = Line(i, $"Pressure lost  {Units.FormatPressure(System.Math.Abs(r.DpFittings[k]), fluid)}");
            if (valve.Model == "valve" && valve.MinOpening < 1)
                i = Line(i, $"Drag the Open slider to send {(fluid == "air" ? "air" : "water")} to other branches. Changing the opening is free and works at once, no Commit needed. To put it somewhere else, Remove it and place a new one.");
            return i;
        }

        /// <summary>The room under the pointer: its air from the room grid (<c>05-simulation.md</c> §3.2).</summary>
        int RoomReadout(RoomDef r, int i)
        {
            int index = -1;
            for (int k = 0; k < _s.Layout.Rooms.Count; k++) if (_s.Layout.Rooms[k] == r) index = k;
            RoomTypeDef? type = _data.FindRoomType(r.Type);
            _readoutTitle.text = $"{type?.DisplayName ?? r.Type} · {r.Width * r.Height} m²";
            if (type != null && type.Protected) i = Line(i, "Protected room: pipes cannot cross it.");
            if (type != null && type.Shaft) i = Line(i, "Shaft: risers run between floors here, and pipes may enter from any side.");
            if (type != null && type.BlocksPipes) i = Line(i, "Stairwell: pipes cannot cross it.");
            if (type != null && type.Electrical) i = Line(i, "Electrical room: no water pipes above it.");
            if (index < 0 || index >= _s.Rooms.Rooms.Length) return i;
            Sim.Rooms.RoomSummary sum = _s.Rooms.Rooms[index];
            i = Line(i, $"Air  {sum.MeanTemperature:0.0} °C · outside {_s.Climate.OutsideTemperatureC:0.0} °C");
            i = Line(i, $"CO₂  {sum.Co2Ppm:0} ppm · {sum.AirChangesPerHour:0.0} air changes per hour");
            if (r.Occupants > 0) i = Line(i, $"{r.Occupants:0.#} {(r.Occupants == 1 ? "person" : "people")} · {r.Occupants * _data.Balancing.Climate.OccupantSensibleW + r.MachinesW:0} W of heat");
            return i;
        }

        int OutletReadout(string outletId, int i)
        {
            OutletPlacementDef o = _s.Layout.FindOutlet(outletId)!;
            OutletDef def = _data.Outlet(o.Type);
            _readoutTitle.text = $"{OutletList.NiceName(outletId)} · {def.DisplayName.ToLowerInvariant()}";
            OutletStatus? st = _s.DesignEvaluation.Find(outletId);
            if (st == null || !st.Connected) i = Line(i, "Not connected");
            else if (st.Zone == OutletZone.Isolated) i = Line(i, "Isolated by a closed shutoff: it pays nothing, but does not keep the building offline.");
            else
            {
                string flow = def.Measured == "pressure" ? $" while drawing {Units.FormatWaterFlow(NetworkCompiler.DesignFlow(_data, _s.Layout, o))}" : "";
                i = Line(i, $"Delivery  {UiKit.FormatDelivery(def, st.Delivery)}{flow}");
                i = Line(i, $"Zone  {UiKit.ZoneWord(st.Zone)} · ×{st.Multiplier:0.00} · {Units.FormatMoneyRate(st.BaseRate * st.Multiplier, _symbol)}");
                if (st.Zone == OutletZone.AboveCodeMax) i = Line(i, $"Above code maximum: {def.CodeMaxReason.ToLowerInvariant()}. It pays nothing until the delivery comes down.");
                if (_s.Current != null && _s.Current.Compiled.OutletLink.TryGetValue(outletId, out int k) && _s.Current.Result.Flow[k] < 0)
                    i = Line(i, "Air is flowing backwards into the duct here.");
            }
            if (st != null)
            {
                OutletRequirement q = st.Requirement;
                string max = q.HasMax ? $" · code max {UiKit.FormatDelivery(def, q.Max)} ({def.CodeMaxReason.ToLowerInvariant()})" : "";
                i = Line(i, $"Needs  min {UiKit.FormatDelivery(def, q.Min)} · ideal {UiKit.FormatDelivery(def, q.Ideal)}{max}");
            }
            return i;
        }

        /// <summary>Material, sealing and insulation pickers for selected ducts or pipes (only unlocked choices).</summary>
        void AddSpecDropdowns(List<int> ids, DuctSegment first, string fluid)
        {
            void Picker(string label, List<(string id, string name)> options, string current, System.Func<string, ICommand> make)
            {
                var names = new List<string>();
                foreach (var o in options) names.Add(o.name);
                int at = options.FindIndex(o => o.id == current);
                var dd = new DropdownField(label, names, at < 0 ? 0 : at);
                dd.AddToClassList("hl-dropdown");
                _ui.StyleField(dd);
                dd.style.minWidth = 300;
                dd.RegisterValueChangedCallback(_ =>
                {
                    if (dd.index < 0) return;
                    _s.Execute(make(options[dd.index].id));
                });
                _readoutActions.Add(dd);
            }
            var mats = new List<(string, string)>();
            foreach (MaterialDef m in _data.Materials)
                if (m.Fluids.Contains(fluid) && _s.Unlocked(m.Tier))
                    mats.Add((m.Id, $"{m.DisplayName} · {Units.FormatMoney(Pricing.PerMetre(_data, m.Id, first.DiameterMm), _symbol)}/m · rated {Units.FormatPressure(m.RatedPressure(first.DiameterMm), fluid)}"));
            Picker("Material", mats, first.MaterialId, id => new SetSegmentSpecCommand(ids, materialId: id, fluid: fluid));
            if (fluid == "air")
            {
                var seals = new List<(string, string)>();
                foreach (SealingDef sd in _data.Upgrades.DuctSealing)
                    if (_s.Unlocked(sd.Tier)) seals.Add((sd.Id == _data.Upgrades.DuctSealing[0].Id ? "" : sd.Id, sd.PricePerMetre > 0 ? $"{sd.DisplayName} · +{Units.FormatMoney(sd.PricePerMetre, _symbol)}/m" : sd.DisplayName));
                if (seals.Count > 1) Picker("Sealing", seals, first.Sealing, id => new SetSegmentSpecCommand(ids, sealing: id, fluid: fluid));
            }
            var ins = new List<(string, string)> { ("", "No insulation") };
            foreach (InsulationDef d in _data.Upgrades.Insulation)
                if (_s.Unlocked(d.Tier)) ins.Add((d.Id, d.DisplayName));
            if (ins.Count > 1) Picker("Insulation", ins, first.Insulation, id => new SetSegmentSpecCommand(ids, insulation: id, fluid: fluid));
        }

        /// <summary>Fit or remove a variable-speed drive (bought with the Commit; disabled until unlocked).</summary>
        void AddDriveButton(string category, bool fitted, System.Action toggle)
        {
            EquipmentDef? drive = null;
            foreach (EquipmentDef d in _data.Equipment) if (d.Placement == "addon" && d.FitsCategories.Contains(category)) drive = d;
            if (drive == null) return;
            bool ok = _s.Unlocked(drive.Tier);
            Button b = _ui.Button(_readoutActions, fitted ? "Remove speed drive" : ok ? $"Add speed drive · {Units.FormatMoney(drive.Price, _symbol)}" : $"Speed drive · tier {drive.Tier}", toggle);
            b.SetEnabled(fitted || ok);
        }

        /// <summary>Speed 20–100% (free and live, like a damper, D25).</summary>
        void AddSpeedSlider(double speed, System.Action<double> set)
        {
            var slider = new Slider("Speed", 20, 100) { value = (float)speed * 100 };
            slider.AddToClassList("hl-slider");
            slider.RegisterValueChangedCallback(ev => set(Mathf.Round(ev.newValue) / 100.0));
            _readoutActions.Add(slider);
            _speedSlider = slider;
        }

        /// <summary>The city-supplied sizes of a network on offer: only unlocked ones.</summary>
        List<double> SizesFor(string fluid)
        {
            var list = new List<double>();
            PipeFamilyDef family = _data.PipeFamily(fluid == "air" ? _data.Balancing.Drawing.DefaultDuctFamily : "pipe");
            foreach (PipeSizeDef sz in family.Sizes) if (_s.Unlocked(sz.Tier)) list.Add(sz.DiameterMm);
            return list;
        }

        void AddRiserButtons()
        {
            if (!(_ix.RiserStart() is TilePos start)) return;
            if (start.F < _s.Layout.TopFloor) _ui.Button(_readoutActions, "Riser up", () => _ix.AddRiser(1));
            if (start.F > _s.Layout.LowestFloor) _ui.Button(_readoutActions, "Riser down", () => _ix.AddRiser(-1));
        }

        /// <summary>The repair panel of a burst pipe: the two crews with their price and time, and an optional upgrade fitted in the same visit.</summary>
        void AddRepairActions(string fluid, DuctSegment design, string edge)
        {
            DuctNetworkState built = _s.Committed.Net(fluid);
            DuctSegment? seg = built.Segments.Find(x => BuildingFailures.Edges(fluid, built, x).Contains(edge));
            if (seg == null) return;
            RepairJob? job = _s.Failures.JobFor(fluid, edge);
            if (job != null) return;
            double metres = DuctEditor.Tiles(built, seg).Count - 1;
            if (DuctEditor.IsRiser(built, seg)) metres *= _data.Balancing.Building.FloorHeight;
            var options = new List<(string id, string name)> { ("", "Repair as it was") };
            foreach (MaterialDef m in _data.Materials)
                if (m.Id != seg.MaterialId && m.Fluids.Contains(fluid) && _s.Unlocked(m.Tier)) options.Add((m.Id, $"Fit {m.DisplayName.ToLowerInvariant()} · rated {Units.FormatPressure(m.RatedPressure(seg.DiameterMm), fluid)}"));
            string upgrade = "";
            if (options.Count > 1)
            {
                var names = new List<string>();
                foreach (var o in options) names.Add(o.name);
                var dd = new DropdownField(names, 0);
                dd.AddToClassList("hl-dropdown");
                _ui.StyleField(dd);
                dd.style.minWidth = 300;
                dd.RegisterValueChangedCallback(_ => upgrade = dd.index >= 0 ? options[dd.index].id : "");
                _readoutActions.Add(dd);
            }
            BuildingFailures f = _s.Failures;
            int id = seg.Id;
            foreach (bool emergency in new[] { true, false })
            {
                bool em = emergency;
                string label = $"{(em ? "Emergency" : "Standard")} crew · {Units.FormatDuration(f.RepairSeconds(em))} · {Units.FormatMoney(f.RepairPrice(seg, metres, em), _symbol)}";
                _ui.Button(_readoutActions, label, () => _s.Repair(fluid, id, em, upgrade));
            }
        }

        void BuildActions(Target t)
        {
            _readoutActions.Clear();
            _damperSlider = null;
            _speedSlider = null;
            _settingSlider = null;
            string fluid = t.Fluid ?? View.Fluid;
            switch (t.Kind)
            {
                case TargetKind.Segment:
                {
                    DuctNetworkState net = _s.State.Net(fluid);
                    var ids = new List<int>(_ix.SelectedSegments);
                    DuctSegment? first = ids.Count > 0 ? net.FindSegment(ids[0]) : null;
                    string? burst = first != null ? BurstEdge(fluid, net, first) : null;
                    if (burst != null && first != null) AddRepairActions(fluid, first, burst);
                    List<double> sizes = SizesFor(fluid);
                    var choices = new List<string>();
                    foreach (double d in sizes) choices.Add($"{d:0} mm");
                    if (choices.Count > 0)
                    {
                        int at = first != null ? sizes.IndexOf(first.DiameterMm) : 0;
                        var dd = new DropdownField(choices, at < 0 ? 0 : at);
                        dd.AddToClassList("hl-dropdown");
                        _ui.StyleField(dd);
                        dd.style.minWidth = 140;
                        dd.RegisterValueChangedCallback(_ =>
                        {
                            if (dd.index < 0) return;
                            var cmd = new SetSegmentSizeCommand(ids, sizes[dd.index], fluid);
                            if (!_s.Execute(cmd) && cmd.LastError != DrawError.None) _ix.SetHint(DuctEditor.Describe(cmd.LastError));
                            _ix.ClearSelection();
                        });
                        _readoutActions.Add(dd);
                    }
                    if (first != null) AddSpecDropdowns(ids, first, fluid);
                    _ui.Button(_readoutActions, "Delete", _ix.DeleteSelection);
                    AddRiserButtons();
                    TilePos tile = _ix.SelectedTile;
                    if (ids.Count == 1 && DuctEditor.ValidateInline(net, tile) == DrawError.None)
                        foreach (string part in _data.Game.Toolbox.Parts)
                        {
                            if (!EquipmentFits.InlinePart(_data, part, fluid) || !_s.Unlocked(Tech.ItemTier(_data, part))) continue;
                            string p = part;
                            _ui.Button(_readoutActions, "Add " + PartLabel(part).ToLowerInvariant(), () => _s.Execute(new PlaceInlineCommand(tile, p, false, fluid)));
                        }
                    break;
                }
                case TargetKind.Node:
                {
                    DuctNetworkState net = _s.State.Net(fluid);
                    DuctNode? n = net.FindNode(t.Id);
                    if (n == null) break;
                    AddRiserButtons();
                    if (!(DuctEditor.IsElbow(net, n) || DuctEditor.IsTee(net, n))) break;
                    int id = t.Id;
                    StyleButton(_ui.Button(_readoutActions, "Sharp", () => _s.Execute(new SetElbowCommand(id, false, fluid))), !n.SmoothFittings);
                    Button smooth = _ui.Button(_readoutActions, DuctEditor.IsTee(net, n) ? "Smooth tee" : "Long-radius", () => _s.Execute(new SetElbowCommand(id, true, fluid)));
                    StyleButton(smooth, n.SmoothFittings);
                    smooth.SetEnabled(_s.Unlocked(Tech.SmoothFittingsTier(_data)));
                    break;
                }
                case TargetKind.Intake:
                    OpeningActions(t.Anchor);
                    break;
                case TargetKind.Inline:
                    InlineActions(fluid, t.Id);
                    break;
            }
        }

        void OpeningActions(string opening)
        {
            EntryOpeningDef? o = _s.Layout.FindOpening(opening);
            if (o == null) return;
            EquipmentState? e = _s.State.EquipmentAt(opening);
            if (o.Utility == "water")
                StyleButton(_ui.Button(_readoutActions, "Mains pressure only · free", () => _s.Execute(new SetIntakeEquipmentCommand(opening, ""))), e == null);
            foreach (EquipmentDef def in Catalogue.OpeningEquipment(_data, o.Utility))
            {
                string eqId = def.Id;
                bool here = e != null && e.EquipmentId == eqId;
                bool preplaced = false;
                foreach (EquipmentPlacementDef p in _s.Layout.InitialEquipment)
                    if (p.OpeningId == opening && p.EquipmentId == eqId) preplaced = true;
                bool unlocked = _s.Unlocked(def.Tier);
                if (!unlocked && def.Tier > Tech.UnlockedTier(_s.Game) + 1) continue;
                bool fits = EquipmentFits.Footprint(_data, _s.Layout, _s.State, o, def) != null;
                string price = preplaced ? "free" : Units.FormatMoney(def.Price, _symbol);
                string detail = def.Category == "panel" ? $" · {def.PanelLimitKW:0} kW" : "";
                string label = !unlocked ? $"{def.DisplayName} · tier {def.Tier}" : !fits ? $"{def.DisplayName} · no floor space here" : $"{def.DisplayName}{detail} · {price}";
                Button b = _ui.Button(_readoutActions, label, () => _s.Execute(new SetIntakeEquipmentCommand(opening, eqId)));
                StyleButton(b, here);
                b.SetEnabled(unlocked && (fits || here));
            }
            if (o.Utility == "power")
            {
                if (_s.Record.PanelTripped) _ui.Button(_readoutActions, "Reset panel", () => _s.ResetPanel());
                return;
            }
            if (e == null) return;
            EquipmentDef placed = _data.EquipmentItem(e.EquipmentId);
            if (placed.Curve.Count == 0) return;
            if (placed.SpeedControl != "crank")
                _ui.Button(_readoutActions, e.On ? "Switch off" : "Switch on", () => _s.Execute(new SetEquipmentOnCommand(opening, !e.On)));
            if (placed.SpeedControl == "on-off") AddDriveButton(placed.Category, e.Vsd, () => _s.Execute(new SetVsdCommand(opening, !e.Vsd)));
            if (e.Vsd || placed.SpeedControl == "variable")
                AddSpeedSlider(e.Speed, v => _s.Execute(new SetEquipmentSpeedCommand(opening, v)));
            _ui.Button(_readoutActions, "Remove", () => _s.Execute(new SetIntakeEquipmentCommand(opening, "")));
        }

        void InlineActions(string fluid, int nodeId)
        {
            DuctNode? n = _s.State.Net(fluid).FindNode(nodeId);
            if (n == null) return;
            int id = n.Id;
            TilePos tile = n.Pos;
            EquipmentDef? eq = _data.FindEquipment(n.PartId);
            if (eq != null && eq.Curve.Count > 0)
            {
                bool on = n.On, vsd = n.Vsd;
                _ui.Button(_readoutActions, on ? "Switch off" : "Switch on", () => _s.Execute(new SetInlineOnCommand(tile, !on, fluid)));
                _ui.Button(_readoutActions, "Turn around (R)", () => _s.Execute(new FlipInlineCommand(id, fluid)));
                if (eq.SpeedControl == "on-off") AddDriveButton(eq.Category, vsd, () => _s.Execute(new SetVsdCommand(tile, !vsd, fluid)));
                if (vsd) AddSpeedSlider(n.Speed, v => _s.Execute(new SetInlineSpeedCommand(tile, v, fluid)));
            }
            else if (eq == null)
            {
                FittingDef valve = _data.Fitting(n.PartId);
                switch (valve.Model)
                {
                    case "regulator":
                    case "relief":
                    {
                        double set = n.Setting > 0 ? n.Setting : valve.DefaultSetting;
                        var slider = new Slider(valve.Model == "regulator" ? "Setpoint (bar)" : "Opens at (bar)", (float)(valve.SettingMin / Units.PaPerBar), (float)(valve.SettingMax / Units.PaPerBar)) { value = (float)(set / Units.PaPerBar), showInputField = false };
                        slider.AddToClassList("hl-slider");
                        slider.RegisterValueChangedCallback(ev => _s.Execute(new SetValveSettingCommand(tile, ev.newValue * Units.PaPerBar, fluid)));
                        _readoutActions.Add(slider);
                        _settingSlider = slider;
                        break;
                    }
                    case "check":
                        _ui.Button(_readoutActions, "Turn around (R)", () => _s.Execute(new FlipInlineCommand(id, fluid)));
                        break;
                    case "valve" when valve.MinOpening >= 1:
                    {
                        bool open = n.On;
                        _ui.Button(_readoutActions, open ? "Close valve" : "Open valve", () => _s.Execute(new SetInlineOnCommand(tile, !open, fluid)));
                        break;
                    }
                    case "valve":
                    {
                        var slider = new Slider("Open", (float)valve.MinOpening * 100, 100) { value = (float)n.Opening * 100 };
                        slider.AddToClassList("hl-slider");
                        slider.RegisterValueChangedCallback(ev => _s.Execute(new SetDamperOpeningCommand(tile, Mathf.Round(ev.newValue) / 100.0, fluid)));
                        _readoutActions.Add(slider);
                        _damperSlider = slider;
                        break;
                    }
                }
            }
            _ui.Button(_readoutActions, "Remove", () => _s.Execute(new RemoveInlineCommand(id, fluid)));
        }

        // ---------- code violations ----------

        void RefreshViolations()
        {
            BuildingEvaluation ev = _s.DesignEvaluation;
            _sb.Clear();
            foreach (Violation v in ev.Violations) _sb.Append(v.Reason).Append(v.SegmentId).Append(';');
            string key = _sb.ToString();
            if (key == _shownViolations) return;
            _shownViolations = key;
            _violationRows.Clear();
            _violations.style.display = ev.Violations.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _violationHead.text = $"Code violations · {ev.Violations.Count}";
            foreach (Violation v in ev.Violations)
            {
                Violation at = v;
                Label row = UiKit.Text(_violationRows, v.Reason, "hl-readout-line");
                row.style.color = _p.Danger;
                row.RegisterCallback<ClickEvent>(_ =>
                {
                    if (at.Tiles.Count == 0) return;
                    TilePos t = at.Tiles[0];
                    View.SetFloor(t.F);
                    _cam.CentreOn(DuctGeometry.Centre(t));
                });
            }
        }

        // ---------- world labels ----------

        void RefreshVentLabels()
        {
            NetworkSnapshot? snap = _s.Current;
            _openLabelCount = 0;
            _openLabelWorld.Clear();
            void Note(string text, Vector2 world, Color colour)
            {
                if (_openLabelCount == _openLabels.Count) _openLabels.Add(WorldLabel());
                Label l = _openLabels[_openLabelCount++];
                l.text = text;
                l.style.color = colour;
                _openLabelWorld.Add(world);
            }
            if (snap != null)
            {
                foreach (var kv in snap.Compiled.OpenEndLink)
                {
                    DuctNode? n = snap.State.Air.FindNode(kv.Key);
                    if (n == null || n.Floor != View.Floor || Units.M3sToM3h(snap.Result.Flow[kv.Value]) < 0.5) continue;
                    Note("open end · " + Units.FormatAirFlow(snap.Result.Flow[kv.Value]), DuctGeometry.Centre(n.Pos) + new Vector2(0, 0.5f), _p.Warning);
                }
                // Bursts and weeping joints on this floor.
                foreach (DuctNetworkState net in _s.Committed.Networks)
                    foreach (DuctSegment seg in net.Segments)
                    {
                        List<TilePos> tiles = DuctEditor.Tiles(net, seg);
                        TilePos mid = tiles[tiles.Count / 2];
                        if (mid.F != View.Floor) continue;
                        string? burst = BurstEdge(net.FluidId, net, seg);
                        if (burst != null)
                        {
                            RepairJob? job = _s.Failures.JobFor(net.FluidId, burst);
                            Note(job != null && job.Started ? $"burst · crew {Units.FormatDuration(job.Remaining)}" : "burst", DuctGeometry.Centre(mid) + new Vector2(0, 0.5f), _p.Danger);
                        }
                        else if (_s.Failures.StressOf(net.FluidId, seg.Id, _s.Committed) >= _data.Balancing.Failure.WeepThreshold)
                            Note("weeping", DuctGeometry.Centre(mid) + new Vector2(0, 0.5f), _p.Warning);
                    }
            }
            for (int i = 0; i < _openLabels.Count; i++)
                _openLabels[i].style.display = i < _openLabelCount ? DisplayStyle.Flex : DisplayStyle.None;
            BuildingEvaluation ev = _s.DesignEvaluation;
            for (int i = 0; i < _s.Layout.Outlets.Count; i++)
            {
                OutletPlacementDef o = _s.Layout.Outlets[i];
                OutletStatus? st = ev.Find(o.Id);
                Label l = _ventLabels[i];
                l.style.display = o.Floor == View.Floor ? DisplayStyle.Flex : DisplayStyle.None;
                if (st == null || !st.Connected)
                {
                    l.text = "—";
                    UiKit.Border(l, _p.GridLines);
                    continue;
                }
                l.text = $"{UiKit.FormatDelivery(st.Def, st.Delivery)} · {UiKit.ZoneWord(st.Zone)}";
                UiKit.Border(l, _ui.ZoneColour(st.Zone));
            }
        }

        void RefreshFittingLabels()
        {
            _fittingLabelCount = 0;
            _fittingLabelWorld.Clear();
            DrawPreview? pv = _ix.Preview;
            FluidSolve? fs = _ix.IsDrawing ? pv?.Snapshot?.For(View.Fluid) : null;
            if (fs != null)
            {
                DuctNetworkState net = pv!.Snapshot!.State.Net(View.Fluid);
                var pathTiles = new HashSet<TilePos>(_ix.Path);
                var byNode = new Dictionary<int, string>();
                foreach (CompiledFitting f in fs.Compiled.Fittings)
                {
                    if (f.Kind == FittingKind.Entry) continue;
                    DuctNode? n = net.FindNode(f.NodeId);
                    if (n == null || !pathTiles.Contains(n.Pos)) continue;
                    string s = $"K {f.K:0.0#} · {Units.FormatPressure(FittingLoss(fs, f), View.Fluid)}";
                    byNode[f.NodeId] = byNode.TryGetValue(f.NodeId, out string prev) ? prev + "\n" + s : s;
                }
                foreach (var kv in byNode)
                {
                    DuctNode n = net.FindNode(kv.Key)!;
                    if (_fittingLabelCount == _fittingLabels.Count) _fittingLabels.Add(WorldLabel());
                    Label l = _fittingLabels[_fittingLabelCount++];
                    l.text = kv.Value;
                    _fittingLabelWorld.Add(DuctGeometry.Centre(n.Pos) + new Vector2(0.55f, 0.45f));
                }
            }
            for (int i = 0; i < _fittingLabels.Count; i++)
                _fittingLabels[i].style.display = i < _fittingLabelCount ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void PositionLabels()
        {
            IPanel? panel = _root.panel;
            if (panel == null) return;
            for (int i = 0; i < _ventLabels.Count; i++)
            {
                OutletPlacementDef o = _s.Layout.Outlets[i];
                if (o.Floor == View.Floor) Place(panel, _ventLabels[i], new Vector2(o.X + 0.5f, o.Y + 1.0f));
            }
            for (int i = 0; i < _fittingLabelCount; i++) Place(panel, _fittingLabels[i], _fittingLabelWorld[i]);
            for (int i = 0; i < _openLabelCount; i++) Place(panel, _openLabels[i], _openLabelWorld[i]);
        }

        void Place(IPanel panel, Label l, Vector2 world)
        {
            Vector2 p = RuntimePanelUtils.CameraTransformWorldToPanel(panel, new Vector3(world.x, world.y, 0), _cam.Camera);
            float w = l.resolvedStyle.width;
            float h = l.resolvedStyle.height;
            l.style.left = p.x - (float.IsNaN(w) ? 0 : w * 0.5f);
            l.style.top = p.y - (float.IsNaN(h) ? 0 : h);
        }

        // ---------- debug overlay (dev only) ----------

        void RefreshDebug()
        {
            NetworkSnapshot? snap = _s.Current;
            if (snap == null)
            {
                _debugText.text = "No solve yet";
                return;
            }
            FluidSolve dbg = snap.For(View.Fluid) ?? snap.Air;
            SolveResult r = dbg.Result;
            NetworkGraph g = dbg.Compiled.Graph;
            _sb.Clear();
            _sb.Append($"{dbg.Fluid} · Solver  converged {r.Converged} · iterations {r.Iterations} · residual {r.Residual:E2} · max imbalance {r.MaxImbalance:E2} m³/s\n");
            _sb.Append($"linear iterations {r.LinearIterations} · {r.SolveMilliseconds:0.000} ms · nodes {g.Nodes.Count} · links {g.Links.Count}{(_s.Settling ? " · SETTLING" : "")}");
            _debugText.text = _sb.ToString();
            DebugRow(0, DebugHeader);
            var cells = new string[DebugHeader.Length];
            for (int k = 0; k < g.Links.Count; k++)
            {
                NetworkLink l = g.Links[k];
                cells[0] = l.Label;
                cells[1] = l.Kind.ToString();
                bool on = r.LinkActive[k];
                cells[2] = on ? Units.Number(Units.M3sToM3h(r.Flow[k]), 1) : "off";
                cells[3] = on ? Units.Number(r.Velocity[k], 2) : "";
                cells[4] = on ? Units.Number(r.Reynolds[k], 0) : "";
                cells[5] = on ? Units.Number(r.FrictionFactor[k], 4) : "";
                cells[6] = Units.Number(l.SumK, 2);
                cells[7] = on ? Units.Number(r.DpFriction[k], 2) : "";
                cells[8] = on ? Units.Number(l.Kind == LinkKind.Fan ? -r.DpDevice[k] : r.DpFittings[k], 2) : "";
                cells[9] = Units.Number(r.NodePressure[l.From], 2);
                cells[10] = Units.Number(r.NodePressure[l.To], 2);
                DebugRow(k + 1, cells);
            }
            for (int i = g.Links.Count + 1; i < _debugCells.Count; i++) _debugCells[i][0].parent.style.display = DisplayStyle.None;
        }

        void DebugRow(int index, string[] values)
        {
            if (index == _debugCells.Count)
            {
                var row = new VisualElement();
                row.AddToClassList("hl-debug-row");
                var labels = new Label[DebugHeader.Length];
                for (int c = 0; c < labels.Length; c++)
                {
                    labels[c] = new Label();
                    labels[c].AddToClassList("hl-debug-cell");
                    labels[c].style.width = DebugWidths[c];
                    if (c < 2) labels[c].style.unityTextAlign = TextAnchor.MiddleLeft;
                    row.Add(labels[c]);
                }
                _debugRows.Add(row);
                _debugCells.Add(labels);
            }
            Label[] cells = _debugCells[index];
            cells[0].parent.style.display = DisplayStyle.Flex;
            for (int c = 0; c < cells.Length; c++) cells[c].text = values[c];
            if (index == 0) foreach (Label l in cells) l.style.unityFontStyleAndWeight = FontStyle.Bold;
        }
    }
}
