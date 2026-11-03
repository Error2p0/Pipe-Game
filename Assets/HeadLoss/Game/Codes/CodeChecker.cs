using System.Collections.Generic;
using HeadLoss.Core.Data;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Buildings;

namespace HeadLoss.Game.Codes
{
    /// <summary>A broken building code: the tiles to outline and a one-line reason (<c>09-codes.md</c> §Presentation).</summary>
    public sealed class Violation
    {
        public string CodeId = "";
        /// <summary>One calm line, e.g. "No water above electrics: water pipe over the electrical room".</summary>
        public string Reason = "";
        /// <summary>Layout violations keep the building offline until fixed.</summary>
        public bool Blocking = true;
        public string Fluid = "";
        /// <summary>The segment at fault (0 = none).</summary>
        public int SegmentId;
        public readonly List<TilePos> Tiles = new List<TilePos>();
    }

    /// <summary>
    /// The building codes (<c>09-codes.md</c>). Layout codes are checked here, live in design mode and on what is built;
    /// operating codes (noise and tap pressure: the outlets' code maximum) apply in the economy and only where in force.
    /// Constraints (walls, columns, protected rooms, exterior walls) are drawing rules and never reach a layout.
    /// </summary>
    public static class CodeChecker
    {
        /// <summary>Codes in force in a building: its own list, or the tier's base codes when the list is empty.</summary>
        public static HashSet<string> InForce(DataRepository data, BuildingLayout layout)
        {
            var set = new HashSet<string>(layout.Codes);
            if (set.Count == 0)
                foreach (CodeDef c in data.Codes)
                    if (c.Tiers.Contains(layout.Tier) || (layout.BuildingType.Length > 0 && c.BuildingTypes.Contains(layout.BuildingType))) set.Add(c.Id);
            return set;
        }

        /// <summary>The code behind an outlet type's code maximum (noise for vents, tap pressure for taps), or "".</summary>
        public static string CodeForOutlet(OutletDef outlet) => outlet.CodeMaxReason switch
        {
            "Noise" => "noise",
            "Tap pressure" => "tap-pressure",
            _ => "",
        };

        public static List<Violation> Check(DataRepository data, BuildingLayout layout, BuildingState state)
        {
            var list = new List<Violation>();
            HashSet<string> codes = InForce(data, layout);
            if (codes.Contains("no-water-above-electrics")) NoWaterAboveElectrics(layout, state, list);
            if (codes.Contains("fire-dampers")) FireDampers(data, layout, state, list);
            return list;
        }

        /// <summary>No water pipe on a ceiling tile over an electrical room or the panel.</summary>
        static void NoWaterAboveElectrics(BuildingLayout layout, BuildingState state, List<Violation> list)
        {
            TilePos? panel = layout.PanelTile;
            DuctNetworkState net = state.Water;
            foreach (DuctSegment s in net.Segments)
            {
                Violation? v = null;
                foreach (TilePos t in DuctEditor.Tiles(net, s))
                {
                    bool room = layout.IsElectricalRoom(t);
                    if (!room && !(panel.HasValue && panel.Value == t)) continue;
                    v ??= new Violation
                    {
                        CodeId = "no-water-above-electrics", Fluid = "water", SegmentId = s.Id,
                        Reason = room ? "No water above electrics: water pipe over the electrical room" : "No water above electrics: water pipe over the panel",
                    };
                    v.Tiles.Add(t);
                }
                if (v != null) list.Add(v);
            }
        }

        /// <summary>Every duct crossing a fire wall needs a fire damper on one of the two tiles at the crossing.</summary>
        static void FireDampers(DataRepository data, BuildingLayout layout, BuildingState state, List<Violation> list)
        {
            if (layout.FireWalls.Count == 0) return;
            DuctNetworkState net = state.Air;
            bool Damper(TilePos t) => net.NodeAt(t) is DuctNode n && n.Kind == DuctNodeKind.Inline && n.PartId == "fire-damper";
            foreach (DuctSegment s in net.Segments)
            {
                List<TilePos> tiles = DuctEditor.Tiles(net, s);
                for (int i = 1; i < tiles.Count; i++)
                {
                    if (!layout.IsFireWall(tiles[i - 1], tiles[i])) continue;
                    // The damper splits the duct, so it sits on a node at the end of this segment.
                    if (Damper(tiles[i - 1]) || Damper(tiles[i])) continue;
                    var v = new Violation { CodeId = "fire-dampers", Fluid = "air", SegmentId = s.Id, Reason = "Fire dampers: a duct crosses a fire wall without a fire damper" };
                    v.Tiles.Add(tiles[i - 1]);
                    v.Tiles.Add(tiles[i]);
                    list.Add(v);
                }
            }
        }
    }
}
