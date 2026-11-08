using System.Collections.Generic;
using HeadLoss.Core.Data;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Buildings;

namespace HeadLoss.Game.Networks
{
    /// <summary>
    /// The gas manifold (<c>07-equipment.md</c> §Gas supply, D168): fitted on a gas tee or cross, it has a shutoff valve on each
    /// branch (up to 6: four sides, up and down) and a built-in detector, and counts as the accessible shutoff for every
    /// branch that leaves it. A branch is named by the direction it leaves in, so it survives the pipes being redrawn.
    /// </summary>
    public static class GasManifold
    {
        public const string Id = "gas-manifold";

        /// <summary>Directions a branch can leave in: e, w, n, s, up, down.</summary>
        public static readonly string[] Directions = { "e", "w", "n", "s", "up", "down" };

        public static bool Is(DuctNode n) => n.Kind == DuctNodeKind.Free && n.PartId == Id;

        /// <summary>A node a manifold may be fitted on: a gas junction (three pipes or more) of a network that carries gas.</summary>
        public static bool CanFit(DataRepository data, DuctNetworkState net, DuctNode n) =>
            n.Kind == DuctNodeKind.Free && net.Degree(n.Id) >= 3 && data.FluidIdOf(net.FluidId) == BuildingState.GasFluid;

        /// <summary>The direction a segment leaves a node in.</summary>
        public static string DirectionOf(DuctNetworkState net, DuctNode node, DuctSegment s)
        {
            List<TilePos> tiles = DuctEditor.Tiles(net, s);
            if (tiles.Count < 2) return "";
            TilePos a = node.Pos, b = tiles[0] == a ? tiles[1] : tiles[tiles.Count - 2];
            if (b.F > a.F) return "up";
            if (b.F < a.F) return "down";
            if (b.X > a.X) return "e";
            if (b.X < a.X) return "w";
            return b.Y > a.Y ? "n" : "s";
        }

        /// <summary>The segments that leave a manifold, with their directions.</summary>
        public static List<(DuctSegment segment, string direction)> Branches(DuctNetworkState net, DuctNode node)
        {
            var list = new List<(DuctSegment, string)>();
            foreach (DuctSegment s in net.Segments)
                if (s.NodeA == node.Id || s.NodeB == node.Id) list.Add((s, DirectionOf(net, node, s)));
            return list;
        }

        /// <summary>Manifold nodes of a state's gas networks.</summary>
        public static IEnumerable<DuctNode> All(DataRepository data, BuildingState state)
        {
            foreach (DuctNetworkState net in state.Networks)
            {
                if (data.FluidIdOf(net.FluidId) != BuildingState.GasFluid) continue;
                foreach (DuctNode n in net.Nodes) if (Is(n)) yield return n;
            }
        }

        public static string DirectionName(string d) => d switch
        {
            "e" => "East",
            "w" => "West",
            "n" => "North",
            "s" => "South",
            "up" => "Up",
            "down" => "Down",
            _ => d,
        };
    }
}
