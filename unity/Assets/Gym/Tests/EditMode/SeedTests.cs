using System;
using System.Collections.Generic;
using System.Linq;
using Gym.Core.Accounting;
using Gym.Core.Env;
using Gym.Core.Evaluation;
using Gym.Core.Market;
using NUnit.Framework;

namespace Gym.Tests.EditMode
{
    /// <summary>Seed mixing (Q03 / Q07): R-1 to R-4 of 07B.</summary>
    public class SeedTests
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

        // ---- R-2 training starts for seeds 0..999 are no longer a lattice

        [Test]
        public void R02_TrainingStartsForConsecutiveSeedsAreSpread()
        {
            TradingEnv env = TestData.TrainEnv();
            int lo = Math.Max(env.First, ObservationBuilder.Lookback), hi = env.Last - TradingEnv.TrainingEpisodeLength;

            // Premise: without mixing, the start for seed s is a lattice in s; adjacent starts differ
            // by one of a handful of values (two step sizes, ±1 from rounding).
            var rawStarts = Enumerable.Range(0, 1000).Select(s => new Random(s).Next(lo, hi + 1)).ToList();
            int rawDistinct = rawStarts.Zip(rawStarts.Skip(1), (a, b) => b - a).Distinct().Count();
            Assert.LessOrEqual(rawDistinct, 4, "premise: unmixed seeds give a lattice");

            var starts = new List<int>();
            int withCoin = 0;
            for (int seed = 0; seed < 1000; seed++)
            {
                env.Reset(seed, false, new CostModel());
                Assert.That(env.StartIndex, Is.InRange(lo, hi));
                starts.Add(env.StartIndex);
                if (env.StartedWithCoin) withCoin++;
            }
            int distinct = starts.Zip(starts.Skip(1), (a, b) => b - a).Distinct().Count();
            Assert.Greater(distinct, 100, $"adjacent start differences: {distinct} distinct values");
            Assert.That(withCoin, Is.InRange(400, 600));
            TestContext.WriteLine($"R-2: unmixed {rawDistinct} distinct adjacent differences, mixed {distinct}; {withCoin}/1000 held coin");
        }

        // ---- R-3 same seed, same actions, same bits

        [Test]
        public void R03_SameSeedAndActionsStillGiveIdenticalBits()
        {
            TradingEnv a = TestData.TrainEnv(), b = TestData.TrainEnv();
            var cost = new CostModel(0.001, 0.5, 0.0005);
            a.Reset(12345, false, cost);
            b.Reset(12345, false, cost);
            Assert.AreEqual(a.StartIndex, b.StartIndex);
            Assert.AreEqual(a.StartedWithCoin, b.StartedWithCoin);
            Assert.IsTrue(TestData.SameBits(a.Account.Cash, b.Account.Cash));
            var actions = new Random(3);
            while (!a.Done)
            {
                int branch = actions.Next(3);
                float x = (float)(actions.NextDouble() * 2 - 1);
                a.Step(branch, x);
                b.Step(branch, x);
            }
            Assert.AreEqual(a.EquityCurve.Count, b.EquityCurve.Count);
            for (int i = 0; i < a.EquityCurve.Count; i++)
                Assert.IsTrue(TestData.SameBits(a.EquityCurve[i], b.EquityCurve[i]), $"equity[{i}]");
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

        [Test]
        public void R04_RandomBaselineUsesTheMixedStream()
        {
            // Every filled order of RunRandom(seed 0) carries the fraction drawn at its step
            // from Random(Mix(0)): draw 2k is the choice, draw 2k + 1 the fraction.
            CandleSeries s = TestData.RandomWalk(300, 41, 0.01);
            var env = new TradingEnv(s, SymbolRules.BtcUsdt, 0, 299);
            Baselines.RunRandom(env, new CostModel(), 0);
            var stream = new Random(SeedMixer.Mix(0));
            var fractions = new List<double>();
            for (int k = 0; k < env.StepCount; k++)
            {
                stream.NextDouble();
                fractions.Add(stream.NextDouble());
            }
            Assert.Greater(env.Trades.Count, 5);
            foreach (TradeRecord trade in env.Trades)
                Assert.AreEqual(ActionCodec.Fraction(ActionCodec.FromFraction(fractions[trade.Step])), trade.Fraction, 1e-12,
                    $"step {trade.Step}");
        }
    }
}
