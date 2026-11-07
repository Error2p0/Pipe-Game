using System;
using System.Collections.Generic;
using System.Text;
using HeadLoss.Core;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game;
using HeadLoss.Game.City;
using HeadLoss.Game.Economy;
using HeadLoss.Game.Progression;
using HeadLoss.Game.Session;
using HeadLoss.Unity.View;
using UnityEngine;
using UnityEngine.UIElements;

namespace HeadLoss.Unity.UI
{
    /// <summary>The city view's active tool.</summary>
    public enum CityTool { None, Route, Corridor }

    /// <summary>
    /// City view HUD (<c>11-ui-visuals-audio.md</c> §Screens): the city's name and how much of it is online, the tools (route
    /// a main or cable, utility corridor, utilisation heatmap with a per-utility filter, insurance and engineering
    /// software), the live route quote, the plot panel (difficulty hint on hover, the same panel with an Open button when
    /// a plot is selected by click or tap), the panel of a selected main, a "Start here" marker over the starter house,
    /// and a menu button.
    /// </summary>
    public sealed class CityHud
    {
        readonly GameContext _ctx;
        readonly UiKit _ui;
        readonly CameraController _cam;
        readonly VisualElement _layer, _plotPanel;
        readonly Label _cityName, _cityStatus, _plotTitle, _plotStatus, _plotFacts, _plotDots, _plotNote, _startHere;
        readonly Button _open;
        readonly string _symbol;

        // Tools.
        readonly List<(string utility, Button button)> _utilityTabs = new List<(string, Button)>();
        readonly DropdownField _size, _class;
        readonly List<string> _classes = new List<string>();
        readonly Button _reconstruct;
        readonly List<Label> _alarmLabels = new List<Label>();
        readonly List<double> _sizes = new List<double>();
        readonly Button _routeTool, _corridorTool, _drilling, _heatmap, _insurance, _software;
        readonly List<(string filter, Button button)> _filterTabs = new List<(string, Button)>();
        readonly VisualElement _legend;
        readonly Label _fines, _toolHint;
        readonly VisualElement _routePanel, _mainPanel;
        readonly Label _routeTitle, _mainTitle;
        readonly List<Label> _routeLines = new List<Label>(), _mainLines = new List<Label>();
        readonly Button _removeMain;
        string _shownSizes = "";
        static readonly float[] LegendStops = { 0f, 0.25f, 0.5f, 0.75f, 1f };
        readonly List<Label> _legendChips = new List<Label>();
        string _legendPalette = "";

        /// <summary>Colours the legend from the heatmap palette in the settings.</summary>
        public void SetLegendPalette(string palette)
        {
            if (palette == _legendPalette) return;
            _legendPalette = palette;
            for (int i = 0; i < _legendChips.Count; i++)
            {
                Color c = _ui.P.Ramp(palette, LegendStops[i]);
                _legendChips[i].style.backgroundColor = c;
                _legendChips[i].style.color = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b > 0.45f ? new Color(0.12f, 0.15f, 0.19f) : Color.white;
            }
        }

        /// <summary>The active tool.</summary>
        public CityTool Tool { get; set; }
        /// <summary>Gas mains: the pressure class to lay ("" for other utilities).</summary>
        public string PressureClass { get; private set; } = "";
        /// <summary>The utility being routed: water, gas or power.</summary>
        public string Utility { get; private set; } = "water";
        /// <summary>The main size to lay, mm.</summary>
        public double DiameterMm { get; private set; } = 100;
        /// <summary>The cable size to lay, kW.</summary>
        public double CapacityKW { get; private set; }
        /// <summary>Directional drilling: no recurring fines, no crossing cost, dear per tile.</summary>
        public bool Drilled { get; private set; }
        /// <summary>The utilisation heatmap is on.</summary>
        public bool Heatmap { get; private set; }
        /// <summary>Only this utility is drawn ("" = all).</summary>
        public string Filter { get; private set; } = "";
        /// <summary>The selected main or cable (0 = none).</summary>
        public int SelectedMain { get; set; }
        /// <summary>Audio cues the root plays and clears.</summary>
        public bool Clicked, Laid;
        string _shownPlot = "";
        int _shownState = -1;
        float _next;

        /// <summary>The player asked to open this plot's building (consumed by the scene).</summary>
        public string? OpenRequested;
        public bool MenuRequested;
        public bool ShopRequested;
        public bool SettingsRequested;
        public bool PrestigeRequested;

