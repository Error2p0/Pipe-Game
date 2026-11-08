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
        public DevicesDef Devices = new DevicesDef();
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
        public WaterDef Water = new WaterDef();
        public FailureDef Failure = new FailureDef();
        public PowerDef Power = new PowerDef();
        public CityUtilitiesDef City = new CityUtilitiesDef();
        public GasDef Gas = new GasDef();
        public FinesDef Fines = new FinesDef();
        public InspectionDef Inspection = new InspectionDef();
        public PrestigeBalancingDef Prestige = new PrestigeBalancingDef();
        public EventsBalancingDef Events = new EventsBalancingDef();
        public OutageDef Outage = new OutageDef();
        public DemoDef Demo = new DemoDef();
    }

    /// <summary>Natural gas in buildings: leaks, alarms and safety gear (<c>06-fluids.md</c>, <c>08-failures.md</c>).</summary>
    public sealed class GasDef
    {
        /// <summary>Temperature of the gas in the building, °C.</summary>
        public double SupplyTemperatureC;
        /// <summary>The auto-router's reference assumes a connection pressure of this share of the LP facility pressure.</summary>
        public double ReferenceConnectionFactor;
        /// <summary>Alarm at a detector cell, as a share of the lower flammable limit.</summary>
        public double AlarmDetectorFraction;
        /// <summary>Alarm anywhere ("smelled by occupants"), as a share of the lower flammable limit.</summary>
        public double AlarmAnywhereFraction;
        /// <summary>Evacuation ends once the leak is repaired and every cell is below this share of the lower flammable limit.</summary>
        public double ClearFraction;
        /// <summary>A threshold must hold this long before the alarm sounds, s (the rapid-response bonus shortens it).</summary>
        public double DetectionSeconds;
        /// <summary>Gas in range this close to an ignition source can explode, m.</summary>
        public double IgnitionRadiusMetres;
        /// <summary>Orifice discharge coefficient C_d of a leak.</summary>
        public double LeakDischargeCoefficient;
        /// <summary>Leak area of a burst gas pipe, as a share of its internal cross-section.</summary>
        public double BurstLeakAreaFraction;
        /// <summary>Leak area of a weeping pipe, as a share of the burst leak area.</summary>
        public double WeepLeakFraction;
        /// <summary>Leak area of a leaking fitting, as a share of its largest pipe's cross-section.</summary>
        public double FittingLeakAreaFraction;
        /// <summary>Fresh air a passive vent opening lets in at wind factor 1, m³/h.</summary>
        public double PassiveVentFlowM3h;
        /// <summary>Tech tier that unlocks spark-free variants.</summary>
        public int SparkFreeTier;
        /// <summary>A spark-free variant costs this factor of the item's price.</summary>
        public double SparkFreeFactor;
        /// <summary>The least a spark-free upgrade costs, $ (the pre-placed panel is otherwise free).</summary>
        public double SparkFreeMinPrice;
        /// <summary>Leak every gas fitting has all the time, m³/s (<c>10-events.md</c> §Power outage cascade).</summary>
        public double BackgroundLeakPerFitting;
    }

    /// <summary>One-off fines (<c>13-balancing.md</c> §Failures).</summary>
    public sealed class FinesDef
    {
        /// <summary>Explosion fine: this many hours of the building's gross income, h.</summary>
        public double ExplosionGrossHours;
        /// <summary>Explosion fine at least this, $ (× tier factor).</summary>
        public double ExplosionMinimum;
        /// <summary>Damaged neighbour: this many hours of its gross income × damage, h.</summary>
        public double NeighbourGrossHours;
        /// <summary>Damaged neighbour at least this, $ (× its tier factor).</summary>
        public double NeighbourMinimum;
        /// <summary>Failed inspection: this many minutes of the building's gross income, min.</summary>
        public double InspectionFailGrossMinutes;
        /// <summary>Failed inspection at least this, $ (× tier factor).</summary>
        public double InspectionFailMinimum;
    }

    /// <summary>Periodic checks (<c>10-events.md</c> §Inspections). Phase 5: the gas safety certificate.</summary>
    public sealed class InspectionDef
    {
        /// <summary>A gas safety certificate is valid this long, real time, h.</summary>
        public double GasCertHours;
        /// <summary>Renewal price, $ (× tier factor).</summary>
        public double GasCertRenewalPrice;
        /// <summary>The renewal check takes this long, s.</summary>
        public double GasCertCheckSeconds;
        /// <summary>Pay factor while the certificate has expired.</summary>
        public double GasCertExpiredFactor;
        /// <summary>A reminder appears this long before expiry, h.</summary>
        public double GasCertReminderHours;
        /// <summary>Random inspection: chance per hour for each online building.</summary>
        public double ChancePerHour;
        /// <summary>The inspector's walk to the building, s (cosmetic).</summary>
        public double InspectorWalkSeconds;
        /// <summary>Pay multiplier after a pass.</summary>
        public double PassPayFactor;
        /// <summary>How long the pass multiplier lasts, min.</summary>
        public double PassMinutes;
        /// <summary>Time to fix after a fail before the second fine, min (pauses offline).</summary>
        public double FixDeadlineMinutes;
        /// <summary>Inspections and their timers run at this rate, Hz (<c>05-simulation.md</c> §Tick rates: 1 Hz).</summary>
        public double TickHz;
        /// <summary>Energy rating: the reference's power scales with (delivery ÷ its delivery) to this power for flow and heat outlets (affinity laws, D159).</summary>
        public double RatingFlowExponent;
        /// <summary>The same for pressure outlets (taps, stoves): P ∝ Q³ and Δp ∝ Q², so P ∝ Δp^1.5.</summary>
        public double RatingPressureExponent;
        /// <summary>A reference drawing less than this is not rated (a hand crank uses no electricity), W.</summary>
        public double RatingMinReferenceW;
    }

    /// <summary>The event scheduler (<c>10-events.md</c> §Event scheduler).</summary>
    public sealed class EventsBalancingDef
    {
        /// <summary>Scheduler rate, Hz.</summary>
        public double TickHz;
        /// <summary>Mean time between events (exponential), h.</summary>
        public double MeanIntervalHours;
        /// <summary>Least time between two event starts, min.</summary>
        public double MinGapMinutes;
        /// <summary>Active events per district at most.</summary>
        public int MaxPerDistrict;
        /// <summary>Active city-wide events at most.</summary>
        public int MaxCityWide;
        /// <summary>Events kept in the log.</summary>
        public int LogSize;
    }

    /// <summary>The power outage cascade and its protections (<c>10-events.md</c> §Power outage cascade).</summary>
    public sealed class OutageDef
    {
        /// <summary>Gas buildings without ventilation: time before the leak escalation starts at the alarm stage, s.</summary>
        public double GasSafetyWindowSeconds;
        /// <summary>A gas storage tank without its chiller warms this fast, K/min.</summary>
        public double TankWarmRateKPerMinute;
        /// <summary>Groundwater entering a basement, L/s per 100 m² of basement.</summary>
        public double GroundwaterLpsPer100M2;
        /// <summary>Water level that floods a basement panel that is not raised, m.</summary>
        public double FloodPanelLevelM;
        /// <summary>A backup generator starts this long after power is lost, s.</summary>
        public double GeneratorStartSeconds;
        /// <summary>Generator fuel, $ per kW·s.</summary>
        public double GeneratorFuelPerKWs;
        /// <summary>A raised panel costs this share more than the panel.</summary>
        public double RaisedPanelPriceFactor;
    }

    /// <summary>The prestige milestone and reward (<c>03-city.md</c> §Prestige).</summary>
    public sealed class PrestigeBalancingDef
    {
        /// <summary>Share of the plots that must be online.</summary>
        public double ShareOnline;
        /// <summary>Lifetime city income target of the first city, $.</summary>
        public double IncomeTargetFirst;
        /// <summary>Each later city's target is this factor of the one before.</summary>
        public double IncomeTargetGrowth;
        /// <summary>Reputation = floor(k · log10(lifetime city income)).</summary>
        public double K;
        /// <summary>Rebuilding a ruin costs this factor of the plot's reference build cost.</summary>
        public double ReconstructionCostFactor;
    }

    /// <summary>What the demo build allows (<c>14-roadmap.md</c> Phase 5 task 8).</summary>
    public sealed class DemoDef
    {
        /// <summary>Highest tech tier a demo player can reach.</summary>
        public int MaxTier;
        /// <summary>Founding a new city is available in the demo.</summary>
        public bool Prestige;
    }

    /// <summary>Building water networks (<c>05-simulation.md</c>, <c>06-fluids.md</c>).</summary>
    public sealed class WaterDef
    {
        /// <summary>Pressure an expansion vessel holds a closed heating loop at, Pa gauge.</summary>
        public double HeatingFillPressurePa;
        /// <summary>Temperature of city water in the building, °C (no thermal model for cold water yet).</summary>
        public double MainsTemperatureC;
        /// <summary>The auto-router's reference assumes a connection pressure of this share of the facility pressure.</summary>
        public double ReferenceConnectionFactor;
        /// <summary>Open pipe ends (a burst) discharge through this K referenced to the pipe area.</summary>
        public double OpenEndK;
    }

    /// <summary>Burst model, repairs and water hammer (<c>08-failures.md</c>, <c>05-simulation.md</c> §6).</summary>
    public sealed class FailureDef
    {
        /// <summary>How far ahead the engineering software projects stress, h.</summary>
        public double ForecastHours = 24;
        /// <summary>Failure checks per second, Hz.</summary>
        public double TickHz;
        /// <summary>k_s: stress rate at overpressure ratio 2, 1/s.</summary>
        public double StressRate;
        /// <summary>a: exponent on (x − 1).</summary>
        public double StressExponent;
        /// <summary>Weibull shape β.</summary>
        public double WeibullBeta;
        /// <summary>Weibull scale η (stress units).</summary>
        public double WeibullEta;
        /// <summary>Stress at which a segment weeps (and leak sensors alert).</summary>
        public double WeepThreshold;
        /// <summary>Optional slow ageing below rating, stress per second (0 = off, the default).</summary>
        public double AgeingRate;
        /// <summary>Standard crew repair time, s.</summary>
        public double RepairStandardSeconds;
        /// <summary>Emergency crew repair time, s.</summary>
        public double RepairEmergencySeconds;
        /// <summary>Emergency price = factor × standard price.</summary>
        public double RepairEmergencyFactor;
        /// <summary>Standard repair price per metre at the reference diameter, $ (× D/D_ref × tier factor).</summary>
        public double RepairPricePerMetre;
        /// <summary>Minimum standard repair price, $ (× tier factor).</summary>
        public double RepairMinPrice;
        /// <summary>A water-hammer surge lasts this long, s.</summary>
        public double HammerDurationSeconds;
        /// <summary>Surges on a network with a pressure tank are multiplied by this.</summary>
        public double AccumulatorSurgeFactor;
        /// <summary>Change times for the surge scaling (2L/a)/t: manual valve, pump stop or start, burst, check valve, s.</summary>
        public double ValveCloseSeconds;
        public double PumpChangeSeconds;
        public double BurstSeconds;
        public double CheckValveSeconds;
        /// <summary>Random leaks per gas fitting per hour (flat rate).</summary>
        public double FittingLeakPerHour;
        /// <summary>The explosion timer after an alarm, s.</summary>
        public double ExplosionTimerSeconds;
        /// <summary>Share of the gas's heating value that goes into the blast.</summary>
        public double YieldFactor;
        /// <summary>Blast radius = k_r · E^(1/3), m/J^(1/3).</summary>
        public double BlastRadiusK;
        /// <summary>Damage at or above this ruins a neighbour.</summary>
        public double HeavyDamage;
        /// <summary>Damage below this has no effect.</summary>
        public double LightDamage;
        /// <summary>Repairing damaged equipment (standard crew) costs this share of its price, at least the minimum repair price.</summary>
        public double EquipmentRepairFraction;
    }

    /// <summary>Electricity (<c>03-city.md</c> §Electricity, <c>04-buildings.md</c> §Electrical panel).</summary>
    public sealed class PowerDef
    {
        /// <summary>Every building's implicit connection until a cable is routed, kW.</summary>
        public double BasicConnectionKW;
        /// <summary>Capacity of each district substation, kW.</summary>
        public double SubstationKW;
        /// <summary>Draw above the panel limit for longer than this trips the panel, s.</summary>
        public double TripDelaySeconds;
        /// <summary>Tech tier at which power cables can be routed.</summary>
        public int CableTier;
    }

    /// <summary>City utility network (<c>03-city.md</c>, <c>13-balancing.md</c> §City).</summary>
    public sealed class CityUtilitiesDef
    {
        /// <summary>Facility source pressure per utility (water, gas-lp, gas-mp, gas-hp, heating, special-gas), Pa gauge.</summary>
        public Dictionary<string, double> FacilityPressure = new Dictionary<string, double>();
        /// <summary>One-off cost per main-road or rail tile crossed, $.</summary>
        public double CrossingCost;
        /// <summary>Directional drilling per tile, $.</summary>
        public double DrillingPerTile;
        /// <summary>Utility corridor per road tile, $.</summary>
        public double CorridorPerTile;
        /// <summary>Mains and cables a corridor tile holds.</summary>
        public int CorridorCapacity;
        /// <summary>Mains and cables an ordinary tile holds.</summary>
        public int TileCapacity;
        /// <summary>Recurring fines per income cycle (× the destination building's tier factor), $.</summary>
        public Dictionary<string, double> Fines = new Dictionary<string, double>();
        /// <summary>Multiplier on every fine of the industrial gas depot's mains.</summary>
        public double DepotFineFactor;
        /// <summary>Gas mains closer than this to a substation are a violation, city tiles.</summary>
        public int SubstationClearance;
        /// <summary>Gas and water mains closer than this are a violation, city tiles.</summary>
        public int MainSeparation;
        /// <summary>City solve interval, s (and on change).</summary>
        public double SolveIntervalSeconds;
        /// <summary>Capacity sharing passes per solve.</summary>
        public int CapacityPasses;
        /// <summary>Building ↔ city coupling iterations per change.</summary>
        public int CouplingIterations;
        /// <summary>Coupling relative tolerance.</summary>
        public double CouplingTolerance;
        /// <summary>Share of the price refunded when a main or cable is removed.</summary>
        public double RemoveRefundFraction;
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
        /// <summary>Wind factor on passive vents, range and period (<c>07-equipment.md</c>: 0.5–1.5 from climate).</summary>
        public double WindFactorMin;
        public double WindFactorMax;
        /// <summary>Period of the wind's slow swing, h.</summary>
        public double WindPeriodHours;
        /// <summary>Design outside temperature for room heating loads, °C (<c>13-balancing.md</c> §Outlets; climate modifiers shift it).</summary>
        public double DesignHeatingC;
        /// <summary>Design outside temperature for room cooling loads, °C.</summary>
        public double DesignCoolingC;
        /// <summary>Indoor temperature heat outlets are rated at and heating loads are figured for, °C.</summary>
        public double HeatingSetpointC;
        /// <summary>Indoor temperature cooling terminals are rated at and cooling loads are figured for, °C.</summary>
        public double CoolingSetpointC;
        /// <summary>Relative humidity per room type, 0–1 ("other" for the rest; the condensation code, <c>05-simulation.md</c> §4).</summary>
        public Dictionary<string, double> RoomHumidity = new Dictionary<string, double>();

        /// <summary>Relative humidity of a room type, 0–1.</summary>
        public double HumidityOf(string roomType) =>
            RoomHumidity.TryGetValue(roomType, out double h) ? h : RoomHumidity.TryGetValue("other", out double o) ? o : 0.45;
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
        /// <summary>Pipe sizing velocity limit for gas, m/s.</summary>
        public double GasVelocity;
        /// <summary>Straight tiles the trunk runs in from the intake before it may turn or branch.</summary>
        public int LeadInTiles;
        /// <summary>Path cost of a turn, in tiles (fewer elbows).</summary>
        public double TurnPenalty;
        /// <summary>Extra path cost, in tiles, for a branch that does not leave its outlet with two straight tiles (no room for a damper).</summary>
        public double ShortApproachPenalty;

        /// <summary>Extra path cost of core-drilling a wall, tiles (routes prefer doors).</summary>
        public double CoreDrillPenalty;
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
        /// <summary>Length of the 100 mm main a first water building is charged with in the balancing report, city tiles.</summary>
        public int ReferenceMainTiles;
        /// <summary>First building payback per tier, minutes.</summary>
        public List<double> PaybackMinutes = new List<double>();
        /// <summary>Electricity share of gross at the reference layout [low, high].</summary>
        public List<double> ElectricityShare = new List<double>();
        /// <summary>First prestige available, hours of play including offline [low, high].</summary>
        public List<double> FirstPrestigeHours = new List<double>();
        /// <summary>The balancing report's pacing estimate: buildings a player brings online per hour of the time away and in game.</summary>
        public double BuildingsPerHour;
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
        /// <summary>Ceiling capacity units per tile (the old-buildings modifier lowers it).</summary>
        public int CeilingCapacity;
        /// <summary>Core drilling a wall tile to pass a pipe (tier 2+), $ each.</summary>
        public double CoreDrillPrice;
        /// <summary>Lowest tier that may core-drill walls.</summary>
        public int CoreDrillTier;
        /// <summary>Tier at which the wall cavity layer unlocks.</summary>
        public int WallCavityTier;
    }

    public sealed class HeatmapDef
    {
        /// <summary>A pipe's heatmap colour blends toward its joint over this distance from each end, m (at most half the pipe).</summary>
        public double JointBlendMetres;
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
        /// <summary>Water pressure scale 0 → anchor, extended in steps, bar.</summary>
        public double PressureAnchorMaxWaterBar;
        public double PressureStepWaterBar;
        /// <summary>Water flow scale 0 → anchor, extended in steps, L/s.</summary>
        public double FlowAnchorMaxWaterLps;
        public double FlowStepWaterLps;
        /// <summary>Gas scales: velocity anchor (m/s), pressure anchor and step (mbar), flow anchor and step (m³/h), density [min, max] (kg/m³).</summary>
        public double VelocityAnchorMaxGas;
        public double PressureAnchorMaxGasMbar;
        public double PressureStepGasMbar;
        public double FlowAnchorMaxGasM3h;
        public double FlowStepGasM3h;
        public List<double> DensityAnchorGas = new List<double>();
        /// <summary>Concentration scale top, as a multiple of the lower flammable limit.</summary>
        public double ConcentrationAnchorFractionOfLfl;
        /// <summary>CO₂ scale [min, max], ppm.</summary>
        public List<double> Co2AnchorPpm = new List<double>();
        /// <summary>O₂ deficit scale [min, max], volume fraction below fresh air.</summary>
        public List<double> O2DeficitAnchor = new List<double>();
        /// <summary>Water density scale [min, max], kg/m³.</summary>
        public List<double> DensityAnchorWater = new List<double>();
        /// <summary>Opacity of the room heatmap over the room fills.</summary>
        public double RoomAlpha;
        /// <summary>Palette ramps as hex colour stops, evenly spaced.</summary>
        public Dictionary<string, List<string>> Palettes = new Dictionary<string, List<string>>();
        /// <summary>
        /// The diverging ramp for temperature relative to the setpoint (<c>11-ui-visuals-audio.md</c>: blue–white–red, colour-blind safe),
        /// cold to warm, its middle stop at <see cref="DivergingCentreC"/>.
        /// </summary>
        public List<string> DivergingPalette = new List<string>();
        /// <summary>Temperature the diverging ramp is white at, °C: the middle of the comfort band between the heating and cooling setpoints.</summary>
        public double DivergingCentreC;
        /// <summary>The diverging scale reaches this far each side of its centre at least, K (it widens in temperatureStepK steps, evenly).</summary>
        public double DivergingHalfRangeK;
    }

    public sealed class DrawingDef
    {
        /// <summary>Opacity of the pipes on the floors above and below the one on show, 0–1.</summary>
        public double GhostOpacity;
        /// <summary>Opacity of the networks not in focus on the floor on show, their parts included, 0–1.</summary>
        public double OtherNetworkOpacity;
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

    /// <summary>Fans and pumps in operation (<c>05-simulation.md</c> §2).</summary>
    public sealed class DevicesDef
    {
        /// <summary>
        /// While a device slows down or starts up the network re-solves each time its speed has moved by this share; below
        /// it the device counts as stopped (0–1). Each device's times are in <c>equipment.json</c>.
        /// </summary>
        public double SpinResolveStep;
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
        /// <summary>Water noise gain at full throughput, 0–1.</summary>
        public double WaterGain;
        /// <summary>Total water throughput that gives the full water level, L/s (log scale below it).</summary>
        public double WaterFullLps;
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
