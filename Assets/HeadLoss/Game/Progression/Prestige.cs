using System;
using System.Collections.Generic;
using HeadLoss.Core.Data;
using HeadLoss.Core.Data.Defs;

namespace HeadLoss.Game.Progression
{
    /// <summary>What Reputation has bought (<c>03-city.md</c> §Prestige). Levels live in <see cref="GameState.Prestige"/>.</summary>
    public static class PrestigeEffects
    {
        public const string ElectricityContracts = "electricity-contracts";
        public const string StarterKit = "starter-kit";
        public const string PayRates = "pay-rates";
        public const string Materials = "materials";
        public const string RapidResponse = "rapid-response";
        public const string ReconstructionPermits = "reconstruction-permits";

        /// <summary>Levels owned of a node.</summary>
        public static int Level(GameState g, string node) => g.Prestige.TryGetValue(node, out int v) ? v : 0;

        /// <summary>Multiplier on electricity prices: −10% per level.</summary>
        public static double ElectricityFactor(DataRepository data, GameState g) => Math.Max(0, 1 - 0.1 * Level(g, ElectricityContracts));

        /// <summary>Multiplier on base pay: +10% per level.</summary>
        public static double PayFactor(DataRepository data, GameState g) => 1 + 0.1 * Level(g, PayRates);

        /// <summary>Multiplier on repair and leak detection times: −20% per level.</summary>
        public static double RepairTimeFactor(DataRepository data, GameState g) => Math.Max(0.1, 1 - 0.2 * Level(g, RapidResponse));

        /// <summary>
        /// True when an item is unlocked from the start by a bonus, whatever the city's tier: copper with materials 1, steel
        /// with materials 2, the inline duct fan with starter kit 2 (the small axial fan is tier 1 for everyone).
        /// </summary>
        public static bool GrantsItem(GameState g, string itemId) => itemId switch
        {
            "copper" => Level(g, Materials) >= 1,
            "steel" => Level(g, Materials) >= 2,
            "inline-duct-fan" => Level(g, StarterKit) >= 2,
            _ => false,
        };

        /// <summary>Items a new city starts with in hand: each is free the first time it is bought.</summary>
        public static List<string> StarterItems(GameState g)
        {
            var items = new List<string>();
            if (Level(g, StarterKit) >= 1) items.Add("small-axial-fan");
            if (Level(g, StarterKit) >= 2) items.Add("inline-duct-fan");
            return items;
        }

        /// <summary>Next level's cost, or −1 at the maximum level.</summary>
        public static int NextCost(PrestigeNodeDef node, GameState g)
        {
            int level = Level(g, node.Id);
            return level < node.Costs.Count ? node.Costs[level] : -1;
        }

        /// <summary>Why a node's next level cannot be bought ("" = it can).</summary>
        public static string Blocked(DataRepository data, PrestigeNodeDef node, GameState g)
        {
            int cost = NextCost(node, g);
            if (cost < 0) return "Maximum level";
            if (Level(g, node.Id) == 0 && node.RequiresOtherNodes > 0)
            {
                int owned = 0;
                foreach (PrestigeNodeDef n in data.Prestige.Nodes)
                    if (n.Id != node.Id && Level(g, n.Id) > 0) owned++;
                if (owned < node.RequiresOtherNodes) return $"Requires {node.RequiresOtherNodes} other bonuses ({owned} owned)";
            }
            return g.Reputation < cost ? "Not enough Reputation" : "";
        }

        /// <summary>Buys a node's next level. False when blocked.</summary>
        public static bool Buy(DataRepository data, string nodeId, GameState g)
        {
            PrestigeNodeDef? node = data.Prestige.Find(nodeId);
            if (node == null || Blocked(data, node, g).Length > 0) return false;
            g.Reputation -= node.Costs[Level(g, nodeId)];
            g.Prestige[nodeId] = Level(g, nodeId) + 1;
            return true;
        }
    }

    /// <summary>The new-city milestone (<c>03-city.md</c> §Prestige [impl]): a share of the plots online and a lifetime income target.</summary>
    public sealed class Milestone
    {
        /// <summary>Plots counted: every plot whose tier has buildings in this version; ruins count and are never online (D-new, see decisions).</summary>
        public int Plots;
        public int Online;
        public int Ruins;
        public double ShareNeeded;
        /// <summary>Lifetime city income so far and its target, $.</summary>
        public double Income;
        public double IncomeTarget;
        /// <summary>Reputation founding a new city now would pay.</summary>
        public int ReputationReward;

        public double Share => Plots > 0 ? (double)Online / Plots : 0;
        public bool ShareMet => Share >= ShareNeeded;
        public bool IncomeMet => Income >= IncomeTarget;
        public bool Met => ShareMet && IncomeMet;

        /// <summary>Income target of a city by its index (1 = the first), $.</summary>
        public static double TargetFor(DataRepository data, int cityIndex) =>
            data.Balancing.Prestige.IncomeTargetFirst * Math.Pow(data.Balancing.Prestige.IncomeTargetGrowth, Math.Max(0, cityIndex - 1));

        /// <summary>Reputation = floor(k · log10(lifetime city income)), at least 1.</summary>
        public static int Reward(DataRepository data, double lifetimeCityIncome) =>
            Math.Max(1, (int)Math.Floor(data.Balancing.Prestige.K * Math.Log10(Math.Max(1, lifetimeCityIncome))));
    }
}
