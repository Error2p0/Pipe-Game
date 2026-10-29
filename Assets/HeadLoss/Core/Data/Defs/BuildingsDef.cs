using System.Collections.Generic;

namespace HeadLoss.Core.Data.Defs
{
    /// <summary><c>buildings.json</c>: tiers, room types, building types and generation rules (<c>04-buildings.md</c>).</summary>
    public sealed class BuildingsDef
    {
        public List<BuildingTierDef> Tiers = new List<BuildingTierDef>();
        public List<RoomTypeDef> RoomTypes = new List<RoomTypeDef>();
        public List<BuildingTypeDef> Types = new List<BuildingTypeDef>();
        public BuildingGenerationDef Generation = new BuildingGenerationDef();
    }

    public sealed class BuildingTierDef
    {
        public int Tier;
        /// <summary>Footprint range [width, height], tiles (1 m).</summary>
        public List<int> FootprintMin = new List<int>();
        public List<int> FootprintMax = new List<int>();
        public int FloorsMin = 1;
        public int FloorsMax = 1;
        /// <summary>Fluids the tier's buildings need (<c>03-city.md</c> §Building tiers).</summary>
        public List<string> Fluids = new List<string>();
        /// <summary>Source equipment the solvability check may use, smallest first (<c>13-balancing.md</c>).</summary>
        public List<string> ReferenceEquipment = new List<string>();
        /// <summary>The generator builds this tier now (later tiers arrive with their phase).</summary>
        public bool Available;
        /// <summary>Shown on a plot whose tier is not available yet.</summary>
        public string UnavailableNote = "";
    }

    public sealed class RoomTypeDef
    {
        public string Id = "";
        public string DisplayName = "";
        /// <summary>Fixed number of people in the room.</summary>
        public double Occupants;
        /// <summary>People per m² of floor, added to <see cref="Occupants"/>.</summary>
        public double OccupantsPerM2;
        /// <summary>Machine heat load, W.</summary>
        public double MachinesW;
        /// <summary>Hall-like room other rooms prefer to open into.</summary>
        public bool Hub;
        /// <summary>Pipes cannot cross the room (server rooms, archives).</summary>
        public bool Protected;
        /// <summary>Relative humidity for the condensation check, 0–1.</summary>
        public double RelativeHumidity;
        public List<RoomOutletRuleDef> Outlets = new List<RoomOutletRuleDef>();
    }

    /// <summary>How many outlets of a type a room gets: max(min, ceil(area / areaPerOutlet)).</summary>
    public sealed class RoomOutletRuleDef
    {
        /// <summary>Outlet type id from <c>outlets.json</c>.</summary>
        public string Type = "";
        public int Min = 1;
        /// <summary>Floor area per outlet, m² (0 = always <see cref="Min"/>).</summary>
        public double AreaPerOutlet;
        /// <summary>Lowest building tier the rule applies to.</summary>
        public int FromTier = 1;
    }

    public sealed class BuildingTypeDef
    {
        public string Id = "";
        public string DisplayName = "";
        public int Tier;
        /// <summary>Weight per district type when rolling a plot's type; a type with no weights is never rolled.</summary>
        public Dictionary<string, double> Districts = new Dictionary<string, double>();
        /// <summary>Rooms per floor; the generator splits the footprint by their area weights.</summary>
        public List<ProgramRoomDef> Program = new List<ProgramRoomDef>();
        /// <summary>Footprint override [width, height] (empty = the tier's range).</summary>
        public List<int> FootprintMin = new List<int>();
        public List<int> FootprintMax = new List<int>();
        /// <summary>Equipment the building starts with at its air intake (the starter's hand crank).</summary>
        public List<string> InitialEquipment = new List<string>();
        /// <summary>Hand-crank speed the reference must meet every minimum at (0 = no crank reference).</summary>
        public double ReferenceCrankSpeed;
        /// <summary>Farthest outlet from the intake, tiles (Manhattan; 0 = no limit).</summary>
        public int MaxIntakeDistance;
        /// <summary>Codes in force beyond the tier's base codes.</summary>
        public List<string> Codes = new List<string>();
    }

    public sealed class ProgramRoomDef
    {
        /// <summary>Room type id.</summary>
        public string Room = "";
        /// <summary>Count range, inclusive.</summary>
        public int CountMin = 1;
        public int CountMax = 1;
        /// <summary>Share of the floor area each room of this kind gets (relative).</summary>
        public double AreaWeight = 1;
        /// <summary>Outlets per room of this kind (0 = the room type's rules).</summary>
        public int Outlets;
    }

    public sealed class BuildingGenerationDef
    {
        /// <summary>Seeded variation of outlet requirements, ± fraction.</summary>
        public double RequirementVariation;
        /// <summary>Requirements round to these steps per display unit (m3/h, bar, mbar).</summary>
        public Dictionary<string, double> RoundingSteps = new Dictionary<string, double>();
        /// <summary>Smallest room side, tiles.</summary>
        public int MinRoomSide;
        /// <summary>Chance the intake sits on the street side of the building (else anywhere on the facade).</summary>
        public double StreetSideChance;
        /// <summary>One window every this many exterior wall tiles.</summary>
        public int WindowSpacing;
        /// <summary>Glazed area of one window, m².</summary>
        public double WindowAreaM2;
        /// <summary>Solvability nudges before the plot drops a tier (<c>04-buildings.md</c> step 11).</summary>
        public int SolvabilityAttempts;
        /// <summary>Of those, how many move the openings before the rooms are regenerated.</summary>
        public int OpeningNudges;
        /// <summary>Fluids the generator builds outlets and openings for in this version (water arrives in Phase 4).</summary>
        public List<string> PhaseFluids = new List<string>();
    }
}
