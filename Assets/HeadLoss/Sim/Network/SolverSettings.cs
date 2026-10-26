using HeadLoss.Core.Data.Defs;
using HeadLoss.Sim.Hydraulics;

namespace HeadLoss.Sim.Network
{
    /// <summary>Solver configuration, normally built from <c>balancing.json</c>.</summary>
    public sealed class SolverSettings
    {
        public int MaxIterations = 60;
        public double FlowTolerance = 1e-6;
        public double MassImbalanceTolerance = 1e-9;
        public double GMinFactor = 1e-6;
        public double ZeroFlowThreshold = 1e-9;
        public double Relaxation = 0.5;
        public int DivergenceIterations = 3;
        /// <summary>Initial guess velocity, m/s.</summary>
        public double InitialVelocity = 1.0;
        /// <summary>m/s².</summary>
        public double Gravity = 9.80665;
        /// <summary>Pa absolute.</summary>
        public double AtmosphericPressure = 101325;
        public FrictionModel Friction = new FrictionModel(2300, 4000);
        public double PcgTolerance = 1e-13;
        public int PcgMaxIterations = 2000;

        public static SolverSettings FromBalancing(BalancingDef b) => new SolverSettings
        {
            MaxIterations = b.Solver.MaxIterations,
            FlowTolerance = b.Solver.FlowTolerance,
            MassImbalanceTolerance = b.Solver.MassImbalanceTolerance,
            GMinFactor = b.Solver.GMinFactor,
            ZeroFlowThreshold = b.Solver.ZeroFlowThreshold,
            Relaxation = b.Solver.Relaxation,
            DivergenceIterations = b.Solver.DivergenceIterations,
            InitialVelocity = b.Solver.InitialVelocity,
            Gravity = b.Physics.Gravity,
            AtmosphericPressure = b.Physics.AtmosphericPressurePa,
            Friction = new FrictionModel(b.Physics.LaminarReynolds, b.Physics.TurbulentReynolds),
            PcgTolerance = b.Solver.PcgTolerance,
            PcgMaxIterations = b.Solver.PcgMaxIterations,
        };
    }
}
