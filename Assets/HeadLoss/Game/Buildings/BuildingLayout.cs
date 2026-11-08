using System.Collections.Generic;
using HeadLoss.Core.Data.Defs;

namespace HeadLoss.Game.Buildings
{
    /// <summary>What separates two adjacent tiles on one floor.</summary>
    public enum WallKind
    {
        /// <summary>Same room: no wall.</summary>
        None,
        /// <summary>A door in an interior wall.</summary>
        Door,
        /// <summary>An interior wall without a door (pipes need core drilling).</summary>
        Interior,
        /// <summary>Into or out of a shaft: shafts are open to pipes on every side.</summary>
        Shaft,
        /// <summary>One side is outside the building.</summary>
        Exterior,
    }

    /// <summary>
    /// The generated (or hand-made) part of a building: footprint, floors, rooms, entry openings, outlets and the
    /// fixed elements (shafts, stairwell, columns, fire walls, furniture). Never saved; rebuilt from data or seed
    /// (<c>01-architecture.md</c> §State model).
    /// </summary>
    public sealed class BuildingLayout
    {
        public string Id { get; }
        public string DisplayName { get; }
        public int Tier { get; }
        public int Width { get; }
        public int Height { get; }
        /// <summary>Number of floors.</summary>
        public int Floors { get; }
        /// <summary>Capacity units per ceiling tile: the layout's own (a city modifier) or its tier's (D165).</summary>
        public int CeilingCapacity(Core.Data.DataRepository data) =>
            Def.CeilingCapacity > 0 ? Def.CeilingCapacity : data.Balancing.Building.CeilingCapacityFor(Tier);

        /// <summary>Lowest floor index (−1 with a basement).</summary>
        public int LowestFloor { get; }
        /// <summary>Highest floor index.</summary>
        public int TopFloor => LowestFloor + Floors - 1;
        public IReadOnlyList<RoomDef> Rooms => _rooms;
        public IReadOnlyList<EntryOpeningDef> EntryOpenings => _openings;
        public IReadOnlyList<OutletPlacementDef> Outlets => _outlets;
        /// <summary>Equipment the layout starts with (pre-placed at openings).</summary>
        public IReadOnlyList<EquipmentPlacementDef> InitialEquipment => _equipment;
        public IReadOnlyList<DoorDef> Doors => _doors;
        public IReadOnlyList<WindowDef> Windows => Def.Windows;
        public IReadOnlyList<FurnitureDef> Furniture => Def.Furniture;
        public IReadOnlyList<TileDef> Columns => Def.Columns;
        public IReadOnlyList<DoorDef> FireWalls => Def.FireWalls;
        /// <summary>Codes in force (code ids).</summary>
        public IReadOnlyList<string> Codes => Def.Codes;
        /// <summary>Building type id ("" for hand-made layouts).</summary>
        public string BuildingType => Def.BuildingType;
        /// <summary>Hand-crank speed the reference layout must meet every minimum at (0 = no crank reference).</summary>
        public double ReferenceCrankSpeed => Def.ReferenceCrankSpeed;
        /// <summary>The definition the layout was built from (hand-made data or the generator).</summary>
        public BuildingLayoutDef Def { get; }

        readonly List<RoomDef> _rooms;
        readonly List<EntryOpeningDef> _openings;
        readonly List<OutletPlacementDef> _outlets;
        readonly List<EquipmentPlacementDef> _equipment;
        readonly List<DoorDef> _doors;
        /// <summary>Room index per tile, per floor (index floor − LowestFloor).</summary>
        readonly int[][] _roomIndex;
        readonly HashSet<long> _doorEdges = new HashSet<long>();
        readonly HashSet<long> _fireEdges = new HashSet<long>();
        readonly HashSet<int> _columns = new HashSet<int>();
        readonly Dictionary<string, RoomTypeInfo> _roomTypes;

