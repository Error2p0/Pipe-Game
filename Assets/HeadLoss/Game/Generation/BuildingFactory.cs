using System.Collections.Generic;
using HeadLoss.Core;
using HeadLoss.Core.Data;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Buildings;
using HeadLoss.Game.City;

namespace HeadLoss.Game.Generation
{
    /// <summary>A generated building that passed (or could not pass) the solvability check.</summary>
    public sealed class GeneratedBuilding
    {
        public BuildingLayout Layout = null!;
        public ReferenceResult Reference = null!;
        /// <summary>Solvability nudges used (0 = the first layout passed).</summary>
        public int Attempt;
        /// <summary>The building is a tier lower than its plot rolled (solvability fallback).</summary>
        public bool TierLowered;
        /// <summary>Budget cap = <c>budgetCapFactor</c> × the reference cost, $.</summary>
        public double BudgetCap;
        /// <summary>Solvability failures on the way (the dev report).</summary>
        public readonly List<string> Notes = new List<string>();
    }

    /// <summary>
    /// Generation plus the solvability check (<c>04-buildings.md</c> step 11): the auto-router builds a reference
    /// layout with the tier's reference equipment; if any minimum fails or any code is violated, the openings move,
    /// then the rooms are regenerated with a new sub-seed (up to <c>solvabilityAttempts</c>). If it still fails, the
    /// plot becomes a building a tier lower. Failures are kept in <see cref="GeneratedBuilding.Notes"/>.
    /// </summary>
    public static class BuildingFactory
    {
        /// <summary>Generates a plot's building. Null when its tier is not built in this version.</summary>
        public static GeneratedBuilding? Create(DataRepository data, Plot plot, DistrictKind district) =>
            Create(data, new BuildingRequest(plot.Id, plot.BuildingType, plot.BuildingSeed, plot.StreetSide), district);

        public static GeneratedBuilding? Create(DataRepository data, BuildingRequest req, DistrictKind district)
        {
            BuildingTypeDef type = data.BuildingType(req.BuildingType);
            if (!data.BuildingTier(type.Tier).Available) return null;
            int attempts = data.Buildings.Generation.SolvabilityAttempts;
            GeneratedBuilding? last = null;
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                BuildingLayoutDef? def = BuildingGenerator.Generate(data, req, attempt);
                if (def == null) continue;
                var layout = new BuildingLayout(def);
                ReferenceResult r = AutoRouter.Build(data, layout);
                last = new GeneratedBuilding
                {
                    Layout = layout,
                    Reference = r,
                    Attempt = attempt,
                    BudgetCap = data.Balancing.Building.BudgetCapFactor * r.Cost,
                };
                if (r.Solvable) return last;
            }
            string note = $"{req.Id} ({req.BuildingType}, seed {req.Seed}): not solvable after {attempts} attempts ({last?.Reference.Reason ?? "no layout"})";
            last?.Notes.Add(note);
            if (type.Tier > 1)
            {
                string lower = CityGenerator.RollType(data, type.Tier - 1, district, new Rng(Seeds.Derive(req.Seed, "tier-lower")));
                if (lower.Length > 0)
                {
                    GeneratedBuilding? g = Create(data, new BuildingRequest(req.Id, lower, Seeds.Derive(req.Seed, "tier-lower"), req.StreetSide), district);
                    if (g != null)
                    {
                        g.TierLowered = true;
                        g.Notes.Insert(0, note);
                    }
                    return g;
                }
            }
            return last;
        }
    }

    /// <summary>
    /// The starter guarantee (<c>03-city.md</c> step 8): the starter house's seed is regenerated until it is solvable
    /// with the hand crank at its reference speed and its intake is close enough to the farthest vent.
    /// </summary>
    public static class StarterGuarantee
    {
        /// <returns>True when a solvable starter seed was found (it is stored in the plot).</returns>
        public static bool Resolve(DataRepository data, CityLayout city)
        {
            Plot? p = city.Starter;
            if (p == null) return false;
            ulong baseSeed = Seeds.Derive(city.Seed, p.Id);
            DistrictKind district = city.Districts[p.District].Kind;
            for (int a = 0; a < data.City.Starter.MaxAttempts; a++)
            {
                ulong seed = a == 0 ? baseSeed : Seeds.Derive(baseSeed, (ulong)a);
                GeneratedBuilding? g = BuildingFactory.Create(data, new BuildingRequest(p.Id, p.BuildingType, seed, p.StreetSide), district);
                if (g == null || !g.Reference.Solvable || g.TierLowered || g.Attempt > 0) continue;
                p.BuildingSeed = seed;
                return true;
            }
            return false;
        }

        /// <summary>A new city: generated, with the starter guarantee settled (false when no starter seed was solvable).</summary>
        public static CityLayout NewCity(DataRepository data, ulong seed, int cityIndex, out bool starterSolvable)
        {
            CityLayout c = CityGenerator.Generate(data, seed, cityIndex);
            starterSolvable = Resolve(data, c);
            return c;
        }
    }
}
