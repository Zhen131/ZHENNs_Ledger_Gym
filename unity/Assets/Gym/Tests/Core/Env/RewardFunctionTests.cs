using System;
using System.Collections.Generic;
using Gym.Core.Accounting;
using Gym.Core.Env;
using Gym.Core.Market;
using NUnit.Framework;

namespace Gym.Tests.Core.Env
{
    public class RewardFunctionTests
    {
        static readonly SymbolRules Btc = SymbolRules.BtcUsdt;

        // ---- reward

        [Test]
        public void Reward_IsTheClippedScaledLogReturn()
        {
            double r = RewardFunction.Compute(10_000, 10_050, out bool clipped);
            Assert.AreEqual(100 * Math.Log(1.005), r, 1e-12);
            Assert.IsFalse(clipped);
            Assert.AreEqual(1, RewardFunction.Compute(10_000, 10_200, out clipped));
            Assert.IsTrue(clipped);
            Assert.AreEqual(-1, RewardFunction.Compute(10_000, 9_800, out clipped));
            Assert.IsTrue(clipped);
            Assert.AreEqual(0, RewardFunction.Compute(10_000, 10_000, out clipped));
            Assert.IsFalse(clipped);
            Assert.Throws<ArgumentOutOfRangeException>(() => RewardFunction.Compute(0, 1, out _));
        }

        [Test]
        public void UnclippedEpisode_RewardsSumToTheLogReturn()
        {
            // 每小时最多变动 0.3 %，所以每个 step 都落在 ±1 以内。
            CandleSeries s = TestData.RandomWalk(800, 8, 0.003);
            var env = new TradingEnv(s, Btc, 0, s.Count - 1);
            env.ResetForEvaluation(0, new CostModel());
            var random = new System.Random(80);
            double sum = 0;
            while (!env.Done)
                sum += env.Step((TradeAction)random.Next(3), (float)(random.NextDouble() * 2 - 1)).Reward;

            Assert.AreEqual(0, env.ClippedRewards);
            Assert.Greater(env.Account.Trades, 10, "the episode should actually trade");
            double first = env.EquityCurve[0], last = env.EquityCurve[env.EquityCurve.Count - 1];
            double expected = 100 * Math.Log(last / first);
            Assert.Greater(Math.Abs(expected), 0.01, "premise: the episode moved equity");
            Assert.That(Math.Abs(sum - expected) / Math.Abs(expected), Is.LessThanOrEqualTo(1e-6));
            Assert.AreEqual(sum, env.RewardSum, 1e-12);
        }

        [Test]
        public void ClippedSteps_AreCounted()
        {
            // 价格 100，在 50 跳到 105，在 60 回到 100，在 70 到 103。
            // 从第 1 个 step 起满仓，每次跳动都让 equity 变动超过 1 % → 3 个被截断的 step。
            CandleSeries s = TestData.Synthetic(100, k => k < 50 ? 100 : k < 60 ? 105 : k < 70 ? 100 : 103);
            var env = new TradingEnv(s, Btc, 0, 99);
            env.ResetForEvaluation(0, new CostModel());
            Assert.IsTrue(env.Step(TradeAction.Buy, 1f).Traded);
            int clippedSeen = 0;
            while (!env.Done)
            {
                StepResult r = env.Step(TradeAction.Hold, 0f);
                if (r.RewardClipped)
                {
                    clippedSeen++;
                    Assert.AreEqual(1, Math.Abs(r.Reward));
                }
            }
            Assert.AreEqual(3, env.ClippedRewards);
            Assert.AreEqual(3, clippedSeen);
        }

        // ---- 风险系数 λ：得分 = r − λ × min(r, 0)²

        /// <summary>改动前（14 批之前）RewardFunction.Compute 的原样副本，用来锁死「λ = 0 时逐位不变」。</summary>
        static double ComputeAsBefore14(double equityBefore, double equityAfter, out bool clipped)
        {
            if (!(equityBefore > 0) || double.IsInfinity(equityBefore))
                throw new ArgumentOutOfRangeException(nameof(equityBefore), equityBefore, "Must be a finite value > 0.");
            if (!(equityAfter > 0))
            {
                clipped = true;
                return -RewardFunction.Limit;
            }

            double raw = RewardFunction.Scale * Math.Log(equityAfter / equityBefore);
            clipped = raw > RewardFunction.Limit || raw < -RewardFunction.Limit;
            return Math.Max(-RewardFunction.Limit, Math.Min(RewardFunction.Limit, raw));
        }

