using System.Collections.Generic;
using HeadLoss.Core.Data.Defs;

namespace HeadLoss.Game.Buildings
{
    /// <summary>
    /// The generated (or hand-made) part of a building: footprint, rooms, entry openings and outlets.
    /// Never saved; rebuilt from data or seed (<c>01-architecture.md</c> §State model).
    /// </summary>
    public sealed class BuildingLayout
    {
        public string Id { get; }
        public string DisplayName { get; }
        public int Tier { get; }
        public int Width { get; }
        public int Height { get; }
        public IReadOnlyList<RoomDef> Rooms => _rooms;
        public IReadOnlyList<EntryOpeningDef> EntryOpenings => _openings;
        public IReadOnlyList<OutletPlacementDef> Outlets => _outlets;
        /// <summary>Equipment the layout starts with (pre-placed at openings).</summary>
        public IReadOnlyList<EquipmentPlacementDef> InitialEquipment => _equipment;
        public IReadOnlyList<DoorDef> Doors => _doors;
        public IReadOnlyList<WindowDef> Windows => Def.Windows;
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
        readonly int[] _roomIndex;
        /// <summary>Tile edges with a door: key of the tile pair.</summary>
        readonly HashSet<long> _doorEdges = new HashSet<long>();

        public BuildingLayout(BuildingLayoutDef def)
        {
            Def = def;
            Id = def.Id;
            DisplayName = def.DisplayName;
            Tier = def.Tier;
            Width = def.Width;
            Height = def.Height;
            _rooms = new List<RoomDef>(def.Rooms);
            _openings = new List<EntryOpeningDef>(def.EntryOpenings);
            _outlets = new List<OutletPlacementDef>(def.Outlets);
            _equipment = new List<EquipmentPlacementDef>(def.Equipment);
            _doors = new List<DoorDef>(def.Doors);
            _roomIndex = new int[Width * Height];
            for (int i = 0; i < _roomIndex.Length; i++) _roomIndex[i] = -1;
            for (int r = 0; r < _rooms.Count; r++)
            {
                RoomDef room = _rooms[r];
                for (int x = room.X; x < room.X + room.Width; x++)
                    for (int y = room.Y; y < room.Y + room.Height; y++)
                        _roomIndex[y * Width + x] = r;
            }
            foreach (DoorDef d in _doors)
            {
                var a = new TilePos(d.X, d.Y);
                _doorEdges.Add(EdgeKey(a, Neighbour(a, d.Side)));
            }
        }

        /// <summary>The adjacent tile on a wall side (north, south, east, west).</summary>
        public static TilePos Neighbour(TilePos t, string side) => side switch
        {
            "north" => new TilePos(t.X, t.Y + 1),
            "south" => new TilePos(t.X, t.Y - 1),
            "east" => new TilePos(t.X + 1, t.Y),
            _ => new TilePos(t.X - 1, t.Y),
        };

        long EdgeKey(TilePos a, TilePos b)
        {
            int ia = a.Y * Width + a.X, ib = b.Y * Width + b.X;
            return ia < ib ? ((long)ia << 32) | (uint)ib : ((long)ib << 32) | (uint)ia;
        }

        /// <summary>True when a door joins two adjacent tiles.</summary>
        public bool HasDoor(TilePos a, TilePos b) => _doorEdges.Contains(EdgeKey(a, b));

        public bool InFootprint(TilePos t) => t.X >= 0 && t.Y >= 0 && t.X < Width && t.Y < Height;

        /// <summary>Index of the room containing the tile, or −1.</summary>
        public int RoomIndexAt(TilePos t) => InFootprint(t) ? _roomIndex[t.Y * Width + t.X] : -1;

        /// <summary>True when the tile is inside a room (ducts may run there).</summary>
        public bool IsInside(TilePos t) => RoomIndexAt(t) >= 0;

        /// <summary>True when a duct may step between two adjacent inside tiles: within a room, or through a door.</summary>
        public bool CanPass(TilePos a, TilePos b) => IsInside(a) && IsInside(b) && (RoomIndexAt(a) == RoomIndexAt(b) || HasDoor(a, b));

        /// <summary>The room an outlet sits in, or null.</summary>
        public RoomDef? RoomOf(TilePos t)
        {
            int i = RoomIndexAt(t);
            return i >= 0 ? _rooms[i] : null;
        }

        public EntryOpeningDef? FindOpening(string id) => _openings.Find(o => o.Id == id);
        public OutletPlacementDef? FindOutlet(string id) => _outlets.Find(o => o.Id == id);
    }
}
