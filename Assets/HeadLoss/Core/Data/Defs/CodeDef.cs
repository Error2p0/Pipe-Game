using System.Collections.Generic;

namespace HeadLoss.Core.Data.Defs
{
    /// <summary>Entry in <c>codes.json</c> (<c>09-codes.md</c>).</summary>
    public sealed class CodeDef
    {
        public string Id = "";
        public string DisplayName = "";
        /// <summary>layout, operating or city.</summary>
        public string Kind = "";
        /// <summary>Tiers where the code is in force.</summary>
        public List<int> Tiers = new List<int>();
        /// <summary>Building types that add the code outside its tiers.</summary>
        public List<string> BuildingTypes = new List<string>();
        /// <summary>Numeric parameters (names in camelCase, SI unless the name says otherwise).</summary>
        public Dictionary<string, double> Parameters = new Dictionary<string, double>();
        /// <summary>One-line rule shown on the requirement sheet.</summary>
        public string Reason = "";
        /// <summary>Why it exists (requirement sheet).</summary>
        public string Why = "";
    }
}
