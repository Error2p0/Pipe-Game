using System;
using System.Collections.Generic;
using HeadLoss.Core;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.Economy;
using HeadLoss.Game.Progression;
using HeadLoss.Game.Session;
using HeadLoss.Sim.Hydraulics;
using UnityEngine.UIElements;

namespace HeadLoss.Unity.UI
{
    /// <summary>
    /// The upgrade shop (<c>03-city.md</c> §Progression, <c>11-ui-visuals-audio.md</c> §Screens): every item by
    /// category and tier with price and stats; locked tiers show their unlock condition. With a building open, each
    /// item compares itself with what is placed there (fans are simulated in the building; ducts and materials compare
    /// their friction). Items are bought by placing them in design mode and committing.
    /// </summary>
    public sealed class ShopScreen
    {
        readonly VisualElement _panel;
        readonly GameContext _ctx;
        readonly BuildingSession? _session;
        readonly UiKit _ui;
        readonly string _symbol;

        public ShopScreen(VisualElement parent, UiKit ui, GameContext ctx, BuildingSession? session, Action onClose)
        {
            _ctx = ctx;
            _session = session;
            _ui = ui;
            _symbol = ctx.Data.Game.CurrencySymbol;
            _panel = ui.Panel(parent, "hl-shop");
            VisualElement head = UiKit.Row(_panel);
            head.style.justifyContent = Justify.SpaceBetween;
            head.style.alignItems = Align.Center;
            int tier = Tech.UnlockedTier(ctx.Game);
            UiKit.Text(head, $"Shop · tier {tier} unlocked", "hl-title");
            ui.Button(head, "Close (Esc)", onClose);
            UiKit.Text(_panel, session != null
                ? $"Buy by placing items in {session.Layout.DisplayName} and committing. Compare shows what an item would do there."
                : "Buy by placing items in a building and committing. Open a building to compare items with what is placed there.", "hl-subtitle");
            if (tier < Tech.MaxTier) UiKit.Text(_panel, $"Tier {tier + 1}: {Tech.Condition(ctx.Data, ctx, tier + 1).ToLowerInvariant()}", "hl-readout-line");

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("hl-shop-list");
            _panel.Add(scroll);
            ShopCategory? last = null;
            foreach (ShopItem item in Catalogue.Items(ctx.Data))
            {
                if (item.Tier > tier + 1 && item.Tier > 1) continue;  // show the next tier as a preview, not the far future
                if (last != item.Category)
                {
                    last = item.Category;
                    UiKit.Text(scroll, Catalogue.CategoryName(item.Category), "hl-section");
                }
                Row(scroll, item, tier);
            }
            int hidden = 0;
            foreach (ShopItem item in Catalogue.Items(ctx.Data)) if (item.Tier > tier + 1) hidden++;
            if (hidden > 0) UiKit.Text(scroll, $"{hidden} more items in later tiers.", "hl-subtitle");
        }

        void Row(VisualElement parent, ShopItem item, int tier)
        {
            var row = new VisualElement();
            row.AddToClassList("hl-shop-row");
            parent.Add(row);
            VisualElement line = UiKit.Row(row);
            line.style.alignItems = Align.Center;
            Label name = UiKit.Text(line, item.Name, "hl-readout-title");
            name.style.flexGrow = 1;
            bool unlocked = item.Tier <= tier;
            Label chip = UiKit.Text(line, unlocked ? $"tier {item.Tier}" : $"tier {item.Tier} · locked", "hl-readout-line");
            chip.style.color = unlocked ? _ui.P.Good : _ui.P.Warning;
            chip.style.marginRight = 10;
            UiKit.Text(line, item.Price, "hl-readout-line");
            UiKit.Text(row, item.Stats, "hl-subtitle");
            if (!unlocked) UiKit.Text(row, Tech.Condition(_ctx.Data, _ctx, item.Tier), "hl-subtitle");
            if (_session == null) return;
            Label result = UiKit.Text(row, "", "hl-readout-line");
            result.style.display = DisplayStyle.None;
            Func<string>? compare = Compare(item);
            if (compare == null) return;
            _ui.Button(row, "Compare with placed", () =>
            {
                result.text = compare();
                result.style.display = DisplayStyle.Flex;
            }).style.alignSelf = Align.FlexStart;
        }

