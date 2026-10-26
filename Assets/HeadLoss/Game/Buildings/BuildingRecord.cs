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

        /// <summary>A fresh record: anchors and pre-placed equipment, committed for free.</summary>
        public static BuildingRecord CreateFor(BuildingLayout layout)
        {
            BuildingState s = BuildingState.CreateFor(layout);
            return new BuildingRecord { BuildingId = layout.Id, Committed = s, Design = s.Clone() };
        }
    }
}
