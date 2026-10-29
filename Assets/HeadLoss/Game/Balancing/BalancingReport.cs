using System;
using System.Collections.Generic;
using HeadLoss.Core;
using HeadLoss.Core.Data;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.Economy;
using HeadLoss.Game.Networks;
using HeadLoss.Sim.Fluids;
using HeadLoss.Sim.Network;

namespace HeadLoss.Game.Balancing
{
    /// <summary>One checked number against its target.</summary>
    public sealed class BalancingCheck
    {
        public string Building = "";
        public string Name = "";
        public string Value = "";
        public string Target = "";
        public bool Pass;
    }

    /// <summary>
    /// The balancing report (<c>13-balancing.md</c>): for each building, the reference layout's build cost,
    /// minutes of income to pay it back, electricity as a share of gross, whether the next tier's cheapest item is
    /// affordable within the target time, and what doubling the fan speed does to net income. Phase 2 covers the
    /// hand-made buildings; Phase 3 adds 20 generated buildings per tier through the auto-router.
    /// </summary>
    public static class BalancingReport
    {
        public sealed class Result
        {
            public readonly List<string> Lines = new List<string>();
            public readonly List<BalancingCheck> Checks = new List<BalancingCheck>();
        }

        public static Result Run(DataRepository data)
        {
            var r = new Result();
            string sym = data.Game.CurrencySymbol;
            TargetsDef t = data.Balancing.Targets;
            foreach (BuildingLayoutDef def in data.HandmadeBuildings)
            {
                var layout = new BuildingLayout(def);
                BuildingState reference = ReferenceLayout.Build(data, layout);
                double cost = Pricing.Value(data, layout, reference);
                double crank = layout.ReferenceCrankSpeed;
                BuildingEvaluation ev = Evaluate(data, layout, reference, crank > 0 ? crank : 0);
                r.Lines.Add($"{layout.DisplayName} (tier {layout.Tier}, {layout.Outlets.Count} outlets)");
                r.Lines.Add($"  reference: {Equipment(data, reference)}{(crank > 0 ? $" at {crank:P0} crank" : "")}, cost {Units.FormatMoney(cost, sym)}, budget cap {Units.FormatMoney(cost * data.Balancing.Building.BudgetCapFactor, sym)}");
                r.Lines.Add("  " + Deliveries(ev));
                r.Lines.Add($"  online {ev.Online} · gross {Units.FormatMoneyRate(ev.Gross, sym)} · electricity {Units.FormatMoneyRate(ev.Electricity, sym)} ({Share(ev):P0}) · net {Units.FormatMoneyRate(ev.Net, sym)}");

                Check(r, layout, "Reference meets every minimum", ev.Online ? "yes" : "no", "yes", ev.Online);
                Check(r, layout, "Reference has no outlet above code max", ev.AboveCodeMax.ToString(), "0", ev.AboveCodeMax == 0);
                double payback = ev.Net > 0 ? cost / ev.Net / 60 : double.PositiveInfinity;
                double target = t.PaybackMinutes[layout.Tier - 1];
                Check(r, layout, "Payback of the reference build", $"{payback:0.0} min", $"≈ {target:0} min", payback <= target * 1.5 && payback >= target * 0.5);
                if (ev.PowerW > 0)
                    Check(r, layout, "Electricity share of gross", $"{Share(ev):P0}", $"{t.ElectricityShare[0]:P0}–{t.ElectricityShare[1]:P0}",
                        Share(ev) >= t.ElectricityShare[0] && Share(ev) <= t.ElectricityShare[1]);

                if (crank > 0) StarterChecks(data, layout, reference, cost, r);
                else DoublingCheck(data, layout, reference, ev, r);

                (string id, double price) = CheapestNextTier(data, layout.Tier);
                if (id.Length > 0)
                {
                    double minutes = ev.Net > 0 ? price / ev.Net / 60 : double.PositiveInfinity;
                    Check(r, layout, $"Next tier's cheapest item ({id}, {Units.FormatMoney(price, sym)}) from this building alone", $"{minutes:0.0} min", $"≤ {target:0} min", minutes <= target);
                }
                r.Lines.Add("");
            }
            r.Lines.Add("Generated buildings (20 per tier through the auto-router) are added in Phase 3.");
            return r;
        }

