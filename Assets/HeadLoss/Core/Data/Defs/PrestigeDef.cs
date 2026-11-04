using System.Collections.Generic;

namespace HeadLoss.Core.Data.Defs
{
    /// <summary><c>prestige.json</c>: the prestige tree (<c>03-city.md</c> §Prestige).</summary>
    public sealed class PrestigeDef
    {
        public List<PrestigeNodeDef> Nodes = new List<PrestigeNodeDef>();

        public PrestigeNodeDef? Find(string id) => Nodes.Find(n => n.Id == id);
    }

    /// <summary>A node of the tree: its cost per level (the list's length is its maximum level).</summary>
    public sealed class PrestigeNodeDef
    {
        public string Id = "";
        public string DisplayName = "";
        public string Description = "";
        /// <summary>Reputation cost of each level.</summary>
        public List<int> Costs = new List<int>();
        /// <summary>Other nodes that must be owned before this one (at least one level each).</summary>
        public int RequiresOtherNodes;
    }
}
