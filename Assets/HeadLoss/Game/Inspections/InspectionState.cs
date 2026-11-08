using System.Collections.Generic;

namespace HeadLoss.Game.Inspections
{
    /// <summary>A building's random inspections and energy rating, saved with its record (<c>10-events.md</c> §Inspections).</summary>
    public sealed class InspectionState
    {
        /// <summary>An inspector is on the way: seconds of the walk left (cosmetic; 0 = none).</summary>
        public double WalkLeft;
        /// <summary>A passed inspection's pay bonus: seconds left (real time, also while away).</summary>
        public double BonusLeft;
        /// <summary>The last inspection failed and its findings are not fixed yet.</summary>
        public bool Failed;
        /// <summary>Seconds left to fix the findings before the second fine (pauses while away).</summary>
        public double DeadlineLeft;
        /// <summary>The deadline passed: the building stays offline until the findings are fixed.</summary>
        public bool Closed;
        /// <summary>What the inspector found, one calm line each.</summary>
        public List<string> Findings = new List<string>();
        /// <summary>Random rolls drawn so far (each roll has its own seeded stream).</summary>
        public ulong Counter;
        /// <summary>Energy rating grade, "" = not rated.</summary>
        public string Grade = "";
        /// <summary>The rating's score, <c>P_actual / P_reference</c>.</summary>
        public double Score;
        /// <summary>The rating has been worked out for what is built (false after a Commit, until the next one).</summary>
        public bool Rated;

        public InspectionState Clone()
        {
            var c = (InspectionState)MemberwiseClone();
            c.Findings = new List<string>(Findings);
            return c;
        }
    }
}
