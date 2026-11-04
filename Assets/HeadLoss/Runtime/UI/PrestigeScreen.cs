using System;
using HeadLoss.Core;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game;
using HeadLoss.Game.Progression;
using UnityEngine.UIElements;

namespace HeadLoss.Unity.UI
{
    /// <summary>
    /// The prestige screen (<c>11-ui-visuals-audio.md</c> §Screens, <c>03-city.md</c> §Prestige): milestone progress (share online and
    /// income target), the Reputation this city would pay, the tree, and Found new city with a confirmation listing what
    /// resets and what stays. A seed can be entered; otherwise it is random.
    /// </summary>
    public sealed class PrestigeScreen
    {
        readonly VisualElement _panel, _tree, _confirm;
        readonly GameContext _ctx;
        readonly UiKit _ui;
        readonly string _symbol;
        readonly Label _share, _income, _reward, _reputation, _modifiers;
        readonly Button _found;
        readonly TextField _seed;
        readonly Action<string> _onFound;
        float _next;

        /// <param name="onFound">Founds a new city with the seed text ("" = random).</param>
        public PrestigeScreen(VisualElement parent, UiKit ui, GameContext ctx, Action onClose, Action<string> onFound)
        {
            _ctx = ctx;
            _ui = ui;
            _symbol = ctx.Data.Game.CurrencySymbol;
            _onFound = onFound;
            _panel = ui.Panel(parent, "hl-shop");
            VisualElement head = UiKit.Row(_panel);
            head.style.justifyContent = Justify.SpaceBetween;
            head.style.alignItems = Align.Center;
            UiKit.Text(head, $"New city · {ctx.City!.Name} is city {ctx.Game.City!.Index}", "hl-title");
            ui.Button(head, "Close (Esc)", onClose);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("hl-shop-list");
            _panel.Add(scroll);

            UiKit.Text(scroll, "Milestone", "hl-section");
            _share = UiKit.Text(scroll, "", "hl-readout-line");
            _income = UiKit.Text(scroll, "", "hl-readout-line");
            _reward = UiKit.Text(scroll, "", "hl-readout-line");
            _modifiers = UiKit.Text(scroll, "", "hl-subtitle");

            UiKit.Text(scroll, "Permanent bonuses", "hl-section");
            _reputation = UiKit.Text(scroll, "", "hl-readout-line");
            _tree = new VisualElement();
            scroll.Add(_tree);

            UiKit.Text(scroll, "Found a new city", "hl-section");
            _seed = new TextField("Seed (empty = random)");
            _seed.AddToClassList("hl-dropdown");
            ui.StyleField(_seed);
            scroll.Add(_seed);
            _found = ui.Button(scroll, "Found new city", () => _confirm!.style.display = DisplayStyle.Flex);
            _found.style.alignSelf = Align.FlexStart;
            _confirm = new VisualElement();
            scroll.Add(_confirm);
            _confirm.style.display = DisplayStyle.None;
            UiKit.Text(_confirm, "Resets: your wallet, unlocks, buildings, ruins and mains.", "hl-readout-line");
            UiKit.Text(_confirm, "Stays: Reputation and its bonuses, settings and statistics.", "hl-readout-line");
            VisualElement row = UiKit.Row(_confirm);
            ui.Button(row, "Found it", () => _onFound(_seed.value ?? ""));
            ui.Button(row, "Keep playing here", () => _confirm.style.display = DisplayStyle.None);
            RebuildTree();
            Update(0);
        }

        void RebuildTree()
        {
            _tree.Clear();
            GameState g = _ctx.Game;
            foreach (PrestigeNodeDef node in _ctx.Data.Prestige.Nodes)
            {
                string id = node.Id;
                int level = PrestigeEffects.Level(g, id), cost = PrestigeEffects.NextCost(node, g);
                string blocked = PrestigeEffects.Blocked(_ctx.Data, node, g);
                VisualElement row = new VisualElement();
                row.AddToClassList("hl-shop-row");
                _tree.Add(row);
                VisualElement text = new VisualElement();
                text.style.flexGrow = 1;
                text.style.flexShrink = 1;
                row.Add(text);
                UiKit.Text(text, $"{node.DisplayName} · level {level} of {node.Costs.Count}", "hl-readout-title");
                UiKit.Text(text, node.Description, "hl-subtitle");
                Button buy = _ui.Button(row, cost < 0 ? "Maximum" : $"Buy · {cost} Reputation", () =>
                {
                    if (PrestigeEffects.Buy(_ctx.Data, id, _ctx.Game)) RebuildTree();
                });
                buy.SetEnabled(blocked.Length == 0);
                if (blocked.Length > 0 && cost >= 0) buy.tooltip = blocked;
            }
        }

        /// <summary>Refreshes the milestone numbers a few times a second.</summary>
        public void Update(float time)
        {
            if (time < _next) return;
            _next = time + 0.25f;
            Milestone m = _ctx.Milestone();
            _share.text = $"Buildings online  {m.Online} of {m.Plots} · {m.Share * 100:0}% (needs {m.ShareNeeded * 100:0}%){(m.Ruins > 0 ? $" · {m.Ruins} ruin{(m.Ruins == 1 ? "" : "s")} count as not online" : "")}";
            _share.style.color = m.ShareMet ? _ui.P.Good : _ui.P.Text;
            _income.text = $"City income  {Units.FormatMoney(m.Income, _symbol)} of {Units.FormatMoney(m.IncomeTarget, _symbol)}";
            _income.style.color = m.IncomeMet ? _ui.P.Good : _ui.P.Text;
            _reward.text = $"Founding a new city now pays {m.ReputationReward} Reputation";
            _reputation.text = $"Reputation  {_ctx.Game.Reputation}";
            var names = new System.Collections.Generic.List<string>();
            foreach (string id in _ctx.City!.Modifiers)
            {
                CityModifierDef? def = _ctx.Data.City.Modifiers.Find(x => x.Id == id);
                if (def != null) names.Add($"{def.DisplayName}: {def.Description}");
            }
            _modifiers.text = names.Count > 0 ? "This city: " + string.Join(" ", names) : "New cities can roll modifiers: a cold climate, or old buildings with strict ceiling space.";
            bool demoBlocks = _ctx.Demo && !_ctx.Data.Balancing.Demo.Prestige;
            _found.SetEnabled(m.Met && !demoBlocks);
            _found.text = demoBlocks ? "New cities are in the full game" : m.Met ? "Found new city" : "Found new city · milestone not met";
        }

        public void Dispose() => _panel.RemoveFromHierarchy();
    }
}
