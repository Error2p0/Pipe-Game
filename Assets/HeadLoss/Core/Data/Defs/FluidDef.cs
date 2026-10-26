using System.Collections.Generic;

namespace HeadLoss.Core.Data.Defs
{
    /// <summary>Entry in <c>fluids.json</c>.</summary>
    public sealed class FluidDef
    {
        public string Id = "";
        public string DisplayName = "";
        /// <summary>Plain-view line colour (hex).</summary>
        public string Colour = "";
        /// <summary>Line pattern so colour is never the only cue: solid, dashed, dotted, double.</summary>
        public string LinePattern = "";
        public bool Risky;
        /// <summary>True for gases whose density follows the ideal gas law in the solver.</summary>
        public bool IdealGas;
        /// <summary>Molar mass, kg/mol.</summary>
        public double MolarMass;
        /// <summary>Ratio of specific heats.</summary>
        public double Gamma;
        /// <summary>Property table path relative to the data folder, e.g. fluids/air.json. Empty = no table yet.</summary>
        public string Table = "";
        public List<string> Hazards = new List<string>();
        public string Notes = "";
    }

    /// <summary>A fluid property table in the fluids folder. Value arrays are indexed [pressure][temperature].</summary>
    public sealed class FluidTableDef
    {
        public string Fluid = "";
        /// <summary>Where the numbers come from (CoolProp version, or hand-checked).</summary>
        public string Source = "";
        /// <summary>Temperatures, °C, ascending.</summary>
        public List<double> TemperaturesC = new List<double>();
        /// <summary>Absolute pressures, Pa, ascending.</summary>
        public List<double> PressuresPa = new List<double>();
        /// <summary>Density, kg/m³.</summary>
        public List<List<double>> Density = new List<List<double>>();
        /// <summary>Dynamic viscosity, Pa·s.</summary>
        public List<List<double>> Viscosity = new List<List<double>>();
        /// <summary>Specific heat c_p, J/(kg·K).</summary>
        public List<List<double>> SpecificHeat = new List<List<double>>();
        /// <summary>Thermal conductivity, W/(m·K).</summary>
        public List<List<double>> Conductivity = new List<List<double>>();
        /// <summary>Molar mass, kg/mol.</summary>
        public double MolarMass;
        public double Gamma;
        /// <summary>Speed of sound at 20 °C and 1 atm, m/s.</summary>
        public double SpeedOfSound20C;
    }
}
