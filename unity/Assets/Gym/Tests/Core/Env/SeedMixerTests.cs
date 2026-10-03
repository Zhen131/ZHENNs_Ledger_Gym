using System;
using System.Collections.Generic;
using System.Linq;
using Gym.Core.Env;
using NUnit.Framework;

namespace Gym.Tests.Core.Env
{
    /// <summary>Seed mixing (Q03 / Q07): R-1 to R-4 of 07B.</summary>
    public class SeedMixerTests
    {
        const long Modulus = int.MaxValue; // System.Random's legacy generator works modulo 2^31 − 1

        // ---- R-1 SeedMixer matches the Python reference

        [Test]
        public void R01_MixMatchesThePythonReference()
        {
            // Reference (07C): z = x + 0x9E3779B97F4A7C15; z = (z ^ z>>30) * 0xBF58476D1CE4E5B9;
            // z = (z ^ z>>27) * 0x94D049BB133111EB; z ^= z>>31 (all mod 2^64); keep the low 31 bits.
            Assert.AreEqual(2065550767, SeedMixer.Mix(0)); // SplitMix64(0) = 0xE220A8397B1DCDAF
            Assert.AreEqual(151149761, SeedMixer.Mix(1));
            Assert.AreEqual(479680206, SeedMixer.Mix(2));
            Assert.AreEqual(701567392, SeedMixer.Mix(12345));
            Assert.AreEqual(459615264, SeedMixer.Mix(-1));
            Assert.AreEqual(639257575, SeedMixer.Mix(int.MaxValue));
            Assert.AreEqual(571453829, SeedMixer.Mix(int.MinValue));

            Assert.AreEqual(1911829812, SeedMixer.Mix(0, 0));
            Assert.AreEqual(1415491057, SeedMixer.Mix(0, 1));
            Assert.AreEqual(582130682, SeedMixer.Mix(7, 0));
            Assert.AreEqual(1136688097, SeedMixer.Mix(7, 15));
            Assert.AreEqual(354123802, SeedMixer.Mix(-5, 3));
        }

        [Test]
        public void R01_MixIsNonNegativeAndSpreadsNearbySeeds()
        {
            var seen = new HashSet<int>();
            for (int s = -500; s < 500; s++)
            {
                int m = SeedMixer.Mix(s);
                Assert.GreaterOrEqual(m, 0);
                seen.Add(m);
            }
            Assert.AreEqual(1000, seen.Count, "no collisions among 1000 nearby seeds");
            var perAgent = Enumerable.Range(0, 16).Select(i => SeedMixer.Mix(7, i)).ToList();
            Assert.AreEqual(16, perAgent.Distinct().Count());
        }

        // ---- R-4 the random baseline's draws for seeds 0, 1, 2 are not shifted copies

        /// <summary>
        /// The first 2 × <paramref name="steps"/> draws of a generator as the integers behind
        /// NextDouble (Sample × (2^31 − 1)). The random baseline draws exactly two per step:
        /// the choice, then the fraction.
        /// </summary>
        static long[] Draws(Random random, int steps) =>
            Enumerable.Range(0, 2 * steps).Select(_ => (long)Math.Round(random.NextDouble() * Modulus)).ToArray();

        /// <summary>
        /// Criterion: seeds 0, 1, 2 are "shifted copies" at draw k when
        /// draw₂[k] − draw₁[k] ≡ draw₁[k] − draw₀[k] (mod 2^31 − 1). Counts such k.
        /// </summary>
        static int ArithmeticPositions(long[] d0, long[] d1, long[] d2)
        {
            int count = 0;
            for (int k = 0; k < d0.Length; k++)
            {
                long step1 = ((d1[k] - d0[k]) % Modulus + Modulus) % Modulus;
                long step2 = ((d2[k] - d1[k]) % Modulus + Modulus) % Modulus;
                if (step1 == step2) count++;
            }
            return count;
        }

        [Test]
        public void R04_RandomBaselineDrawsAreNotShiftedCopies()
        {
            const int steps = 20;
            int raw = ArithmeticPositions(Draws(new Random(0), steps), Draws(new Random(1), steps), Draws(new Random(2), steps));
            Assert.AreEqual(2 * steps, raw, "premise: without mixing every one of the first 40 draws is an arithmetic step");

            int mixed = ArithmeticPositions(
                Draws(new Random(SeedMixer.Mix(0)), steps),
                Draws(new Random(SeedMixer.Mix(1)), steps),
                Draws(new Random(SeedMixer.Mix(2)), steps));
            Assert.AreEqual(0, mixed, "with mixing none of the first 40 draws is");
            TestContext.WriteLine($"R-4: arithmetic positions among the first {2 * steps} draws: unmixed {raw}, mixed {mixed}");
        }
    }
}
