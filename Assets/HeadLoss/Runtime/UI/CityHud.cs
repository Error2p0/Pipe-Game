using System;
using System.Collections.Generic;
using System.Text;
using HeadLoss.Core;
using HeadLoss.Game;
using HeadLoss.Game.City;
using HeadLoss.Game.Economy;
using HeadLoss.Game.Session;
using HeadLoss.Unity.View;
using UnityEngine;
using UnityEngine.UIElements;

namespace HeadLoss.Unity.UI
{
    /// <summary>
    /// City view HUD (<c>11-ui-visuals-audio.md</c> §Screens): the city's name and how much of it is online, the
    /// plot panel (difficulty hint on hover, the same panel with an Open button when a plot is selected by click or
    /// tap), a "Start here" marker over the starter house, and a menu button.
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
        string _shownPlot = "";
        int _shownState = -1;
        float _next;

        /// <summary>The player asked to open this plot's building (consumed by the scene).</summary>
        public string? OpenRequested;
        public bool MenuRequested;
        public bool ShopRequested;

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

            VisualElement city = ui.Panel(_layer, "hl-citypanel");
            _cityName = UiKit.Text(city, ctx.City!.Name, "hl-title");
            _cityStatus = UiKit.Text(city, "", "hl-subtitle");
            UiKit.Text(city, "Hover or tap a building to see what it needs. Open it to design its ducts.", "hl-subtitle");

            _plotPanel = ui.Panel(_layer, "hl-plotpanel");
            _plotTitle = UiKit.Text(_plotPanel, "", "hl-title");
            _plotStatus = UiKit.Text(_plotPanel, "", "hl-readout-line");
            _plotDots = UiKit.Text(_plotPanel, "", "hl-readout-line");
            _plotFacts = UiKit.Text(_plotPanel, "", "hl-readout-line");
            _plotNote = UiKit.Text(_plotPanel, "", "hl-subtitle");
            _open = ui.Button(_plotPanel, "Open", () => OpenRequested = _shownPlot);
            _open.style.alignSelf = Align.FlexStart;
            _plotPanel.style.display = DisplayStyle.None;

            VisualElement bottom = new VisualElement();
            bottom.AddToClassList("hl-citymenu");
            _layer.Add(bottom);
            ui.Button(bottom, "Menu", () => MenuRequested = true);
            ui.Button(bottom, "Shop", () => ShopRequested = true);
        }

        public void Dispose() => _layer.RemoveFromHierarchy();

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
            PositionStartHere();
            int state = (selected ? 1 : 0) + (plotId == _shownPlot ? 0 : 2);
            if (time < _next && plotId == _shownPlot && state == _shownState) return;
            _next = time + 0.25f;
            _shownPlot = plotId;
            _shownState = state;

            int online = 0;
            double net = 0;
            foreach (BuildingSession s in _ctx.Sessions)
            {
                if (s.CommittedEvaluation.Online) online++;
                net += s.CommittedEvaluation.Net;
            }
            _cityStatus.text = $"{online} of {_ctx.City!.Plots.Count} buildings online · {Units.FormatMoneyRate(net, _symbol)}";

            if (plotId.Length == 0)
            {
                _plotPanel.style.display = DisplayStyle.None;
                return;
            }
            _plotPanel.style.display = DisplayStyle.Flex;
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
            _plotNote.text = !h.Available ? h.UnavailableNote : selected ? "" : "Click to select · double-click to open";
            _open.style.display = selected && h.Available ? DisplayStyle.Flex : DisplayStyle.None;
            _plotStatus.style.color = _ctx.FindSession(plotId)?.CommittedEvaluation.Online == true ? _ui.P.Good : _ui.P.Text;
        }

        string Status(DifficultyHint h)
        {
            if (!h.Available) return "Not available yet";
            BuildingSession? s = _ctx.FindSession(h.PlotId);
            if (s == null || (!s.Record.EverOnline && s.Committed.Air.Segments.Count == 0))
                return $"Not built yet · budget cap {Units.FormatMoney(h.BudgetCap, _symbol)}";
            BuildingEvaluation ev = s.CommittedEvaluation;
            if (ev.Online) return $"Online · {Units.FormatMoneyRate(ev.Net, _symbol)}";
            return $"Offline · {ev.BelowMinimum} below minimum" + (ev.Electricity > 0 ? $" · paying {Units.FormatMoneyRate(ev.Electricity, _symbol)} electricity" : "");
        }

        string Facts(DifficultyHint h)
        {
            var sb = new StringBuilder();
            sb.Append($"{h.WidthM} × {h.HeightM} m · {h.Floors} floor{(h.Floors == 1 ? "" : "s")}");
            var fluids = new List<string>();
            foreach (string f in h.Fluids) fluids.Add(_ctx.Data.Fluid(f).DisplayName);
            sb.Append($"\nNeeds {string.Join(", ", fluids)}");
            if (h.Available)
            {
                sb.Append($"\n{h.Outlets} outlets · about {Units.M3sToM3h(h.FlowDemand):0} m³/h in total");
                sb.Append($"\nLongest run from the intake {h.MaxPathMetres:0} m · {h.Codes} code{(h.Codes == 1 ? "" : "s")} in force");
            }
            foreach (var kv in h.FacilityDistances) sb.Append($"\n{Utility(kv.Key)} {kv.Value:0} m away");
            if (h.Fluids.Count == 1 && h.Fluids[0] == "air") sb.Append("\nAir comes from the building's own intake");
            return sb.ToString();
        }

        static string Utility(string u) => u switch
        {
            "water" => "Water treatment plant",
            "gas" => "Gas receiving station",
            "heating" => "District heating plant",
            "special-gas" => "Industrial gas depot",
            "power" => "Substation",
            _ => u,
        };

        void PositionStartHere()
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