        public CityHud(VisualElement root, UiKit ui, GameContext ctx, CameraController camera)
        {
            _ctx = ctx;
            _ui = ui;
            _cam = camera;
            _symbol = ctx.Data.Game.CurrencySymbol;
            _layer = new VisualElement { pickingMode = PickingMode.Ignore };
            _layer.AddToClassList("hl-root");
            root.Add(_layer);

            _startHere = UiKit.Text(_layer, "Start here", "hl-world-label");
            _startHere.AddToClassList("hl-start-here");
            _startHere.style.backgroundColor = ui.P.Paper;
            _startHere.pickingMode = PickingMode.Ignore;

            var left = new VisualElement { pickingMode = PickingMode.Ignore };
            left.AddToClassList("hl-cityleft");
            _layer.Add(left);
            VisualElement city = ui.Panel(left, "hl-citypanel");
            _cityName = UiKit.Text(city, ctx.City!.Name, "hl-title");
            _cityStatus = UiKit.Text(city, "", "hl-subtitle");
            UiKit.Text(city, "Hover or tap a building to see what it needs. Open it to design its ducts.", "hl-subtitle");

            VisualElement tools = ui.Panel(left, "hl-citytools");
            UiKit.Text(tools, "Utilities", "hl-section");
            VisualElement utilityRow = UiKit.Row(tools);
            foreach ((string id, string name) in new[] { ("water", "Water"), ("gas", "Gas"), ("power", "Power") })
            {
                string u = id;
                _utilityTabs.Add((u, ui.Button(utilityRow, name, () => SetUtility(u))));
            }
            _size = new DropdownField();
            _size.AddToClassList("hl-dropdown");
            ui.StyleField(_size);
            _size.RegisterValueChangedCallback(_ => ApplySize());
            tools.Add(_size);
            _class = new DropdownField();
            _class.AddToClassList("hl-dropdown");
            ui.StyleField(_class);
            _class.RegisterValueChangedCallback(_ => ApplyClass());
            tools.Add(_class);
            VisualElement toolRow = UiKit.Row(tools);
            _routeTool = ui.Button(toolRow, "Route", () => Tool = Tool == CityTool.Route ? CityTool.None : CityTool.Route);
            _corridorTool = ui.Button(toolRow, "Corridor", () => Tool = Tool == CityTool.Corridor ? CityTool.None : CityTool.Corridor);
            _drilling = ui.Button(toolRow, "Drilling", () => Drilled = !Drilled);
            _toolHint = UiKit.Text(tools, "", "hl-subtitle");

            UiKit.Text(tools, "Heatmap", "hl-section");
            _heatmap = ui.Button(tools, "Utilisation", () => Heatmap = !Heatmap);
            VisualElement filterRow = UiKit.Row(tools);
            foreach ((string id, string name) in new[] { ("", "All"), ("water", "Water"), ("gas", "Gas"), ("power", "Power") })
            {
                string f = id;
                _filterTabs.Add((f, ui.Button(filterRow, name, () => Filter = f)));
            }
            _legend = new VisualElement();
            tools.Add(_legend);
            UiKit.Text(_legend, "Share of capacity (0–100%)", "hl-subtitle");
            VisualElement chips = UiKit.Row(_legend);
            foreach (float at in LegendStops)
            {
                var chip = new Label($"{at * 100:0}");
                chip.AddToClassList("hl-legend-chip");
                chips.Add(chip);
                _legendChips.Add(chip);
            }
            var over = new Label("100+");
            over.AddToClassList("hl-legend-chip");
            over.style.backgroundColor = ui.P.Warning;
            over.style.color = Color.white;
            chips.Add(over);

            UiKit.Text(tools, "City services", "hl-section");
            _fines = UiKit.Text(tools, "", "hl-readout-line");
            _insurance = ui.Button(tools, "", ToggleInsurance);
            _software = ui.Button(tools, "", () => { if (_ctx.BuyEngineeringSoftware()) Clicked = true; });

            _routePanel = ui.Panel(left, "hl-routepanel");
            _routeTitle = UiKit.Text(_routePanel, "", "hl-section");
            for (int i = 0; i < 8; i++) _routeLines.Add(UiKit.Text(_routePanel, "", "hl-readout-line"));
            _routePanel.style.display = DisplayStyle.None;

            _mainPanel = ui.Panel(_layer, "hl-plotpanel");
            _mainTitle = UiKit.Text(_mainPanel, "", "hl-title");
            for (int i = 0; i < 5; i++) _mainLines.Add(UiKit.Text(_mainPanel, "", "hl-readout-line"));
            _removeMain = ui.Button(_mainPanel, "Remove", RemoveSelected);
            _removeMain.style.alignSelf = Align.FlexStart;
            _mainPanel.style.display = DisplayStyle.None;

            _plotPanel = ui.Panel(_layer, "hl-plotpanel");
            _plotTitle = UiKit.Text(_plotPanel, "", "hl-title");
            _plotStatus = UiKit.Text(_plotPanel, "", "hl-readout-line");
            _plotDots = UiKit.Text(_plotPanel, "", "hl-readout-line");
            _plotFacts = UiKit.Text(_plotPanel, "", "hl-readout-line");
            _plotNote = UiKit.Text(_plotPanel, "", "hl-subtitle");
            _open = ui.Button(_plotPanel, "Open", () => OpenRequested = _shownPlot);
            _open.style.alignSelf = Align.FlexStart;
            _reconstruct = ui.Button(_plotPanel, "", () => { if (_ctx.Reconstruct(_shownPlot)) Clicked = true; });
            _reconstruct.style.alignSelf = Align.FlexStart;
            _reconstruct.style.display = DisplayStyle.None;
            _plotPanel.style.display = DisplayStyle.None;

            VisualElement bottom = new VisualElement();
            bottom.AddToClassList("hl-citymenu");
            _layer.Add(bottom);
            ui.Button(bottom, "Menu", () => MenuRequested = true);
            ui.Button(bottom, "Shop", () => ShopRequested = true);
            ui.Button(bottom, "New city", () => PrestigeRequested = true);
            ui.Button(bottom, "Settings", () => SettingsRequested = true);
        }

