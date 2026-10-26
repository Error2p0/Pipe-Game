using System.Collections.Generic;
using HeadLoss.Core;
using HeadLoss.Core.Data;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.Economy;
using HeadLoss.Game.Session;
using UnityEngine.UIElements;

namespace HeadLoss.Unity.UI
{
    /// <summary>
    /// Requirement sheet (<c>04-buildings.md</c> §Requirement sheet): tier and floors, outlets grouped by fluid
    /// with min / ideal / code max and the reason, base pay, entry openings, the budget cap and the codes in
    /// force. Opens by itself the first time a building is opened, then with Tab or the sheet button.
    /// </summary>
    public sealed class RequirementSheet
    {
        readonly VisualElement _panel;
        readonly BuildingSession _s;

        public bool Visible => _panel.style.display == DisplayStyle.Flex;
        public VisualElement Root => _panel;

        public RequirementSheet(VisualElement parent, UiKit ui, BuildingSession session)
        {
            _s = session;
            DataRepository data = session.Data;
            BuildingLayout l = session.Layout;
            string symbol = data.Game.CurrencySymbol;
            _panel = ui.Panel(parent, "hl-sheet");
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            _panel.Add(scroll);

            UiKit.Text(scroll, $"Requirement sheet · {l.DisplayName}", "hl-title");
            UiKit.Text(scroll, $"Tier {l.Tier} · 1 floor · {l.Width} × {l.Height} m · {l.Rooms.Count} room{(l.Rooms.Count == 1 ? "" : "s")}", "hl-subtitle");
            Line(scroll, "The building earns only while every outlet gets at least its minimum. Between minimum and ideal an outlet pays its base rate; above the ideal it pays more, up to "
                + $"{data.Balancing.Economy.CapMultiplier:0.#}× at the code maximum. Above the code maximum it pays nothing.");

            // Outlets grouped by fluid.
            var byFluid = new Dictionary<string, List<OutletPlacementDef>>();
            foreach (OutletPlacementDef o in l.Outlets)
            {
                string fluid = data.Outlet(o.Type).Fluid;
                if (!byFluid.TryGetValue(fluid, out var list)) byFluid[fluid] = list = new List<OutletPlacementDef>();
                list.Add(o);
            }
            foreach (var kv in byFluid)
            {
                FluidDef fluid = data.Fluid(kv.Key);
                UiKit.Text(scroll, $"{fluid.DisplayName} · {kv.Value.Count} outlets", "hl-section");
                foreach (OutletPlacementDef o in kv.Value)
                {
                    OutletDef def = data.Outlet(o.Type);
                    OutletRequirementDef r = def.Tiers[l.Tier.ToString()];
                    string unit = def.Unit == "m3/h" ? "m³/h" : def.Unit;
                    RoomDef? room = l.RoomOf(new TilePos(o.X, o.Y));
                    string where = room != null ? $" ({OutletList.NiceName(room.Id)})" : "";
                    string max = r.Max > 0 ? $" · code max {r.Max:0.##} {unit} ({def.CodeMaxReason.ToLowerInvariant()})" : "";
                    double pay = BuildingEconomy.BaseRate(data, def, l.Tier);
                    Line(scroll, $"{OutletList.NiceName(o.Id)}{where} · {def.DisplayName.ToLowerInvariant()}: min {r.Min:0.##} · ideal {r.Ideal:0.##}{max} · base pay {Units.FormatMoneyRate(pay, symbol)}");
                }
            }

            UiKit.Text(scroll, "Entry openings", "hl-section");
            foreach (EntryOpeningDef o in l.EntryOpenings)
            {
                EquipmentPlacementDef? pre = null;
                foreach (EquipmentPlacementDef e in l.InitialEquipment) if (e.OpeningId == o.Id) pre = e;
                string eq = pre != null ? $" · {data.EquipmentItem(pre.EquipmentId).DisplayName.ToLowerInvariant()} already in place" : " · empty: place a fan here";
                Line(scroll, $"{OutletList.NiceName(o.Id)} · {o.Utility} · {o.Facing} wall{eq}");
            }

            UiKit.Text(scroll, "Budget cap", "hl-section");
            Line(scroll, $"{Units.FormatMoney(session.BudgetCap, symbol)} for the first build. Until the building has been online once, the parts you commit here may not cost more than this. After that there is no cap.");

            UiKit.Text(scroll, "Codes in force", "hl-section");
            foreach (CodeDef c in data.Codes)
                if (c.Tiers.Contains(l.Tier)) Line(scroll, $"{c.DisplayName}: {c.Reason} {c.Why}");

            Button close = ui.Button(_panel, "Close (Tab)", () => Show(false));
            close.style.alignSelf = Align.FlexEnd;
            Show(false);
        }

        static void Line(VisualElement parent, string text) => UiKit.Text(parent, text, "hl-readout-line");

        public void Show(bool on)
        {
            _panel.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            if (on) _s.Record.SheetSeen = true;
        }

        public void Toggle() => Show(!Visible);
    }
}
