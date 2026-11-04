using System.Collections.Generic;
using HeadLoss.Core;
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

        /// <param name="snapshot">The state's solve: the operating codes that block a building read it (null = layout codes only).</param>
        public static List<Violation> Check(DataRepository data, BuildingLayout layout, BuildingState state, Session.NetworkSnapshot? snapshot = null)
        {
            var list = new List<Violation>();
            HashSet<string> codes = InForce(data, layout);
            if (codes.Contains("no-water-above-electrics")) NoWaterAboveElectrics(layout, state, list);
            if (codes.Contains("fire-dampers")) FireDampers(data, layout, state, list);
            if (codes.Contains("gas-separation")) GasSeparation(data, layout, state, list);
            if (codes.Contains("accessible-shutoffs")) AccessibleShutoffs(data, state, list);
            if (snapshot != null && codes.Contains("gas-room-ventilation")) GasRoomVentilation(data, layout, state, snapshot, list);
            return list;
        }

        /// <summary>No gas segment within N tiles (Chebyshev, same floor) of the panel or a heat source (Phase 5: the panel; heaters and boilers arrive with heating).</summary>
        static void GasSeparation(DataRepository data, BuildingLayout layout, BuildingState state, List<Violation> list)
        {
            TilePos? panel = layout.PanelTile;
            if (panel == null) return;
            int reach = (int)(data.FindCode("gas-separation")?.Parameters.GetValueOrDefault("tiles", 2) ?? 2);
            DuctNetworkState net = state.Gas;
            foreach (DuctSegment s in net.Segments)
            {
                Violation? v = null;
                foreach (TilePos t in DuctEditor.Tiles(net, s))
                {
                    if (t.F != panel.Value.F || System.Math.Max(System.Math.Abs(t.X - panel.Value.X), System.Math.Abs(t.Y - panel.Value.Y)) > reach) continue;
                    int d = System.Math.Max(System.Math.Abs(t.X - panel.Value.X), System.Math.Abs(t.Y - panel.Value.Y));
                    v ??= new Violation { CodeId = "gas-separation", Fluid = BuildingState.GasFluid, SegmentId = s.Id, Reason = $"Gas separation: gas pipe {d} tiles from the panel (needs more than {reach})" };
                    v.Tiles.Add(t);
                }
                if (v != null) list.Add(v);
            }
        }

        /// <summary>From each tee, every gas branch that reaches an outlet before the next tee needs a shutoff valve on the way.</summary>
        static void AccessibleShutoffs(DataRepository data, BuildingState state, List<Violation> list)
        {
            DuctNetworkState net = state.Gas;
            if (net.Segments.Count == 0) return;
            Dictionary<int, int> hops = DuctEditor.HopsFromIntake(net);
            foreach (DuctNode tee in net.Nodes)
            {
                if (tee.Kind != DuctNodeKind.Free || net.Degree(tee.Id) < 3) continue;
                int at = hops.TryGetValue(tee.Id, out int h) ? h : int.MaxValue;
                foreach (DuctSegment first in net.Segments)
                {
                    if (first.NodeA != tee.Id && first.NodeB != tee.Id) continue;
                    int next = first.Other(tee.Id);
                    // Skip the way the gas comes from.
                    if (at != int.MaxValue && hops.TryGetValue(next, out int nh) && nh < at) continue;
                    var tiles = new List<TilePos>(DuctEditor.Tiles(net, first));
                    int fromSegment = first.Id, node = next;
                    bool valve = false, outlet = false;
                    for (int guard = 0; guard < 256; guard++)
                    {
                        DuctNode n = net.FindNode(node)!;
                        if (n.Kind == DuctNodeKind.Inline && IsShutoff(data, n)) valve = true;
                        if (n.Kind == DuctNodeKind.Outlet) { outlet = true; break; }
                        if (n.Kind == DuctNodeKind.Free && net.Degree(n.Id) != 2) break;
                        DuctSegment? go = net.Segments.Find(x => x.Id != fromSegment && (x.NodeA == node || x.NodeB == node));
                        if (go == null) break;
                        tiles.AddRange(DuctEditor.Tiles(net, go));
                        fromSegment = go.Id;
                        node = go.Other(node);
                    }
                    if (!outlet || valve) continue;
                    var v = new Violation { CodeId = "accessible-shutoffs", Fluid = BuildingState.GasFluid, SegmentId = first.Id, Reason = "Accessible shutoffs: a gas branch has no shutoff valve before its outlet" };
                    v.Tiles.AddRange(tiles);
                    list.Add(v);
                }
            }
        }

        /// <summary>A gas part that can close a branch: a shutoff, ball valve or automatic shutoff.</summary>
        static bool IsShutoff(DataRepository data, DuctNode n)
        {
            FittingDef? f = data.FindFitting(n.PartId);
            return f != null && f.Model == "valve" && f.MinOpening >= 1;
        }

        /// <summary>Rooms with a connected gas appliance need the code's air changes per hour, from vents and passive vents; below it the building cannot come online.</summary>
        static void GasRoomVentilation(DataRepository data, BuildingLayout layout, BuildingState state, Session.NetworkSnapshot snap, List<Violation> list)
        {
            double need = data.FindCode("gas-room-ventilation")?.Parameters.GetValueOrDefault("kitchenAch", 0.5) ?? 0.5;
            var seen = new HashSet<int>();
            foreach (OutletPlacementDef o in layout.Outlets)
            {
                if (BuildingState.OutletFluid(o.Type) != BuildingState.GasFluid) continue;
                DuctNode? node = state.Gas.Nodes.Find(x => x.Kind == DuctNodeKind.Outlet && x.AnchorId == o.Id);
                if (node == null || state.Gas.Degree(node.Id) == 0) continue;
                TilePos tile = BuildingLayout.PosOf(o);
                int room = layout.RoomIndexAt(tile);
                if (room < 0 || !seen.Add(room)) continue;
                double ach = AirChanges(data, layout, state, snap, room);
                if (ach >= need) continue;
                RoomDef r = layout.Rooms[room];
                var v = new Violation
                {
                    CodeId = "gas-room-ventilation", Fluid = BuildingState.GasFluid,
                    Reason = $"Gas room ventilation: {data.RoomType(r.Type).DisplayName.ToLowerInvariant()} has {ach:0.0} air changes an hour (needs {need:0.0})",
                };
                v.Tiles.Add(tile);
                list.Add(v);
            }
        }

        /// <summary>Air changes per hour of a room: ACH = 3600 · Q / V, with Q its vents' flow and its passive vents at wind factor 1.</summary>
        public static double AirChanges(DataRepository data, BuildingLayout layout, BuildingState state, Session.NetworkSnapshot snap, int roomIndex)
        {
            RoomDef r = layout.Rooms[roomIndex];
            double q = 0;
            foreach (OutletPlacementDef o in layout.Outlets)
                if (layout.RoomIndexAt(BuildingLayout.PosOf(o)) == roomIndex && snap.Air.Compiled.OutletLink.TryGetValue(o.Id, out int link))
                    q += System.Math.Max(0, snap.Air.Result.Flow[link]);
            foreach (SafetyDevice d in state.Safety)
                if (d.EquipmentId == Failures.GasSafetyIds.PassiveVent && layout.RoomIndexAt(d.Pos) == roomIndex)
                    q += Units.M3hToM3s(data.Balancing.Gas.PassiveVentFlowM3h);
            double volume = r.Width * r.Height * data.Balancing.RoomGrid.RoomHeight;
            return volume > 0 ? 3600 * q / volume : 0;
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
