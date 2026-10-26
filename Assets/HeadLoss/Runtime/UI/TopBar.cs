using HeadLoss.Core;
using HeadLoss.Game;
using HeadLoss.Game.Economy;
using HeadLoss.Game.Session;
using UnityEngine.UIElements;

namespace HeadLoss.Unity.UI
{
    /// <summary>
    /// Always-visible money bar (<c>02-economy.md</c> §Money display): wallet, debt in red, net income per second,
    /// and a breakdown (gross, electricity, fines, premiums) that opens on hover or tap.
    /// </summary>
    public sealed class TopBar
    {
        readonly GameContext _ctx;
        readonly UiKit _ui;
        readonly string _symbol;
        readonly VisualElement _bar, _breakdown;
        readonly Label _wallet, _debt, _net;
        readonly Label _gross, _electricity, _fines, _premiums, _netLine, _cycle;
        bool _pinned;
        float _nextRefresh;

        public VisualElement Root => _bar;

        public TopBar(VisualElement parent, UiKit ui, GameContext ctx)
        {
            _ctx = ctx;
            _ui = ui;
            _symbol = ctx.Data.Game.CurrencySymbol;
            _bar = ui.Panel(parent, "hl-moneybar");
            _wallet = UiKit.Text(_bar, "", "hl-money");
            _debt = UiKit.Text(_bar, "", "hl-money-debt");
            _debt.style.color = ui.P.Danger;
            Button net = ui.Button(_bar, "", TogglePinned);
            net.AddToClassList("hl-money-rate");
            _net = new Label();
            net.Add(_net);
            net.RegisterCallback<PointerEnterEvent>(_ => Show(true));
            net.RegisterCallback<PointerLeaveEvent>(_ => Show(_pinned));

            _breakdown = ui.Panel(parent, "hl-breakdown");
            UiKit.Text(_breakdown, "Income per second", "hl-section");
            _gross = UiKit.Text(_breakdown, "", "hl-readout-line");
            _electricity = UiKit.Text(_breakdown, "", "hl-readout-line");
            _fines = UiKit.Text(_breakdown, "", "hl-readout-line");
            _premiums = UiKit.Text(_breakdown, "", "hl-readout-line");
            _netLine = UiKit.Text(_breakdown, "", "hl-readout-line");
            _netLine.style.unityFontStyleAndWeight = UnityEngine.FontStyle.Bold;
            _cycle = UiKit.Text(_breakdown, "", "hl-subtitle");
            _breakdown.pickingMode = PickingMode.Ignore;
            Show(false);
        }

        void TogglePinned()
        {
            _pinned = !_pinned;
            Show(_pinned);
        }

        void Show(bool on) => _breakdown.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;

        /// <summary>Refreshes the numbers at the economy tick rate (no per-frame text churn).</summary>
        public void Update(float time)
        {
            if (time < _nextRefresh) return;
            _nextRefresh = time + 0.1f;
            GameState g = _ctx.Game;
            double gross = 0, electricity = 0, fines = 0, premiums = 0;
            foreach (BuildingSession s in _ctx.Sessions)
            {
                BuildingEvaluation ev = s.CommittedEvaluation;
                gross += ev.Gross;
                electricity += ev.Electricity;
                fines += ev.Fines;
                premiums += ev.Premiums;
            }
            double net = gross - electricity - fines - premiums;
            _wallet.text = Units.FormatMoney(g.Wallet, _symbol);
            _debt.text = g.Debt > 0 ? "debt " + Units.FormatMoney(g.Debt, _symbol) : "";
            _debt.style.display = g.Debt > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _net.text = (net >= 0 ? "+" : "") + Units.FormatMoneyRate(net, _symbol);
            _net.style.color = net < 0 ? _ui.P.Danger : _ui.P.Text;
            _gross.text = "Outlets  " + Units.FormatMoneyRate(gross, _symbol);
            _electricity.text = "Electricity  −" + Units.FormatMoneyRate(electricity, _symbol);
            _fines.text = "Fines  −" + Units.FormatMoneyRate(fines, _symbol);
            _premiums.text = "Premiums  −" + Units.FormatMoneyRate(premiums, _symbol);
            _netLine.text = "Net  " + Units.FormatMoneyRate(net, _symbol);
            double cycle = _ctx.Data.Balancing.Economy.IncomeCycleSeconds;
            _cycle.text = $"{Units.FormatMoney(net * cycle, _symbol)} per {cycle:0} s cycle";
        }
    }
}
