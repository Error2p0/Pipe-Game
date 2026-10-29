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
    }

    /// <summary>A window in the exterior wall on one side of a tile.</summary>
    public sealed class WindowDef
    {
        public int X;
        public int Y;
        /// <summary>Wall side of the tile: north, south, east or west.</summary>
        public string Side = "";
    }

    /// <summary>A door in the wall on one side of a tile.</summary>
    public sealed class DoorDef
    {
        public int X;
        public int Y;
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
