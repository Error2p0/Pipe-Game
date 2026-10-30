using System.Collections.Generic;
using HeadLoss.Game.Buildings;

namespace HeadLoss.Game
{
    /// <summary>Root of the saved game (<c>01-architecture.md</c> §State model). Plain data.</summary>
    public sealed class GameState
    {
        /// <summary>Spendable money, $ (never below 0).</summary>
        public double Wallet;
        /// <summary>Costs that could not be paid, $; repaid from income before the wallet grows.</summary>
        public double Debt;
        /// <summary>Net income earned over the game, $.</summary>
        public double LifetimeIncome;
        /// <summary>Time played (in game), s.</summary>
        public double PlayTimeSeconds;
        /// <summary>Building open in the building view ("" = the city view).</summary>
        public string CurrentBuildingId = "";
        /// <summary>The current city (null only in test fixtures made of hand-made buildings).</summary>
        public CityState? City;
        public List<BuildingRecord> Buildings = new List<BuildingRecord>();
        public SettingsState Settings = new SettingsState();

        public BuildingRecord? FindBuilding(string id) => Buildings.Find(b => b.BuildingId == id);

        /// <summary>Adds income (positive) or charges a running cost (negative): debt is repaid first, and costs the wallet cannot cover become debt.</summary>
        /// <param name="amount">$.</param>
        public void ApplyIncome(double amount)
        {
            if (amount >= 0)
            {
                double repay = System.Math.Min(Debt, amount);
                Debt -= repay;
                Wallet += amount - repay;
                return;
            }
            double cost = -amount;
            double paid = System.Math.Min(Wallet, cost);
            Wallet -= paid;
            Debt += cost - paid;
        }

        /// <summary>True when a purchase of <paramref name="cost"/> ($) is allowed: no debt and enough money.</summary>
        public bool CanAfford(double cost) => Debt <= 0 && Wallet >= cost - 1e-9;
    }

    /// <summary>What is saved about the current city; its layout is regenerated from the seed.</summary>
    public sealed class CityState
    {
        public ulong Seed;
        /// <summary>1 for the first city, counting up with each new city (modifiers roll from the second).</summary>
        public int Index = 1;
        /// <summary>Highest tech tier unlocked in this city (<c>03-city.md</c> §Progression).</summary>
        public int UnlockedTier = 1;
    }

    /// <summary>Phase 2 settings kept in the save (unused since Phase 3: settings live in settings.json, D68).</summary>
    public sealed class SettingsState
    {
        /// <summary>Master volume, 0–1 (−1 = use the data default).</summary>
        public double MasterVolume = -1;
    }
}
