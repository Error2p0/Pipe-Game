using System.Collections.Generic;
using HeadLoss.Core.Data;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Economy;

namespace HeadLoss.Game.Buildings
{
    /// <summary>
    /// A hand-made building's reference layout (stands in for the Phase 3 auto-router): the budget cap is
    /// <c>budgetCapFactor × its cost</c>, and it must meet every minimum with the reference equipment
    /// (<c>04-buildings.md</c> §Procedural generation, steps 10–11).
    /// </summary>
    public static class ReferenceLayout
    {
        /// <summary>Tiles along orthogonal legs through the given corner points.</summary>
        public static List<TilePos> Expand(IReadOnlyList<List<int>> points)
        {
            var path = new List<TilePos> { new TilePos(points[0][0], points[0][1]) };
            for (int i = 1; i < points.Count; i++)
            {
                TilePos cur = path[path.Count - 1];
                var target = new TilePos(points[i][0], points[i][1]);
                (int dx, int dy) = cur.DirectionTo(target);
                while (cur != target)
                {
                    cur = new TilePos(cur.X + dx, cur.Y + dy);
                    path.Add(cur);
                }
            }
            return path;
        }

        /// <summary>Builds the reference state. Throws when a run breaks the drawing rules (a data error).</summary>
        public static BuildingState Build(DataRepository data, BuildingLayout layout)
        {
            BuildingState s = BuildingState.CreateFor(layout);
            s.Equipment.Clear();
            foreach (EquipmentPlacementDef e in layout.Reference.Equipment)
                s.Equipment.Add(new EquipmentState { EquipmentId = e.EquipmentId, OpeningId = e.OpeningId, On = e.On });
            foreach (ReferenceRunDef run in layout.Reference.Runs)
            {
                List<TilePos> path = Expand(run.Points);
                DrawError err = DuctEditor.Validate(s.Air, layout, path, data.Balancing.Drawing.MaxNodeDegree);
                if (err != DrawError.None)
                    throw new System.InvalidOperationException($"{layout.Id}: reference run from {path[0]} is invalid ({err})");
                string material = string.IsNullOrEmpty(run.MaterialId) ? data.Balancing.Drawing.DefaultMaterial : run.MaterialId;
                DuctEditor.PlaceRun(s.Air, path, run.DiameterMm, material, run.SmoothElbows);
            }
            return s;
        }

        /// <summary>Catalogue cost of the reference layout, $.</summary>
        public static double Cost(DataRepository data, BuildingLayout layout) => Pricing.Value(data, layout, Build(data, layout));

        /// <summary>Budget cap for the building's first build, $.</summary>
        public static double BudgetCap(DataRepository data, BuildingLayout layout) =>
            data.Balancing.Building.BudgetCapFactor * Cost(data, layout);
    }
}