        public void Dispose() => _layer.RemoveFromHierarchy();

        void SetUtility(string utility)
        {
            Utility = utility;
            _shownSizes = "";
        }

        void ApplyClass()
        {
            PipeFamilyDef? family = Utilities.FamilyFor(_ctx.Data, Utility);
            if (_class.index < 0 || _class.index >= _classes.Count || family == null) return;
            PressureClass = _classes[_class.index];
            _shownSizes = "";
        }

        /// <summary>The unlocked pressure classes of the utility (gas only; the picker hides for the rest).</summary>
        void RefreshClasses()
        {
            PipeFamilyDef? family = Utilities.FamilyFor(_ctx.Data, Utility);
            bool any = family != null && family.PressureClasses.Count > 0;
            _class.style.display = any ? DisplayStyle.Flex : DisplayStyle.None;
            if (!any)
            {
                PressureClass = "";
                _classes.Clear();
                return;
            }
            var names = new List<string>();
            var ids = new List<string>();
            foreach (PressureClassDef c in family!.PressureClasses)
            {
                if (!Tech.Unlocked(_ctx.Game, c.Tier)) continue;
                ids.Add(c.Id);
                names.Add(c.DisplayName);
            }
            if (ids.Count == 0)
            {
                PressureClass = family.PressureClasses[0].Id;
                _class.SetEnabled(false);
                return;
            }
            _class.SetEnabled(true);
            if (ids.Count != _classes.Count)
            {
                _classes.Clear();
                _classes.AddRange(ids);
                _class.choices = names;
            }
            if (!_classes.Contains(PressureClass)) PressureClass = _classes[0];
            _class.SetValueWithoutNotify(names[_classes.IndexOf(PressureClass)]);
        }

        void ToggleInsurance()
        {
            UtilityState? u = _ctx.Game.City?.Utilities;
            if (u != null && _ctx.SetInsurance(!u.Insurance)) Clicked = true;
        }

        void RemoveSelected()
        {
            if (_ctx.RemoveMain(SelectedMain)) Clicked = true;
            SelectedMain = 0;
        }

        void ApplySize()
        {
            if (_size.index < 0 || _size.index >= _sizes.Count) return;
            if (Utility == "power") CapacityKW = _sizes[_size.index];
            else DiameterMm = _sizes[_size.index];
        }

