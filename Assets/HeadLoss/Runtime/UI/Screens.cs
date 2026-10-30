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

    /// <summary>
    /// "While you were away" (<c>11-ui-visuals-audio.md</c> §Screens, <c>12-save-offline.md</c>): how long, what the
    /// buildings earned and paid for electricity, the result in the wallet, unlocks, and that nothing broke (bursts,
    /// events and timers arrive with later phases). Continue (Enter or Esc) closes it.
    /// </summary>
    public sealed class AwayScreen
    {
        readonly VisualElement _panel;

        public AwayScreen(VisualElement parent, UiKit ui, GameContext ctx, Game.Offline.OfflineReport r, Action onClose)
        {
            string sym = ctx.Data.Game.CurrencySymbol;
            _panel = ui.Panel(parent, "hl-away");
            UiKit.Text(_panel, "While you were away", "hl-title");
            string time = Duration(r.Seconds);
            UiKit.Text(_panel, r.Capped
                ? $"You were away {Duration(r.AwaySeconds)}. Your buildings worked for the first {time} (the offline limit)."
                : $"You were away {time}. Your buildings kept working.", "hl-subtitle");
            UiKit.Text(_panel, "Money", "hl-section");
            UiKit.Text(_panel, $"Outlets earned  {Units.FormatMoney(r.Gross, sym)}", "hl-readout-line");
            UiKit.Text(_panel, $"Electricity  −{Units.FormatMoney(r.Electricity, sym)}", "hl-readout-line");
            if (r.Fines > 0) UiKit.Text(_panel, $"Fines  −{Units.FormatMoney(r.Fines, sym)}", "hl-readout-line");
            if (r.Premiums > 0) UiKit.Text(_panel, $"Premiums  −{Units.FormatMoney(r.Premiums, sym)}", "hl-readout-line");
            Label net = UiKit.Text(_panel, $"Net  {(r.Net >= 0 ? "+" : "")}{Units.FormatMoney(r.Net, sym)}", "hl-readout-line");
            net.style.unityFontStyleAndWeight = UnityEngine.FontStyle.Bold;
            net.style.color = r.Net >= 0 ? ui.P.Good : ui.P.Warning;
            string wallet = $"Wallet  {Units.FormatMoney(r.WalletBefore, sym)} → {Units.FormatMoney(r.WalletAfter, sym)}";
            if (r.DebtAfter > 0) wallet += $" · debt {Units.FormatMoney(r.DebtAfter, sym)}";
            UiKit.Text(_panel, wallet, "hl-readout-line");
            if (r.Buildings.Count > 0)
            {
                UiKit.Text(_panel, "Buildings", "hl-section");
                int shown = 0;
                foreach (Game.Offline.OfflineBuildingLine b in r.Buildings)
                {
                    if (shown++ == 6) break;
                    string online = b.OnlineSeconds <= 0 ? " · offline (crank buildings rest while you are away)" : "";
                    UiKit.Text(_panel, $"{b.Name}  {Units.FormatMoney(b.Net, sym)}{online}", "hl-readout-line");
                }
                if (r.Buildings.Count > 6) UiKit.Text(_panel, $"and {r.Buildings.Count - 6} more", "hl-subtitle");
            }
            foreach (int t in r.Unlocked) UiKit.Text(_panel, $"Tier {t} unlocked: new equipment, sizes and upgrades in the shop.", "hl-readout-line");
            UiKit.Text(_panel, "Nothing broke while you were away.", "hl-subtitle");
            ui.Button(_panel, "Continue", onClose).style.alignSelf = Align.FlexEnd;
        }

        static string Duration(double s) =>
            s < 3600 ? $"{s / 60:0} min" : s < 48 * 3600 ? $"{s / 3600:0.#} h" : $"{s / 86400:0.#} days";

        public void Dispose() => _panel.RemoveFromHierarchy();
    }
}
