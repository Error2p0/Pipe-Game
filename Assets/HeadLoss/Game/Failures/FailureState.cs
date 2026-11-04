using System.Collections.Generic;
using HeadLoss.Game.Buildings;

namespace HeadLoss.Game.Failures
{
    /// <summary>Keys of equipment knocked offline by a blast (<see cref="FailureState.DamagedEquipment"/>).</summary>
    public static class DamageKeys
    {
        public static string Opening(string openingId) => "opening:" + openingId;
        public static string Inline(string fluid, TilePos t) => $"inline:{fluid}:{t.X},{t.Y},{t.F}";
    }

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

    /// <summary>A gas leak (<c>08-failures.md</c> §Flammable gas escalation), until a crew repairs it.</summary>
    public sealed class LeakState
    {
        /// <summary>Unique within the building ("leak-3"); repair jobs refer to it.</summary>
        public string Key = "";
        /// <summary>What started it: burst, weep, fitting or damage.</summary>
        public string Cause = "";
        /// <summary>Stress edge of the segment it is on ("" for a fitting).</summary>
        public string Edge = "";
        /// <summary>Tile it leaks at.</summary>
        public int X;
        public int Y;
        public int Floor;
        /// <summary>Orifice area, m².</summary>
        public double AreaM2;
        /// <summary>Game time it started, s.</summary>
        public double At;

        public TilePos Pos => new TilePos(X, Y, Floor);
        public LeakState Clone() => (LeakState)MemberwiseClone();
    }

    /// <summary>The alarm and the explosion timer of a building. Saved: a paused timer resumes where it stopped.</summary>
    public sealed class GasEmergencyState
    {
        /// <summary>The alarm has sounded: the building is evacuated and pays nothing.</summary>
        public bool Alarm;
        /// <summary>The explosion timer is counting (false when an automatic shutoff has stopped the escalation).</summary>
        public bool TimerRunning;
        /// <summary>Explosion timer left, s. Pauses while the player is away.</summary>
        public double TimerLeft;
        /// <summary>How long a threshold has been passed before the alarm, s.</summary>
        public double Detecting;
        /// <summary>Automatic shutoff valves closed by the alarm, tile keys (x,y,floor).</summary>
        public List<string> AutoClosed = new List<string>();

        public GasEmergencyState Clone()
        {
            var c = (GasEmergencyState)MemberwiseClone();
            c.AutoClosed = new List<string>(AutoClosed);
            return c;
        }
    }

    /// <summary>The gas safety certificate (<c>10-events.md</c> §Inspections).</summary>
    public sealed class GasCertificateState
    {
        /// <summary>The first certificate comes with the first gas pipe built.</summary>
        public bool Issued;
        /// <summary>Real time left, s (counts while away); at or below 0 it has expired.</summary>
        public double SecondsLeft;
        /// <summary>A renewal check is running, s left (0 = none).</summary>
        public double CheckLeft;
        /// <summary>The two-hour reminder has been shown for this certificate.</summary>
        public bool Reminded;
        /// <summary>Why the last renewal check failed ("" = it did not).</summary>
        public string LastFailure = "";

        public GasCertificateState Clone() => (GasCertificateState)MemberwiseClone();
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
        /// <summary>Gas leaks not yet repaired.</summary>
        public List<LeakState> Leaks = new List<LeakState>();
        /// <summary>Leaks started so far (numbers the keys).</summary>
        public int NextLeak = 1;
        public GasEmergencyState Gas = new GasEmergencyState();
        public GasCertificateState Certificate = new GasCertificateState();
        /// <summary>Equipment knocked offline by a blast, until repaired: "opening:id" or "inline:fluid:x,y,floor".</summary>
        public List<string> DamagedEquipment = new List<string>();

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
            c.Leaks = Leaks.ConvertAll(l => l.Clone());
            c.Gas = Gas.Clone();
            c.Certificate = Certificate.Clone();
            c.DamagedEquipment = new List<string>(DamagedEquipment);
            return c;
        }
    }

}
