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

        readonly List<RoomDef> _rooms;
        readonly List<EntryOpeningDef> _openings;
        readonly List<OutletPlacementDef> _outlets;
        readonly List<EquipmentPlacementDef> _equipment;
        readonly int[] _roomIndex;

        public BuildingLayout(HandmadeBuildingDef def)
        {
            Id = def.Id;
            DisplayName = def.DisplayName;
            Tier = def.Tier;
            Width = def.Width;
            Height = def.Height;
            _rooms = new List<RoomDef>(def.Rooms);
            _openings = new List<EntryOpeningDef>(def.EntryOpenings);
            _outlets = new List<OutletPlacementDef>(def.Outlets);
            _equipment = new List<EquipmentPlacementDef>(def.Equipment);
            _roomIndex = new int[Width * Height];
            for (int i = 0; i < _roomIndex.Length; i++) _roomIndex[i] = -1;
            for (int r = 0; r < _rooms.Count; r++)
            {
                RoomDef room = _rooms[r];
                for (int x = room.X; x < room.X + room.Width; x++)
                    for (int y = room.Y; y < room.Y + room.Height; y++)
                        _roomIndex[y * Width + x] = r;
            }
        }

        public bool InFootprint(TilePos t) => t.X >= 0 && t.Y >= 0 && t.X < Width && t.Y < Height;

        /// <summary>Index of the room containing the tile, or −1.</summary>
        public int RoomIndexAt(TilePos t) => InFootprint(t) ? _roomIndex[t.Y * Width + t.X] : -1;

        /// <summary>True when the tile is inside a room (ducts may run there).</summary>
        public bool IsInside(TilePos t) => RoomIndexAt(t) >= 0;

        /// <summary>True when a duct may step between two adjacent inside tiles (walls block; doors arrive with multi-room houses).</summary>
        public bool CanPass(TilePos a, TilePos b) => IsInside(a) && IsInside(b) && RoomIndexAt(a) == RoomIndexAt(b);

        public EntryOpeningDef? FindOpening(string id) => _openings.Find(o => o.Id == id);
        public OutletPlacementDef? FindOutlet(string id) => _outlets.Find(o => o.Id == id);
    }
}
