using System.Collections.Generic;

namespace HeadLoss.Core.Data.Defs
{
    /// <summary>Entry in <c>materials.json</c>.</summary>
    public sealed class MaterialDef
    {
        public string Id = "";
        public string DisplayName = "";
        /// <summary>duct or pipe.</summary>
        public string Kind = "";
        /// <summary>Absolute roughness, mm.</summary>
        public double RoughnessMm;
        /// <summary>Rated pressure p_ref at the reference diameter, Pa.</summary>
        public double RatedPressurePa;
        /// <summary>Reference diameter for the rating, mm (0 = rating independent of size).</summary>
        public double RatedReferenceDiameterMm;
        /// <summary>Overall heat transfer coefficient uninsulated, W/(m²·K) per outer area.</summary>
        public double UUninsulated;
        /// <summary>Pressure wave speed, m/s (0 for ducts).</summary>
        public double WaveSpeed;
        public List<string> Fluids = new List<string>();
        public int Tier;
        /// <summary>Price per metre factor on the duct or pipe base price.</summary>
        public double PriceFactor;
        /// <summary>Pipes: wall thickness per outer diameter (internal diameter = OD − 2 × wall).</summary>
        public List<PipeWallDef> Walls = new List<PipeWallDef>();

        /// <summary>Wall thickness for an outer diameter, mm (the nearest listed size; 0 for ducts).</summary>
        public double WallMm(double outerDiameterMm)
        {
            double best = 0, bestGap = double.MaxValue;
            foreach (PipeWallDef w in Walls)
            {
                double gap = System.Math.Abs(w.DiameterMm - outerDiameterMm);
                if (gap < bestGap) { bestGap = gap; best = w.WallMm; }
            }
            return best;
        }

        /// <summary>Internal diameter for a nominal size, mm (ducts: the nominal size is internal).</summary>
        public double InternalDiameterMm(double nominalMm) => Kind == "pipe" ? nominalMm - 2 * WallMm(nominalMm) : nominalMm;

        /// <summary>Rated pressure for a size, Pa: p_ref · D_ref / D (thin-wall hoop stress), or p_ref when no reference size.</summary>
        public double RatedPressure(double nominalMm) =>
            RatedReferenceDiameterMm > 0 ? RatedPressurePa * RatedReferenceDiameterMm / nominalMm : RatedPressurePa;
    }

    public sealed class PipeWallDef
    {
        /// <summary>Outer diameter, mm.</summary>
        public double DiameterMm;
        public double WallMm;
    }

    /// <summary>Entry in <c>pipes.json</c>: one size family.</summary>
    public sealed class PipeFamilyDef
    {
        public string Id = "";
        public string DisplayName = "";
        /// <summary>duct, pipe (buildings), main or cable (city).</summary>
        public string Kind = "";
        /// <summary>City mains and cables: the utility they carry (water, gas, heating, special-gas, power).</summary>
        public string Utility = "";
        /// <summary>Mains: capacity = internal area × this velocity, m/s.</summary>
        public double CapacityVelocity;
        /// <summary>Mains: roughness, mm.</summary>
        public double RoughnessMm;
        /// <summary>Mains: price per city tile at <see cref="PriceReferenceDiameterMm"/>, $ (× (D/D_ref)^exponent).</summary>
        public double PricePerTile;
        public double PriceReferenceDiameterMm;
        public List<PipeSizeDef> Sizes = new List<PipeSizeDef>();
        /// <summary>Gas mains: pressure classes (empty for other families).</summary>
        public List<PressureClassDef> PressureClasses = new List<PressureClassDef>();

        /// <summary>A pressure class by id, or null.</summary>
        public PressureClassDef? ClassById(string id) => PressureClasses.Find(c => c.Id == id);
    }

    /// <summary>A gas main pressure class (<c>07-equipment.md</c> §Sizes).</summary>
    public sealed class PressureClassDef
    {
        public string Id = "";
        public string DisplayName = "";
        /// <summary>Key in <c>city.facilityPressure</c>.</summary>
        public string FacilityPressureKey = "";
        /// <summary>Capacity = internal area × this velocity at line conditions, m/s.</summary>
        public double CapacityVelocity;
        /// <summary>Price factor on the family's price per tile.</summary>
        public double PriceFactor = 1;
        public int Tier = 1;
    }

    public sealed class PipeSizeDef
    {
        /// <summary>Nominal diameter (ducts: internal; pipes: outer), mm.</summary>
        public double DiameterMm;
        public int CapacityUnits;
        /// <summary>Tech tier that unlocks the size.</summary>
        public int Tier = 1;
        /// <summary>Cables: capacity, kW.</summary>
        public double CapacityKW;
        /// <summary>Cables: price per city tile, $ (mains are priced by diameter from the family).</summary>
        public double PricePerTile;
    }
}
