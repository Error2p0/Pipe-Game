using System;
using System.Collections.Generic;
using HeadLoss.Core;
using HeadLoss.Game;
using HeadLoss.Game.Economy;
using HeadLoss.Game.Session;
using UnityEngine.UIElements;

namespace HeadLoss.Unity.UI
{
    /// <summary>Main menu (<c>11-ui-visuals-audio.md</c> §Screens): Continue, New game, Settings, Quit.</summary>
    public sealed class MenuScreen
    {
        readonly VisualElement _panel;
        readonly VisualElement _confirm;

        public MenuScreen(VisualElement parent, UiKit ui, string title, string? continueInfo, Action onContinue, Action onNew, Action onQuit)
        {
            _panel = ui.Panel(parent, "hl-menu");
            UiKit.Text(_panel, title, "hl-menu-title");
            UiKit.Text(_panel, "Design real duct networks across a city. Every outlet that gets enough air earns.", "hl-subtitle");
            if (continueInfo != null)
            {
                ui.Button(_panel, "Continue", onContinue).AddToClassList("hl-menu-button");
                UiKit.Text(_panel, continueInfo, "hl-subtitle");
            }
            _confirm = new VisualElement();
            ui.Button(_panel, "New city", () =>
            {
                if (continueInfo == null) onNew();
                else _confirm.style.display = DisplayStyle.Flex;
            }).AddToClassList("hl-menu-button");
            _panel.Add(_confirm);
            _confirm.style.display = DisplayStyle.None;
            UiKit.Text(_confirm, "Start again in a new city? The current save is replaced (its last backups are kept).", "hl-readout-line");
            VisualElement row = UiKit.Row(_confirm);
            ui.Button(row, "Start a new city", onNew);
            ui.Button(row, "Keep playing", () => _confirm.style.display = DisplayStyle.None);
            Button settings = ui.Button(_panel, "Settings", () => { });
            settings.AddToClassList("hl-menu-button");
            settings.SetEnabled(false);
            UiKit.Text(_panel, "Settings arrive with the city (Phase 3).", "hl-subtitle");
            ui.Button(_panel, "Quit", onQuit).AddToClassList("hl-menu-button");
        }

        public void Dispose() => _panel.RemoveFromHierarchy();
    }

    /// <summary>
    /// The building list: stands in for the city view until Phase 3 (D34). One row per building with its state
    /// and income, and an Open button.
    /// </summary>
    public sealed class BuildingListScreen
    {
        readonly VisualElement _panel;
        readonly GameContext _ctx;
        readonly List<(BuildingSession s, Label status)> _rows = new List<(BuildingSession, Label)>();
        readonly UiKit _ui;
        float _next;

        public BuildingListScreen(VisualElement parent, UiKit ui, GameContext ctx, Action<string> onOpen, Action onMenu)
        {
            _ctx = ctx;
            _ui = ui;
            _panel = ui.Panel(parent, "hl-buildings");
            UiKit.Text(_panel, "Buildings", "hl-title");
            UiKit.Text(_panel, "The city arrives in Phase 3. For now, pick a house to work on. Every house keeps earning while you are in another one.", "hl-subtitle");
            foreach (BuildingSession s in ctx.Sessions)
            {
                VisualElement row = UiKit.Row(_panel);
                row.AddToClassList("hl-building-row");
                var text = new VisualElement();
                text.style.flexGrow = 1;
                row.Add(text);
                UiKit.Text(text, $"{s.Layout.DisplayName} · tier {s.Layout.Tier} · {s.Layout.Outlets.Count} vents", "hl-readout-title");
                Label status = UiKit.Text(text, "", "hl-readout-line");
                string id = s.Record.BuildingId;
                ui.Button(row, "Open", () => onOpen(id));
                _rows.Add((s, status));
            }
            ui.Button(_panel, "Main menu", onMenu).style.alignSelf = Align.FlexStart;
        }

        public void Update(float time)
        {
            if (time < _next) return;
            _next = time + 0.25f;
            string symbol = _ctx.Data.Game.CurrencySymbol;
            foreach ((BuildingSession s, Label status) in _rows)
            {
                BuildingEvaluation ev = s.CommittedEvaluation;
                if (ev.Online)
                {
                    status.text = $"Online · {Units.FormatMoneyRate(ev.Net, symbol)}";
                    status.style.color = _ui.P.Good;
                }
                else if (s.Committed.Air.Segments.Count == 0 && !s.Record.EverOnline)
                {
                    status.text = $"Not built yet · budget cap {Units.FormatMoney(s.BudgetCap, symbol)}";
                    status.style.color = _ui.P.Text;
                }
                else
                {
                    status.text = $"Offline · {ev.BelowMinimum} below minimum" + (ev.Electricity > 0 ? $" · paying {Units.FormatMoneyRate(ev.Electricity, symbol)} electricity" : "");
                    status.style.color = _ui.P.Warning;
                }
            }
        }

        public void Dispose() => _panel.RemoveFromHierarchy();
    }
}