        /// <summary>The unlocked sizes of the utility in the dropdown (rebuilt when they change).</summary>
        void RefreshSizes()
        {
            PipeFamilyDef? family = Utilities.FamilyFor(_ctx.Data, Utility);
            int tier = Tech.UnlockedTier(_ctx.Game);
            string key = $"{Utility}/{tier}/{PressureClass}";
            bool any = false;
            if (family != null)
                foreach (PipeSizeDef s in family.Sizes) any |= _ctx.Game.City != null && Tech.Unlocked(_ctx.Game, s.Tier);
            if (key != _shownSizes && family != null)
            {
                _shownSizes = key;
                _sizes.Clear();
                var names = new List<string>();
                foreach (PipeSizeDef s in family.Sizes)
                {
                    if (!Tech.Unlocked(_ctx.Game, s.Tier)) continue;
                    if (family.Kind == "cable")
                    {
                        _sizes.Add(s.CapacityKW);
                        names.Add($"{s.CapacityKW:0} kW · {Units.FormatMoney(s.PricePerTile, _symbol)} per tile");
                    }
                    else
                    {
                        _sizes.Add(s.DiameterMm);
                        double q = Utilities.CapacityM3s(family, s.DiameterMm, PressureClass);
                        names.Add($"{s.DiameterMm:0} mm · {Units.M3sToM3h(q):0} m³/h · {Units.FormatMoney(Utilities.PricePerTile(_ctx.Data, family, s.DiameterMm, 0, PressureClass), _symbol)} per tile");
                    }
                }
                _size.choices = names;
                double current = Utility == "power" ? CapacityKW : DiameterMm;
                int at = _sizes.IndexOf(current);
                if (at < 0) at = 0;
                if (_sizes.Count > 0)
                {
                    _size.SetValueWithoutNotify(names[at]);
                    if (Utility == "power") CapacityKW = _sizes[at];
                    else DiameterMm = _sizes[at];
                }
                else _size.SetValueWithoutNotify("");
            }
            _size.SetEnabled(any);
            _routeTool.SetEnabled(any);
            if (!any && Tool == CityTool.Route) Tool = CityTool.None;
            int firstTier = family != null && family.Sizes.Count > 0 ? family.Sizes[0].Tier : 0;
            string word = Utility == "power" ? "Cables" : Utility == "gas" ? "Gas mains" : "Water mains";
            string hint = !any ? $"{word} unlock at tier {firstTier}. Until then {(Utility == "power" ? "every building has its basic connection." : Utility == "gas" ? "buildings with stoves wait for gas." : "buildings with taps wait for water.")}"
                : Tool == CityTool.Route ? Utility == "power" ? "Drag from a substation or a cable to a building. Right click cancels."
                    : Utility == "gas" ? "Drag from the gas station or a main of the same class to a building. Keep clear of substations and water mains. Right click cancels."
                    : "Drag from the treatment plant or a main to a building. Right click cancels."
                : Tool == CityTool.Corridor ? $"Click road tiles to add a shared trench: {_ctx.Data.Balancing.City.CorridorCapacity} lines per tile, {Units.FormatMoney(_ctx.Data.Balancing.City.CorridorPerTile, _symbol)} per tile."
                : "Click a main or cable to see it.";
            _toolHint.text = hint;
        }

        void RefreshTools()
        {
            RefreshClasses();
            RefreshSizes();
            foreach ((string u, Button b) in _utilityTabs) _ui.Style(b, u == Utility);
            _ui.Style(_routeTool, Tool == CityTool.Route);
            _ui.Style(_corridorTool, Tool == CityTool.Corridor);
            _ui.Style(_drilling, Drilled);
            _drilling.text = Drilled ? $"Drilling on · +{Units.FormatMoney(_ctx.Data.Balancing.City.DrillingPerTile, _symbol)}/tile" : "Drilling off";
            _ui.Style(_heatmap, Heatmap);
            foreach ((string f, Button b) in _filterTabs) _ui.Style(b, f == Filter);
            _legend.style.display = Heatmap ? DisplayStyle.Flex : DisplayStyle.None;

            int violations = _ctx.CityViolations.Count;
            _fines.text = violations > 0
                ? $"Fines  {Units.FormatMoney(_ctx.FinesPerCycle, _symbol)} per cycle · {violations} violation{(violations == 1 ? "" : "s")}"
                : "No routing violations";
            _fines.style.color = violations > 0 ? _ui.P.Warning : _ui.P.Text;

            MetaUpgradeDef? insurance = _ctx.Data.Upgrades.Meta.Find(m => m.Id == "insurance");
            MetaUpgradeDef? software = _ctx.Data.Upgrades.Meta.Find(m => m.Id == "engineering-software");
            UtilityState? ut = _ctx.Game.City?.Utilities;
            if (insurance != null && ut != null)
            {
                bool ok = Tech.Unlocked(_ctx.Game, insurance.Tier);
                _insurance.text = !ok ? $"Insurance · tier {insurance.Tier}" : ut.Insurance ? $"Insurance on · {insurance.PremiumFraction * 100:0}% of gross · fines ×{insurance.FineFactor:0.##}" : $"Insurance · {insurance.PremiumFraction * 100:0}% of gross · fines ×{insurance.FineFactor:0.##}";
                _insurance.SetEnabled(ok);
                _ui.Style(_insurance, ut.Insurance);
            }
            if (software != null && ut != null)
            {
                bool ok = Tech.Unlocked(_ctx.Game, software.Tier);
                _software.text = ut.EngineeringSoftware ? "Engineering software owned" : ok ? $"Engineering software · {Units.FormatMoney(software.Price, _symbol)}" : $"Engineering software · tier {software.Tier}";
                _software.SetEnabled(ok && !ut.EngineeringSoftware);
            }
            RefreshMain();
        }