        /// <summary>手挑的 E₀、E₁：赚、亏、不动、两头被截断、E₁ = 0、E₁ 为负、很小的 E₀。</summary>
        static readonly (double before, double after)[] HandPicked =
        {
            (10_000, 10_050), (10_000, 9_950), (10_000, 9_990), (10_000, 10_000), (10_000, 10_000.000001),
            (10_000, 10_200), (10_000, 9_800), (10_000, 0), (10_000, -3), (1e-9, 2e-9), (12_345.678, 12_345.677),
        };

        [Test]
        public void ZeroRiskAversion_IsBitForBitTheRewardBeforeThisChange_ThroughTheOldAndTheNewEntry()
        {
            var pairs = new List<(double before, double after)>(HandPicked);
            var random = new System.Random(14);
            for (int i = 0; i < 5000; i++)
            {
                double before = 1 + random.NextDouble() * 50_000;
                pairs.Add((before, before * (1 + (random.NextDouble() * 2 - 1) * 0.03))); // ±3 %：有截断，也有没截断的
            }

            int clippedSeen = 0, negativeSeen = 0;
            foreach ((double before, double after) in pairs)
            {
                double expected = ComputeAsBefore14(before, after, out bool expectedClipped);
                double old = RewardFunction.Compute(before, after, out bool oldClipped);
                double withZero = RewardFunction.Compute(before, after, 0, out bool zeroClipped);
                double withPenalty = RewardFunction.Compute(before, after, 0, out bool penaltyClipped, out double penalty);
                string at = $"E0={before:R}, E1={after:R}";
                Assert.IsTrue(TestData.SameBits(expected, old), at + " (old entry)");
                Assert.IsTrue(TestData.SameBits(expected, withZero), at + " (lambda 0)");
                Assert.IsTrue(TestData.SameBits(expected, withPenalty), at + " (lambda 0, with the penalty)");
                Assert.IsTrue(TestData.SameBits(0.0, penalty), at + " penalty");
                Assert.AreEqual(expectedClipped, oldClipped, at);
                Assert.AreEqual(expectedClipped, zeroClipped, at);
                Assert.AreEqual(expectedClipped, penaltyClipped, at);
                if (expectedClipped) clippedSeen++;
                if (expected < 0) negativeSeen++;
            }
            Assert.Greater(clippedSeen, 100, "premise: some rewards are clipped");
            Assert.Greater(negativeSeen, 1000, "premise: many rewards are losses");
        }

