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
        public EconomyDef Economy = new EconomyDef();
        public CrankDef Crank = new CrankDef();
        public PricesDef Prices = new PricesDef();
        public SaveDef Save = new SaveDef();
        public OfflineDef Offline = new OfflineDef();
        public AudioDef Audio = new AudioDef();
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
        /// <summary>Budget cap = factor × reference layout cost (<c>04-buildings.md</c>).</summary>
        public double BudgetCapFactor;
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
        /// <summary>Fan blade animation, degrees per second per m³/h of fan flow.</summary>
        public double FanSpinDegreesPerM3h;
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

    /// <summary>Money rules (<c>02-economy.md</c>). Arrays indexed by tier − 1.</summary>
    public sealed class EconomyDef
    {
        /// <summary>Delivery ratio d/min at which overdelivery reaches the cap [doc].</summary>
        public double OverdeliveryCapRatio;
        /// <summary>Payout multiplier at the cap [doc].</summary>
        public double CapMultiplier;
        /// <summary>Payout multiplier at the cap for risky fluids [doc].</summary>
        public double CapMultiplierRisky;
        /// <summary>Income factor for a building with an unrepaired burst while offline [doc].</summary>
        public double OfflineBurstFactor;
        /// <summary>Income factor for a building with an unrepaired burst in game [doc].</summary>
        public double InGameBurstFactor;
        /// <summary>Electricity price, $ per kW·s (× the building's electricity tier factor).</summary>
        public double ElectricityPricePerKWs;
        /// <summary>Recurring fines and premiums are quoted per cycle, s.</summary>
        public double IncomeCycleSeconds;
        /// <summary>Share of the price refunded when committed parts are removed.</summary>
        public double RefundFraction;
        /// <summary>Wallet of a new game, $.</summary>
        public double StartingWallet;
        /// <summary>Economy tick rate, Hz.</summary>
        public double TickHz;
        /// <summary>Base pay of an air supply vent per tier, $/s (other outlets × their pay factor).</summary>
        public List<double> BasePay = new List<double>();
        /// <summary>Fines, repairs and renewals scale per tier.</summary>
        public List<double> TierFactor = new List<double>();
        /// <summary>Electricity price multiplier per tier.</summary>
        public List<double> ElectricityTierFactor = new List<double>();
    }

    /// <summary>Hand-crank fan (<c>02-economy.md</c> §Hand crank).</summary>
    public sealed class CrankDef
    {
        /// <summary>Speed fraction added per click.</summary>
        public double SpeedPerClick;
        /// <summary>Exponential decay time constant of the crank speed, s.</summary>
        public double DecaySeconds;
        /// <summary>Holding the crank button or key counts as this many clicks per second.</summary>
        public double HoldClicksPerSecond;
        /// <summary>The network re-solves when the crank speed has moved by this much since the last solve.</summary>
        public double ResolveSpeedStep;
    }

    /// <summary>Price rules (<c>13-balancing.md</c> §Prices). Item prices live in their own files.</summary>
    public sealed class PricesDef
    {
        /// <summary>Duct price per metre at the reference diameter, $ (× material price factor).</summary>
        public double DuctBasePerMetre;
        /// <summary>Duct reference diameter, mm.</summary>
        public double DuctReferenceDiameterMm;
        /// <summary>Pipe price per metre at the reference diameter, $ (× material price factor).</summary>
        public double PipeBasePerMetre;
        /// <summary>Pipe reference diameter, mm.</summary>
        public double PipeReferenceDiameterMm;
        /// <summary>Price per metre scales with (D / D_ref)^exponent.</summary>
        public double DiameterExponent;
    }

    /// <summary>Save files (<c>12-save-offline.md</c>).</summary>
    public sealed class SaveDef
    {
        /// <summary>Autosave interval, s.</summary>
        public double AutosaveSeconds;
        /// <summary>Rolling backups kept (.bak1 … .bakN).</summary>
        public int BackupCount;
        /// <summary>Saves larger than this are gzip-compressed, bytes.</summary>
        public int GzipThresholdBytes;
    }

    public sealed class OfflineDef
    {
        /// <summary>Offline progress cap, h [confirm].</summary>
        public double MaxHours;
    }

    /// <summary>Procedural audio levels and ranges (<c>11-ui-visuals-audio.md</c> §Procedural audio).</summary>
    public sealed class AudioDef
    {
        /// <summary>Default master volume, 0–1.</summary>
        public double MasterVolume;
        /// <summary>Airflow noise gain at full throughput, 0–1.</summary>
        public double AirflowGain;
        /// <summary>Total air throughput that gives the full airflow level, m³/h (log scale below it).</summary>
        public double AirflowFullM3h;
        /// <summary>Airflow low-pass cutoff range, Hz.</summary>
        public double AirflowCutoffMinHz;
        public double AirflowCutoffMaxHz;
        /// <summary>Fan hum frequency at speed 0 and 1, Hz.</summary>
        public double HumMinHz;
        public double HumMaxHz;
        /// <summary>Fan hum gain per running fan at full speed, 0–1.</summary>
        public double HumGain;
        /// <summary>UI click gain, 0–1.</summary>
        public double ClickGain;
        /// <summary>Limiter ceiling, 0–1.</summary>
        public double LimiterCeiling;
    }
}
