using System.Collections.Generic;

namespace HeadLoss.Core.Data.Defs
{
    /// <summary><c>city.json</c>: city generation rules (<c>03-city.md</c> §City generation). Distances in city tiles.</summary>
    public sealed class CityDef
    {
        /// <summary>City map size, tiles.</summary>
        public int Width;
        public int Height;
        /// <summary>One city tile, m.</summary>
        public double TileMetres;
        /// <summary>Countryside band along the map edge (no plots), tiles.</summary>
        public int EdgeBand;
        public CityDistrictsDef Districts = new CityDistrictsDef();
        public CityRoadsDef Roads = new CityRoadsDef();
        public CityRailDef Rail = new CityRailDef();
        public CityAreaRuleDef Parks = new CityAreaRuleDef();
        public CityAreaRuleDef ProtectedSites = new CityAreaRuleDef();
        public CityAreaRuleDef Historic = new CityAreaRuleDef();
        public List<FacilityTypeDef> Facilities = new List<FacilityTypeDef>();
        public CityPlotsDef Plots = new CityPlotsDef();
        public CityStarterDef Starter = new CityStarterDef();
        public List<CityModifierDef> Modifiers = new List<CityModifierDef>();
        /// <summary>Most modifiers a new city rolls (the first city rolls none).</summary>
        public int MaxModifiers;
        public CityNamesDef Names = new CityNamesDef();
    }

    public sealed class CityDistrictsDef
    {
        /// <summary>Voronoi seed points, inclusive range.</summary>
        public int SeedPointsMin;
        public int SeedPointsMax;
        /// <summary>District type weights (residential, commercial, industrial).</summary>
        public Dictionary<string, double> Weights = new Dictionary<string, double>();
        /// <summary>How strongly industrial leans to one edge and residential to the opposite one, 0–1.</summary>
        public double EdgeBias;
    }

    public sealed class CityRoadsDef
    {
        /// <summary>Main roads crossing the city, inclusive range.</summary>
        public int MainMin;
        public int MainMax;
        /// <summary>Main roads keep within this many tiles of their straight line.</summary>
        public int MainWander;
        /// <summary>Chance per tile that a main road steps one tile sideways.</summary>
        public double MainStepChance;
        /// <summary>Local road grid spacing per district, inclusive range, tiles.</summary>
        public int LocalSpacingMin;
        public int LocalSpacingMax;
        /// <summary>Each local road line is shifted by up to this many tiles.</summary>
        public int LocalJitter;
    }

    public sealed class CityRailDef
    {
        /// <summary>Chance that the city has a rail line.</summary>
        public double Chance;
    }

    /// <summary>How many of a city feature to place and how large (square sides, tiles, inclusive).</summary>
    public sealed class CityAreaRuleDef
    {
        public int Min;
        public int Max;
        public int SizeMin = 1;
        public int SizeMax = 1;
        /// <summary>District types the feature may sit in (empty = any).</summary>
        public List<string> Districts = new List<string>();
    }

    /// <summary>A central facility (<c>03-city.md</c> §Central facilities).</summary>
    public sealed class FacilityTypeDef
    {
        public string Id = "";
        public string DisplayName = "";
        /// <summary>Utility it supplies: water, gas, heating, special-gas or power.</summary>
        public string Utility = "";
        /// <summary>Footprint, tiles.</summary>
        public int Width = 1;
        public int Height = 1;
        /// <summary>one (per city) or per-district.</summary>
        public string Count = "";
        /// <summary>District type it must sit in ("" = any).</summary>
        public string District = "";
    }

    public sealed class CityPlotsDef
    {
        /// <summary>Plot count the generator aims for.</summary>
        public int Target;
        /// <summary>Plot footprint per tier, [width, height] tiles (rotated freely).</summary>
        public List<List<int>> TierSizes = new List<List<int>>();
        /// <summary>Tier weights per district type (index = tier − 1).</summary>
        public Dictionary<string, List<double>> TierMix = new Dictionary<string, List<double>>();
        /// <summary>Random spread of the centre-first fill order (0 = strictly nearest first).</summary>
        public double OrderJitter;
        /// <summary>Chance a frontage tile is left empty, so blocks are not packed solid.</summary>
        public double SkipChance;
    }

    /// <summary>Starter guarantee (<c>03-city.md</c> step 8).</summary>
    public sealed class CityStarterDef
    {
        /// <summary>Tier-1 plots guaranteed near the centre (one is the starter house).</summary>
        public int Count;
        /// <summary>Within this distance of the centre, tiles.</summary>
        public int Radius;
        /// <summary>Building type of the starter house.</summary>
        public string BuildingType = "";
        /// <summary>Most building seeds tried for a solvable starter house.</summary>
        public int MaxAttempts;
    }

    public sealed class CityModifierDef
    {
        public string Id = "";
        public string DisplayName = "";
        public string Description = "";
        /// <summary>Outside temperature shift, K.</summary>
        public double OutsideTemperatureShift;
        /// <summary>Heating requirement factor.</summary>
        public double HeatingFactor = 1;
        /// <summary>Ceiling capacity units per tile (0 = unchanged).</summary>
        public int CeilingCapacity;
    }

    /// <summary>City names are built from a seeded first part and an ending.</summary>
    public sealed class CityNamesDef
    {
        public List<string> First = new List<string>();
        public List<string> Last = new List<string>();
    }
}
