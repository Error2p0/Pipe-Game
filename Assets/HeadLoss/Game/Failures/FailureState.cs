using System.Collections.Generic;
using HeadLoss.Game.Buildings;

namespace HeadLoss.Game.Failures
{
    /// <summary>A burst segment, remembered by one of its tile edges (segment ids change with edits).</summary>
    public sealed class BurstState
    {
        public string Fluid = "water";
        /// <summary>Edge key (<see cref="FailureState.EdgeKey"/>) where the segment gave way.</summary>
        public string Edge = "";
        /// <summary>Game time it burst at, s (play time).</summary>
        public double At;

        public BurstState Clone() => (BurstState)MemberwiseClone();
    }

    /// <summary>A repair crew job (<c>08-failures.md</c> §Repairs).</summary>
    public sealed class RepairJob
    {
        public string Fluid = "water";
        public string Edge = "";
        public bool Emergency;
        /// <summary>Time left, s (counts while offline for standard crews).</summary>
        public double Remaining;
        public double Total;
        /// <summary>Upgrade fitted with the repair: new material and size ("" / 0 = as it was).</summary>
        public string UpgradeMaterial = "";
        public double UpgradeDiameterMm;
        /// <summary>The crew is on site (only one per building; the rest queue).</summary>
        public bool Started;

        public RepairJob Clone() => (RepairJob)MemberwiseClone();
    }

    /// <summary>
    /// What is saved about a building's pipes over time (<c>08-failures.md</c>): stress per tile edge, bursts, repairs,
    /// the failure roll counter, leak sensors per network and the maintenance contract.
    /// </summary>
    public sealed class FailureState
    {
        /// <summary>Accumulated stress per tile edge (fluid and the two tiles), dimensionless.</summary>
        public Dictionary<string, double> Stress = new Dictionary<string, double>();
        public List<BurstState> Bursts = new List<BurstState>();
        public List<RepairJob> Repairs = new List<RepairJob>();
        /// <summary>Failure rolls made so far (the failure RNG stream, <c>08-failures.md</c>).</summary>
        public ulong Counter;
        /// <summary>Networks with pipe-wall leak sensors fitted (fluid ids).</summary>
        public List<string> LeakSensors = new List<string>();
        /// <summary>Maintenance contract: bursts are repaired automatically while offline.</summary>
        public bool MaintenanceContract;
        /// <summary>The crew the contract (and the default button) sends: emergency or standard.</summary>
        public bool PreferEmergency;
        /// <summary>Segments that have already raised a weeping alert (edge keys), so each alerts once.</summary>
        public List<string> WeepAlerted = new List<string>();

        /// <summary>Key of the edge between two adjacent tiles (or floors) of a network.</summary>
        public static string EdgeKey(string fluid, TilePos a, TilePos b)
        {
            bool swap = a.F > b.F || (a.F == b.F && (a.X > b.X || (a.X == b.X && a.Y > b.Y)));
            if (swap) (a, b) = (b, a);
            return $"{fluid}|{a.X},{a.Y},{a.F}|{b.X},{b.Y},{b.F}";
        }

        public FailureState Clone()
        {
            var c = (FailureState)MemberwiseClone();
            c.Stress = new Dictionary<string, double>(Stress);
            c.Bursts = Bursts.ConvertAll(b => b.Clone());
            c.Repairs = Repairs.ConvertAll(r => r.Clone());
            c.LeakSensors = new List<string>(LeakSensors);
            c.WeepAlerted = new List<string>(WeepAlerted);
            return c;
        }
    }

}
