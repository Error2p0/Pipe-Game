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
        public TargetsDef Targets = new TargetsDef();
        public AutoRouterDef AutoRouter = new AutoRouterDef();
        public DifficultyDef Difficulty = new DifficultyDef();
        public ProgressionDef Progression = new ProgressionDef();
        public ClimateDef Climate = new ClimateDef();
        public RoomGridDef RoomGrid = new RoomGridDef();
    }

    /// <summary>Outside conditions (<c>13-balancing.md</c> §Climate).</summary>
    public sealed class ClimateDef
    {
        /// <summary>Mean outside temperature, °C.</summary>
        public double MeanTemperatureC;
        /// <summary>Daily swing (± around the mean), K.</summary>
        public double DailyAmplitudeK;
        /// <summary>Local hour of the warmest moment.</summary>
        public double WarmestHour;
        /// <summary>Solar gain at its peak, W per m² of window.</summary>
        public double SolarPeakWm2;
        /// <summary>Solar factor per facade (north, south, east, west).</summary>
        public Dictionary<string, double> FacadeFactors = new Dictionary<string, double>();
        /// <summary>Sun up between these local hours (solar gain follows a half sine in between).</summary>
        public double SunriseHour;
        public double SunsetHour;
        /// <summary>Exterior wall U-value, W/(m²·K) (old buildings 1.2).</summary>
        public double WallU;
        /// <summary>Sensible heat per occupant, W (of 100 W total).</summary>
        public double OccupantSensibleW;
        /// <summary>CO₂ breathed out per occupant, L/s.</summary>
        public double OccupantCo2Lps;
        public double OutdoorCo2Ppm;
        /// <summary>Oxygen volume fraction of fresh air.</summary>
        public double OutdoorO2;
    }

    /// <summary>Room grid (<c>05-simulation.md</c> §3, <c>13-balancing.md</c> §Room grid).</summary>
    public sealed class RoomGridDef
    {
        /// <summary>Cell size, m.</summary>
        public double CellSize;
        /// <summary>Eddy diffusivity D_t, m²/s.</summary>
        public double EddyDiffusivity;
        /// <summary>Vent jet decay length L_jet, m.</summary>
        public double JetLength;
        /// <summary>Buoyancy transfer k_b, 1/s.</summary>
        public double Stratification;
        /// <summary>Slow mixing between the two layers, 1/s.</summary>
        public double LayerMixing;
        /// <summary>Exchange between floors through stairwells and shafts k_v, 1/s.</summary>
        public double FloorExchange;
        /// <summary>Room heat capacity as a multiple of the air's (thermal mass).</summary>
        public double ThermalMassFactor;
        /// <summary>SOR iterations per step for the velocity potential.</summary>
        public int SorIterations;
        public double SorOmega;
        /// <summary>Height of the lower layer, m (the upper one reaches the ceiling layer).</summary>
        public double LayerSplitHeight;
        /// <summary>Room air height (floor to ceiling layer), m.</summary>
        public double RoomHeight;
        /// <summary>Door opening conductance for the room balance, m³/s per Pa.</summary>
        public double DoorConductance;
        /// <summary>Exterior-wall leakage conductance, m³/s per Pa per m² of wall.</summary>
        public double FacadeConductancePerM2;
        /// <summary>Step rate in the open building, Hz.</summary>
        public double OpenHz;
        /// <summary>Step rate in buildings that are not open, Hz.</summary>
        public double BackgroundHz;
        /// <summary>Background steps use sub-steps this many times the open building's step.</summary>
        public double BackgroundStepFactor;
        /// <summary>Thermal (duct temperature) solve rate, Hz.</summary>
        public double ThermalHz;
    }

    /// <summary>Tech unlocks (<c>03-city.md</c> §Progression).</summary>
    public sealed class ProgressionDef
    {
        /// <summary>Buildings of tier N online at the same time to unlock tier N + 1 (index N − 1).</summary>
        public List<int> UnlockOnline = new List<int>();
    }

    /// <summary>The 1–5 dot difficulty estimate on a plot's hover (<c>03-city.md</c> §Difficulty hint).</summary>
    public sealed class DifficultyDef
    {
        /// <summary>Sum of outlet minimums worth one dot, m³/h.</summary>
        public double DemandReferenceM3h;
        /// <summary>Longest entry-to-outlet duct path worth one dot, m.</summary>
        public double PathReferenceMetres;
        /// <summary>Dots per floor above the first.</summary>
        public double FloorWeight;
        /// <summary>Dots per code in force.</summary>
        public double CodeWeight;
    }

    /// <summary>Auto-router for reference layouts (<c>04-buildings.md</c> §Auto-router).</summary>
    public sealed class AutoRouterDef
    {
        /// <summary>Duct sizing velocity limit on trunks (serving two or more outlets), m/s.</summary>
        public double AirTrunkVelocity;
        /// <summary>Duct sizing velocity limit on branches (one outlet), m/s.</summary>
        public double AirBranchVelocity;
        /// <summary>Pipe sizing velocity limit for water, m/s.</summary>
        public double WaterVelocity;
        /// <summary>Straight tiles the trunk runs in from the intake before it may turn or branch.</summary>
        public int LeadInTiles;
        /// <summary>Path cost of a turn, in tiles (fewer elbows).</summary>
        public double TurnPenalty;
        /// <summary>Extra path cost, in tiles, for a branch that does not leave its outlet with two straight tiles (no room for a damper).</summary>
        public double ShortApproachPenalty;

        /// <summary>Damper balancing passes (one solve each).</summary>
        public int BalancingPasses;
        /// <summary>Damper target within the overdelivery band: ideal + fraction × (max − ideal).</summary>
        public double BalanceTarget;
    }

    /// <summary>Tuning targets the balancing report checks (<c>13-balancing.md</c> §Targets).</summary>
    public sealed class TargetsDef
    {
        /// <summary>Starter house: minutes of cranking to pay a small axial fan plus ducts [low, high].</summary>
        public List<double> StarterCrankPaysFanMinutes = new List<double>();
        /// <summary>First building payback per tier, minutes.</summary>
        public List<double> PaybackMinutes = new List<double>();
        /// <summary>Electricity share of gross at the reference layout [low, high].</summary>
        public List<double> ElectricityShare = new List<double>();
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
        /// <summary>Room velocity scale (rooms move far slower than ducts), m/s.</summary>
        public double RoomVelocityAnchorMax;
        /// <summary>Pressure scale 0 → anchor, extended in steps, Pa.</summary>
        public double PressureAnchorMaxAirPa;
        public double PressureStepPa;
        /// <summary>Flow rate scale 0 → anchor, extended in steps, m³/h.</summary>
        public double FlowAnchorMaxM3h;
        public double FlowStepM3h;
        /// <summary>Temperature scale [min, max] widened in steps, °C.</summary>
        public List<double> TemperatureAnchorC = new List<double>();
        public double TemperatureStepK;
        /// <summary>Density scale [min, max] widened in steps, kg/m³.</summary>
        public List<double> DensityAnchor = new List<double>();
        public double DensityStep;
        /// <summary>Opacity of the room heatmap over the room fills.</summary>
        public double RoomAlpha;
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
        /// <summary>Orthographic size limits in the city view, m.</summary>
        public double CityZoomMin;
        public double CityZoomMax;
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
        /// <summary>Offline catch-up re-solves each building at the outside temperature every this many hours.</summary>
        public double ClimateStepHours;
        /// <summary>The "While you were away" report shows after at least this long away, s.</summary>
        public double ReportMinimumSeconds;
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
