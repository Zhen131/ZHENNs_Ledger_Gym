using System;
using System.Globalization;
using System.Linq;
using Gym.Core.Accounting;
using Gym.Core.Env;
using Gym.Core.Market;
using NUnit.Framework;
using UnityEngine;
using Random = System.Random;

namespace Gym.Tests.Core.Env
{
    /// <summary>混训时选币的纯函数：只看本局的 seed 和币的个数。</summary>
    public class SymbolPickerTests
    {
        const int Seeds = 10_000;
        const int Count = 4;
        const double Low = 0.20;
        const double High = 0.30;

        static readonly int[] EdgeSeeds = { int.MinValue, -1, 0, 1, 7, 12345, int.MaxValue };

        [Test]
        public void OneSymbol_IsAlwaysIndexZero()
        {
            foreach (int seed in EdgeSeeds) Assert.AreEqual(0, SymbolPicker.Pick(seed, 1), $"seed {seed}");
            for (int seed = 0; seed < Seeds; seed++) Assert.AreEqual(0, SymbolPicker.Pick(seed, 1), $"seed {seed}");
        }

        [Test]
        public void TheSameSeed_PicksTheSameSymbol_FromTheTwoArgumentMix()
        {
            Assert.AreEqual(0x53594D42, SymbolPicker.StreamIndex, "the stream index is fixed: changing it changes every pick");
            for (int count = 2; count <= 5; count++)
            {
                foreach (int seed in EdgeSeeds.Concat(Enumerable.Range(-200, 400)))
                {
                    int first = SymbolPicker.Pick(seed, count);
                    Assert.AreEqual(first, SymbolPicker.Pick(seed, count), $"seed {seed}, {count} symbols, asked twice");
                    Assert.That(first, Is.InRange(0, count - 1));
                    // 定义：两参数的 Mix 建 System.Random，取 Next(个数)
                    Assert.AreEqual(new Random(SeedMixer.Mix(seed, SymbolPicker.StreamIndex)).Next(count), first, $"seed {seed}");
                }
            }
        }

        [Test]
        public void NoSymbols_IsRefused()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => SymbolPicker.Pick(0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => SymbolPicker.Pick(0, -1));
        }

        [Test]
        public void TenThousandConsecutiveSeeds_SpreadOverFourSymbolsWithinTwentyToThirtyPercentEach()
        {
            var counts = new int[Count];
            for (int seed = 0; seed < Seeds; seed++) counts[SymbolPicker.Pick(seed, Count)]++;
            Debug.Log($"[Gym] symbol picker test: seeds 0..{Seeds - 1}, {Count} symbols: {Shares(counts)}");
            for (int i = 0; i < Count; i++)
                Assert.That(counts[i] / (double)Seeds, Is.InRange(Low, High), $"index {i}: {Shares(counts)}");
        }

        /// <summary>
        /// 选币不能和训练起点、开局是否持币绑在一起：用 <see cref="TradingEnv.ResetForTraining"/> 本身从同一个种子
        /// 算出起点和开局是否持币（BTC、gym-config.json 现在的训练段 2017-08-17～2021-12-31、base 档、720 步一局、
        /// 一半概率开局持币），按起点分四桶、按是否持币分两桶，每一桶里各币的占比仍在 20 %～30 % 之间。
        /// </summary>
        [Test]
        public void WithinEachStartBucket_AndEachInitialHolding_TheSymbolsStayWithinTwentyToThirtyPercent()
        {
            TradingEnv env = TradingEnv.ForSegment(TestData.Btc, SymbolRules.BtcUsdt, SegmentSpec.Parse("train", "2017-08-17", "2021-12-31"));
            var cost = new CostModel(0.001, 0, 0);
            int lo = env.FirstDecisionIndex;
            int hi = env.Last - env.EpisodeLength;
            const int startBuckets = 4;
            var byStart = new int[startBuckets, Count];
            var byHolding = new int[2, Count];
            for (int seed = 0; seed < Seeds; seed++)
            {
                env.ResetForTraining(seed, cost);
                int pick = SymbolPicker.Pick(seed, Count);
                int bucket = Math.Min(startBuckets - 1, (int)((long)(env.StartIndex - lo) * startBuckets / (hi - lo + 1)));
                byStart[bucket, pick]++;
                byHolding[env.StartedWithCoin ? 1 : 0, pick]++;
            }
            for (int b = 0; b < startBuckets; b++)
                AssertBucket($"start bucket {b} of [{lo}, {hi}]", Row(byStart, b));
            AssertBucket("episodes that start in cash", Row(byHolding, 0));
            AssertBucket("episodes that start holding coin", Row(byHolding, 1));
        }

        static int[] Row(int[,] table, int row) => Enumerable.Range(0, table.GetLength(1)).Select(i => table[row, i]).ToArray();

        static void AssertBucket(string name, int[] counts)
        {
            int total = counts.Sum();
            Debug.Log($"[Gym] symbol picker test: {name}, {total} seeds: {Shares(counts)}");
            Assert.Greater(total, 1000, $"premise: {name} has enough seeds");
            for (int i = 0; i < counts.Length; i++)
                Assert.That(counts[i] / (double)total, Is.InRange(Low, High), $"{name}, index {i}: {Shares(counts)}");
        }

        static string Shares(int[] counts)
        {
            int total = counts.Sum();
            return string.Join(", ", counts.Select((c, i) =>
                $"{i}: {c} ({(100.0 * c / total).ToString("F2", CultureInfo.InvariantCulture)} %)"));
        }
    }
}
