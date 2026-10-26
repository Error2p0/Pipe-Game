namespace HeadLoss.Core.Data.Defs
{
    /// <summary>Entry in <c>fittings.json</c>. Which K fields apply depends on <see cref="Model"/>.</summary>
    public sealed class FittingDef
    {
        public string Id = "";
        public string DisplayName = "";
        /// <summary>fixed, elbow, tee, reducer, valve, filter.</summary>
        public string Model = "";
        public int Tier;
        /// <summary>Loss coefficient for the fixed and valve models (valve: K at 100% open).</summary>
        public double K;
        /// <summary>Elbow K in ducts.</summary>
        public double KDuct;
        /// <summary>Elbow K in pipes.</summary>
        public double KPipe;
        /// <summary>Tee K through the run.</summary>
        public double KRun;
        /// <summary>Tee K into the branch.</summary>
        public double KBranch;
        /// <summary>Reducer K for a contraction, referenced to the smaller (downstream) velocity.</summary>
        public double KContraction;
        /// <summary>Elbow turn angle, degrees.</summary>
        public double AngleDegrees;
        /// <summary>Smooth elbows and tees are the upgraded quality.</summary>
        public bool Smooth;
        /// <summary>Minimum opening fraction for valves.</summary>
        public double MinOpening;
        /// <summary>Filter pressure drop at rated flow, Pa (proportional to Q²).</summary>
        public double RatedPressureDropPa;
        public string Notes = "";
    }
}