        void RefreshMain()
        {
            MainState? m = SelectedMain != 0 ? _ctx.Game.City?.Utilities.Mains.Find(x => x.Id == SelectedMain) : null;
            _mainPanel.style.display = m != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (m == null) return;
            _plotPanel.style.display = DisplayStyle.None;
            int line = 0;
            void Line(string text)
            {
                _mainLines[line].text = text;
                _mainLines[line].style.display = DisplayStyle.Flex;
                line++;
            }
            PressureClassDef? cls = Utilities.FamilyFor(_ctx.Data, m.Utility)?.ClassById(m.PressureClass);
            _mainTitle.text = m.Utility == "power" ? $"Power cable · {m.CapacityKW:0} kW" : m.Utility == "gas" ? $"Gas main · {m.DiameterMm:0} mm · {cls?.DisplayName ?? m.PressureClass}" : $"Water main · {m.DiameterMm:0} mm";
            double metres = (m.Tiles.Count - 1) * _ctx.City!.TileMetres;
            Line($"Length  {metres:0} m{(m.Drilled ? " · directionally drilled" : "")}");
            double use = _ctx.CityResult != null && _ctx.CityResult.Utilisation.TryGetValue(m.Id, out double u) ? u : 0;
            Line($"Utilisation  {use * 100:0}%{(use > 1 ? " · over capacity: the buildings it feeds share what it can carry" : "")}");
            Line(m.Destination.Length > 0 ? $"Feeds  {_ctx.CityBuildings!.Hint(m.Destination)?.DisplayName ?? m.Destination}" : "A trunk: branch from it by routing from any of its tiles");
            int here = 0;
            foreach (CityViolation v in _ctx.CityViolations)
                foreach (int[] t in m.Tiles)
                    if (t[0] == v.X && t[1] == v.Y) { here++; break; }
            if (here > 0) Line($"{here} tile{(here == 1 ? "" : "s")} in violation (fines every cycle until it moves)");
            for (int i = line; i < _mainLines.Count; i++) _mainLines[i].style.display = DisplayStyle.None;
            _removeMain.text = $"Remove · refund {Units.FormatMoney(m.Paid * _ctx.Data.Balancing.City.RemoveRefundFraction, _symbol)}";
        }

