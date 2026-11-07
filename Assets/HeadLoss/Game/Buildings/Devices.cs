using System.Collections.Generic;
using HeadLoss.Core.Data;
using HeadLoss.Core.Data.Defs;

namespace HeadLoss.Game.Buildings
{
    /// <summary>
    /// A device the player stands on a room floor that joins networks through its ports: a water heater (cold in, hot out), a boiler
    /// (gas in, heating loop through it), later heat exchangers and heat pumps (<c>05-simulation.md</c> §1.5 coupling nodes).
    /// </summary>
    public sealed class PlacedDevice
    {
        public string EquipmentId = "";
        /// <summary>Origin tile (port offsets are counted from here).</summary>
        public int X;
        public int Y;
        public int Floor;
        /// <summary>Direction its width runs: east (as defined), north, west or south (turned with R).</summary>
        public string Facing = "east";
        /// <summary>Switched on (off: water passes through unheated).</summary>
        public bool On = true;
        /// <summary>Outlet setpoint, °C (0 = the device's default).</summary>
        public double SetpointC;

        public TilePos Pos => new TilePos(X, Y, Floor);
        /// <summary>The device's key: its anchor in the networks, its damage key and its place in the bill. Two devices never share an origin.</summary>
        public string Key => $"{EquipmentId}@{X},{Y},{Floor}";
        public PlacedDevice Clone() => (PlacedDevice)MemberwiseClone();
    }

    /// <summary>Where a device's tiles and ports are, and whether it may stand somewhere.</summary>
    public static class Devices
    {
        /// <summary>Unit steps of a facing: along its width (dx) and along its depth (dy).</summary>
        static ((int x, int y) along, (int x, int y) depth) Axes(string facing) => facing switch
        {
            "north" => ((0, 1), (-1, 0)),
            "west" => ((-1, 0), (0, -1)),
            "south" => ((0, -1), (1, 0)),
            _ => ((1, 0), (0, 1)),
        };

        /// <summary>The next facing clockwise (R).</summary>
        public static string Turn(string facing) => facing switch
        {
            "east" => "south",
            "south" => "west",
            "west" => "north",
            _ => "east",
        };

        /// <summary>The tile of an offset in a device's footprint.</summary>
        public static TilePos TileOf(TilePos origin, string facing, int dx, int dy)
        {
            ((int ax, int ay), (int bx, int by)) = Axes(facing);
            return new TilePos(origin.X + dx * ax + dy * bx, origin.Y + dx * ay + dy * by, origin.F);
        }

        /// <summary>Every floor tile a device takes.</summary>
        public static List<TilePos> Tiles(EquipmentDef def, TilePos origin, string facing)
        {
            var tiles = new List<TilePos>();
            for (int dx = 0; dx < def.Footprint.Width; dx++)
                for (int dy = 0; dy < def.Footprint.Height; dy++) tiles.Add(TileOf(origin, facing, dx, dy));
            return tiles;
        }

        public static List<TilePos> Tiles(DataRepository data, PlacedDevice d) => Tiles(data.EquipmentItem(d.EquipmentId), d.Pos, d.Facing);

        /// <summary>The tile of a port.</summary>
        public static TilePos PortTile(PlacedDevice d, PortDef p) => TileOf(d.Pos, d.Facing, p.Dx, p.Dy);

        /// <summary>The node kind a port's anchor takes: the network feeds the device (in, fuel, return) or the device feeds it (out, flow).</summary>
        public static DuctNodeKind PortKind(PortDef p) => p.Role == "out" || p.Role == "flow" ? DuctNodeKind.DeviceOut : DuctNodeKind.DeviceIn;

        /// <summary>The anchor node of a device's port in its network, or null.</summary>
        public static DuctNode? PortNode(DuctNetworkState net, PlacedDevice d, PortDef p)
        {
            string key = d.Key;
            return net.Nodes.Find(n => (n.Kind == DuctNodeKind.DeviceIn || n.Kind == DuctNodeKind.DeviceOut) && n.AnchorId == key && n.PartId == p.Role);
        }

        public static PlacedDevice? Find(BuildingState s, string key) => s.Devices.Find(d => d.Key == key);

