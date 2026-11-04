namespace HeadLoss.Core.Data.Defs
{
    /// <summary>Entry in <c>fittings.json</c>. Which K fields apply depends on <see cref="Model"/>.</summary>
    public sealed class FittingDef
    {
        public string Id = "";
        public string DisplayName = "";
        /// <summary>fixed, elbow, tee, reducer, valve, check, regulator, relief, filter.</summary>
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
        /// <summary>Closes by itself at a gas alarm and on power loss.</summary>
        public bool AutoShutoff;
        /// <summary>Filter pressure drop at rated flow, Pa (proportional to Q²).</summary>
        public double RatedPressureDropPa;
        /// <summary>Regulators and relief valves: default setpoint, Pa (gauge).</summary>
        public double DefaultSetting;
        /// <summary>Setpoint adjustment step, Pa (0.1 bar for water, <c>07-equipment.md</c> §Control parts).</summary>
        public double SettingStep;
        public double SettingMin;
        public double SettingMax;
        /// <summary>Networks an inline part of this kind goes on (empty = air only).</summary>
        public System.Collections.Generic.List<string> Fluids = new System.Collections.Generic.List<string>();
        /// <summary>Price at the reference diameter, $ (× D/D_ref above it).</summary>
        public double Price;
        public string Notes = "";
    }
}
