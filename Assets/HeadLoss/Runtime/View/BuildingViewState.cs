using System.Collections.Generic;
using HeadLoss.Game.Buildings;

namespace HeadLoss.Unity.View
{
    /// <summary>Layers per floor (<c>04-buildings.md</c> §Layers).</summary>
    public enum ViewLayer { Floor, Ceiling, WallCavity }

    /// <summary>
    /// What the building view shows and edits: one floor at a time (with ghosts of the floors above and below), the layer
    /// in focus, and the network the tools work on (<c>11-ui-visuals-audio.md</c> §Building controls).
    /// </summary>
    public sealed class BuildingViewState
    {
        readonly BuildingLayout _layout;
        readonly List<string> _fluids = new List<string> { "air" };

        public int Floor { get; private set; }
        public ViewLayer Layer { get; private set; } = ViewLayer.Ceiling;
        /// <summary>Network the drawing tool, parts and heatmap work on (air, water).</summary>
        public string Fluid { get; private set; } = "air";
        /// <summary>Wall cavity unlocked (tier 4): until then the layer button stays greyed out.</summary>
        public bool WallCavityAvailable { get; set; }
        /// <summary>Bumped when the floor, layer or network changes.</summary>
        public int Revision { get; private set; }

        public IReadOnlyList<string> Fluids => _fluids;

        public BuildingViewState(BuildingLayout layout, bool hasWater)
        {
            _layout = layout;
            if (hasWater) _fluids.Add("water");
            Floor = 0 >= layout.LowestFloor && 0 <= layout.TopFloor ? 0 : layout.LowestFloor;
        }

        public void SetFloor(int floor)
        {
            floor = System.Math.Max(_layout.LowestFloor, System.Math.Min(_layout.TopFloor, floor));
            if (floor == Floor) return;
            Floor = floor;
            Revision++;
        }

        /// <summary>PageUp / PageDown.</summary>
        public void StepFloor(int delta) => SetFloor(Floor + delta);

        public void SetLayer(ViewLayer layer)
        {
            if (layer == ViewLayer.WallCavity && !WallCavityAvailable) return;
            if (layer == Layer) return;
            Layer = layer;
            Revision++;
        }

        /// <summary>L: floor → ceiling → wall cavity (when unlocked) → floor.</summary>
        public void CycleLayer() => SetLayer(Layer switch
        {
            ViewLayer.Floor => ViewLayer.Ceiling,
            ViewLayer.Ceiling => WallCavityAvailable ? ViewLayer.WallCavity : ViewLayer.Floor,
            _ => ViewLayer.Floor,
        });

        public void SetFluid(string fluid)
        {
            if (!_fluids.Contains(fluid) || fluid == Fluid) return;
            Fluid = fluid;
            Revision++;
        }

        /// <summary>Display name of a floor: G (ground), 1, 2 … or B (basement).</summary>
        public static string FloorName(int floor) => floor == 0 ? "Ground" : floor < 0 ? "Basement" : $"Floor {floor}";
    }
}