        /// <summary>What the layout needs to know about a room type (passed in so hand-made fixtures need no data).</summary>
        public readonly struct RoomTypeInfo
        {
            public readonly bool Shaft;
            public readonly bool BlocksPipes;
            public readonly bool Protected;
            public readonly bool Electrical;

            public RoomTypeInfo(bool shaft, bool blocksPipes, bool isProtected, bool electrical)
            {
                Shaft = shaft;
                BlocksPipes = blocksPipes;
                Protected = isProtected;
                Electrical = electrical;
            }
        }

        /// <summary>A layout with room types and wall rules from the data files.</summary>
        public static BuildingLayout From(HeadLoss.Core.Data.DataRepository data, BuildingLayoutDef def)
        {
            var layout = new BuildingLayout(def, RoomTypesFrom(data.Buildings), data.Balancing.Building.CoreDrillTier > 0 ? data.Balancing.Building.CoreDrillTier : int.MaxValue);
            layout._outletNetwork = new Dictionary<string, string>();
            foreach (OutletDef o in data.Outlets) layout._outletNetwork[o.Id] = o.Fluid;
            layout._utilityNetwork = new Dictionary<string, string>();
            foreach (NetworkDef n in data.Networks) if (n.Utility.Length > 0) layout._utilityNetwork[n.Utility] = n.Id;
            return layout;
        }

        /// <summary>Outlet type → network id, and opening utility → network id, from the data (null for layouts built without data).</summary>
        Dictionary<string, string>? _outletNetwork, _utilityNetwork;

        /// <summary>The network an outlet is on (<c>outlets.json</c>).</summary>
        public string NetworkOf(OutletPlacementDef o) =>
            _outletNetwork != null && _outletNetwork.TryGetValue(o.Type, out string? n) ? n : BuildingState.OutletFluid(o.Type);

        /// <summary>The network an entry opening starts ("" for power).</summary>
        public string NetworkOf(EntryOpeningDef o) =>
            _utilityNetwork != null ? (_utilityNetwork.TryGetValue(o.Utility, out string? n) ? n : "") : BuildingState.UtilityFluid(o.Utility);

        /// <summary>The networks this layout has outlets or openings for, each once.</summary>
        public List<string> NetworkIds()
        {
            var ids = new List<string>();
            foreach (EntryOpeningDef o in _openings)
            {
                string n = NetworkOf(o);
                if (n.Length > 0 && !ids.Contains(n)) ids.Add(n);
            }
            foreach (OutletPlacementDef o in _outlets)
            {
                string n = NetworkOf(o);
                if (!ids.Contains(n)) ids.Add(n);
            }
            return ids;
        }

        /// <param name="roomTypes">Room type flags by id (null = the built-in defaults: shaft, stairwell, protected-room, electrical-room).</param>
        /// <param name="coreDrillTier">Lowest tier that may core-drill interior walls (default: never).</param>
        public BuildingLayout(BuildingLayoutDef def, IReadOnlyDictionary<string, RoomTypeInfo>? roomTypes = null, int coreDrillTier = int.MaxValue)
        {
            CoreDrillTier = coreDrillTier;
            Def = def;
            Id = def.Id;
            DisplayName = def.DisplayName;
            Tier = def.Tier;
            Width = def.Width;
            Height = def.Height;
            Floors = System.Math.Max(1, def.Floors);
            LowestFloor = def.LowestFloor;
            _roomTypes = roomTypes != null ? new Dictionary<string, RoomTypeInfo>(roomTypes) : DefaultRoomTypes();
            _rooms = new List<RoomDef>(def.Rooms);
            _openings = new List<EntryOpeningDef>(def.EntryOpenings);
            _outlets = new List<OutletPlacementDef>(def.Outlets);
            _equipment = new List<EquipmentPlacementDef>(def.Equipment);
            _doors = new List<DoorDef>(def.Doors);
            _roomIndex = new int[Floors][];
            for (int f = 0; f < Floors; f++)
            {
                _roomIndex[f] = new int[Width * Height];
                for (int i = 0; i < _roomIndex[f].Length; i++) _roomIndex[f][i] = -1;
            }
            for (int r = 0; r < _rooms.Count; r++)
            {
                RoomDef room = _rooms[r];
                int fi = room.Floor - LowestFloor;
                if (fi < 0 || fi >= Floors) continue;
                for (int x = room.X; x < room.X + room.Width; x++)
                    for (int y = room.Y; y < room.Y + room.Height; y++)
                        if (x >= 0 && y >= 0 && x < Width && y < Height) _roomIndex[fi][y * Width + x] = r;
            }
            foreach (DoorDef d in _doors)
            {
                var a = new TilePos(d.X, d.Y, d.Floor);
                _doorEdges.Add(EdgeKey(a, Neighbour(a, d.Side)));
            }
            foreach (DoorDef d in def.FireWalls)
            {
                var a = new TilePos(d.X, d.Y, d.Floor);
                _fireEdges.Add(EdgeKey(a, Neighbour(a, d.Side)));
            }
            foreach (TileDef c in def.Columns) _columns.Add(c.Y * Width + c.X);
        }

