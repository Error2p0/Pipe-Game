using System;
using System.Collections.Generic;

namespace HeadLoss.Game.City
{
    /// <summary>What a city tile holds. Several can combine (a rail line crossing a road).</summary>
    [Flags]
    public enum CityTile : ushort
    {
        None = 0,
        /// <summary>Edge band outside the city (no plots).</summary>
        Countryside = 1,
        Road = 2,
        /// <summary>Part of a main road (always also <see cref="Road"/>).</summary>
        MainRoad = 4,
        Rail = 8,
        Park = 16,
        Protected = 32,
        Historic = 64,
        Facility = 128,
        Plot = 256,
    }

    public enum DistrictKind { Residential, Commercial, Industrial }

    public sealed class District
    {
        public int Index;
        public DistrictKind Kind;
        /// <summary>Voronoi seed point, city tiles.</summary>
        public int SeedX;
        public int SeedY;
        /// <summary>The district nearest the centre (historic buildings stand here).</summary>
        public bool Oldest;

        public static string KindId(DistrictKind k) => k switch
        {
            DistrictKind.Residential => "residential",
            DistrictKind.Commercial => "commercial",
            _ => "industrial",
        };
    }

    /// <summary>A central facility on the map (<c>03-city.md</c> §Central facilities).</summary>
    public sealed class FacilityPlacement
    {
        public string Id = "";
        /// <summary>Facility type id from <c>city.json</c>.</summary>
        public string TypeId = "";
        public string Utility = "";
        public int X;
        public int Y;
        public int Width;
        public int Height;
        public int District;
    }

    /// <summary>A city area holding one building (<c>00-index.md</c> glossary).</summary>
    public sealed class Plot
    {
        /// <summary>Plot id; also the id of its building (lowercase kebab).</summary>
        public string Id = "";
        public int X;
        public int Y;
        public int Width;
        public int Height;
        public int Tier;
        /// <summary>Building type id from <c>buildings.json</c>.</summary>
        public string BuildingType = "";
        /// <summary><c>hash(citySeed, plotId)</c>; the starter house may use a later attempt (starter guarantee).</summary>
        public ulong BuildingSeed;
        public int District;
        /// <summary>Plot side that faces a road: north, south, east or west.</summary>
        public string StreetSide = "south";
        /// <summary>The starter house (starter guarantee).</summary>
        public bool Starter;

        public double CentreX => X + Width * 0.5;
        public double CentreY => Y + Height * 0.5;
    }

    /// <summary>
    /// A generated city (<c>03-city.md</c>): tile map, districts, facilities and plots. Never saved; regenerated
    /// from the seed (<c>01-architecture.md</c> §State model). Tile coordinates, origin bottom-left.
    /// </summary>
    public sealed class CityLayout
    {
        public ulong Seed;
        public string Name = "";
        public int Width;
        public int Height;
        /// <summary>One city tile, m.</summary>
        public double TileMetres;
        public int EdgeBand;
        public CityTile[] Tiles = Array.Empty<CityTile>();
        /// <summary>District index per tile (−1 in the countryside).</summary>
        public int[] DistrictOf = Array.Empty<int>();
        /// <summary>Plot index per tile (−1 = none).</summary>
        public int[] PlotOf = Array.Empty<int>();
        public readonly List<District> Districts = new List<District>();
        public readonly List<FacilityPlacement> Facilities = new List<FacilityPlacement>();
        public readonly List<Plot> Plots = new List<Plot>();
        /// <summary>City modifier ids (the first city has none).</summary>
        public readonly List<string> Modifiers = new List<string>();

        public double CentreX => Width * 0.5;
        public double CentreY => Height * 0.5;

        public bool InMap(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;
        public bool InCity(int x, int y) => x >= EdgeBand && y >= EdgeBand && x < Width - EdgeBand && y < Height - EdgeBand;
        public int Index(int x, int y) => y * Width + x;
        public CityTile At(int x, int y) => InMap(x, y) ? Tiles[Index(x, y)] : CityTile.Countryside;
        public bool Has(int x, int y, CityTile flag) => (At(x, y) & flag) != 0;
        public int DistrictAt(int x, int y) => InMap(x, y) ? DistrictOf[Index(x, y)] : -1;

        public Plot? PlotAt(int x, int y)
        {
            if (!InMap(x, y)) return null;
            int i = PlotOf[Index(x, y)];
            return i >= 0 ? Plots[i] : null;
        }

        public Plot? FindPlot(string id) => Plots.Find(p => p.Id == id);
        public Plot? Starter => Plots.Find(p => p.Starter);

        /// <summary>Distance from a point to the city centre, tiles.</summary>
        public double DistanceToCentre(double x, double y)
        {
            double dx = x - CentreX, dy = y - CentreY;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>Distance between a plot and a facility (centre to centre), m.</summary>
        public double DistanceMetres(Plot p, FacilityPlacement f)
        {
            double dx = p.CentreX - (f.X + f.Width * 0.5), dy = p.CentreY - (f.Y + f.Height * 0.5);
            return Math.Sqrt(dx * dx + dy * dy) * TileMetres;
        }
    }
}
