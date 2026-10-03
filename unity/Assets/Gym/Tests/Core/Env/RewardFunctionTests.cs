using System;
using Gym.Core.Accounting;
using Gym.Core.Env;
using Gym.Core.Market;
using NUnit.Framework;

namespace Gym.Tests.Core.Env
{
    public class RewardFunctionTests
    {
        static readonly SymbolRules Btc = SymbolRules.BtcUsdt;

        // ---- T-8 reward

        [Test]
        public void T08_RewardFormula()
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
        public void T08_UnclippedEpisodeRewardsSumToTheLogReturn()
        {
            // Hourly moves of at most 0.3 % keep every step inside ±1.
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
        public void T08_ClippedStepsAreCounted()
        {
            // Price 100, jumps to 105 at 50, back to 100 at 60, to 103 at 70.
            // Fully invested from step 1, each jump moves equity by more than 1 % → 3 clipped steps.
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
    }
}
