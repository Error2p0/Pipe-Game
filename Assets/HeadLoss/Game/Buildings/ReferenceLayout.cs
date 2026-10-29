using HeadLoss.Core.Data;
using HeadLoss.Game.Generation;

namespace HeadLoss.Game.Buildings
{
    /// <summary>
    /// A building's reference layout, built by the auto-router (<c>04-buildings.md</c> §Procedural generation,
    /// steps 10–11): the budget cap is <c>budgetCapFactor × its cost</c>, and it must meet every minimum with the
    /// reference equipment. (Phase 2's hand-drawn reference layouts are retired, D28.)
    /// </summary>
    public static class ReferenceLayout
    {
        public static ReferenceResult Route(DataRepository data, BuildingLayout layout) => AutoRouter.Build(data, layout);

        /// <summary>The reference state.</summary>
        public static BuildingState Build(DataRepository data, BuildingLayout layout) => Route(data, layout).State;

        /// <summary>Catalogue cost of the reference layout, $.</summary>
        public static double Cost(DataRepository data, BuildingLayout layout) => Route(data, layout).Cost;

        /// <summary>Budget cap for the building's first build, $.</summary>
        public static double BudgetCap(DataRepository data, BuildingLayout layout) =>
            data.Balancing.Building.BudgetCapFactor * Cost(data, layout);
    }
}
