namespace Gym.Core
{
    /// <summary>
    /// Scrambles seeds before they reach <see cref="System.Random"/> (Q03).
    ///
    /// .NET's seeded <c>System.Random</c> is linear in its seed: for seeds s and
    /// s + 1, every output of the sequence differs by the same fixed amount (modulo
    /// 2³¹ − 1). Consecutive seeds therefore give shifted copies of one sequence,
    /// not independent ones. Passing each seed through SplitMix64 first removes
    /// that structure while keeping every seed reproducible.
    /// </summary>
    public static class SeedMixer
    {
        /// <summary>
        /// SplitMix64 of the seed taken as a 64-bit unsigned integer (negative seeds
        /// sign-extended, as <c>unchecked((ulong)seed)</c> does); returns the low 31 bits,
        /// a non-negative int.
        /// </summary>
        public static int Mix(int seed) => Low31(SplitMix64(unchecked((ulong)seed)));

        /// <summary>
        /// A seed for item <paramref name="b"/> of base seed <paramref name="a"/>: Mix(a) in the
        /// high 32 bits and b's 32-bit pattern in the low 32 bits, through SplitMix64 again.
        /// </summary>
        public static int Mix(int a, int b) =>
            Low31(SplitMix64(((ulong)(uint)Mix(a) << 32) | (uint)b));

        static ulong SplitMix64(ulong x)
        {
            unchecked
            {
                ulong z = x + 0x9E3779B97F4A7C15UL;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        static int Low31(ulong value) => (int)(value & 0x7FFFFFFFUL);
    }
}
