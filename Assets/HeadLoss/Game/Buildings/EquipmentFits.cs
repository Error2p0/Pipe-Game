using System.Collections.Generic;
using HeadLoss.Core.Data;
using HeadLoss.Core.Data.Defs;
using HeadLoss.Game.Session;

namespace HeadLoss.Game.Buildings
{
    /// <summary>
    /// Where equipment may go (<c>04-buildings.md</c> §Layers, §Entry openings): source equipment matches its opening's
    /// utility and takes floor space next to it (footprints cannot overlap furniture, outlets, doors, columns or other
    /// equipment); inline parts match their network's fluid.
    /// </summary>
    public static class EquipmentFits
    {
        /// <summary>The category of source equipment an opening of a utility takes.</summary>
        public static string CategoryFor(string utility) => utility switch
        {
            "air" => "air-mover",
            "water" => "water-mover",
            "gas" => "gas-supply",
            "power" => "panel",
            _ => "",
        };

        /// <summary>True when the equipment belongs at this kind of opening.</summary>
        public static bool Opening(DataRepository data, EntryOpeningDef opening, EquipmentDef def) =>
            def.Placement == "intake" && def.Category == CategoryFor(opening.Utility);

        /// <summary>
        /// True when an inline part (fitting or equipment) works on this network. An item's <c>fluids</c> list names fluids (any
        /// network carrying one fits, so an air part fits the exhaust too) or network ids.
        /// </summary>
        public static bool InlinePart(DataRepository data, string partId, string fluid)
        {
            EquipmentDef? e = data.FindEquipment(partId);
            if (e != null) return e.Placement == "inline" && Carries(data, e.Fluids, fluid);
            FittingDef? f = data.FindFitting(partId);
            if (f == null) return false;
            return f.Fluids.Count == 0 ? data.FluidIdOf(fluid) == "air" : Carries(data, f.Fluids, fluid);
        }

        /// <summary>True when a <c>fluids</c> list names this network or the fluid it carries.</summary>
        public static bool Carries(DataRepository data, System.Collections.Generic.List<string> fluids, string network) =>
            fluids.Contains(network) || fluids.Contains(data.FluidIdOf(network));

        /// <summary>Unit step from a facade opening into the building.</summary>
        public static (int dx, int dy) Inward(string facing) => facing switch
        {
            "north" => (0, -1),
            "south" => (0, 1),
            "east" => (-1, 0),
            _ => (1, 0),
        };

        /// <summary>
        /// The floor tiles a piece of source equipment would take at an opening: its width along the wall and its depth
        /// inward (or turned when that does not fit), shifted along the wall until the opening's tile is covered and every
        /// tile is free. Empty for equipment without a floor footprint; null when it does not fit.
        /// </summary>
        public static List<TilePos>? Footprint(DataRepository data, BuildingLayout layout, BuildingState state, EntryOpeningDef opening, EquipmentDef def)
        {
            if (def.Footprint.Layer != "floor") return new List<TilePos>();
            var blocked = BlockedTiles(data, layout, state, opening.Id);
            TilePos at = BuildingLayout.PosOf(opening);
            int room = layout.RoomIndexAt(at);
            (int ix, int iy) = Inward(opening.Facing);
            (int ax, int ay) = (iy != 0 ? 1 : 0, ix != 0 ? 1 : 0);  // along the wall
            foreach ((int along, int depth) in new[] { (def.Footprint.Width, def.Footprint.Height), (def.Footprint.Height, def.Footprint.Width) })
            {
                for (int shift = 0; shift < along; shift++)
                {
                    var tiles = new List<TilePos>();
                    bool ok = true;
                    for (int a = 0; a < along && ok; a++)
                        for (int d = 0; d < depth && ok; d++)
                        {
                            var t = new TilePos(at.X + (a - shift) * ax + d * ix, at.Y + (a - shift) * ay + d * iy, at.F);
                            if (layout.RoomIndexAt(t) != room || layout.IsColumn(t) || blocked.Contains(t)) ok = false;
                            else tiles.Add(t);
                        }
                    if (ok) return tiles;
                }
                if (def.Footprint.Width == def.Footprint.Height) break;
            }
            return null;
        }

        /// <summary>Tiles source equipment may not use: outlets, door tiles, furniture and other equipment's footprints.</summary>
        static HashSet<TilePos> BlockedTiles(DataRepository data, BuildingLayout layout, BuildingState state, string exceptOpening)
        {
            var b = new HashSet<TilePos>();
            foreach (OutletPlacementDef o in layout.Outlets) b.Add(BuildingLayout.PosOf(o));
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
                if (e.OpeningId == exceptOpening) continue;
                EntryOpeningDef? o = layout.FindOpening(e.OpeningId);
                EquipmentDef? def = data.FindEquipment(e.EquipmentId);
                if (o == null || def == null) continue;
                List<TilePos>? fp = Footprint(data, layout, WithoutOthers(state, e.OpeningId), o, def);
                if (fp != null) foreach (TilePos t in fp) b.Add(t);
                else b.Add(BuildingLayout.PosOf(o));
            }
            // Other openings' own tiles stay free for their equipment.
            foreach (EntryOpeningDef o in layout.EntryOpenings) if (o.Id != exceptOpening) b.Add(BuildingLayout.PosOf(o));
            return b;
        }

        /// <summary>A state holding only one opening's equipment (so footprints are resolved one at a time, in order).</summary>
        static BuildingState WithoutOthers(BuildingState s, string keepOpening)
        {
            var c = new BuildingState { BuildingId = s.BuildingId };
            EquipmentState? keep = s.EquipmentAt(keepOpening);
            if (keep != null) c.Equipment.Add(keep);
            return c;
        }

        /// <summary>Checks floor space for equipment at an opening in the session's design.</summary>
        public static DrawError FloorSpace(BuildingSession s, string openingId, EquipmentDef def, out List<TilePos> tiles)
        {
            EntryOpeningDef? o = s.Layout.FindOpening(openingId);
            tiles = new List<TilePos>();
            if (o == null) return DrawError.OutsideBuilding;
            List<TilePos>? fp = Footprint(s.Data, s.Layout, s.State, o, def);
            if (fp == null) return DrawError.NoFloorSpace;
            tiles = fp;
            return DrawError.None;
        }
    }
}