        /// <summary>Shows the route tool's quote (null hides the panel): cost, fines with each reason, resulting pressure, or why it cannot be laid.</summary>
        public void ShowRoute(RouteQuote? q, WaterConnection? water)
        {
            _routePanel.style.display = q == null ? DisplayStyle.None : DisplayStyle.Flex;
            if (q == null) return;
            int line = 0;
            void Line(string text, Color colour)
            {
                if (line >= _routeLines.Count) return;
                _routeLines[line].text = text;
                _routeLines[line].style.color = colour;
                _routeLines[line].style.display = DisplayStyle.Flex;
                line++;
            }
            if (!q.Valid && q.Error.Length > 0 && q.Error != "Drag at least one tile")
            {
                _routeTitle.text = "Cannot lay this route";
                Line(q.Error, _ui.P.Warning);
            }
            else if (!q.Valid)
            {
                _routeTitle.text = "New route";
                Line("Drag along the tile grid", _ui.P.Text);
            }
            else
            {
                _routeTitle.text = q.Destination.Length > 0 ? $"To {_ctx.CityBuildings!.Hint(q.Destination)?.DisplayName ?? q.Destination}" : "A trunk: end it on a building to connect it";
                string parts = Units.FormatMoney(q.PipeCost, _symbol);
                if (q.CrossingCost > 0) parts += $" + crossings {Units.FormatMoney(q.CrossingCost, _symbol)}";
                if (q.DrillingCost > 0) parts += $" + drilling {Units.FormatMoney(q.DrillingCost, _symbol)}";
                Line($"Cost  {Units.FormatMoney(q.Cost, _symbol)} ({parts})", _ui.P.Text);
                if (water is WaterConnection w)
                {
                    string at = Utility == "gas" ? Units.FormatMbar(w.PressurePa) : Units.FormatBar(w.PressurePa);
                    Line(w.Share < 0.999 ? $"Connection  {at} · mains carry {w.Share * 100:0}% of its demand" : $"Connection  {at}", _ui.P.Text);
                }
                if (q.Violations.Count > 0)
                {
                    Line($"Fines  {Units.FormatMoney(q.FinesPerCycle, _symbol)} per cycle until it moves", _ui.P.Danger);
                    var seen = new HashSet<string>();
                    foreach (CityViolation v in q.Violations)
                    {
                        if (!seen.Add(v.Reason)) continue;
                        int count = q.Violations.FindAll(x => x.Reason == v.Reason).Count;
                        Line($"{v.Reason} · {count} tile{(count == 1 ? "" : "s")}", _ui.P.Danger);
                    }
                }
            }
            for (int i = line; i < _routeLines.Count; i++) _routeLines[i].style.display = DisplayStyle.None;
        }

