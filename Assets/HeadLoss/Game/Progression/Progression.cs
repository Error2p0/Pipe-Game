using System;
using System.Collections.Generic;
using HeadLoss.Core.Data;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Session;

namespace HeadLoss.Game.Progression
{
    /// <summary>
    /// Tech unlocks (<c>03-city.md</c> §Progression and tech unlocks [confirm]): equipment, pipe sizes, materials,
    /// fittings and upgrades carry a tier; tier-1 items are available from the start, and tier N + 1 unlocks when
    /// <c>progression.unlockOnline[N]</c> buildings of tier N are online at the same time. Unlocked tiers stay
    /// unlocked for the rest of the city.
    /// </summary>
    public static class Tech
    {
        public const int MaxTier = 5;

        /// <summary>Highest unlocked tier (test fixtures without a city have everything).</summary>
        public static int UnlockedTier(GameState g) => g.City?.UnlockedTier ?? MaxTier;

        public static bool Unlocked(GameState g, int tier) => tier <= UnlockedTier(g);

        /// <summary>Buildings of a tier online right now.</summary>
        public static int OnlineCount(IReadOnlyList<BuildingSession> sessions, int tier)
        {
            int n = 0;
            foreach (BuildingSession s in sessions)
                if (s.Layout.Tier == tier && s.CommittedEvaluation.Online) n++;
            return n;
        }

        /// <summary>Buildings of tier N needed online to unlock N + 1 (0 when N is the last tier).</summary>
        public static int Needed(DataRepository data, int tier)
        {
            List<int> u = data.Balancing.Progression.UnlockOnline;
            return tier >= 1 && tier <= u.Count ? u[tier - 1] : 0;
        }

        /// <summary>Unlocks every tier whose condition is met now. Returns the highest tier newly unlocked, or 0.</summary>
        public static int Update(GameContext ctx)
        {
            if (ctx.Game.City == null) return 0;
            int unlocked = 0;
            while (ctx.Game.City.UnlockedTier < MaxTier)
            {
                int tier = ctx.Game.City.UnlockedTier;
                int need = Needed(ctx.Data, tier);
                if (need <= 0 || OnlineCount(ctx.Sessions, tier) < need) break;
                ctx.Game.City.UnlockedTier = tier + 1;
                unlocked = tier + 1;
            }
            return unlocked;
        }

        /// <summary>The unlock condition of a tier, with progress ("2 tier-1 buildings online at once · 1 of 2").</summary>
        public static string Condition(DataRepository data, GameContext ctx, int tier)
        {
            if (Unlocked(ctx.Game, tier)) return "Unlocked";
            int below = tier - 1;
            int need = Needed(data, below);
            int have = OnlineCount(ctx.Sessions, below);
            string first = below == UnlockedTier(ctx.Game) ? $" · {Math.Min(have, need)} of {need}" : "";
            return $"Unlocks with {need} tier-{below} buildings online at once{first}";
        }

        /// <summary>Tech tier of a duct or pipe size (1 when the size is not listed).</summary>
        public static int SizeTier(DataRepository data, string materialId, double diameterMm)
        {
            MaterialDef? m = data.FindMaterial(materialId);
            if (m == null) return 1;
            foreach (PipeFamilyDef fam in data.PipeFamilies)
                if (fam.Kind == m.Kind)
                    foreach (PipeSizeDef s in fam.Sizes)
                        if (s.DiameterMm == diameterMm) return s.Tier;
            return 1;
        }

        /// <summary>Tech tier of any catalogue item id (equipment, fitting, material, sealing, insulation); 1 when unknown.</summary>
        public static int ItemTier(DataRepository data, string id)
        {
            if (id.Length == 0) return 1;
            if (data.FindEquipment(id) is EquipmentDef e) return e.Tier;
            if (data.FindFitting(id) is FittingDef f) return f.Tier;
            if (data.FindMaterial(id) is MaterialDef m) return m.Tier;
            if (data.FindSealing(id) is SealingDef s) return s.Tier;
            if (data.FindInsulation(id) is InsulationDef i) return i.Tier;
            return 1;
        }

        /// <summary>The tier of smooth fittings (long-radius elbows and smooth tees).</summary>
        public static int SmoothFittingsTier(DataRepository data) =>
            Math.Max(data.Fitting("elbow-90-long-radius").Tier, data.Fitting("tee-smooth").Tier);
    }
}
