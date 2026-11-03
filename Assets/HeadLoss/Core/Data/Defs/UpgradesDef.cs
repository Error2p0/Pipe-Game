using System.Collections.Generic;

namespace HeadLoss.Core.Data.Defs
{
    /// <summary><c>upgrades.json</c>: network upgrades applied per segment (<c>07-equipment.md</c> §Network upgrades).</summary>
    public sealed class UpgradesDef
    {
        /// <summary>Duct leakage classes, Q_leak = C_L · A_surface · p^n.</summary>
        public List<SealingDef> DuctSealing = new List<SealingDef>();
        /// <summary>Leakage pressure exponent n.</summary>
        public double LeakageExponent;
        public List<InsulationDef> Insulation = new List<InsulationDef>();
        /// <summary>Meta upgrades (<c>07-equipment.md</c> §Meta upgrades, leak sensors).</summary>
        public List<MetaUpgradeDef> Meta = new List<MetaUpgradeDef>();
    }

    /// <summary>A purchase that is not placed: per network, one-off, a city toggle or a per-building contract.</summary>
    public sealed class MetaUpgradeDef
    {
        public string Id = "";
        public string DisplayName = "";
        public int Tier;
        /// <summary>per-network, one-off, toggle or per-building.</summary>
        public string Kind = "";
        /// <summary>One-off price, $.</summary>
        public double Price;
        /// <summary>Recurring premium as a share of gross income per cycle.</summary>
        public double PremiumFraction;
        /// <summary>Insurance: multiplier on fines.</summary>
        public double FineFactor = 1;
        public string Notes = "";
    }

    public sealed class SealingDef
    {
        public string Id = "";
        public string DisplayName = "";
        public int Tier;
        /// <summary>C_L, m³/s per m² of duct surface per Pa^n.</summary>
        public double LeakageCoefficient;
        /// <summary>Added price per metre of duct, $.</summary>
        public double PricePerMetre;
    }

    public sealed class InsulationDef
    {
        public string Id = "";
        public string DisplayName = "";
        public int Tier;
        public double ThicknessMm;
        /// <summary>Overall heat transfer coefficient with this insulation, W/(m²·K) per outer area.</summary>
        public double U;
        /// <summary>Added price per metre at the reference diameter, $ (× (D/D_ref)^exponent like the duct itself).</summary>
        public double PricePerMetre;
        /// <summary>Extra ceiling capacity units for pipes above <see cref="CapacityAboveMm"/> (Phase 4).</summary>
        public int ExtraCapacityUnits;
        public double CapacityAboveMm;
    }
}
