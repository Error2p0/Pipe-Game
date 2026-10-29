namespace HeadLoss.Core
{
    /// <summary>
    /// Derived seeds: <c>hash(citySeed, "step-name")</c> (<c>03-city.md</c> §City generation), so changing one
    /// generation step does not reshuffle the others. FNV-1a 64 over the name, mixed with the seed by SplitMix64.
    /// </summary>
    public static class Seeds
    {
        /// <summary>A seed for a named step or item, derived from <paramref name="seed"/>.</summary>
        public static ulong Derive(ulong seed, string name)
        {
            ulong h = 14695981039346656037UL;
            foreach (char c in name) h = (h ^ c) * 1099511628211UL;
            return Mix(seed ^ Mix(h));
        }

        /// <summary>A seed for a numbered attempt or item, derived from <paramref name="seed"/>.</summary>
        public static ulong Derive(ulong seed, ulong index) => Mix(seed ^ Mix(index + 0x632BE59BD9B4E019UL));

        /// <summary>SplitMix64 finaliser.</summary>
        public static ulong Mix(ulong z)
        {
            unchecked
            {
                z += 0x9E3779B97F4A7C15UL;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }
    }
}
