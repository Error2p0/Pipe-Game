using System;
using System.Collections.Generic;
using HeadLoss.Core.Data.Defs;

namespace HeadLoss.Sim.Fluids
{
    /// <summary>Fluid property lookup. Pressures absolute (Pa), temperatures in K.</summary>
    public interface IFluidProperties
    {
        /// <summary>Density, kg/m³.</summary>
        double Density(double pressureAbsPa, double temperatureK);
        /// <summary>Dynamic viscosity, Pa·s.</summary>
        double Viscosity(double pressureAbsPa, double temperatureK);
        /// <summary>Specific heat c_p, J/(kg·K).</summary>
        double SpecificHeat(double pressureAbsPa, double temperatureK);
        /// <summary>Thermal conductivity, W/(m·K).</summary>
        double Conductivity(double pressureAbsPa, double temperatureK);
        /// <summary>Speed of sound, m/s (positive infinity when not meaningful).</summary>
        double SpeedOfSound(double temperatureK);
        /// <summary>True when density depends on pressure (the solver then updates it per iteration).</summary>
        bool IsCompressible { get; }
    }

    /// <summary>Fixed properties (textbook reference cases in tests).</summary>
    public sealed class ConstantFluid : IFluidProperties
    {
        readonly double _density, _viscosity, _cp, _k;

        /// <param name="density">kg/m³.</param>
        /// <param name="viscosity">Pa·s.</param>
        /// <param name="specificHeat">J/(kg·K).</param>
        /// <param name="conductivity">W/(m·K).</param>
        public ConstantFluid(double density, double viscosity, double specificHeat = 1005, double conductivity = 0.026)
        {
            _density = density;
            _viscosity = viscosity;
            _cp = specificHeat;
            _k = conductivity;
        }

        public double Density(double pressureAbsPa, double temperatureK) => _density;
        public double Viscosity(double pressureAbsPa, double temperatureK) => _viscosity;
        public double SpecificHeat(double pressureAbsPa, double temperatureK) => _cp;
        public double Conductivity(double pressureAbsPa, double temperatureK) => _k;
        public double SpeedOfSound(double temperatureK) => double.PositiveInfinity;
        public bool IsCompressible => false;
    }

    /// <summary>Ideal gas law helpers.</summary>
    public static class IdealGas
    {
        /// <summary>ρ = p_abs M / (R T), kg/m³.</summary>
        /// <param name="pressureAbsPa">Absolute pressure, Pa.</param>
        /// <param name="molarMass">kg/mol.</param>
        /// <param name="temperatureK">K.</param>
        /// <param name="gasConstant">J/(mol·K).</param>
        public static double Density(double pressureAbsPa, double molarMass, double temperatureK, double gasConstant) =>
            pressureAbsPa * molarMass / (gasConstant * temperatureK);

        /// <summary>c = sqrt(γ R T / M), m/s.</summary>
        public static double SpeedOfSound(double gamma, double molarMass, double temperatureK, double gasConstant) =>
            Math.Sqrt(gamma * gasConstant * temperatureK / molarMass);
    }

    /// <summary>
    /// Properties from a property table (bilinear in T and log p, clamped at the table edges).
    /// Ideal gases take density straight from the ideal gas law (<c>06-fluids.md</c>).
    /// </summary>
    public sealed class TableFluid : IFluidProperties
    {
        readonly double[] _tK;
        readonly double[] _logP;
        readonly double[,] _rho, _mu, _cp, _k;
        readonly bool _idealGas;
        readonly double _molarMass, _gamma, _gasConstant, _speedOfSound20C;

        public TableFluid(FluidDef fluid, FluidTableDef table, double gasConstant)
        {
            _idealGas = fluid.IdealGas;
            _molarMass = table.MolarMass;
            _gamma = table.Gamma;
            _gasConstant = gasConstant;
            _speedOfSound20C = table.SpeedOfSound20C;
            _tK = new double[table.TemperaturesC.Count];
            for (int i = 0; i < _tK.Length; i++) _tK[i] = table.TemperaturesC[i] + 273.15;
            _logP = new double[table.PressuresPa.Count];
            for (int i = 0; i < _logP.Length; i++) _logP[i] = Math.Log(table.PressuresPa[i]);
            _rho = ToGrid(table.Density);
            _mu = ToGrid(table.Viscosity);
            _cp = ToGrid(table.SpecificHeat);
            _k = ToGrid(table.Conductivity);
        }

        static double[,] ToGrid(List<List<double>> rows)
        {
            var g = new double[rows.Count, rows[0].Count];
            for (int i = 0; i < rows.Count; i++)
                for (int j = 0; j < rows[i].Count; j++) g[i, j] = rows[i][j];
            return g;
        }

        public bool IsCompressible => _idealGas;

        public double Density(double pressureAbsPa, double temperatureK) =>
            _idealGas ? IdealGas.Density(pressureAbsPa, _molarMass, temperatureK, _gasConstant) : Lookup(_rho, pressureAbsPa, temperatureK);

        /// <summary>Density from the table itself (also for ideal gases; used to check the ideal gas law).</summary>
        public double TableDensity(double pressureAbsPa, double temperatureK) => Lookup(_rho, pressureAbsPa, temperatureK);

        public double Viscosity(double pressureAbsPa, double temperatureK) => Lookup(_mu, pressureAbsPa, temperatureK);
        public double SpecificHeat(double pressureAbsPa, double temperatureK) => Lookup(_cp, pressureAbsPa, temperatureK);
        public double Conductivity(double pressureAbsPa, double temperatureK) => Lookup(_k, pressureAbsPa, temperatureK);

        public double SpeedOfSound(double temperatureK) =>
            _idealGas ? IdealGas.SpeedOfSound(_gamma, _molarMass, temperatureK, _gasConstant) : _speedOfSound20C;

        double Lookup(double[,] grid, double pAbs, double tK)
        {
            Locate(_logP, Math.Log(Math.Max(pAbs, 1.0)), out int i, out double fp);
            Locate(_tK, tK, out int j, out double ft);
            double a = grid[i, j] + (grid[i, j + 1] - grid[i, j]) * ft;
            double b = grid[i + 1, j] + (grid[i + 1, j + 1] - grid[i + 1, j]) * ft;
            return a + (b - a) * fp;
        }

        /// <summary>Finds the cell [i, i+1] containing x (clamped) and the fraction within it.</summary>
        static void Locate(double[] axis, double x, out int i, out double frac)
        {
            int n = axis.Length;
            if (x <= axis[0]) { i = 0; frac = 0; return; }
            if (x >= axis[n - 1]) { i = n - 2; frac = 1; return; }
            int lo = 0, hi = n - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (axis[mid] <= x) lo = mid; else hi = mid;
            }
            i = lo;
            frac = (x - axis[lo]) / (axis[lo + 1] - axis[lo]);
        }
    }
}
