namespace HeadLoss.Game.Buildings
{
    /// <summary>
    /// Everything saved about one building: the committed state (it earns), the design draft (what the player
    /// edits and previews), and the money spent on it.
    /// </summary>
    public sealed class BuildingRecord
    {
        public string BuildingId = "";
        /// <summary>What is built and paid for. Income comes from this state.</summary>
        public BuildingState Committed = new BuildingState();
        /// <summary>The design-mode draft. Equal to <see cref="Committed"/> when nothing is pending.</summary>
        public BuildingState Design = new BuildingState();
        /// <summary>Net money committed to the building (purchases − refunds), $.</summary>
        public double SpentTotal;
        /// <summary>The building has been online at least once; the budget cap no longer applies.</summary>
        public bool EverOnline;
        /// <summary>The requirement sheet has been shown (it opens by itself only the first time).</summary>
        public bool SheetSeen;
        /// <summary>Gross income earned by this building over its life, $.</summary>
        public double LifetimeIncome;
        /// <summary>The electrical panel has tripped: everything stops until the player resets it (<c>04-buildings.md</c>).</summary>
        public bool PanelTripped;
        /// <summary>Pipe stress, bursts and repairs (<c>08-failures.md</c>).</summary>
        public Failures.FailureState Failures = new Failures.FailureState();
        /// <summary>Random inspections and the energy rating (<c>10-events.md</c> §Inspections).</summary>
        public Inspections.InspectionState Inspection = new Inspections.InspectionState();

        /// <summary>A fresh record: anchors and pre-placed equipment, committed for free.</summary>
        public static BuildingRecord CreateFor(BuildingLayout layout)
        {
            BuildingState s = BuildingState.CreateFor(layout);
            return new BuildingRecord { BuildingId = layout.Id, Committed = s, Design = s.Clone() };
        }
    }
}
