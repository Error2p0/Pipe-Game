using System.Collections.Generic;
using System.Text;
using HeadLoss.Core;
using HeadLoss.Core.Data;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.Commands;
using HeadLoss.Game.Economy;
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
        const int ReadoutLines = 10;

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
        DropdownField _size = null!;
        Button _sharp = null!, _smooth = null!, _upgrade = null!;
        readonly List<(string id, Button button)> _partButtons = new List<(string, Button)>();
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

        int _solveRev = -1, _ixRev = -1, _hmRev = -1, _stateRev = -1;
        float _nextMoneyRefresh;
        string _shownActionsKey = "";
        Slider? _damperSlider;
        readonly List<double> _sizes = new List<double>();
        readonly StringBuilder _sb = new StringBuilder(4096);

        /// <summary>The on-screen crank button is held down.</summary>
        public bool CrankHeld { get; private set; }
        /// <summary>The Buildings (back) button was pressed.</summary>
        public bool BackRequested;

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
            _ui.Button(cardRow, "Buildings (Esc)", () => BackRequested = true);

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
            _readout = _ui.Panel(right, "hl-readout");
            _readoutTitle = UiKit.Text(_readout, "", "hl-readout-title");
            for (int i = 0; i < ReadoutLines; i++) _lines[i] = UiKit.Text(_readout, "", "hl-readout-line");
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

        void BuildToolbox(VisualElement parent)
        {
            VisualElement tools = _ui.Panel(parent, "hl-toolbox");
            UiKit.Text(tools, "Duct size", "hl-section");
            var choices = new List<string>();
            PipeFamilyDef family = _data.PipeFamily(_data.Balancing.Drawing.DefaultDuctFamily);
            foreach (PipeSizeDef sz in family.Sizes)
            {
                _sizes.Add(sz.DiameterMm);
                choices.Add($"{sz.DiameterMm:0} mm · {Units.FormatMoney(Pricing.PerMetre(_data, _ix.DrawMaterialId, sz.DiameterMm), _symbol)}/m");
            }
            _size = new DropdownField(choices, _sizes.IndexOf(_ix.DrawDiameterMm));
            _size.AddToClassList("hl-dropdown");
            _size.RegisterValueChangedCallback(_ => _ix.DrawDiameterMm = _sizes[_size.index]);
            tools.Add(_size);

            UiKit.Text(tools, "Elbows", "hl-section");
            VisualElement row = UiKit.Row(tools);
            _sharp = _ui.Button(row, "Sharp", () => SetSmooth(false));
            _smooth = _ui.Button(row, "Long-radius", () => SetSmooth(true));
            _upgrade = _ui.Button(tools, "Upgrade all bends", () => _s.Execute(new UpgradeAllBendsCommand()));

            UiKit.Text(tools, "Parts (click a straight duct)", "hl-section");
            foreach (string id in _data.Game.Toolbox.Parts)
            {
                string label = PartLabel(id);
                string part = id;
                Button b = _ui.Button(tools, label, () => _ix.SetPlaceTool(part));
                _partButtons.Add((id, b));
            }

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
            _ix.DrawSmoothElbows = smooth;
            _hmRev = -1;
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
            _solveRev = _s.SolveRevision;
            _ixRev = _ix.Revision;
            _hmRev = _hm.Revision;
            _stateRev = _s.StateRevision;

            if (_outlets.Clicked.Length > 0)
            {
                _ix.Select(new Target { Kind = TargetKind.Outlet, Anchor = _outlets.Clicked });
                _outlets.Clicked = "";
            }
            if (solveChanged || stateChanged || hmChanged || ixChanged) RefreshTools();
            if (solveChanged || hmChanged) RefreshLegend();
            if (solveChanged || ixChanged || stateChanged) RefreshReadout(stateChanged);
            if (solveChanged || ixChanged) RefreshFittingLabels();
            if (solveChanged || ixChanged || stateChanged) RefreshHint();
            if (solveChanged)
            {
                RefreshVentLabels();
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
            bool anyBuilt = _s.Committed.Air.Segments.Count > 0;
            string settle = _s.Settling ? " · network still settling" : "";
            if (ev.Online)
            {
                string over = ev.AboveCodeMax > 0 ? $" · {ev.AboveCodeMax} above code max" : "";
                _status.text = $"Online · earning {Units.FormatMoneyRate(ev.Net, _symbol)}{over}{settle}";
                _status.style.color = _p.Good;
            }
            else if (!anyBuilt && !_s.Record.EverOnline)
            {
                _status.text = "Not built yet · draw ducts from the intake to every vent, then Commit";
                _status.style.color = _p.Text;
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
                FluidDef air = _data.Fluid("air");
                _legendTitle.text = $"Plain · {air.DisplayName} ({air.LinePattern} line)";
                _legendBar.style.backgroundImage = StyleKeyword.None;
                _legendBar.style.backgroundColor = _p.Fluid("air");
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
            _legendTitle.text = "Velocity in ducts (m/s)";
            _legendBar.style.backgroundColor = StyleKeyword.None;
            _legendBar.style.backgroundImage = new StyleBackground(_legendTexture);
            _legendTicks.style.display = DisplayStyle.Flex;
            _lock.style.display = DisplayStyle.Flex;
            _legendMin.text = "0";
            _legendMid.text = Units.Number(_hm.RangeMax / 2, 1);
            _legendMax.text = Units.Number(_hm.RangeMax, 1) + " m/s";
            _lock.text = _hm.Locked ? "Unlock range" : "Lock range";
            StyleButton(_lock, _hm.Locked);
        }

        void RefreshHint()
        {
            string hint = _ix.Hint;
            bool warn = hint.Length > 0 && _ix.PlaceTool.Length == 0;
            if (_ix.PlaceTool.Length > 0 && _ix.PlaceHovering && !_ix.PlaceValid) warn = true;
            if (hint.Length == 0 && _s.State.Air.Segments.Count == 0)
                hint = _s.State.Equipment.Count == 0
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
            switch (t.Kind)
            {
                case TargetKind.Segment:
                    if (t.Id == 0 || _ix.SelectedSegments.Count > 1 && selected)
                    {
                        _readoutTitle.text = $"{_ix.SelectedSegments.Count} ducts selected";
                        lines = Line(lines, "Change their size or delete them.");
                    }
                    else lines = SegmentReadout(t.Id, snap, lines);
                    break;
                case TargetKind.Node: lines = NodeReadout(t.Id, snap, lines); break;
                case TargetKind.Intake: lines = IntakeReadout(t.Anchor, snap, lines); break;
                case TargetKind.Outlet: lines = OutletReadout(t.Anchor, lines); break;
                case TargetKind.Inline: lines = InlineReadout(t.Id, snap, lines); break;
            }
            for (int i = lines; i < ReadoutLines; i++) _lines[i].style.display = DisplayStyle.None;
            _readout.style.display = t.Kind == TargetKind.None ? DisplayStyle.None : DisplayStyle.Flex;

            string key = actions ? ActionsKey(t) : "";
            if (key != _shownActionsKey)
            {
                _shownActionsKey = key;
                BuildActions(actions ? t : Target.None);
            }
            else if (_damperSlider != null && t.Kind == TargetKind.Inline && _s.State.Air.FindNode(t.Id) is DuctNode d)
                _damperSlider.SetValueWithoutNotify((float)d.Opening * 100);
        }

        /// <summary>What the action buttons depend on; they are rebuilt only when it changes (a slider keeps its drag).</summary>
        string ActionsKey(Target t)
        {
            _sb.Clear();
            _sb.Append((int)t.Kind).Append('/').Append(t.Id).Append('/').Append(t.Anchor).Append('/');
            switch (t.Kind)
            {
                case TargetKind.Segment:
                    foreach (int id in _ix.SelectedSegments) _sb.Append(id).Append(',');
                    _sb.Append(_ix.SelectedTile.X).Append(',').Append(_ix.SelectedTile.Y).Append('/').Append(_s.StateRevision);
                    break;
                case TargetKind.Node:
                    if (_s.State.Air.FindNode(t.Id) is DuctNode n) _sb.Append(n.SmoothFittings).Append(_s.State.Air.Degree(n.Id));
                    break;
                case TargetKind.Intake:
                    if (_s.State.EquipmentAt(t.Anchor) is EquipmentState e) _sb.Append(e.EquipmentId).Append(e.On);
                    break;
                case TargetKind.Inline:
                    if (_s.State.Air.FindNode(t.Id) is DuctNode p) _sb.Append(p.PartId).Append(p.On);
                    break;
            }
            return _sb.ToString();
        }

        int Line(int i, string text)
        {
            if (i >= ReadoutLines) return i;
            _lines[i].text = text;
            _lines[i].style.display = DisplayStyle.Flex;
            return i + 1;
        }

        int SegmentReadout(int id, NetworkSnapshot? snap, int i)
        {
            DuctSegment? seg = _s.State.Air.FindSegment(id);
            if (seg == null) return i;
            MaterialDef mat = _data.Material(seg.MaterialId);
            _readoutTitle.text = $"Duct · {seg.DiameterMm:0} mm {mat.DisplayName.ToLowerInvariant()}";
            if (snap == null || !snap.Compiled.SegmentLink.TryGetValue(id, out int k)) return Line(i, "Solving…");
            SolveResult r = snap.Result;
            NetworkLink l = snap.Compiled.Graph.Links[k];
            i = Line(i, $"Flow  {Units.FormatAirFlow(System.Math.Abs(r.Flow[k]))}");
            i = Line(i, $"Velocity  {Units.FormatVelocity(System.Math.Abs(r.Velocity[k]))}");
            double pIn = r.Flow[k] >= 0 ? r.PressureFrom(snap.Compiled.Graph, k) : r.PressureTo(snap.Compiled.Graph, k);
            double pOut = r.Flow[k] >= 0 ? r.PressureTo(snap.Compiled.Graph, k) : r.PressureFrom(snap.Compiled.Graph, k);
            i = Line(i, $"Pressure  {Units.FormatPa(pIn)} → {Units.FormatPa(pOut)}");
            i = Line(i, $"Loss  friction {Units.FormatPa(System.Math.Abs(r.DpFriction[k]))} · fittings {Units.FormatPa(System.Math.Abs(r.DpFittings[k]))} · elevation {Units.FormatPa(r.DpElevation[k])}");
            string regime = r.Reynolds[k] <= _data.Balancing.Physics.LaminarReynolds ? "laminar" : r.Reynolds[k] >= _data.Balancing.Physics.TurbulentReynolds ? "turbulent" : "transitional";
            i = Line(i, $"Re  {r.Reynolds[k]:0} ({regime}) · f {r.FrictionFactor[k]:0.0000}");
            i = Line(i, $"Length  {l.Length:0} m · ΣK {l.SumK:0.00} · {Units.FormatMoney(Pricing.PerMetre(_data, seg.MaterialId, seg.DiameterMm), _symbol)}/m");
            i = Line(i, $"Temperature  {Units.FormatTemperatureK(l.Temperature)}");
            i = Line(i, "Stress  — (from Phase 4)");
            return i;
        }

        int NodeReadout(int id, NetworkSnapshot? snap, int i)
        {
            DuctNode? n = _s.State.Air.FindNode(id);
            if (n == null) return i;
            bool elbow = DuctEditor.IsElbow(_s.State.Air, n);
            int degree = _s.State.Air.Degree(id);
            _readoutTitle.text = elbow
                ? _data.Fitting(n.SmoothFittings ? "elbow-90-long-radius" : "elbow-90-sharp").DisplayName
                : degree >= 4 ? _data.Fitting("cross-sharp").DisplayName
                : degree == 3 ? _data.Fitting("tee-sharp").DisplayName
                : degree == 1 ? _data.Fitting("open-end").DisplayName
                : _data.Fitting("reducer").DisplayName;
            if (snap == null) return Line(i, "Solving…");
            if (degree == 1)
            {
                if (!snap.Compiled.OpenEndLink.TryGetValue(id, out int k)) return Line(i, "Solving…");
                double q = snap.Result.Flow[k];
                i = Line(i, $"Air escaping  {Units.FormatAirFlow(q)}");
                i = Line(i, $"Exit loss  K {_data.Fitting("open-end").K:0.0} · {Units.FormatPa(System.Math.Abs(snap.Result.DpFittings[k]))}");
                return Line(i, "This air reaches no outlet. Extend the duct to a vent or remove it.");
            }
            bool any = false;
            foreach (CompiledFitting f in snap.Compiled.Fittings)
            {
                if (f.NodeId != id) continue;
                any = true;
                double dp = FittingLoss(snap, f);
                string part = f.Kind == FittingKind.TeeRun ? " (run)" : f.Kind == FittingKind.TeeBranch ? " (branch)" : "";
                i = Line(i, $"{_data.Fitting(f.FittingId).DisplayName}{part}  K {f.K:0.00} · loses {Units.FormatPa(dp)}");
            }
            if (!any) i = Line(i, "No loss here");
            if (elbow) i = Line(i, n.SmoothFittings ? "Long-radius: gentle turn, low loss." : "Sharp: abrupt turn, high loss.");
            return i;
        }

        static double FittingLoss(NetworkSnapshot snap, CompiledFitting f)
        {
            double v = snap.Result.Velocity[f.LinkIndex];
            return f.K * 0.5 * snap.Result.Density[f.LinkIndex] * v * v;
        }

        int IntakeReadout(string openingId, NetworkSnapshot? snap, int i)
        {
            EquipmentState? e = _s.State.EquipmentAt(openingId);
            if (e == null)
            {
                _readoutTitle.text = "Air intake · empty";
                i = Line(i, "An open louvre. Place a fan here to move air, or put an inline fan on the duct.");
                return i;
            }
            EquipmentDef def = _data.EquipmentItem(e.EquipmentId);
            _readoutTitle.text = def.DisplayName;
            bool crank = def.SpeedControl == "crank";
            if (!crank && !e.On) return Line(i, "Switched off");
            if (crank) i = Line(i, $"Crank speed  {_s.CrankSpeed * 100:0} % · click or hold C (no electricity, nothing offline)");
            if (snap == null || !snap.Compiled.FanLink.TryGetValue(openingId, out int k)) return Line(i, "Solving…");
            return DeviceLines(snap, k, def, i);
        }

        int DeviceLines(NetworkSnapshot snap, int k, EquipmentDef def, int i)
        {
            SolveResult r = snap.Result;
            i = Line(i, $"Flow  {Units.FormatAirFlow(r.Flow[k])}");
            i = Line(i, $"Pressure rise  {Units.FormatPa(r.DpDevice[k])} (shutoff {def.Curve[0].PressurePa:0} Pa)");
            if (def.PowerModel != "none")
            {
                double cost = r.Power[k] / 1000 * _data.Balancing.Economy.ElectricityPricePerKWs * _data.Balancing.Economy.ElectricityTierFactor[_s.Layout.Tier - 1];
                i = Line(i, $"Efficiency  {r.Efficiency[k] * 100:0} %");
                i = Line(i, $"Power  {Units.FormatPower(r.Power[k])} · electricity {Units.FormatMoneyRate(cost, _symbol)}");
            }
            return i;
        }

        int InlineReadout(int nodeId, NetworkSnapshot? snap, int i)
        {
            DuctNode? n = _s.State.Air.FindNode(nodeId);
            if (n == null) return i;
            EquipmentDef? fan = _data.FindEquipment(n.PartId);
            int k = -1;
            bool solved = snap != null && snap.Compiled.InlineLink.TryGetValue(nodeId, out k);
            if (fan != null)
            {
                _readoutTitle.text = fan.DisplayName;
                if (!n.On) return Line(i, "Switched off (closed to air)");
                if (!solved) return Line(i, "Solving…");
                return DeviceLines(snap!, k, fan, i);
            }
            FittingDef valve = _data.Fitting(n.PartId);
            _readoutTitle.text = valve.DisplayName;
            i = Line(i, $"Open  {n.Opening * 100:0} % · K {valve.K / (n.Opening * n.Opening):0.0#}");
            if (!solved) return Line(i, "Solving…");
            SolveResult r = snap!.Result;
            i = Line(i, $"Flow  {Units.FormatAirFlow(System.Math.Abs(r.Flow[k]))}");
            i = Line(i, $"Pressure lost  {Units.FormatPa(System.Math.Abs(r.DpFittings[k]))}");
            return Line(i, "Drag the Open slider to send air to other branches. Changing the opening is free and works at once, no Commit needed. To put the damper somewhere else, Remove it and place a new one.");
        }

        int OutletReadout(string outletId, int i)
        {
            OutletPlacementDef o = _s.Layout.FindOutlet(outletId)!;
            OutletDef def = _data.Outlet(o.Type);
            _readoutTitle.text = $"{OutletList.NiceName(outletId)} · {def.DisplayName.ToLowerInvariant()}";
            OutletStatus? st = _s.DesignEvaluation.Find(outletId);
            if (st == null || !st.Connected) i = Line(i, "Not connected");
            else
            {
                i = Line(i, $"Delivery  {Units.FormatAirFlow(st.Delivery)}");
                i = Line(i, $"Zone  {UiKit.ZoneWord(st.Zone)} · ×{st.Multiplier:0.00} · {Units.FormatMoneyRate(st.BaseRate * st.Multiplier, _symbol)}");
                if (st.Zone == OutletZone.AboveCodeMax) i = Line(i, $"Above code maximum: {def.CodeMaxReason.ToLowerInvariant()}. It pays nothing until the flow comes down.");
                if (_s.Current != null && _s.Current.Compiled.OutletLink.TryGetValue(outletId, out int k) && _s.Current.Result.Flow[k] < 0)
                    i = Line(i, "Air is flowing backwards into the duct here.");
            }
            if (def.Tiers.TryGetValue(_s.Layout.Tier.ToString(), out OutletRequirementDef req))
                i = Line(i, $"Needs  min {req.Min:0} · ideal {req.Ideal:0} · code max {req.Max:0} m³/h ({def.CodeMaxReason.ToLowerInvariant()})");
            return i;
        }

        void BuildActions(Target t)
        {
            _readoutActions.Clear();
            _damperSlider = null;
            switch (t.Kind)
            {
                case TargetKind.Segment:
                {
                    var ids = new List<int>(_ix.SelectedSegments);
                    var choices = new List<string>();
                    foreach (double d in _sizes) choices.Add($"{d:0} mm");
                    DuctSegment? first = ids.Count > 0 ? _s.State.Air.FindSegment(ids[0]) : null;
                    var dd = new DropdownField(choices, first != null ? _sizes.IndexOf(first.DiameterMm) : 0);
                    dd.AddToClassList("hl-dropdown");
                    dd.style.minWidth = 140;
                    dd.RegisterValueChangedCallback(_ =>
                    {
                        _s.Execute(new SetSegmentSizeCommand(ids, _sizes[dd.index]));
                        _ix.ClearSelection();
                    });
                    _readoutActions.Add(dd);
                    _ui.Button(_readoutActions, "Delete", _ix.DeleteSelection);
                    TilePos tile = _ix.SelectedTile;
                    if (ids.Count == 1 && DuctEditor.ValidateInline(_s.State.Air, tile) == DrawError.None)
                        foreach (string part in _data.Game.Toolbox.Parts)
                        {
                            string p = part;
                            _ui.Button(_readoutActions, "Add " + PartLabel(part).ToLowerInvariant(), () => _s.Execute(new PlaceInlineCommand(tile, p)));
                        }
                    break;
                }
                case TargetKind.Node:
                {
                    DuctNode? n = _s.State.Air.FindNode(t.Id);
                    if (n == null || !DuctEditor.IsElbow(_s.State.Air, n)) break;
                    int id = t.Id;
                    StyleButton(_ui.Button(_readoutActions, "Sharp", () => _s.Execute(new SetElbowCommand(id, false))), !n.SmoothFittings);
                    StyleButton(_ui.Button(_readoutActions, "Long-radius", () => _s.Execute(new SetElbowCommand(id, true))), n.SmoothFittings);
                    break;
                }
                case TargetKind.Intake:
                {
                    string opening = t.Anchor;
                    EquipmentState? e = _s.State.EquipmentAt(opening);
                    foreach (string eqId in _data.Game.Toolbox.IntakeEquipment)
                    {
                        EquipmentDef def = _data.EquipmentItem(eqId);
                        bool here = e != null && e.EquipmentId == eqId;
                        bool preplaced = false;
                        foreach (EquipmentPlacementDef p in _s.Layout.InitialEquipment)
                            if (p.OpeningId == opening && p.EquipmentId == eqId) preplaced = true;
                        string price = preplaced ? "free" : Units.FormatMoney(def.Price, _symbol);
                        string id = eqId;
                        StyleButton(_ui.Button(_readoutActions, $"{def.DisplayName} · {price}", () => _s.Execute(new SetIntakeEquipmentCommand(opening, id))), here);
                    }
                    if (e == null) break;
                    if (_data.EquipmentItem(e.EquipmentId).SpeedControl != "crank")
                        _ui.Button(_readoutActions, e.On ? "Switch off" : "Switch on", () => _s.Execute(new SetEquipmentOnCommand(opening, !e.On)));
                    _ui.Button(_readoutActions, "Remove", () => _s.Execute(new SetIntakeEquipmentCommand(opening, "")));
                    break;
                }
                case TargetKind.Inline:
                {
                    DuctNode? n = _s.State.Air.FindNode(t.Id);
                    if (n == null) break;
                    int id = n.Id;
                    TilePos tile = n.Pos;
                    if (_data.FindEquipment(n.PartId) != null)
                    {
                        bool on = n.On;
                        _ui.Button(_readoutActions, on ? "Switch off" : "Switch on", () => _s.Execute(new SetInlineOnCommand(tile, !on)));
                        _ui.Button(_readoutActions, "Turn around (R)", () => _s.Execute(new FlipInlineCommand(id)));
                    }
                    else
                    {
                        FittingDef valve = _data.Fitting(n.PartId);
                        var slider = new Slider("Open", (float)valve.MinOpening * 100, 100) { value = (float)n.Opening * 100 };
                        slider.AddToClassList("hl-slider");
                        slider.RegisterValueChangedCallback(ev => _s.Execute(new SetDamperOpeningCommand(tile, Mathf.Round(ev.newValue) / 100.0)));
                        _readoutActions.Add(slider);
                        _damperSlider = slider;
                    }
                    _ui.Button(_readoutActions, "Remove", () => _s.Execute(new RemoveInlineCommand(id)));
                    break;
                }
            }
        }

        // ---------- world labels ----------

        void RefreshVentLabels()
        {
            NetworkSnapshot? snap = _s.Current;
            _openLabelCount = 0;
            _openLabelWorld.Clear();
            if (snap != null)
            {
                foreach (var kv in snap.Compiled.OpenEndLink)
                {
                    DuctNode? n = snap.State.Air.FindNode(kv.Key);
                    if (n == null || Units.M3sToM3h(snap.Result.Flow[kv.Value]) < 0.5) continue;
                    if (_openLabelCount == _openLabels.Count)
                    {
                        Label l = WorldLabel();
                        l.style.color = _p.Warning;
                        _openLabels.Add(l);
                    }
                    _openLabels[_openLabelCount++].text = "open end · " + Units.FormatAirFlow(snap.Result.Flow[kv.Value]);
                    _openLabelWorld.Add(DuctGeometry.Centre(n.Pos) + new Vector2(0, 0.5f));
                }
            }
            for (int i = 0; i < _openLabels.Count; i++)
                _openLabels[i].style.display = i < _openLabelCount ? DisplayStyle.Flex : DisplayStyle.None;
            BuildingEvaluation ev = _s.DesignEvaluation;
            for (int i = 0; i < _s.Layout.Outlets.Count; i++)
            {
                OutletStatus? st = ev.Find(_s.Layout.Outlets[i].Id);
                Label l = _ventLabels[i];
                if (st == null || !st.Connected)
                {
                    l.text = "—";
                    UiKit.Border(l, _p.GridLines);
                    continue;
                }
                l.text = $"{Units.FormatAirFlow(st.Delivery)} · {UiKit.ZoneWord(st.Zone)}";
                UiKit.Border(l, _ui.ZoneColour(st.Zone));
            }
        }

        void RefreshFittingLabels()
        {
            _fittingLabelCount = 0;
            _fittingLabelWorld.Clear();
            DrawPreview? pv = _ix.Preview;
            if (_ix.IsDrawing && pv?.Snapshot != null)
            {
                NetworkSnapshot snap = pv.Snapshot;
                var pathTiles = new HashSet<TilePos>(_ix.Path);
                var byNode = new Dictionary<int, string>();
                foreach (CompiledFitting f in snap.Compiled.Fittings)
                {
                    if (f.Kind == FittingKind.Entry) continue;
                    DuctNode? n = snap.State.Air.FindNode(f.NodeId);
                    if (n == null || !pathTiles.Contains(n.Pos)) continue;
                    string s = $"K {f.K:0.0#} · {Units.FormatPa(FittingLoss(snap, f))}";
                    byNode[f.NodeId] = byNode.TryGetValue(f.NodeId, out string prev) ? prev + "\n" + s : s;
                }
                foreach (var kv in byNode)
                {
                    DuctNode n = snap.State.Air.FindNode(kv.Key)!;
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
                Place(panel, _ventLabels[i], new Vector2(o.X + 0.5f, o.Y + 1.0f));
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
            SolveResult r = snap.Result;
            NetworkGraph g = snap.Compiled.Graph;
            _sb.Clear();
            _sb.Append($"Solver  converged {r.Converged} · iterations {r.Iterations} · residual {r.Residual:E2} · max imbalance {r.MaxImbalance:E2} m³/s\n");
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
