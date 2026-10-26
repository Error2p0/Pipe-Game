using System.Collections.Generic;
using System.Text;
using HeadLoss.Core;
using HeadLoss.Core.Data;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.Commands;
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
    /// Building-view HUD (UI Toolkit): title, toolbox, heatmap selector, legend, readout/selection panel,
    /// world labels (vent deliveries, fitting K and Δp while drawing), hint line and the dev debug overlay.
    /// Text is rebuilt only when its inputs change; label positions follow the camera every frame.
    /// </summary>
    public sealed class BuildingHud
    {
        const int ReadoutLines = 10;

        readonly VisualElement _root;
        readonly ViewPalette _p;
        readonly BuildingSession _s;
        readonly BuildingInteraction _ix;
        readonly HeatmapState _hm;
        readonly CameraController _cam;
        readonly DataRepository _data;

        Label _title = null!, _status = null!, _hint = null!;
        DropdownField _size = null!;
        Button _sharp = null!, _smooth = null!, _upgrade = null!, _fanToggle = null!;
        readonly Button[] _modeButtons = new Button[8];
        Label _legendTitle = null!, _legendMin = null!, _legendMid = null!, _legendMax = null!;
        VisualElement _legendBar = null!, _legendTicks = null!;
        Button _lock = null!;
        Texture2D? _legendTexture;
        string _legendPalette = "";
        VisualElement _readout = null!, _readoutActions = null!;
        Label _readoutTitle = null!;
        readonly Label[] _lines = new Label[ReadoutLines];
        VisualElement _labelLayer = null!;
        readonly List<Label> _ventLabels = new List<Label>();
        readonly List<Label> _fittingLabels = new List<Label>();
        readonly List<Vector2> _fittingLabelWorld = new List<Vector2>();
        int _fittingLabelCount;
        VisualElement _debug = null!;
        Label _debugText = null!;
        public bool DebugAvailable { get; set; }
        bool _debugVisible;

        int _solveRev = -1, _ixRev = -1, _hmRev = -1, _stateRev = -1;
        Target _shownTarget = Target.None;
        bool _shownActions;
        int _shownSelectionCount;
        readonly List<double> _sizes = new List<double>();
        readonly StringBuilder _sb = new StringBuilder(4096);

        public BuildingHud(VisualElement root, StyleSheet style, ViewPalette palette, BuildingSession session,
            BuildingInteraction interaction, HeatmapState heatmap, CameraController camera)
        {
            _root = root;
            _p = palette;
            _s = session;
            _ix = interaction;
            _hm = heatmap;
            _cam = camera;
            _data = session.Data;
            root.styleSheets.Add(style);
            root.AddToClassList("hl-root");
            root.pickingMode = PickingMode.Ignore;
            root.style.color = palette.Text;
            Build();
        }

        // ---------- construction ----------

        VisualElement Panel(string cls)
        {
            var v = new VisualElement();
            v.AddToClassList("hl-panel");
            v.AddToClassList(cls);
            Color bg = _p.Paper;
            bg.a = 0.94f;
            v.style.backgroundColor = bg;
            v.style.borderTopColor = v.style.borderBottomColor = v.style.borderLeftColor = v.style.borderRightColor = _p.GridLines;
            _root.Add(v);
            return v;
        }

        Button MakeButton(string text, System.Action onClick)
        {
            var b = new Button(onClick) { text = text };
            b.AddToClassList("hl-button");
            StyleButton(b, false);
            return b;
        }

        void StyleButton(Button b, bool active)
        {
            Color bg = active ? _p.Walls : _p.Paper;
            Color fg = active ? _p.Paper : _p.Text;
            b.style.backgroundColor = bg;
            b.style.color = fg;
            b.style.borderTopColor = b.style.borderBottomColor = b.style.borderLeftColor = b.style.borderRightColor = _p.Walls;
            b.EnableInClassList("hl-button--active", active);
        }

        static Label Text(VisualElement parent, string text, string cls)
        {
            var l = new Label(text);
            l.AddToClassList(cls);
            parent.Add(l);
            return l;
        }

        void Build()
        {
            _labelLayer = new VisualElement { pickingMode = PickingMode.Ignore };
            _labelLayer.style.position = Position.Absolute;
            _labelLayer.style.left = _labelLayer.style.top = _labelLayer.style.right = _labelLayer.style.bottom = 0;
            _root.Add(_labelLayer);

            VisualElement card = Panel("hl-titlecard");
            _title = Text(card, _s.Layout.DisplayName, "hl-title");
            _status = Text(card, "", "hl-subtitle");

            VisualElement tools = Panel("hl-toolbox");
            Text(tools, "Duct size", "hl-section");
            var choices = new List<string>();
            PipeFamilyDef family = _data.PipeFamily(_data.Balancing.Drawing.DefaultDuctFamily);
            foreach (PipeSizeDef sz in family.Sizes)
            {
                _sizes.Add(sz.DiameterMm);
                choices.Add($"{sz.DiameterMm:0} mm");
            }
            _size = new DropdownField(choices, _sizes.IndexOf(_ix.DrawDiameterMm));
            _size.AddToClassList("hl-dropdown");
            _size.RegisterValueChangedCallback(_ => _ix.DrawDiameterMm = _sizes[_size.index]);
            tools.Add(_size);

            Text(tools, "Elbows", "hl-section");
            var row = new VisualElement();
            row.AddToClassList("hl-row");
            _sharp = MakeButton("Sharp", () => SetSmooth(false));
            _smooth = MakeButton("Long-radius", () => SetSmooth(true));
            row.Add(_sharp);
            row.Add(_smooth);
            tools.Add(row);
            _upgrade = MakeButton("Upgrade all bends", () => _s.Execute(new UpgradeAllBendsCommand()));
            tools.Add(_upgrade);

            Text(tools, "Fan", "hl-section");
            _fanToggle = MakeButton("", ToggleFan);
            tools.Add(_fanToggle);

            VisualElement modes = new VisualElement();
            modes.AddToClassList("hl-heatmaps");
            _root.Add(modes);
            for (int i = 0; i < 8; i++)
            {
                var mode = (HeatmapMode)i;
                Button b = MakeButton($"{i} {HeatmapState.DisplayName(mode)}", () => _hm.SetMode(mode));
                b.SetEnabled(HeatmapState.IsAvailable(mode));
                if (!HeatmapState.IsAvailable(mode)) b.tooltip = "Arrives in a later phase";
                _modeButtons[i] = b;
                modes.Add(b);
            }

            VisualElement legend = Panel("hl-legend");
            var legendHead = new VisualElement();
            legendHead.AddToClassList("hl-row");
            legendHead.style.justifyContent = Justify.SpaceBetween;
            legendHead.style.alignItems = Align.Center;
            _legendTitle = Text(legendHead, "", "hl-section");
            _lock = MakeButton("Lock range", () => _hm.SetLocked(!_hm.Locked));
            legendHead.Add(_lock);
            legend.Add(legendHead);
            _legendBar = new VisualElement();
            _legendBar.AddToClassList("hl-legend-bar");
            legend.Add(_legendBar);
            _legendTicks = new VisualElement();
            _legendTicks.AddToClassList("hl-legend-ticks");
            _legendMin = Text(_legendTicks, "", "hl-subtitle");
            _legendMid = Text(_legendTicks, "", "hl-subtitle");
            _legendMax = Text(_legendTicks, "", "hl-subtitle");
            legend.Add(_legendTicks);

            _readout = Panel("hl-readout");
            _readoutTitle = Text(_readout, "", "hl-title");
            for (int i = 0; i < ReadoutLines; i++) _lines[i] = Text(_readout, "", "hl-readout-line");
            _readoutActions = new VisualElement();
            _readoutActions.AddToClassList("hl-row");
            _readoutActions.style.marginTop = 8;
            _readout.Add(_readoutActions);

            _hint = Text(_root, "", "hl-hint");
            _hint.pickingMode = PickingMode.Ignore;

            _debug = Panel("hl-debug");
            var scroll = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
            scroll.style.flexGrow = 1;
            _debugText = new Label();
            _debugText.AddToClassList("hl-debug-text");
            scroll.Add(_debugText);
            _debug.Add(scroll);
            _debug.style.display = DisplayStyle.None;

            foreach (OutletPlacementDef _ in _s.Layout.Outlets) _ventLabels.Add(WorldLabel());
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

        void ToggleFan()
        {
            foreach (EquipmentState e in _s.State.Equipment)
            {
                _s.Execute(new SetEquipmentOnCommand(e.OpeningId, !e.On));
                break;
            }
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

        public void Update()
        {
            bool solveChanged = _s.SolveRevision != _solveRev;
            bool ixChanged = _ix.Revision != _ixRev;
            bool hmChanged = _hm.Revision != _hmRev;
            bool stateChanged = _s.StateRevision != _stateRev;
            _solveRev = _s.SolveRevision;
            _ixRev = _ix.Revision;
            _hmRev = _hm.Revision;
            _stateRev = _s.StateRevision;

            if (solveChanged || stateChanged || hmChanged) RefreshTools();
            if (solveChanged || hmChanged) RefreshLegend();
            if (solveChanged || ixChanged || stateChanged) RefreshReadout(stateChanged);
            if (solveChanged || ixChanged) RefreshFittingLabels();
            if (solveChanged || ixChanged || stateChanged) RefreshHint();
            if (solveChanged) RefreshVentLabels();
            if (solveChanged && _debugVisible) RefreshDebug();
            PositionLabels();
        }

        void RefreshTools()
        {
            int connected = 0;
            foreach (OutletPlacementDef o in _s.Layout.Outlets)
                if (_s.Current != null && _s.Current.Compiled.OutletLink.ContainsKey(o.Id)) connected++;
            string settle = _s.Settling ? " · Network still settling" : "";
            _status.text = $"Air · {connected} of {_s.Layout.Outlets.Count} vents connected{settle}";
            StyleButton(_sharp, !_ix.DrawSmoothElbows);
            StyleButton(_smooth, _ix.DrawSmoothElbows);
            EquipmentState? fan = _s.State.Equipment.Count > 0 ? _s.State.Equipment[0] : null;
            _fanToggle.SetEnabled(fan != null);
            _fanToggle.text = fan == null ? "No fan" : (fan.On ? "Fan on" : "Fan off");
            StyleButton(_fanToggle, fan != null && fan.On);
            for (int i = 0; i < 8; i++) StyleButton(_modeButtons[i], (int)_hm.Mode == i);
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
            if (!string.IsNullOrEmpty(_ix.Hint)) _hint.text = _ix.Hint;
            else if (_s.State.Air.Segments.Count == 0) _hint.text = "Drag from the fan to a vent to draw a duct";
            else _hint.text = "";
            _hint.style.color = string.IsNullOrEmpty(_ix.Hint) ? _p.Text : _p.Warning;
        }

        // ---------- readout ----------

        void RefreshReadout(bool stateChanged)
        {
            Target t = _ix.Hover.Kind != TargetKind.None ? _ix.Hover : _ix.Selected;
            bool actions = t.Kind != TargetKind.None && _ix.Hover.Kind == TargetKind.None;
            int lines = 0;
            NetworkSnapshot? snap = _s.Current;
            switch (t.Kind)
            {
                case TargetKind.Segment:
                    if (t.Id == 0 || _ix.SelectedSegments.Count > 1 && _ix.Hover.Kind == TargetKind.None)
                    {
                        _readoutTitle.text = $"{_ix.SelectedSegments.Count} ducts selected";
                        lines = Line(lines, "Change their size or delete them.");
                    }
                    else lines = SegmentReadout(t.Id, snap, lines);
                    break;
                case TargetKind.Node: lines = NodeReadout(t.Id, snap, lines); break;
                case TargetKind.Fan: lines = FanReadout(t.Anchor, snap, lines); break;
                case TargetKind.Outlet: lines = OutletReadout(t.Anchor, snap, lines); break;
            }
            for (int i = lines; i < ReadoutLines; i++) _lines[i].style.display = DisplayStyle.None;
            _readout.style.display = t.Kind == TargetKind.None ? DisplayStyle.None : DisplayStyle.Flex;

            if (!t.Same(_shownTarget) || actions != _shownActions || _ix.SelectedSegments.Count != _shownSelectionCount || stateChanged)
            {
                _shownTarget = t;
                _shownActions = actions;
                _shownSelectionCount = _ix.SelectedSegments.Count;
                BuildActions(actions ? t : Target.None);
            }
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
            i = Line(i, $"Length  {l.Length:0} m · ΣK {l.SumK:0.00}");
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
                : degree >= 3 ? _data.Fitting("tee-sharp").DisplayName : _data.Fitting("reducer").DisplayName;
            if (snap == null) return Line(i, "Solving…");
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

        int FanReadout(string openingId, NetworkSnapshot? snap, int i)
        {
            EquipmentState? e = _s.State.EquipmentAt(openingId);
            if (e == null) return i;
            EquipmentDef def = _data.EquipmentItem(e.EquipmentId);
            _readoutTitle.text = def.DisplayName;
            if (!e.On) return Line(i, "Switched off");
            if (snap == null || !snap.Compiled.FanLink.TryGetValue(openingId, out int k)) return Line(i, "Solving…");
            SolveResult r = snap.Result;
            i = Line(i, $"Flow  {Units.FormatAirFlow(r.Flow[k])}");
            i = Line(i, $"Pressure rise  {Units.FormatPa(r.DpDevice[k])} (shutoff {def.Curve[0].PressurePa:0} Pa)");
            i = Line(i, $"Efficiency  {r.Efficiency[k] * 100:0} %");
            i = Line(i, $"Power  {Units.FormatPower(r.Power[k])}");
            i = Line(i, $"Speed  {e.Speed * 100:0} %");
            return i;
        }

        int OutletReadout(string outletId, NetworkSnapshot? snap, int i)
        {
            OutletPlacementDef o = _s.Layout.FindOutlet(outletId)!;
            OutletDef def = _data.Outlet(o.Type);
            _readoutTitle.text = def.DisplayName;
            if (snap == null || !snap.Compiled.OutletLink.TryGetValue(outletId, out int k)) i = Line(i, "Not connected");
            else
            {
                SolveResult r = snap.Result;
                i = Line(i, $"Delivery  {Units.FormatAirFlow(r.Flow[k])}");
                i = Line(i, $"Pressure at grille  {Units.FormatPa(r.PressureFrom(snap.Compiled.Graph, k))}");
                i = Line(i, $"Face velocity  {Units.FormatVelocity(System.Math.Abs(r.Velocity[k]))}");
            }
            if (def.Tiers.TryGetValue(_s.Layout.Tier.ToString(), out OutletRequirementDef req))
                i = Line(i, $"Tier {_s.Layout.Tier} requirement  min {req.Min:0} · ideal {req.Ideal:0} · max {req.Max:0} m³/h ({def.CodeMaxReason.ToLowerInvariant()})");
            return i;
        }

        void BuildActions(Target t)
        {
            _readoutActions.Clear();
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
                    _readoutActions.Add(MakeButton("Delete", _ix.DeleteSelection));
                    break;
                }
                case TargetKind.Node:
                {
                    DuctNode? n = _s.State.Air.FindNode(t.Id);
                    if (n == null || !DuctEditor.IsElbow(_s.State.Air, n)) break;
                    int id = t.Id;
                    Button sharp = MakeButton("Sharp", () => _s.Execute(new SetElbowCommand(id, false)));
                    Button smooth = MakeButton("Long-radius", () => _s.Execute(new SetElbowCommand(id, true)));
                    StyleButton(sharp, !n.SmoothFittings);
                    StyleButton(smooth, n.SmoothFittings);
                    _readoutActions.Add(sharp);
                    _readoutActions.Add(smooth);
                    break;
                }
                case TargetKind.Fan:
                {
                    EquipmentState? e = _s.State.EquipmentAt(t.Anchor);
                    if (e == null) break;
                    string opening = t.Anchor;
                    Button b = MakeButton(e.On ? "Switch off" : "Switch on", () => _s.Execute(new SetEquipmentOnCommand(opening, !e.On)));
                    _readoutActions.Add(b);
                    break;
                }
            }
        }

        // ---------- world labels ----------

        void RefreshVentLabels()
        {
            NetworkSnapshot? snap = _s.Current;
            for (int i = 0; i < _s.Layout.Outlets.Count; i++)
            {
                OutletPlacementDef o = _s.Layout.Outlets[i];
                bool connected = snap != null && snap.Compiled.OutletLink.ContainsKey(o.Id);
                _ventLabels[i].text = connected ? Units.FormatAirFlow(snap!.Result.Flow[snap.Compiled.OutletLink[o.Id]]) : "—";
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
            _sb.Clear();
            if (snap == null)
            {
                _debugText.text = "No solve yet";
                return;
            }
            SolveResult r = snap.Result;
            NetworkGraph g = snap.Compiled.Graph;
            _sb.Append($"Solver  converged {r.Converged} · iterations {r.Iterations} · residual {r.Residual:E2} · max imbalance {r.MaxImbalance:E2} m³/s\n");
            _sb.Append($"        linear iterations {r.LinearIterations} · {r.SolveMilliseconds:0.000} ms · nodes {g.Nodes.Count} · links {g.Links.Count}{(_s.Settling ? " · SETTLING" : "")}\n\n");
            _sb.Append(string.Format("{0,-22}{1,-6}{2,10}{3,8}{4,9}{5,8}{6,6}{7,9}{8,9}{9,9}{10,9}\n",
                "link", "kind", "Q m³/h", "v m/s", "Re", "f", "ΣK", "Δp fr", "Δp fit", "p from", "p to"));
            for (int k = 0; k < g.Links.Count; k++)
            {
                NetworkLink l = g.Links[k];
                if (!r.LinkActive[k]) { _sb.Append($"{l.Label,-22}{l.Kind,-6}  inactive\n"); continue; }
                _sb.Append(string.Format("{0,-22}{1,-6}{2,10:0.0}{3,8:0.00}{4,9:0}{5,8:0.0000}{6,6:0.00}{7,9:0.00}{8,9:0.00}{9,9:0.00}{10,9:0.00}\n",
                    l.Label, l.Kind, Units.M3sToM3h(r.Flow[k]), r.Velocity[k], r.Reynolds[k], r.FrictionFactor[k], l.SumK,
                    r.DpFriction[k], l.Kind == LinkKind.Fan ? -r.DpDevice[k] : r.DpFittings[k], r.NodePressure[l.From], r.NodePressure[l.To]));
            }
            _debugText.text = _sb.ToString();
        }
    }
}