        /// <summary>Room type flags used when no data is given.</summary>
        public static Dictionary<string, RoomTypeInfo> DefaultRoomTypes() => new Dictionary<string, RoomTypeInfo>
        {
            ["shaft"] = new RoomTypeInfo(true, false, false, false),
            ["stairwell"] = new RoomTypeInfo(false, true, false, false),
            ["protected-room"] = new RoomTypeInfo(false, false, true, false),
            ["electrical-room"] = new RoomTypeInfo(false, false, false, true),
        };

        /// <summary>Room type flags from <c>buildings.json</c>.</summary>
        public static Dictionary<string, RoomTypeInfo> RoomTypesFrom(BuildingsDef b)
        {
            var d = new Dictionary<string, RoomTypeInfo>();
            foreach (RoomTypeDef t in b.RoomTypes) d[t.Id] = new RoomTypeInfo(t.Shaft, t.BlocksPipes, t.Protected, t.Electrical);
            return d;
        }

        RoomTypeInfo TypeOf(RoomDef room) => _roomTypes.TryGetValue(room.Type, out RoomTypeInfo i) ? i : default;

        /// <summary>The adjacent tile on a wall side (north, south, east, west), same floor.</summary>
        public static TilePos Neighbour(TilePos t, string side) => side switch
        {
            "north" => new TilePos(t.X, t.Y + 1, t.F),
            "south" => new TilePos(t.X, t.Y - 1, t.F),
            "east" => new TilePos(t.X + 1, t.Y, t.F),
            _ => new TilePos(t.X - 1, t.Y, t.F),
        };

        long EdgeKey(TilePos a, TilePos b)
        {
            int plane = Width * Height;
            int ia = (a.F - LowestFloor) * plane + a.Y * Width + a.X, ib = (b.F - LowestFloor) * plane + b.Y * Width + b.X;
            return ia < ib ? ((long)ia << 32) | (uint)ib : ((long)ib << 32) | (uint)ia;
        }

        /// <summary>True when a door joins two adjacent tiles.</summary>
        public bool HasDoor(TilePos a, TilePos b) => _doorEdges.Contains(EdgeKey(a, b));

        /// <summary>True when a fire wall runs between two adjacent tiles.</summary>
        public bool IsFireWall(TilePos a, TilePos b) => _fireEdges.Contains(EdgeKey(a, b));

        public bool InFootprint(TilePos t) => t.X >= 0 && t.Y >= 0 && t.X < Width && t.Y < Height && t.F >= LowestFloor && t.F <= TopFloor;

        /// <summary>Index of the room containing the tile, or −1.</summary>
        public int RoomIndexAt(TilePos t) => InFootprint(t) ? _roomIndex[t.F - LowestFloor][t.Y * Width + t.X] : -1;

        /// <summary>True when the tile is inside a room on its floor.</summary>
        public bool IsInside(TilePos t) => RoomIndexAt(t) >= 0;

        /// <summary>True when a structural column stands on the tile (every floor).</summary>
        public bool IsColumn(TilePos t) => _columns.Contains(t.Y * Width + t.X);

