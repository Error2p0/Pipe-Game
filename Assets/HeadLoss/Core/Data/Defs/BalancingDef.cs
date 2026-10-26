using System.Collections.Generic;

namespace HeadLoss.Core.Data.Defs
{
    /// <summary><c>balancing.json</c>: every tunable number (defaults in <c>13-balancing.md</c>).</summary>
    public sealed class BalancingDef
    {
        public PhysicsDef Physics = new PhysicsDef();
        public SolverDef Solver = new SolverDef();
        public BuildingBalancingDef Building = new BuildingBalancingDef();
        public HeatmapDef Heatmap = new HeatmapDef();
        public DrawingDef Drawing = new DrawingDef();
        public ParticlesDef Particles = new ParticlesDef();
        public CameraDef Camera = new CameraDef();
    }

    /// <summary>Physical constants (SI).</summary>
    public sealed class PhysicsDef
    {
        /// <summary>Gravitational acceleration, m/s².</summary>
        public double Gravity;
        /// <summary>Universal gas constant, J/(mol·K).</summary>
        public double GasConstant;
        /// <summary>Atmospheric pressure, Pa (absolute).</summary>
        public double AtmosphericPressurePa;
        /// <summary>Air temperature used until the thermal model exists, °C.</summary>
        public double DefaultAirTemperatureC;
        /// <summary>Reynolds number at or below which flow is laminar.</summary>
        public double LaminarReynolds;
        /// <summary>Reynolds number at or above which Swamee-Jain applies.</summary>
        public double TurbulentReynolds;
    }

    /// <summary>Global gradient algorithm settings (<c>05-simulation.md</c> §1.3).</summary>
    public sealed class SolverDef
    {
        public int MaxIterations;
        /// <summary>Converged when Σ|ΔQ| / Σ|Q| is below this.</summary>
        public double FlowTolerance;
        /// <summary>Max node mass imbalance at convergence, m³/s.</summary>
        public double MassImbalanceTolerance;
        /// <summary>g_min = factor × scale.</summary>
        public double GMinFactor;
        /// <summary>|Q| below this (m³/s) uses the linear near-zero term.</summary>
        public double ZeroFlowThreshold;
        /// <summary>Relaxation applied to flow updates when the residual keeps growing.</summary>
        public double Relaxation;
        /// <summary>Consecutive growing-residual iterations before relaxation.</summary>
        public int DivergenceIterations;
        /// <summary>Initial guess velocity in every link, m/s.</summary>
        public double InitialVelocity;
        /// <summary>Slow-drift re-solve interval, s.</summary>
        public double ResolveIntervalSeconds;
        /// <summary>Solves slower than this (ms) move to a worker thread.</summary>
        public double AsyncThresholdMs;
        /// <summary>Relative residual tolerance of the conjugate gradient linear solve.</summary>
        public double PcgTolerance;
        public int PcgMaxIterations;
    }

    public sealed class BuildingBalancingDef
    {
        /// <summary>Floor-to-floor height, m.</summary>
        public double FloorHeight;
        /// <summary>Elevation of the floor layer above the floor, m.</summary>
        public double FloorLayerOffset;
        /// <summary>Elevation of the ceiling layer above the floor, m.</summary>
        public double CeilingLayerOffset;
        /// <summary>Elevation of the wall cavity layer above the floor, m.</summary>
        public double WallCavityLayerOffset;
    }

    public sealed class HeatmapDef
    {
        /// <summary>Velocity heatmap upper anchor for air, m/s.</summary>
        public double VelocityAnchorMaxAir;
        /// <summary>Velocity heatmap upper anchor for water, m/s.</summary>
        public double VelocityAnchorMaxWater;
        /// <summary>When values exceed the anchor, the range rounds up to this step, m/s.</summary>
        public double AutoRangeStep;
        public string DefaultPalette = "";
        /// <summary>Palette ramps as hex colour stops, evenly spaced.</summary>
        public Dictionary<string, List<string>> Palettes = new Dictionary<string, List<string>>();
    }

    public sealed class DrawingDef
    {
        public string DefaultMaterial = "";
        public string DefaultDuctFamily = "";
        /// <summary>Duct size selected when the building opens, mm.</summary>
        public double DefaultDuctSizeMm;
        /// <summary>Max number of segments meeting at a node (3 = tee).</summary>
        public int MaxNodeDegree;
        /// <summary>Drawn duct width = base + diameter × factor, m (world).</summary>
        public double DuctDisplayWidthBase;
        public double DuctDisplayWidthFactor;
    }

    public sealed class ParticlesDef
    {
        /// <summary>Distance between particles along a duct, m.</summary>
        public double SpacingMetres;
        /// <summary>Display speed = velocity × factor, clamped, m/s.</summary>
        public double SpeedFactor;
        public double MinSpeed;
        public double MaxSpeed;
        /// <summary>Velocities below this (m/s) show no particles.</summary>
        public double MinVisibleVelocity;
        /// <summary>Particle quad size, m.</summary>
        public double Size;
        public int MaxParticles;
    }

    public sealed class CameraDef
    {
        /// <summary>Orthographic size limits, m.</summary>
        public double ZoomMin;
        public double ZoomMax;
        /// <summary>Zoom factor per wheel notch.</summary>
        public double ZoomStep;
        /// <summary>Keyboard pan speed, multiples of the orthographic size per second.</summary>
        public double KeyPanSpeed;
    }
}
