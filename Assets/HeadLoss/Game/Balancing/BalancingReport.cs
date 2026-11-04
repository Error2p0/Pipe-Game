using System;
using System.Collections.Generic;
using HeadLoss.Core;
using HeadLoss.Core.Data;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.City;
using HeadLoss.Game.Economy;
using HeadLoss.Game.Generation;

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
    /// The balancing report (<c>13-balancing.md</c>): for every tier that is generated, 20 buildings through the
    /// auto-router reference layout, with build cost, minutes of income to pay it back, electricity as a share of
    /// gross, what doubling the fan speed does to net income, and whether the next tier's cheapest item is
    /// affordable within the target time; plus the starter house checks (crank targets) over several cities.
    /// </summary>
    public static class BalancingReport
    {
        public sealed class Result
        {
            public readonly List<string> Lines = new List<string>();
            public readonly List<BalancingCheck> Checks = new List<BalancingCheck>();
        }

        sealed class Row
        {
            public string Name = "";
            public double Cost, Net, Gross, Electricity, DoubledNet, Payback;
            public int Outlets;
        }

        public static Result Run(DataRepository data, int perTier = 20, int starterCities = 5)
        {
            var r = new Result();
            Starter(data, r, starterCities);
            var meanNet = new Dictionary<int, double>();
            for (int tier = 1; tier <= 5; tier++)
                if (data.BuildingTier(tier).Available) meanNet[tier] = Tier(data, r, tier, perTier);
            FirstPrestige(data, r, meanNet);
            return r;
        }

        // ---------- starter house ----------

        static void Starter(DataRepository data, Result r, int cities)
        {
            string sym = data.Game.CurrencySymbol;
            List<double> band = data.Balancing.Targets.StarterCrankPaysFanMinutes;
            r.Lines.Add($"Starter house ({cities} cities)");
            double fanPrice = data.EquipmentItem("small-axial-fan").Price;
            int metAtRef = 0, fanOverMaxOk = 0;
            var minutes = new List<double>();
            for (ulong seed = 1; seed <= (ulong)cities; seed++)
            {
                CityLayout city = StarterGuarantee.NewCity(data, seed, 1, out _);
                Plot p = city.Starter!;
                GeneratedBuilding? g = BuildingFactory.Create(data, p, city.Districts[p.District].Kind);
                if (g == null) continue;
                ReferenceResult refr = g.Reference;
                if (refr.Solvable) metAtRef++;
                BuildingEvaluation full = AutoRouter.Evaluate(data, g.Layout, refr.State, 1.0);
                double toPay = Math.Max(0, refr.Cost + fanPrice - data.Balancing.Economy.StartingWallet);
                double m = full.Net > 0 ? toPay / full.Net / 60 : double.PositiveInfinity;
                minutes.Add(m);
                BuildingState withFan = refr.State.Clone();
                foreach (EquipmentState e in withFan.Equipment) e.EquipmentId = "small-axial-fan";
                BuildingEvaluation f = AutoRouter.Evaluate(data, g.Layout, withFan, 0);
                if (f.AboveCodeMax <= 1) fanOverMaxOk++;
                r.Lines.Add($"  city {seed}: reference {Units.FormatMoney(refr.Cost, sym)} (cap {Units.FormatMoney(g.BudgetCap, sym)}) · at 70% crank {Deliveries(refr.Evaluation)}");
                r.Lines.Add($"    crank 100%: {Deliveries(full)} · net {Units.FormatMoneyRate(full.Net, sym)} · pays fan + ducts in {m:0.0} min");
                r.Lines.Add($"    small fan on the same layout: {Deliveries(f)} · net {Units.FormatMoneyRate(f.Net, sym)} ({Share(f):P0} electricity)");
            }
            double mean = Mean(minutes);
            Check(r, "Starter house", "Reference meets every minimum at 70% crank", $"{metAtRef} of {cities}", "all", metAtRef == cities);
            Check(r, "Starter house", "Cranking (100%) pays the small fan + ducts after the starting wallet (mean)", $"{mean:0.0} min",
                $"{band[0]:0}–{band[1]:0} min", mean >= band[0] * 0.5 && mean <= band[1]);
            Check(r, "Starter house", "Small fan on the starter layout: at most one vent above code max (D18)", $"{fanOverMaxOk} of {cities}", "all", fanOverMaxOk == cities);
            r.Lines.Add("");
        }

        // ---------- generated buildings per tier ----------

        /// <summary>
        /// How long the first prestige takes (<c>13-balancing.md</c> §Targets): a player brings <c>buildingsPerHour</c> buildings online, the
        /// unlock counts of each tier first and then the best-paying ones, each earning its tier's mean net from then on. The
        /// estimate ends when both the share of plots and the income target are met.
        /// </summary>
        static void FirstPrestige(DataRepository data, Result r, Dictionary<int, double> meanNet)
        {
            string sym = data.Game.CurrencySymbol;
            TargetsDef t = data.Balancing.Targets;
            PrestigeBalancingDef pb = data.Balancing.Prestige;
            var hours = new List<double>();
            for (ulong seed = 1; seed <= 3; seed++)
            {
                CityLayout city = CityGenerator.Generate(data, seed, 1);
                var left = new Dictionary<int, int>();
                int plots = 0;
                foreach (Plot p in city.Plots)
                {
                    if (!data.BuildingTier(p.Tier).Available) continue;
                    left[p.Tier] = (left.TryGetValue(p.Tier, out int c) ? c : 0) + 1;
                    plots++;
                }
                int needed = (int)Math.Ceiling(pb.ShareOnline * plots);
                var order = new List<int>();
                foreach (int tier in new List<int>(meanNet.Keys))
                {
                    int count = Math.Min(left.TryGetValue(tier, out int c) ? c : 0, data.Balancing.Progression.UnlockOnline[tier - 1]);
                    for (int i = 0; i < count; i++) order.Add(tier);
                    if (left.ContainsKey(tier)) left[tier] -= count;
                }
                for (int tier = 5; tier >= 1; tier--)
                    if (left.TryGetValue(tier, out int c) && meanNet.ContainsKey(tier))
                        for (int i = 0; i < c; i++) order.Add(tier);
                double income = 0, earning = 0, stepHours = 1 / t.BuildingsPerHour, hour = 0;
                int online = 0;
                foreach (int tier in order)
                {
                    if (online >= needed && income >= pb.IncomeTargetFirst) break;
                    earning += meanNet[tier];
                    online++;
                    income += earning * stepHours * 3600;
                    hour += stepHours;
                }
                if (income < pb.IncomeTargetFirst && earning > 0) hour += (pb.IncomeTargetFirst - income) / earning / 3600;
                hours.Add(hour);
                r.Lines.Add($"  city {seed}: {plots} plots with buildings, needs {needed} online and {Units.FormatMoney(pb.IncomeTargetFirst, sym)} · about {hour:0.0} h at {t.BuildingsPerHour:0.#} buildings an hour · earning {Units.FormatMoneyRate(earning, sym)} by then");
            }
            double mean = Mean(hours);
            Check(r, "First prestige", "Hours until both parts of the milestone are met (estimate)", $"{mean:0.0} h", $"{t.FirstPrestigeHours[0]:0}–{t.FirstPrestigeHours[1]:0} h", mean >= t.FirstPrestigeHours[0] && mean <= t.FirstPrestigeHours[1]);
            r.Lines.Add("");
        }

        /// <returns>The tier's mean net income per building, $/s.</returns>
        static double Tier(DataRepository data, Result r, int tier, int count)
        {
            string sym = data.Game.CurrencySymbol;
            TargetsDef t = data.Balancing.Targets;
            var types = new List<BuildingTypeDef>();
            foreach (BuildingTypeDef bt in data.Buildings.Types)
                if (bt.Tier == tier && bt.Districts.Count > 0) types.Add(bt);
            if (types.Count == 0) return 0;
            r.Lines.Add($"Tier {tier}: {count} generated buildings ({string.Join(", ", types.ConvertAll(x => x.DisplayName.ToLowerInvariant()))})");
            var rows = new List<Row>();
            int unsolvable = 0;
            for (int i = 0; i < count; i++)
            {
                BuildingTypeDef bt = types[i % types.Count];
                ulong seed = Seeds.Derive((ulong)(tier * 1000 + i), "balancing");
                GeneratedBuilding? g = BuildingFactory.Create(data, new BuildingRequest($"plot-{i + 1}", bt.Id, seed, "south"),
                    bt.Districts.ContainsKey("residential") ? DistrictKind.Residential : DistrictKind.Commercial);
                if (g == null || g.Layout.Tier != tier) continue;
                if (!g.Reference.Solvable) unsolvable++;
                BuildingEvaluation ev = g.Reference.Evaluation;
                var row = new Row
                {
                    Name = $"{bt.DisplayName} {i + 1}",
                    Cost = g.Reference.Cost,
                    Net = ev.Net,
                    Gross = ev.Gross,
                    Electricity = ev.Electricity,
                    Outlets = g.Layout.Outlets.Count,
                };
                row.Payback = row.Net > 0 ? row.Cost / row.Net / 60 : double.PositiveInfinity;
                row.DoubledNet = Doubled(data, g).Net;
                rows.Add(row);
                r.Lines.Add($"  {row.Name}: {row.Outlets} outlets · {g.Reference.EquipmentId}{(g.Reference.Dampers > 0 ? $" + {g.Reference.Dampers} dampers" : "")} · " +
                    $"cost {Units.FormatMoney(row.Cost, sym)} · net {Units.FormatMoneyRate(row.Net, sym)} · payback {row.Payback:0.0} min · " +
                    $"electricity {Share(row):P0} · at 2× speed {Units.FormatMoneyRate(row.DoubledNet, sym)}");
            }
            string who = $"Tier {tier}";
            Check(r, who, "Reference solvable", $"{rows.Count - unsolvable} of {rows.Count}", "all", unsolvable == 0 && rows.Count > 0);
            // A first water building also pays for its main, and a first gas building for its gas main (03 §City utility network).
            double mainCost = 0;
            PipeFamilyDef? main = Utilities.FamilyFor(data, "water");
            if (tier >= 2 && main != null) mainCost = t.ReferenceMainTiles * Utilities.PricePerTile(data, main, main.Sizes[0].DiameterMm, 0);
            PipeFamilyDef? gasMain = Utilities.FamilyFor(data, "gas");
            if (data.BuildingTier(tier).Fluids.Contains(BuildingState.GasFluid) && gasMain != null)
                mainCost += t.ReferenceMainTiles * Utilities.PricePerTile(data, gasMain, gasMain.Sizes[0].DiameterMm, 0, "lp");
            double payback = Mean(rows.ConvertAll(x => x.Net > 0 ? (x.Cost + mainCost) / x.Net / 60 : double.PositiveInfinity)), target = t.PaybackMinutes[tier - 1];
            string withMain = mainCost > 0 ? $" with {t.ReferenceMainTiles}-tile mains ({Units.FormatMoney(mainCost, sym)})" : "";
            Check(r, who, $"Payback of the reference build{withMain} (mean)", $"{payback:0.0} min", $"≈ {target:0} min", payback >= target * 0.5 && payback <= target * 1.5);
            double share = Mean(rows.ConvertAll(Share));
            Check(r, who, "Electricity share of gross (mean)", $"{share:P0}", $"{t.ElectricityShare[0]:P0}–{t.ElectricityShare[1]:P0}",
                share >= t.ElectricityShare[0] && share <= t.ElectricityShare[1]);
            int lower = rows.FindAll(x => x.DoubledNet < x.Net).Count;
            Check(r, who, "Doubling flow by speed earns less", $"{lower} of {rows.Count}", "all", lower == rows.Count);
            (string id, double price) = CheapestNextTier(data, tier);
            if (id.Length > 0 && tier < 5 && data.BuildingTier(tier + 1).Available)
            {
                double net = Mean(rows.ConvertAll(x => x.Net));
                double minutes = net > 0 ? price / net / 60 : double.PositiveInfinity;
                Check(r, who, $"Next tier's cheapest item ({id}, {Units.FormatMoney(price, sym)}) from one building's income (mean)", $"{minutes:0.0} min", $"≤ {target:0} min", minutes <= target);
            }
            r.Lines.Add($"  mean cost {Units.FormatMoney(Mean(rows.ConvertAll(x => x.Cost)), sym)} · mean net {Units.FormatMoneyRate(Mean(rows.ConvertAll(x => x.Net)), sym)}");
            r.Lines.Add("");
            return Mean(rows.ConvertAll(x => x.Net));
        }

        /// <summary>The reference with its fan at twice the speed (a drive on every fan, affinity laws).</summary>
        static BuildingEvaluation Doubled(DataRepository data, GeneratedBuilding g)
        {
            BuildingState fast = g.Reference.State.Clone();
            foreach (EquipmentState e in fast.Equipment)
            {
                e.Vsd = true;
                e.Speed = 2.0;
            }
            foreach (DuctNode n in fast.Air.Nodes)
            {
                if (n.Kind != DuctNodeKind.Inline || data.FindEquipment(n.PartId) == null) continue;
                n.Vsd = true;
                n.Speed = 2.0;
            }
            return AutoRouter.Evaluate(data, g.Layout, fast, g.Reference.CrankSpeed);
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

        static double Mean(List<double> v)
        {
            if (v.Count == 0) return double.NaN;
            double s = 0;
            foreach (double x in v) s += x;
            return s / v.Count;
        }

        static double Share(BuildingEvaluation ev) => ev.Gross > 0 ? ev.Electricity / ev.Gross : 0;
        static double Share(Row x) => x.Gross > 0 ? x.Electricity / x.Gross : 0;

        static string Deliveries(BuildingEvaluation ev)
        {
            var parts = new List<string>();
            foreach (OutletStatus o in ev.Outlets) parts.Add($"{Units.M3sToM3h(o.Delivery):0}");
            return $"[{string.Join(" ", parts)}] m³/h{(ev.Online ? "" : " (offline)")}{(ev.AboveCodeMax > 0 ? $" ({ev.AboveCodeMax} over max)" : "")}";
        }

        static void Check(Result r, string building, string name, string value, string target, bool pass)
        {
            r.Checks.Add(new BalancingCheck { Building = building, Name = name, Value = value, Target = target, Pass = pass });
            r.Lines.Add($"  [{(pass ? "ok" : "off target")}] {name}: {value} (target {target})");
        }
    }
}
