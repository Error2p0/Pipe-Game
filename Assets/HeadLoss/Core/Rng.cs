using System;

namespace HeadLoss.Core
{
    /// <summary>
    /// PCG32 (XSH RR) seeded random number generator (O'Neill 2014). The only RNG allowed in
    /// game logic (CLAUDE.md rule 10). State is two ulongs so it can be saved and restored.
    /// </summary>
    public sealed class Rng
    {
        const ulong Multiplier = 6364136223846793005UL;

        /// <summary>Internal LCG state.</summary>
        public ulong State { get; private set; }
        /// <summary>Stream increment (always odd).</summary>
        public ulong Increment { get; private set; }

        /// <summary>Creates a generator from a seed and an optional stream id.</summary>
        public Rng(ulong seed, ulong stream = 0xda3e39cb94b95bdbUL)
        {
            State = 0;
            Increment = (stream << 1) | 1UL;
            NextUInt();
            State += seed;
            NextUInt();
        }

        Rng(ulong state, ulong increment, bool _)
        {
            State = state;
            Increment = increment | 1UL;
        }

        /// <summary>Restores a generator from a saved <see cref="State"/> and <see cref="Increment"/>.</summary>
        public static Rng FromState(ulong state, ulong increment) => new Rng(state, increment, true);

        /// <summary>Next uniformly distributed 32-bit value.</summary>
        public uint NextUInt()
        {
            ulong old = State;
            State = unchecked(old * Multiplier + Increment);
            uint xorShifted = (uint)(((old >> 18) ^ old) >> 27);
            int rot = (int)(old >> 59);
            return (xorShifted >> rot) | (xorShifted << ((-rot) & 31));
        }

        /// <summary>Uniform integer in [0, bound) without modulo bias.</summary>
        public uint NextUInt(uint bound)
        {
            if (bound == 0) throw new ArgumentOutOfRangeException(nameof(bound));
            uint threshold = (uint)(-(int)bound) % bound;
            while (true)
            {
                uint r = NextUInt();
                if (r >= threshold) return r % bound;
            }
        }

        /// <summary>Uniform integer in [minInclusive, maxExclusive).</summary>
        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
            return minInclusive + (int)NextUInt((uint)(maxExclusive - minInclusive));
        }

        /// <summary>Uniform double in [0, 1) with 53 bits of randomness.</summary>
        public double NextDouble()
        {
            ulong hi = NextUInt() >> 5;  // 27 bits
            ulong lo = NextUInt() >> 6;  // 26 bits
            return (hi * 67108864.0 + lo) * (1.0 / 9007199254740992.0);
        }

        /// <summary>Uniform double in [min, max).</summary>
        public double NextRange(double min, double max) => min + (max - min) * NextDouble();

        /// <summary>True with probability <paramref name="p"/>.</summary>
        public bool Chance(double p) => NextDouble() < p;
    }
}
