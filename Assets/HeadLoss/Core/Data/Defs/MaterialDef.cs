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
    }

    /// <summary>Entry in <c>pipes.json</c>: one size family.</summary>
    public sealed class PipeFamilyDef
    {
        public string Id = "";
        public string DisplayName = "";
        /// <summary>duct or pipe (matches <see cref="MaterialDef.Kind"/>).</summary>
        public string Kind = "";
        public List<PipeSizeDef> Sizes = new List<PipeSizeDef>();
    }

    public sealed class PipeSizeDef
    {
        /// <summary>Nominal diameter (ducts: internal; pipes: outer), mm.</summary>
        public double DiameterMm;
        public int CapacityUnits;
    }
}