        /// <summary>True when the tile is in a shaft (risers pass between floors here).</summary>
        public bool IsShaft(TilePos t)
        {
            int r = RoomIndexAt(t);
            return r >= 0 && TypeOf(_rooms[r]).Shaft;
        }

        /// <summary>True when the tile is in an electrical room.</summary>
        public bool IsElectricalRoom(TilePos t)
        {
            int r = RoomIndexAt(t);
            return r >= 0 && TypeOf(_rooms[r]).Electrical;
        }

        /// <summary>True when pipes may run on the tile: inside, no column, not a stairwell or protected room (<c>09-codes.md</c> §Constraints).</summary>
        public bool PipesAllowed(TilePos t)
        {
            int r = RoomIndexAt(t);
            if (r < 0 || IsColumn(t)) return false;
            RoomTypeInfo info = TypeOf(_rooms[r]);
            return !info.BlocksPipes && !info.Protected;
        }

        /// <summary>What separates two adjacent tiles on one floor.</summary>
        public WallKind WallBetween(TilePos a, TilePos b)
        {
            int ra = RoomIndexAt(a), rb = RoomIndexAt(b);
            if (ra < 0 || rb < 0) return WallKind.Exterior;
            if (ra == rb) return WallKind.None;
            if (HasDoor(a, b)) return WallKind.Door;
            if (TypeOf(_rooms[ra]).Shaft || TypeOf(_rooms[rb]).Shaft) return WallKind.Shaft;
            return WallKind.Interior;
        }

        /// <summary>True when the building's tier may core-drill interior walls (<c>09-codes.md</c> §Constraints).</summary>
        public int CoreDrillTier { get; }

        /// <summary>
        /// True when a pipe may step between two tiles: horizontally within a room, through a door, into a shaft, or
        /// through a core-drilled interior wall (tier 2+); vertically only inside a shaft.
        /// </summary>
        public bool CanPass(TilePos a, TilePos b)
        {
            if (!PipesAllowed(a) || !PipesAllowed(b)) return false;
            if (a.IsVerticalNeighbour(b)) return IsShaft(a) && IsShaft(b);
            if (!a.IsAdjacentTo(b)) return false;
            WallKind w = WallBetween(a, b);
            return w == WallKind.None || w == WallKind.Door || w == WallKind.Shaft || (w == WallKind.Interior && Tier >= CoreDrillTier);
        }

        /// <summary>True when stepping from a to b goes through a wall that must be core-drilled.</summary>
        public bool NeedsCoreDrill(TilePos a, TilePos b) => a.IsAdjacentTo(b) && WallBetween(a, b) == WallKind.Interior;

        /// <summary>The room an outlet sits in, or null.</summary>
        public RoomDef? RoomOf(TilePos t)
        {
            int i = RoomIndexAt(t);
            return i >= 0 ? _rooms[i] : null;
        }

        public EntryOpeningDef? FindOpening(string id) => _openings.Find(o => o.Id == id);
        public OutletPlacementDef? FindOutlet(string id) => _outlets.Find(o => o.Id == id);

        /// <summary>The entry opening of a utility (air, water, power, …), or null.</summary>
        public EntryOpeningDef? OpeningFor(string utility) => _openings.Find(o => o.Utility == utility);

        /// <summary>Tile of an outlet.</summary>
        public static TilePos PosOf(OutletPlacementDef o) => new TilePos(o.X, o.Y, o.Floor);

        /// <summary>Tile of an entry opening.</summary>
        public static TilePos PosOf(EntryOpeningDef o) => new TilePos(o.X, o.Y, o.Floor);

        /// <summary>True when the tile has an outside wall on the given side (exterior openings go there).</summary>
        public bool HasOutsideWall(TilePos t, string side) => IsInside(t) && !IsInside(Neighbour(t, side));

        /// <summary>The panel's tile (at the power connection), if the layout has one.</summary>
        public TilePos? PanelTile
        {
            get
            {
                EntryOpeningDef? p = OpeningFor("power");
                return p != null ? PosOf(p) : (TilePos?)null;
            }
        }
    }
}