        /// <summary>A compare function for items that can be compared in the open building, or null.</summary>
        Func<string>? Compare(ShopItem item)
        {
            BuildingSession s = _session!;
            EquipmentDef? eq = _ctx.Data.FindEquipment(item.Id);
            EntryOpeningDef? intake = null;
            foreach (EntryOpeningDef o in s.Layout.EntryOpenings) if (o.Utility == "air") intake = o;
            if (eq != null && eq.Placement == "intake" && intake != null)
            {
                string opening = intake.Id;
                return () =>
                {
                    BuildingEvaluation now = s.DesignEvaluation;
                    BuildingEvaluation with = s.EvaluateWith(st =>
                    {
                        st.Equipment.RemoveAll(e => e.OpeningId == opening);
                        st.Equipment.Add(new EquipmentState { EquipmentId = eq.Id, OpeningId = opening, On = true });
                    });
                    EquipmentState? placed = s.State.EquipmentAt(opening);
                    string placedName = placed != null ? _ctx.Data.EquipmentItem(placed.EquipmentId).DisplayName.ToLowerInvariant() : "an empty intake";
                    return $"Here: {Summary(with)} · now ({placedName}): {Summary(now)}";
                };
            }
            if (item.Category == ShopCategory.DuctSizes || item.Category == ShopCategory.DuctMaterials)
            {
                DuctSegment? common = MostCommon(s.State.Air);
                if (common == null) return () => "Draw some ducts first; then this compares friction with them.";
                return () =>
                {
                    MaterialDef placed = _ctx.Data.Material(common.MaterialId);
                    MaterialDef mat = item.Category == ShopCategory.DuctMaterials ? _ctx.Data.Material(item.Id) : placed;
                    double d0 = Units.MmToM(common.DiameterMm), d1 = item.DiameterMm > 0 ? Units.MmToM(item.DiameterMm) : d0;
                    // Friction per metre at the same flow (Q fixed): Δp ∝ f / D⁵; f from Swamee-Jain at the placed duct's Reynolds number.
                    double q = 0.01, nu = 1.5e-5;
                    double re0 = 4 * q / (Math.PI * d0 * nu), re1 = 4 * q / (Math.PI * d1 * nu);
                    var model = new FrictionModel(_ctx.Data.Balancing.Physics.LaminarReynolds, _ctx.Data.Balancing.Physics.TurbulentReynolds);
                    double f0 = model.Factor(re0, Units.MmToM(placed.RoughnessMm) / d0);
                    double f1 = model.Factor(re1, Units.MmToM(mat.RoughnessMm) / d1);
                    double ratio = f1 / f0 * Math.Pow(d0 / d1, 5);
                    return $"Friction per metre {ratio * 100:0}% of your {common.DiameterMm:0} mm {placed.DisplayName.ToLowerInvariant()} at the same flow";
                };
            }
            return null;
        }

        string Summary(BuildingEvaluation ev)
        {
            int ok = ev.Outlets.Count - ev.BelowMinimum;
            string state = ev.Online ? $"online, {Units.FormatMoneyRate(ev.Net, _symbol)}" : $"offline, {ok} of {ev.Outlets.Count} outlets at minimum";
            string over = ev.AboveCodeMax > 0 ? $", {ev.AboveCodeMax} above code max" : "";
            return $"{state}{over}, {Units.FormatPower(ev.PowerW)}";
        }

        static DuctSegment? MostCommon(DuctNetworkState net)
        {
            var metres = new Dictionary<string, (int m, DuctSegment s)>();
            foreach (DuctSegment seg in net.Segments)
            {
                string key = seg.MaterialId + "/" + seg.DiameterMm;
                int len = net.FindNode(seg.NodeA)!.Pos.ManhattanTo(net.FindNode(seg.NodeB)!.Pos);
                metres[key] = metres.TryGetValue(key, out var v) ? (v.m + len, v.s) : (len, seg);
            }
            DuctSegment? best = null;
            int bestM = -1;
            foreach (var kv in metres) if (kv.Value.m > bestM) { bestM = kv.Value.m; best = kv.Value.s; }
            return best;
        }

        public void Dispose() => _panel.RemoveFromHierarchy();
    }

    /// <summary>A short, quiet message under the money bar that fades out by itself (unlocks, milestones).</summary>
    public sealed class Notice
    {
        readonly Label _label;
        float _hideAt;

        public Notice(VisualElement parent, UiKit ui)
        {
            VisualElement panel = ui.Panel(parent, "hl-notice");
            panel.pickingMode = PickingMode.Ignore;
            _label = UiKit.Text(panel, "", "hl-readout-line");
            panel.style.display = DisplayStyle.None;
        }

        public VisualElement Root => _label.parent;

        public void Show(string text, float now, float seconds = 7f)
        {
            _label.text = text;
            _hideAt = now + seconds;
            Root.style.display = DisplayStyle.Flex;
            Root.BringToFront();
        }

        public void Update(float now)
        {
            if (Root.style.display == DisplayStyle.Flex && now >= _hideAt) Root.style.display = DisplayStyle.None;
        }
    }
}
