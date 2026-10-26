using System.Collections.Generic;

namespace HeadLoss.Core.Data.Defs
{
    /// <summary>A hand-made building layout in the handmade data folder. Tile coordinates, 1 m tiles, origin bottom-left.</summary>
    public sealed class HandmadeBuildingDef
    {
        public string Id = "";
        public string DisplayName = "";
        public int Tier;
        /// <summary>Footprint width, tiles.</summary>
        public int Width;
        /// <summary>Footprint height, tiles.</summary>
        public int Height;
        public List<RoomDef> Rooms = new List<RoomDef>();
        public List<EntryOpeningDef> EntryOpenings = new List<EntryOpeningDef>();
        public List<OutletPlacementDef> Outlets = new List<OutletPlacementDef>();
        public List<EquipmentPlacementDef> Equipment = new List<EquipmentPlacementDef>();
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
