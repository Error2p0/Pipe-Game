using System.Collections.Generic;
using System.Text;
using HeadLoss.Game.Buildings;

namespace HeadLoss.Game.Generation
{
    /// <summary>
    /// A text picture of a layout, one block per floor (dev tools and test logs). Rooms by the first letter of their
    /// type, shafts '#', stairwell '%', columns 'O', vents 'v', taps 't', openings 'A' (air), 'W' (water), 'P' (power),
    /// furniture '·', and with a state: air ducts '=', water pipes '~', both '≈', inline parts '*'.
    /// </summary>
    public static class LayoutAscii
    {
        public static string Render(BuildingLayout layout, BuildingState? state = null)
        {
            var sb = new StringBuilder();
            var air = new HashSet<TilePos>();
            var water = new HashSet<TilePos>();
            var parts = new HashSet<TilePos>();
            if (state != null)
            {
                foreach (DuctSegment s in state.Air.Segments) foreach (TilePos t in DuctEditor.Tiles(state.Air, s)) air.Add(t);
                foreach (DuctSegment s in state.Water.Segments) foreach (TilePos t in DuctEditor.Tiles(state.Water, s)) water.Add(t);
                foreach (DuctNetworkState n in state.Networks) foreach (DuctNode node in n.Nodes) if (node.Kind == DuctNodeKind.Inline) parts.Add(node.Pos);
            }
            for (int f = layout.TopFloor; f >= layout.LowestFloor; f--)
            {
                sb.AppendLine($"floor {f}:");
                for (int y = layout.Height - 1; y >= 0; y--)
                {
                    for (int x = 0; x < layout.Width; x++) sb.Append(Cell(layout, new TilePos(x, y, f), air, water, parts));
                    sb.AppendLine();
                }
            }
            return sb.ToString();
        }

        static char Cell(BuildingLayout layout, TilePos t, HashSet<TilePos> air, HashSet<TilePos> water, HashSet<TilePos> parts)
        {
            foreach (var o in layout.EntryOpenings)
                if (BuildingLayout.PosOf(o) == t) return o.Utility == "air" ? 'A' : o.Utility == "water" ? 'W' : 'P';
            foreach (var o in layout.Outlets)
                if (BuildingLayout.PosOf(o) == t) return BuildingState.OutletFluid(o.Type) == "water" ? 't' : 'v';
            if (parts.Contains(t)) return '*';
            bool a = air.Contains(t), w = water.Contains(t);
            if (a && w) return '≈';
            if (a) return '=';
            if (w) return '~';
            if (layout.IsColumn(t) && layout.IsInside(t)) return 'O';
            var room = layout.RoomOf(t);
            if (room == null) return ' ';
            if (room.Type == "shaft") return '#';
            if (room.Type == "stairwell") return '%';
            foreach (var fu in layout.Furniture)
                if (fu.Floor == t.F && t.X >= fu.X && t.X < fu.X + fu.Width && t.Y >= fu.Y && t.Y < fu.Y + fu.Height) return '·';
            return char.ToLowerInvariant(room.Type[0]);
        }
    }
}
