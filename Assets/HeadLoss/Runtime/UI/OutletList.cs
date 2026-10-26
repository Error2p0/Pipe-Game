using System.Collections.Generic;
using HeadLoss.Core;
using HeadLoss.Game.Economy;
using HeadLoss.Game.Session;
using UnityEngine;
using UnityEngine.UIElements;

namespace HeadLoss.Unity.UI
{
    /// <summary>
    /// Outlet list with live zones (<c>11-ui-visuals-audio.md</c> §Screens): one row per outlet with its delivery,
    /// zone word and colour, multiplier and pay, and a gauge marking minimum, ideal and code maximum. Shows the
    /// design preview; clicking a row selects the outlet.
    /// </summary>
    public sealed class OutletList
    {
        sealed class Row
        {
            public string Id = "";
            public VisualElement Root = null!, Chip = null!, Dot = null!, Min = null!, Ideal = null!, Max = null!, Band = null!;
            public Label Name = null!, Delivery = null!, Zone = null!;
        }

        readonly UiKit _ui;
        readonly BuildingSession _s;
        readonly string _symbol;
        readonly VisualElement _panel, _rows;
        readonly Label _header;
        readonly List<Row> _list = new List<Row>();
        bool _collapsed;

        public VisualElement Root => _panel;
        /// <summary>Outlet id clicked by the player (consumed by the HUD).</summary>
        public string Clicked = "";

        public OutletList(VisualElement parent, UiKit ui, BuildingSession session)
        {
            _ui = ui;
            _s = session;
            _symbol = session.Data.Game.CurrencySymbol;
            _panel = ui.Panel(parent, "hl-outlets");
            VisualElement head = UiKit.Row(_panel);
            head.style.justifyContent = Justify.SpaceBetween;
            head.style.alignItems = Align.Center;
            _header = UiKit.Text(head, "Outlets", "hl-section");
            _rows = new VisualElement();
            Button toggle = ui.Button(head, "Hide", () => { });
            toggle.clicked += () =>
            {
                _collapsed = !_collapsed;
                _rows.style.display = _collapsed ? DisplayStyle.None : DisplayStyle.Flex;
                toggle.text = _collapsed ? "Show" : "Hide";
            };
            _panel.Add(_rows);
            foreach (var o in session.Layout.Outlets) _list.Add(BuildRow(o.Id));
        }

        /// <summary>"living-1" → "Living 1".</summary>
        public static string NiceName(string id)
        {
            string s = id.Replace('-', ' ');
            return s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
        }

        Row BuildRow(string id)
        {
            var r = new Row { Id = id };
            r.Root = new VisualElement();
            r.Root.AddToClassList("hl-outlet-row");
            r.Root.RegisterCallback<ClickEvent>(_ => Clicked = id);
            _rows.Add(r.Root);

            VisualElement top = UiKit.Row(r.Root);
            top.style.alignItems = Align.Center;
            r.Chip = new VisualElement();
            r.Chip.AddToClassList("hl-zone-chip");
            top.Add(r.Chip);
            r.Name = UiKit.Text(top, NiceName(id), "hl-outlet-name");
            r.Delivery = UiKit.Text(top, "", "hl-outlet-value");
            r.Zone = UiKit.Text(r.Root, "", "hl-outlet-zone");

            var gauge = new VisualElement();
            gauge.AddToClassList("hl-gauge");
            gauge.style.backgroundColor = _ui.P.GridLines;
            r.Root.Add(gauge);
            r.Band = Marker(gauge, "hl-gauge-band");
            Color band = _ui.P.Good;
            band.a = 0.35f;
            r.Band.style.backgroundColor = band;
            r.Min = Marker(gauge, "hl-gauge-mark");
            r.Ideal = Marker(gauge, "hl-gauge-mark");
            r.Max = Marker(gauge, "hl-gauge-mark");
            r.Min.style.backgroundColor = r.Ideal.style.backgroundColor = _ui.P.Walls;
            r.Max.style.backgroundColor = _ui.P.Danger;
            r.Dot = Marker(gauge, "hl-gauge-dot");
            return r;
        }

        static VisualElement Marker(VisualElement gauge, string cls)
        {
            var m = new VisualElement { pickingMode = PickingMode.Ignore };
            m.AddToClassList(cls);
            gauge.Add(m);
            return m;
        }

        public void Refresh()
        {
            BuildingEvaluation ev = _s.DesignEvaluation;
            _header.text = _s.HasPendingDesign ? "Outlets · preview" : "Outlets";
            foreach (Row r in _list)
            {
                OutletStatus? st = ev.Find(r.Id);
                if (st == null) continue;
                Color zc = _ui.ZoneColour(st.Zone);
                r.Chip.style.backgroundColor = zc;
                r.Delivery.text = st.Connected ? Units.FormatAirFlow(st.Delivery) : "—";
                string pay = st.Zone == OutletZone.Working || st.Zone == OutletZone.Overdelivery
                    ? $" · ×{st.Multiplier:0.00} · {Units.FormatMoneyRate(st.BaseRate * st.Multiplier, _symbol)}"
                    : "";
                r.Zone.text = UiKit.ZoneWord(st.Zone) + pay;
                r.Zone.style.color = st.Zone == OutletZone.Working ? _ui.P.Text : zc;

                OutletRequirement q = st.Requirement;
                double top = System.Math.Max(q.HasMax ? q.Max * 1.25 : q.Ideal * 2, st.Delivery * 1.05);
                Place(r.Min, q.Min / top);
                Place(r.Ideal, q.Ideal / top);
                r.Max.style.display = q.HasMax ? DisplayStyle.Flex : DisplayStyle.None;
                if (q.HasMax) Place(r.Max, q.Max / top);
                r.Band.style.left = Length.Percent((float)(q.Ideal / top * 100));
                r.Band.style.width = Length.Percent((float)(((q.HasMax ? q.Max : top) - q.Ideal) / top * 100));
                r.Dot.style.display = st.Connected ? DisplayStyle.Flex : DisplayStyle.None;
                Place(r.Dot, st.Delivery / top);
                r.Dot.style.backgroundColor = zc;
            }
        }

        static void Place(VisualElement e, double fraction) =>
            e.style.left = Length.Percent(Mathf.Clamp01((float)fraction) * 100);
    }
}
