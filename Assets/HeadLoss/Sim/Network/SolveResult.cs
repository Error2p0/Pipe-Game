namespace HeadLoss.Sim.Network
{
    /// <summary>Results of one network solve (<c>05-simulation.md</c> §1.4). Arrays are indexed like the graph.</summary>
    public sealed class SolveResult
    {
        public bool Converged;
        /// <summary>Not converged within the iteration limit ("Network still settling").</summary>
        public bool Unstable;
        public int Iterations;
        /// <summary>Final Σ|ΔQ| / Σ|Q|.</summary>
        public double Residual;
        /// <summary>Max node continuity error at the end, m³/s.</summary>
        public double MaxImbalance;
        /// <summary>Total linear-solver iterations over all Newton iterations.</summary>
        public int LinearIterations;
        /// <summary>Wall time of the solve, ms (set by the caller that timed it).</summary>
        public double SolveMilliseconds;
        public long TopologyKey;

        /// <summary>Gauge pressure per node, Pa (0 for inactive nodes).</summary>
        public double[] NodePressure = System.Array.Empty<double>();
        /// <summary>False for nodes not connected to any fixed-pressure node.</summary>
        public bool[] NodeActive = System.Array.Empty<bool>();

        /// <summary>Flow per link, m³/s (positive From → To).</summary>
        public double[] Flow = System.Array.Empty<double>();
        /// <summary>Mean velocity per link, m/s (signed).</summary>
        public double[] Velocity = System.Array.Empty<double>();
        public double[] Reynolds = System.Array.Empty<double>();
        public double[] FrictionFactor = System.Array.Empty<double>();
        /// <summary>Friction loss, Pa (signed with the flow).</summary>
        public double[] DpFriction = System.Array.Empty<double>();
        /// <summary>Fitting / grille / valve loss, Pa (signed with the flow).</summary>
        public double[] DpFittings = System.Array.Empty<double>();
        /// <summary>Elevation term ρ·g·(z_to − z_from), Pa.</summary>
        public double[] DpElevation = System.Array.Empty<double>();
        /// <summary>Device pressure rise (fans), Pa.</summary>
        public double[] DpDevice = System.Array.Empty<double>();
        /// <summary>Density used per link, kg/m³.</summary>
        public double[] Density = System.Array.Empty<double>();
        /// <summary>Mass flow, kg/s.</summary>
        public double[] MassFlow = System.Array.Empty<double>();
        /// <summary>Fan efficiency at the operating point (0 for other links).</summary>
        public double[] Efficiency = System.Array.Empty<double>();
        /// <summary>Fan input power, W.</summary>
        public double[] Power = System.Array.Empty<double>();
        /// <summary>Velocity above 0.3 × speed of sound (incompressible assumption weak).</summary>
        public bool[] MachWarning = System.Array.Empty<bool>();
        public bool[] LinkActive = System.Array.Empty<bool>();

        /// <summary>Pressure at a link's start node, Pa.</summary>
        public double PressureFrom(NetworkGraph g, int link) => NodePressure[g.Links[link].From];
        /// <summary>Pressure at a link's end node, Pa.</summary>
        public double PressureTo(NetworkGraph g, int link) => NodePressure[g.Links[link].To];
    }
}