        [Test]
        public void PositiveRiskAversion_LeavesGainsAlone_AndTakesLambdaTimesTheSquaredLossFromLosses()
        {
            // 手算（40 位十进制）：r = 100 × ln(E₁ ÷ 10000)，得分 = r − λ × r²（r < 0 时）。
            foreach (double lambda in new[] { 0.005, 0.01, 0.02, 1.0 })
            {
                // 赚的步：r = 100 × ln(1.005) = 0.4987541511039…，得分不变（逐位）
                double gain = RewardFunction.Compute(10_000, 10_050, lambda, out bool gainClipped, out double gainPenalty);
                Assert.IsTrue(TestData.SameBits(RewardFunction.Compute(10_000, 10_050, out _), gain), $"lambda {lambda}");
                Assert.AreEqual(0.4987541511039, gain, 1e-12);
                Assert.AreEqual(0.0, gainPenalty);
                Assert.IsFalse(gainClipped);
                // 不动的步：r = 0，没有罚款
                Assert.AreEqual(0.0, RewardFunction.Compute(10_000, 10_000, lambda, out _, out double flatPenalty));
                Assert.AreEqual(0.0, flatPenalty);
                // 向上被截断：r = 1，不罚
                Assert.AreEqual(1.0, RewardFunction.Compute(10_000, 10_200, lambda, out bool upClipped));
                Assert.IsTrue(upClipped);
            }

            // 亏的步：r = 100 × ln(0.995) = −0.5012541823544282…，r² = 0.2512557553278…
            Assert.AreEqual(-0.5037667399077063, RewardFunction.Compute(10_000, 9_950, 0.01, out bool c1, out double p1), 1e-12);
            Assert.AreEqual(0.0025125575532781, p1, 1e-12);
            Assert.IsFalse(c1);
            Assert.AreEqual(-0.5062792974609843, RewardFunction.Compute(10_000, 9_950, 0.02, out _), 1e-12);
            Assert.AreEqual(-0.7525099376822346, RewardFunction.Compute(10_000, 9_950, 1.0, out _), 1e-12);
            // r = 100 × ln(0.999) = −0.1000500333583534…
            Assert.AreEqual(-0.1001000834042284, RewardFunction.Compute(10_000, 9_990, 0.005, out _), 1e-12);
            Assert.AreEqual(-0.1002502335418535, RewardFunction.Compute(10_000, 9_990, 0.02, out _), 1e-12);
            // 向下被截断：r = −1，得分 −1 − λ，clipped 照旧只看 r
            Assert.AreEqual(-1.02, RewardFunction.Compute(10_000, 9_800, 0.02, out bool c2, out double p2), 1e-15);
            Assert.AreEqual(0.02, p2, 1e-15);
            Assert.IsTrue(c2);

            // 罚款函数本身
            Assert.AreEqual(0.0, RewardFunction.Penalty(0.3, 0.02));
            Assert.AreEqual(0.0, RewardFunction.Penalty(0, 0.02));
            Assert.AreEqual(0.02 * 0.25, RewardFunction.Penalty(-0.5, 0.02), 1e-18);
            Assert.AreEqual(0.0, RewardFunction.Penalty(-0.5, 0));
        }

        [TestCase(0.0)]
        [TestCase(0.005)]
        [TestCase(0.01)]
        [TestCase(0.02)]
        [TestCase(3.5)]
        public void NonPositiveEquityAfter_ScoresMinusOneMinusLambda(double lambda)
        {
            foreach (double after in new[] { 0.0, -3.0, double.NaN })
            {
                double score = RewardFunction.Compute(10_000, after, lambda, out bool clipped, out double penalty);
                Assert.AreEqual(-1 - lambda, score, 1e-15, $"E1={after}");
                Assert.AreEqual(lambda, penalty, 1e-15, $"E1={after}");
                Assert.IsTrue(clipped, $"E1={after}");
            }
            Assert.IsTrue(TestData.SameBits(-1.0, RewardFunction.Compute(10_000, 0, 0, out _)), "lambda 0 gives exactly -1");
        }

        [TestCase(-0.001)]
        [TestCase(-1.0)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        public void NegativeNaNOrInfiniteRiskAversion_Throws(double lambda)
        {
            Assert.IsFalse(RewardFunction.IsValidRiskAversion(lambda));
            Assert.Throws<ArgumentOutOfRangeException>(() => RewardFunction.CheckRiskAversion(lambda));
            Assert.Throws<ArgumentOutOfRangeException>(() => RewardFunction.Compute(10_000, 10_050, lambda, out _));
            Assert.Throws<ArgumentOutOfRangeException>(() => RewardFunction.Compute(10_000, 9_950, lambda, out _, out _));
            Assert.Throws<ArgumentOutOfRangeException>(() => RewardFunction.Penalty(-0.5, lambda));
        }

        [Test]
        public void FiniteNonNegativeRiskAversion_IsValid()
        {
            foreach (double lambda in new[] { 0.0, 1e-300, 0.005, 0.01, 0.02, 100, 1e6, double.MaxValue })
            {
                Assert.IsTrue(RewardFunction.IsValidRiskAversion(lambda), lambda.ToString("R"));
                Assert.DoesNotThrow(() => RewardFunction.CheckRiskAversion(lambda));
            }
        }
    }
}