        /// <summary>
        /// Checks a device placement: inside one room on the floor layer, clear of columns, outlets, doorways, furniture, openings,
        /// opening equipment and other devices, against an exterior wall when it needs one, and every port tile free on its network.
        /// </summary>
        public static DrawError Validate(DataRepository data, BuildingLayout layout, BuildingState state, EquipmentDef def, TilePos origin, string facing)
        {
            List<TilePos> tiles = Tiles(def, origin, facing);
            int room = layout.RoomIndexAt(origin);
            if (room < 0) return DrawError.OutsideBuilding;
            var blocked = Blocked(data, layout, state);
            bool exterior = false;
            foreach (TilePos t in tiles)
            {
                if (layout.RoomIndexAt(t) != room) return DrawError.OutsideBuilding;
                if (layout.IsColumn(t) || blocked.Contains(t)) return DrawError.NoFloorSpace;
                foreach (string side in Sides) if (!layout.IsInside(BuildingLayout.Neighbour(t, side))) exterior = true;
            }
            if (def.NeedsExteriorWall && !exterior) return DrawError.NeedsExteriorWall;
            foreach (PortDef p in def.Ports)
                if (state.FindNet(p.Network)?.NodeAt(TileOf(origin, facing, p.Dx, p.Dy)) != null) return DrawError.Overlap;
            return DrawError.None;
        }

        static readonly string[] Sides = { "north", "south", "east", "west" };

        /// <summary>Floor tiles a device may not use.</summary>
        static HashSet<TilePos> Blocked(DataRepository data, BuildingLayout layout, BuildingState state)
        {
            var b = new HashSet<TilePos>();
            foreach (OutletPlacementDef o in layout.Outlets) b.Add(BuildingLayout.PosOf(o));
            foreach (EntryOpeningDef o in layout.EntryOpenings) b.Add(BuildingLayout.PosOf(o));
            foreach (DoorDef d in layout.Doors)
            {
                var t = new TilePos(d.X, d.Y, d.Floor);
                b.Add(t);
                b.Add(BuildingLayout.Neighbour(t, d.Side));
            }
            foreach (FurnitureDef f in layout.Furniture)
                for (int x = f.X; x < f.X + f.Width; x++)
                    for (int y = f.Y; y < f.Y + f.Height; y++) b.Add(new TilePos(x, y, f.Floor));
            foreach (EquipmentState e in state.Equipment)
            {
                EntryOpeningDef? o = layout.FindOpening(e.OpeningId);
                EquipmentDef? def = data.FindEquipment(e.EquipmentId);
                if (o == null || def == null) continue;
                List<TilePos>? fp = EquipmentFits.Footprint(data, layout, state, o, def);
                if (fp != null) foreach (TilePos t in fp) b.Add(t);
            }
            foreach (PlacedDevice d in state.Devices)
                if (data.FindEquipment(d.EquipmentId) is EquipmentDef dd)
                    foreach (TilePos t in Tiles(dd, d.Pos, d.Facing)) b.Add(t);
            return b;
        }

        /// <summary>Adds a validated device and the anchor node of each of its ports.</summary>
        public static PlacedDevice Place(BuildingState state, EquipmentDef def, TilePos origin, string facing)
        {
            var d = new PlacedDevice { EquipmentId = def.Id, X = origin.X, Y = origin.Y, Floor = origin.F, Facing = facing };
            state.Devices.Add(d);
            foreach (PortDef p in def.Ports)
            {
                DuctNetworkState net = state.Net(p.Network);
                TilePos t = PortTile(d, p);
                net.Nodes.Add(new DuctNode { Id = net.NextNodeId++, X = t.X, Y = t.Y, Floor = t.F, Kind = PortKind(p), AnchorId = d.Key, PartId = p.Role });
            }
            return d;
        }

        /// <summary>Removes a device, its port anchors and the pipes ending on them.</summary>
        public static bool Remove(DataRepository data, BuildingState state, string key)
        {
            PlacedDevice? d = Find(state, key);
            if (d == null) return false;
            state.Devices.Remove(d);
            foreach (DuctNetworkState net in state.Networks)
            {
                var ports = net.Nodes.FindAll(n => (n.Kind == DuctNodeKind.DeviceIn || n.Kind == DuctNodeKind.DeviceOut) && n.AnchorId == key);
                if (ports.Count == 0) continue;
                var segments = new List<int>();
                foreach (DuctNode n in ports)
                    foreach (DuctSegment s in net.Segments) if (s.NodeA == n.Id || s.NodeB == n.Id) segments.Add(s.Id);
                if (segments.Count > 0) DuctEditor.DeleteSegments(net, segments);
                foreach (DuctNode n in ports) net.Nodes.Remove(n);
                DuctEditor.Normalize(net);
            }
            return true;
        }
    }
}
