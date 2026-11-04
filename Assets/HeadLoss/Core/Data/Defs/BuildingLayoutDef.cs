using System.Collections.Generic;

namespace HeadLoss.Core.Data.Defs
{
    /// <summary>
    /// A building layout: hand-made (the handmade data folder, test fixtures) or generated from a plot's seed.
    /// Tile coordinates, 1 m tiles, origin bottom-left.
    /// </summary>
    public sealed class BuildingLayoutDef
    {
        public string Id = "";
        public string DisplayName = "";
        public int Tier;
        /// <summary>Building type id from <c>buildings.json</c> ("" for hand-made layouts).</summary>
        public string BuildingType = "";
        /// <summary>Side of the building that faces the street: north, south, east or west.</summary>
        public string StreetSide = "south";
        /// <summary>Footprint width, tiles.</summary>
        public int Width;
        /// <summary>Footprint height, tiles.</summary>
        public int Height;
        /// <summary>Number of floors (the footprint is the same on every floor).</summary>
        public int Floors = 1;
        /// <summary>Lowest floor index (−1 with a basement, else 0).</summary>
        public int LowestFloor;
        /// <summary>Structural columns: 1×1 tiles that block every layer on every floor.</summary>
        public List<TileDef> Columns = new List<TileDef>();
        /// <summary>Fire walls: wall edges between fire compartments (ducts crossing need a fire damper).</summary>
        public List<DoorDef> FireWalls = new List<DoorDef>();
        /// <summary>Generated furniture: blocks equipment on the floor layer, not pipes.</summary>
        public List<FurnitureDef> Furniture = new List<FurnitureDef>();
        public List<RoomDef> Rooms = new List<RoomDef>();
        public List<EntryOpeningDef> EntryOpenings = new List<EntryOpeningDef>();
        public List<OutletPlacementDef> Outlets = new List<OutletPlacementDef>();
        public List<EquipmentPlacementDef> Equipment = new List<EquipmentPlacementDef>();
        /// <summary>Doors between rooms (ducts cross interior walls only here).</summary>
        public List<DoorDef> Doors = new List<DoorDef>();
        /// <summary>Windows in the exterior walls (solar gain).</summary>
        public List<WindowDef> Windows = new List<WindowDef>();
        /// <summary>Codes in force (ids from <c>codes.json</c>; empty = the tier's base codes).</summary>
        public List<string> Codes = new List<string>();
        /// <summary>Hand-crank speed the reference must meet every minimum at (0 = no crank reference).</summary>
        public double ReferenceCrankSpeed;
        /// <summary>Ceiling capacity units per tile in this building (0 = the data default; the old-buildings modifier lowers it).</summary>
        public int CeilingCapacity;
    }

    /// <summary>A tile (columns).</summary>
    public sealed class TileDef
    {
        public int X;
        public int Y;
    }

    /// <summary>A piece of furniture on the floor layer.</summary>
    public sealed class FurnitureDef
    {
        public int X;
        public int Y;
        public int Width = 1;
        public int Height = 1;
        public int Floor;
        /// <summary>bed, table, sofa, counter, shelf, desk (drawing only).</summary>
        public string Kind = "";
    }

    /// <summary>A window in the exterior wall on one side of a tile.</summary>
    public sealed class WindowDef
    {
        public int X;
        public int Y;
        public int Floor;
        /// <summary>Wall side of the tile: north, south, east or west.</summary>
        public string Side = "";
    }

    /// <summary>A door (or fire-wall edge) in the wall on one side of a tile.</summary>
    public sealed class DoorDef
    {
        public int X;
        public int Y;
        public int Floor;
        /// <summary>Wall side of the tile: north, south, east or west.</summary>
        public string Side = "";
    }

    public sealed class RoomDef
    {
        public string Id = "";
        /// <summary>Room type (bedroom, living, kitchen, …).</summary>
        public string Type = "";
        public int X;
        public int Y;
        public int Width;
        public int Height;
        public int Floor;
        /// <summary>People in the room (heat and CO₂ in the room grid).</summary>
        public double Occupants;
        /// <summary>Machine heat load, W.</summary>
        public double MachinesW;
    }

    public sealed class EntryOpeningDef
    {
        public string Id = "";
        /// <summary>air, water, gas, heating or power.</summary>
        public string Utility = "";
        /// <summary>Tile just inside the outside wall.</summary>
        public int X;
        public int Y;
        public int Floor;
        /// <summary>Wall side: north, south, east or west.</summary>
        public string Facing = "";
    }

    public sealed class OutletPlacementDef
    {
        public string Id = "";
        /// <summary>Outlet type id from <c>outlets.json</c>.</summary>
        public string Type = "";
        public int X;
        public int Y;
        public int Floor;
        /// <summary>Requirement in the outlet's display unit; 0 = the tier's value from <c>outlets.json</c>.</summary>
        public double Min;
        public double Ideal;
        /// <summary>Code maximum in the display unit; 0 = the tier's value.</summary>
        public double Max;
        /// <summary>Direction the grille throws air across the room: north, south, east or west ("" = toward the room centre).</summary>
        public string Facing = "";
    }

    public sealed class EquipmentPlacementDef
    {
        public string EquipmentId = "";
        /// <summary>Entry opening the equipment sits at (intake equipment).</summary>
        public string OpeningId = "";
        /// <summary>Whether it starts switched on.</summary>
        public bool On = true;
    }
}
