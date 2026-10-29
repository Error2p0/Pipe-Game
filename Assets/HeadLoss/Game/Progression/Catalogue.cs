using System.Collections.Generic;
using HeadLoss.Core;
using HeadLoss.Core.Data;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Economy;

namespace HeadLoss.Game.Progression
{
    public enum ShopCategory { AirMovers, Controls, DuctSizes, DuctMaterials, Fittings, Upgrades }

    /// <summary>One line of the upgrade shop (<c>03-city.md</c> §Progression: price, stats, compare with placed).</summary>
    public sealed class ShopItem
    {
        public string Id = "";
        public string Name = "";
        public ShopCategory Category;
        public int Tier;
        /// <summary>Price shown ("$40", "$1.34/m").</summary>
        public string Price = "";
        /// <summary>Key physical numbers in one line.</summary>
        public string Stats = "";
        /// <summary>For duct sizes: the diameter, mm (0 otherwise).</summary>
        public double DiameterMm;
    }

    /// <summary>Everything the player can buy, by category and tier, built from the data files.</summary>
    public static class Catalogue
    {
        public static string CategoryName(ShopCategory c) => c switch
        {
            ShopCategory.AirMovers => "Air movers",
            ShopCategory.Controls => "Controls",
            ShopCategory.DuctSizes => "Duct sizes",
            ShopCategory.DuctMaterials => "Duct materials",
            ShopCategory.Fittings => "Fittings",
            _ => "Network upgrades",
        };

        /// <summary>All items in display order (category, then tier, then price).</summary>
        public static List<ShopItem> Items(DataRepository data)
        {
            string sym = data.Game.CurrencySymbol;
            var list = new List<ShopItem>();
            foreach (EquipmentDef e in data.Equipment)
            {
                if (!e.Fluids.Contains("air")) continue;
                if (e.Placement == "addon")
                {
                    list.Add(new ShopItem
                    {
                        Id = e.Id, Name = e.DisplayName, Category = ShopCategory.Controls, Tier = e.Tier, Price = Units.FormatMoney(e.Price, sym),
                        Stats = $"Speed 20–100% on an on/off fan · {(1 - e.EfficiencyFactor) * 100:0}% efficiency cost",
                    });
                    continue;
                }
                if (e.Category != "air-mover") continue;
                string stats = e.Curve.Count > 0
                    ? $"Shutoff {e.Curve[0].PressurePa:0} Pa · up to {e.Curve[e.Curve.Count - 1].FlowM3h:0} m³/h"
                    : "";
                double peak = 0;
                foreach (CurvePointDef p in e.Curve) peak = System.Math.Max(peak, p.Efficiency);
                if (e.PowerModel == "curve") stats += $" · peak efficiency {peak * 100:0}%";
                if (e.SpeedControl == "variable") stats += " · built-in variable speed";
                if (e.SpeedControl == "crank") stats += " · cranked by hand, no electricity";
                stats += e.Placement == "inline" ? " · inline on a duct" : " · at the air intake";
                list.Add(new ShopItem { Id = e.Id, Name = e.DisplayName, Category = ShopCategory.AirMovers, Tier = e.Tier, Price = Units.FormatMoney(e.Price, sym), Stats = stats });
            }
            foreach (string part in data.Game.Toolbox.Parts)
            {
                FittingDef? f = data.FindFitting(part);
                if (f == null) continue;
                list.Add(new ShopItem
                {
                    Id = f.Id, Name = f.DisplayName, Category = ShopCategory.Controls, Tier = f.Tier, Price = "from " + Units.FormatMoney(f.Price, sym),
                    Stats = f.Model == "valve" ? $"K {f.K:0.0#} open, K/θ² when closing to {f.MinOpening * 100:0}%" : $"K {f.K:0.0#}",
                });
            }
            string material = data.Balancing.Drawing.DefaultMaterial;
            foreach (PipeFamilyDef fam in data.PipeFamilies)
            {
                if (fam.Kind != "duct") continue;
                foreach (PipeSizeDef s in fam.Sizes)
                    list.Add(new ShopItem
                    {
                        Id = $"size-{s.DiameterMm:0}", Name = $"{s.DiameterMm:0} mm duct", Category = ShopCategory.DuctSizes, Tier = s.Tier, DiameterMm = s.DiameterMm,
                        Price = Units.FormatMoney(Pricing.PerMetre(data, material, s.DiameterMm), sym) + "/m",
                        Stats = $"{s.CapacityUnits} ceiling unit{(s.CapacityUnits == 1 ? "" : "s")} · friction ∝ 1/D⁵ at the same flow",
                    });
            }
            foreach (MaterialDef m in data.Materials)
            {
                if (m.Kind != "duct") continue;
                list.Add(new ShopItem
                {
                    Id = m.Id, Name = m.DisplayName, Category = ShopCategory.DuctMaterials, Tier = m.Tier,
                    Price = Units.FormatMoney(Pricing.PerMetre(data, m.Id, data.Balancing.Prices.DuctReferenceDiameterMm), sym) + "/m at 100 mm",
                    Stats = $"Roughness {m.RoughnessMm:0.0##} mm · U {m.UUninsulated:0} W/m²K · rated {m.RatedPressurePa:0} Pa",
                });
            }
            foreach (string id in new[] { "elbow-90-sharp", "elbow-90-long-radius", "tee-sharp", "tee-smooth" })
            {
                FittingDef f = data.Fitting(id);
                list.Add(new ShopItem
                {
                    Id = f.Id, Name = f.DisplayName, Category = ShopCategory.Fittings, Tier = f.Tier, Price = Units.FormatMoney(f.Price, sym),
                    Stats = f.Model == "tee" ? $"K run {f.KRun:0.0#} · branch {f.KBranch:0.0#}" : $"K {f.KDuct:0.0#} in ducts",
                });
            }
            double cl0 = data.Upgrades.DuctSealing[0].LeakageCoefficient;
            foreach (SealingDef s in data.Upgrades.DuctSealing)
                list.Add(new ShopItem
                {
                    Id = s.Id, Name = s.DisplayName, Category = ShopCategory.Upgrades, Tier = s.Tier,
                    Price = s.PricePerMetre > 0 ? "+" + Units.FormatMoney(s.PricePerMetre, sym) + "/m" : "standard",
                    Stats = $"Leakage {s.LeakageCoefficient / cl0 * 100:0}% of unsealed",
                });
            foreach (InsulationDef i in data.Upgrades.Insulation)
                list.Add(new ShopItem
                {
                    Id = i.Id, Name = i.DisplayName, Category = ShopCategory.Upgrades, Tier = i.Tier,
                    Price = "+" + Units.FormatMoney(i.PricePerMetre, sym) + "/m at 100 mm",
                    Stats = $"U {i.U:0.0#} W/m²K: less heat lost or gained along the duct",
                });
            // Stable: items keep their data order within a category and tier.
            return new List<ShopItem>(System.Linq.Enumerable.ThenBy(System.Linq.Enumerable.OrderBy(list, i => i.Category), i => i.Tier));
        }

        /// <summary>Intake air movers available to place at an air intake, in tier order.</summary>
        public static List<EquipmentDef> IntakeAirMovers(DataRepository data)
        {
            var list = new List<EquipmentDef>();
            foreach (EquipmentDef e in data.Equipment)
                if (e.Placement == "intake" && e.Category == "air-mover" && e.Fluids.Contains("air")) list.Add(e);
            list.Sort((a, b) => a.Tier != b.Tier ? a.Tier.CompareTo(b.Tier) : a.Price.CompareTo(b.Price));
            return list;
        }
    }
}