        /// <summary>True when the pointer (Input System screen coordinates, origin bottom-left) is over a HUD element.</summary>
        public bool IsPointerOverUi(Vector2 screen)
        {
            IPanel? panel = _layer.panel;
            if (panel == null) return false;
            Vector2 pos = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screen.x, Screen.height - screen.y));
            return panel.Pick(pos) != null;
        }

        /// <param name="plotId">Plot to show (selected, else hovered; "" hides the panel).</param>
        /// <param name="selected">The plot is selected (the Open button shows).</param>
        public void Update(float time, string plotId, bool selected)
        {
            int state = (selected ? 1 : 0) + (plotId == _shownPlot ? 0 : 2);
            if (time < _next && plotId == _shownPlot && state == _shownState) return;
            _next = time + 0.25f;
            _shownPlot = plotId;
            _shownState = state;
            RefreshTools();

            int online = 0;
            double net = 0;
            foreach (BuildingSession s in _ctx.Sessions)
            {
                if (s.CommittedEvaluation.Online) online++;
                net += s.CommittedEvaluation.Net;
            }
            GameLoop.FinesPerSecond(_ctx, out double fines);
            net -= fines + GameLoop.PremiumsPerSecond(_ctx);
            _cityStatus.text = $"{online} of {_ctx.City!.Plots.Count} buildings online · {Units.FormatMoneyRate(net, _symbol)}";
            if (_ctx.City.Modifiers.Count > 0)
            {
                var mods = new List<string>();
                foreach (string id in _ctx.City.Modifiers) mods.Add(_ctx.Data.City.Modifiers.Find(x => x.Id == id)?.DisplayName ?? id);
                _cityStatus.text += " · " + string.Join(", ", mods);
            }

            if (plotId.Length == 0 || (SelectedMain != 0 && _mainPanel.style.display == DisplayStyle.Flex))
            {
                _plotPanel.style.display = DisplayStyle.None;
                return;
            }
            _plotPanel.style.display = DisplayStyle.Flex;
            _reconstruct.style.display = DisplayStyle.None;
            if (_ctx.IsRuin(plotId))
            {
                bool can = _ctx.CanReconstruct;
                _plotTitle.text = "Ruin";
                _plotStatus.text = can ? $"Rebuild with Reconstruction permit: {Units.FormatMoney(_ctx.ReconstructionPrice(plotId), _symbol)}" : "Ruin · cannot be rebuilt in this city";
                _plotStatus.style.color = _ui.P.Text;
                _plotDots.text = _plotFacts.text = "";
                _plotNote.text = "Founding a new city clears ruins. It counts as a building that is not online.";
                _open.style.display = DisplayStyle.None;
                if (can && selected)
                {
                    _reconstruct.style.display = DisplayStyle.Flex;
                    _reconstruct.text = $"Rebuild · {Units.FormatMoney(_ctx.ReconstructionPrice(plotId), _symbol)}";
                    _reconstruct.SetEnabled(_ctx.Game.CanAfford(_ctx.ReconstructionPrice(plotId)));
                }
                return;
            }
            DifficultyHint? h = _ctx.CityBuildings!.Hint(plotId);
            if (h == null)
            {
                _plotTitle.text = "Surveying…";
                _plotStatus.text = _plotDots.text = _plotFacts.text = _plotNote.text = "";
                _open.style.display = DisplayStyle.None;
                _next = 0;
                return;
            }
            _plotTitle.text = h.Starter ? $"{h.DisplayName} · start here" : h.DisplayName;
            _plotStatus.text = Status(h);
            _plotDots.text = $"Difficulty  {new string('●', h.Dots)}{new string('○', 5 - h.Dots)}   · tier {h.Tier}";
            _plotFacts.text = Facts(h);
            string needs = h.Fluids.Contains("water") && !Tech.Unlocked(_ctx.Game, 2) ? "Requires water (tier 2) · "
                : h.Fluids.Contains(Game.Buildings.BuildingState.GasFluid) && !Tech.Unlocked(_ctx.Game, 3) ? "Requires natural gas (tier 3) · " : "";
            _plotNote.text = needs + (!h.Available ? h.UnavailableNote : selected ? "" : "Click to select · double-click to open");
            _open.style.display = selected && h.Available ? DisplayStyle.Flex : DisplayStyle.None;
            _plotStatus.style.color = _ctx.FindSession(plotId)?.CommittedEvaluation.Online == true ? _ui.P.Good : _ui.P.Text;
        }

        string Status(DifficultyHint h)
        {
            if (!h.Available) return "Not available yet";
            BuildingSession? s = _ctx.FindSession(h.PlotId);
            if (s == null || (!s.Record.EverOnline && s.Committed.Air.Segments.Count == 0 && s.Committed.Water.Segments.Count == 0 && s.Committed.Gas.Segments.Count == 0))
                return $"Not built yet · budget cap {Units.FormatMoney(h.BudgetCap, _symbol)}";
            BuildingEvaluation ev = s.CommittedEvaluation;
            string paying = ev.Electricity > 0 ? $" · paying {Units.FormatMoneyRate(ev.Electricity, _symbol)} electricity" : "";
            if (s.GasSafety.TimerRunning) return $"Evacuated · gas alarm · {Units.FormatDuration(s.GasSafety.TimerLeft)} until it can ignite";
            if (s.GasSafety.Evacuated) return "Evacuated · the gas is stopped; waiting for the repair and clear air" + paying;
            if (s.Record.Failures.Leaks.Count > 0) return $"Gas leak · {s.Record.Failures.Leaks.Count} to repair";
            if (s.Record.Failures.DamagedEquipment.Count > 0) return $"Damaged · {s.Record.Failures.DamagedEquipment.Count} part{(s.Record.Failures.DamagedEquipment.Count == 1 ? "" : "s")} knocked offline";
            if (s.Record.PanelTripped) return $"Offline · panel tripped: {Units.FormatPower(s.RequestedPowerW)} of {s.PanelLimitKW:0.#} kW";
            if (ev.Burst) return $"Burst · {Units.FormatMoneyRate(ev.Gross, _symbol)} until it is repaired{paying}";
            if (ev.Online) return $"Online · {Units.FormatMoneyRate(ev.Net, _symbol)}{(ev.CertificateExpired ? " · gas certificate expired: pay halved" : "")}";
            if (s.HasWater && !s.Supply.Connected) return "Offline · no city water: route a main here" + paying;
            if (s.HasGas && !s.GasSupply.Connected) return "Offline · no city gas: route a gas main here" + paying;
            if (ev.BlockingViolations > 0) return $"Offline · blocked by {ev.BlockingViolations} code violation{(ev.BlockingViolations == 1 ? "" : "s")}" + paying;
            return $"Offline · {ev.BelowMinimum} below minimum" + paying;
        }

        string Facts(DifficultyHint h)
        {
            var sb = new StringBuilder();
            sb.Append($"{h.WidthM} × {h.HeightM} m · {h.Floors} floor{(h.Floors == 1 ? "" : "s")}");
            var fluids = new List<string>();
            foreach (string f in h.Fluids) fluids.Add(_ctx.Data.FindNetwork(f)?.DisplayName ?? _ctx.Data.Fluid(f).DisplayName);
            sb.Append($"\nNeeds {string.Join(", ", fluids)}");
            if (h.Available)
            {
                sb.Append($"\n{h.Outlets} outlets · about {Units.M3sToM3h(h.FlowDemand):0} m³/h in total");
                if (_ctx.CityResult != null && _ctx.CityResult.Water.TryGetValue(h.PlotId, out WaterConnection water) && water.Connected)
                    sb.Append($"\nCity water  {Units.FormatBar(water.PressurePa)}{(water.Share < 0.999 ? $" · mains carry {water.Share * 100:0}%" : "")}");
                if (_ctx.CityResult != null && _ctx.CityResult.Gas.TryGetValue(h.PlotId, out WaterConnection gasIn) && gasIn.Connected)
                    sb.Append($"\nCity gas  {Units.FormatMbar(gasIn.PressurePa)}{(gasIn.Share < 0.999 ? $" · mains carry {gasIn.Share * 100:0}%" : "")}");
                if (_ctx.CityResult != null && _ctx.CityResult.Power.TryGetValue(h.PlotId, out PowerConnection power) && power.Cabled)
                    sb.Append($"\nCable  {power.ConnectionKW:0} kW · the city gives {power.AllocatedKW:0.#} kW");
                sb.Append($"\nLongest run from the intake {h.MaxPathMetres:0} m · {h.Codes} code{(h.Codes == 1 ? "" : "s")} in force");
            }
            foreach (var kv in h.FacilityDistances) sb.Append($"\n{FacilityName(kv.Key)} {kv.Value:0} m away");
            if (h.Fluids.Count == 1 && h.Fluids[0] == "air") sb.Append("\nAir comes from the building's own intake");
            return sb.ToString();
        }

        static string FacilityName(string u) => u switch
        {
            "water" => "Water treatment plant",
            "gas" => "Gas receiving station",
            "heating" => "District heating plant",
            "special-gas" => "Industrial gas depot",
            "power" => "Substation",
            _ => u,
        };

        /// <summary>A clear countdown over every building with its explosion timer running (08 §Flammable gas escalation).</summary>
        public void PositionAlarms()
        {
            IPanel? panel = _layer.panel;
            int used = 0;
            float t = (float)_ctx.City!.TileMetres;
            foreach (BuildingSession s in _ctx.Sessions)
            {
                if (panel == null || !s.GasSafety.TimerRunning) continue;
                Plot? p = _ctx.City.FindPlot(s.Record.BuildingId);
                if (p == null) continue;
                if (used == _alarmLabels.Count)
                {
                    Label fresh = UiKit.Text(_layer, "", "hl-world-label");
                    fresh.style.backgroundColor = _ui.P.Paper;
                    fresh.style.color = _ui.P.Danger;
                    fresh.pickingMode = PickingMode.Ignore;
                    _alarmLabels.Add(fresh);
                }
                Label l = _alarmLabels[used++];
                l.style.display = DisplayStyle.Flex;
                int left = (int)System.Math.Ceiling(s.GasSafety.TimerLeft);
                l.text = $"{left / 60}:{left % 60:00}";
                Vector2 pos = RuntimePanelUtils.CameraTransformWorldToPanel(panel, new Vector3((float)(p.CentreX * t), (float)((p.Y + p.Height) * t + 6f), 0), _cam.Camera);
                float w = l.resolvedStyle.width, h = l.resolvedStyle.height;
                l.style.left = pos.x - (float.IsNaN(w) ? 0 : w * 0.5f);
                l.style.top = pos.y - (float.IsNaN(h) ? 0 : h);
            }
            for (int i = used; i < _alarmLabels.Count; i++) _alarmLabels[i].style.display = DisplayStyle.None;
        }

        /// <summary>Keeps the "Start here" label over the starter house (every frame, also under a modal screen).</summary>
        public void PositionStartHere()
        {
            Plot? p = _ctx.City!.Starter;
            BuildingSession? s = p != null ? _ctx.FindSession(p.Id) : null;
            bool show = p != null && (s == null || !s.Record.EverOnline);
            _startHere.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            IPanel? panel = _layer.panel;
            if (!show || panel == null) return;
            float t = (float)_ctx.City.TileMetres;
            Vector3 world = new Vector3((float)(p!.CentreX * t), (float)((p.Y + p.Height) * t + 3f), 0);
            Vector2 pos = RuntimePanelUtils.CameraTransformWorldToPanel(panel, world, _cam.Camera);
            float w = _startHere.resolvedStyle.width, hgt = _startHere.resolvedStyle.height;
            _startHere.style.left = pos.x - (float.IsNaN(w) ? 0 : w * 0.5f);
            _startHere.style.top = pos.y - (float.IsNaN(hgt) ? 0 : hgt);
        }
    }
}
