using System;
using System.Collections.Generic;
using System.Linq;
using Gym.Core.Env;
using NUnit.Framework;

namespace Gym.Tests.Core.Env
{
    /// <summary>seed 的打散。</summary>
    public class SeedMixerTests
    {
        const long Modulus = int.MaxValue; // System.Random 的旧版生成器按模 2^31 − 1 运算

        // ---- SeedMixer 和 Python 参考实现一致

        [Test]
        public void Mix_MatchesThePythonReference()
        {
            // 参考实现：z = x + 0x9E3779B97F4A7C15; z = (z ^ z>>30) * 0xBF58476D1CE4E5B9;
            // z = (z ^ z>>27) * 0x94D049BB133111EB; z ^= z>>31（全部 mod 2^64）；取低 31 位。
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
        public void NearbySeeds_MixToDistinctNonNegativeValues()
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

        // ---- 随机对照组用 seed 0、1、2 取到的数不是彼此平移的副本

        /// <summary>
        /// 一个生成器的前 2 × <paramref name="steps"/> 次取数，换成 NextDouble 背后的整数
        /// （Sample × (2^31 − 1)）。随机对照组每个 step 正好取两个数：先取选择，再取比例。
        /// </summary>
        static long[] Draws(Random random, int steps) =>
            Enumerable.Range(0, 2 * steps).Select(_ => (long)Math.Round(random.NextDouble() * Modulus)).ToArray();

        /// <summary>
        /// 判据：当 draw₂[k] − draw₁[k] ≡ draw₁[k] − draw₀[k] (mod 2^31 − 1) 时，seed 0、1、2 在第 k 次取数上
        /// 是「平移副本」。数出这样的 k 有几个。
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
        public void MixedSeedsZeroOneTwo_DrawStreamsThatAreNotShiftedCopies()
        {
            const int steps = 20;
            int raw = ArithmeticPositions(Draws(new Random(0), steps), Draws(new Random(1), steps), Draws(new Random(2), steps));
            Assert.AreEqual(2 * steps, raw, "premise: without mixing every one of the first 40 draws is an arithmetic step");

            int mixed = ArithmeticPositions(
                Draws(new Random(SeedMixer.Mix(0)), steps),
                Draws(new Random(SeedMixer.Mix(1)), steps),
                Draws(new Random(SeedMixer.Mix(2)), steps));
            Assert.AreEqual(0, mixed, "with mixing none of the first 40 draws is");
            TestContext.WriteLine($"arithmetic positions among the first {2 * steps} draws: unmixed {raw}, mixed {mixed}");
        }
    }
}