        /// <summary>Starter: the crank at full speed, the time to pay the small fan plus the ducts, and the fan on the same layout (D18).</summary>
        static void StarterChecks(DataRepository data, BuildingLayout layout, BuildingState reference, double cost, Result r)
        {
            string sym = data.Game.CurrencySymbol;
            BuildingEvaluation full = Evaluate(data, layout, reference, 1.0);
            double fan = data.EquipmentItem("small-axial-fan").Price;
            double toPay = Math.Max(0, cost + fan - data.Balancing.Economy.StartingWallet);
            double minutes = full.Net > 0 ? toPay / full.Net / 60 : double.PositiveInfinity;
            List<double> band = data.Balancing.Targets.StarterCrankPaysFanMinutes;
            r.Lines.Add($"  crank 100%: {Deliveries(full)} · net {Units.FormatMoneyRate(full.Net, sym)}");
            Check(r, layout, "Cranking (100%) pays the small fan + ducts after the starting wallet", $"{minutes:0.0} min", $"{band[0]:0}–{band[1]:0} min", minutes >= band[0] * 0.5 && minutes <= band[1]);

            BuildingState withFan = reference.Clone();
            foreach (EquipmentState e in withFan.Equipment) e.EquipmentId = "small-axial-fan";
            BuildingEvaluation f = Evaluate(data, layout, withFan, 0);
            r.Lines.Add($"  small axial fan on the same layout: {Deliveries(f)} · net {Units.FormatMoneyRate(f.Net, sym)} ({Share(f):P0} electricity)");
            Check(r, layout, "Small fan on the starter layout: at most one vent above code max (D18)", f.AboveCodeMax.ToString(), "≤ 1", f.AboveCodeMax <= 1);
        }

        /// <summary>Doubling flow by fan speed (affinity, s = 2) must earn less than the sweet spot.</summary>
        static void DoublingCheck(DataRepository data, BuildingLayout layout, BuildingState reference, BuildingEvaluation ev, Result r)
        {
            string sym = data.Game.CurrencySymbol;
            BuildingState fast = reference.Clone();
            foreach (EquipmentState e in fast.Equipment) e.Speed = 2.0;
            BuildingEvaluation f = Evaluate(data, layout, fast, 0);
            r.Lines.Add($"  fan at 2× speed: {Deliveries(f)} · gross {Units.FormatMoneyRate(f.Gross, sym)} · electricity {Units.FormatMoneyRate(f.Electricity, sym)} · net {Units.FormatMoneyRate(f.Net, sym)}");
            Check(r, layout, "Doubling flow by speed earns less", $"{Units.FormatMoneyRate(f.Net, sym)} vs {Units.FormatMoneyRate(ev.Net, sym)}", "lower", f.Net < ev.Net);
        }

        static BuildingEvaluation Evaluate(DataRepository data, BuildingLayout layout, BuildingState s, double crank)
        {
            var air = new TableFluid(data.Fluid("air"), data.FluidTables["air"], data.Balancing.Physics.GasConstant);
            CompiledNetwork c = NetworkCompiler.Compile(layout, s, data, Units.CToK(data.Balancing.Physics.DefaultAirTemperatureC), crank);
            SolveResult res = new NetworkSolver(SolverSettings.FromBalancing(data.Balancing)).Solve(c.Graph, air);
            return BuildingEconomy.Evaluate(data, layout, c, res);
        }

        static (string id, double price) CheapestNextTier(DataRepository data, int tier)
        {
            string id = "";
            double best = double.MaxValue;
            foreach (EquipmentDef e in data.Equipment)
                if (e.Tier == tier + 1 && e.Price > 0 && e.Price < best) { best = e.Price; id = e.Id; }
            foreach (FittingDef f in data.Fittings)
                if (f.Tier == tier + 1 && f.Price > 0 && f.Price < best) { best = f.Price; id = f.Id; }
            return (id, id.Length > 0 ? best : 0);
        }

        static double Share(BuildingEvaluation ev) => ev.Gross > 0 ? ev.Electricity / ev.Gross : 0;

        static string Equipment(DataRepository data, BuildingState s)
        {
            var names = new List<string>();
            foreach (EquipmentState e in s.Equipment) names.Add(data.EquipmentItem(e.EquipmentId).DisplayName.ToLowerInvariant());
            return names.Count > 0 ? string.Join(", ", names) : "no equipment";
        }

        static string Deliveries(BuildingEvaluation ev)
        {
            var parts = new List<string>();
            foreach (OutletStatus o in ev.Outlets) parts.Add($"{o.OutletId} {Units.M3sToM3h(o.Delivery):0} m³/h ({o.Zone}, ×{o.Multiplier:0.00})");
            return string.Join(", ", parts);
        }

        static void Check(Result r, BuildingLayout layout, string name, string value, string target, bool pass)
        {
            r.Checks.Add(new BalancingCheck { Building = layout.DisplayName, Name = name, Value = value, Target = target, Pass = pass });
            r.Lines.Add($"  [{(pass ? "ok" : "off target")}] {name}: {value} (target {target})");
        }
    }
}
