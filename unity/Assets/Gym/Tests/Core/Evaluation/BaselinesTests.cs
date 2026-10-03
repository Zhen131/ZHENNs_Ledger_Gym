using System;
using System.Collections.Generic;
using Gym.Core.Accounting;
using Gym.Core.Env;
using Gym.Core.Evaluation;
using Gym.Core.Market;
using NUnit.Framework;

namespace Gym.Tests.Core.Evaluation
{
    public class BaselinesTests
    {
        static readonly SymbolRules Btc = SymbolRules.BtcUsdt;

        // ---- 对照组

        [Test]
        public void BuyAndHoldOnARisingMarket_EarnsTheMoveLessOneFee()
        {
            // close[k] = 100 × 1.001^k，open[k] = close[k − 1]。评估从 t = 32 开始，
            // 在 open[33] = close[32] 买入；账户在 close[199] 结束。
            //   预期 = (close[199] ÷ close[32]) ÷ (1 + fee) − 1   （fee 0.1 %，只在买入时付一次）
            CandleSeries s = TestData.Synthetic(200, k => 100 * Math.Pow(1.001, k), k => k == 0 ? 100 : 100 * Math.Pow(1.001, k - 1));
            var env = new TradingEnv(s, Btc, 0, 199);
            var cost = new CostModel(0.001);
            EpisodeMetrics m = Baselines.RunBuyAndHold(env, cost);
            double move = s.CloseAt(199) / s.CloseAt(32);
            Assert.AreEqual(move / 1.001 - 1, m.TotalReturn, 1e-5);
            Assert.AreEqual(1, m.Trades);
            Assert.AreEqual(167, m.Steps);
            Assert.AreEqual(1.0, m.Exposure);
            Assert.AreEqual(0, m.MaxDrawdown, 1e-12);

            EpisodeMetrics cash = Baselines.RunCash(env, cost);
            Assert.AreEqual(0, cash.TotalReturn);
            Assert.AreEqual(0, cash.Trades);
            Assert.AreEqual(0, cash.FeesPaid);
            Assert.AreEqual(0, cash.Exposure);
        }

        [Test]
        public void RandomBaseline_IsReproduciblePerSeed()
        {
            CandleSeries s = TestData.RandomWalk(600, 33, 0.01);
            var env = new TradingEnv(s, Btc, 0, 599);
            var cost = new CostModel(0.001, 0.5, 0.0005);
            EpisodeMetrics a = Baselines.RunRandom(env, cost, 7);
            EpisodeMetrics b = Baselines.RunRandom(env, cost, 7);
            Assert.IsTrue(TestData.SameBits(a.TotalReturn, b.TotalReturn));
            Assert.IsTrue(TestData.SameBits(a.SharpeAnnualized, b.SharpeAnnualized));
            Assert.IsTrue(TestData.SameBits(a.FeesPaid, b.FeesPaid));
            Assert.AreEqual(a.Trades, b.Trades);
            Assert.AreEqual(a.Rejected, b.Rejected);
            Assert.Greater(a.Trades, 10);
            EpisodeMetrics c = Baselines.RunRandom(env, cost, 8);
            Assert.IsFalse(TestData.SameBits(a.TotalReturn, c.TotalReturn), "a different seed should trade differently");
        }

        [Test]
        public void RandomBaseline_DrawsFromTheMixedSeedStream()
        {
            // RunRandom(seed 0) 的每一笔成交的订单，都带着它那个 step 从 Random(Mix(0)) 取到的比例：
            // 第 2k 次取的是选择，第 2k + 1 次取的是比例。
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
