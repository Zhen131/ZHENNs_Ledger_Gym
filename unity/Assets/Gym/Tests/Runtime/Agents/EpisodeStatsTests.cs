using System.Collections.Generic;
using Gym.Core.Accounting;
using Gym.Core.Env;
using Gym.Core.Market;
using Gym.Runtime.Agents;
using NUnit.Framework;

namespace Gym.Tests.Runtime.Agents
{
    /// <summary>发给 TensorBoard 的 Trading/RiskAversion 和 Trading/RiskPenalty 取自环境的哪两个数。</summary>
    public class EpisodeStatsTests
    {
        /// <summary>34 根收盘 100，之后 99.9、99.8；base 档从 32 号起决策，第 1 步全仓买入（无费用），之后两步各亏约 0.1 %。</summary>
        static TradingEnv LosingEnv()
        {
            var closes = new List<double>();
            for (int i = 0; i < 34; i++) closes.Add(100);
            closes.Add(99.9);
            closes.Add(99.8);
            var candles = new List<Candle>();
            for (int i = 0; i < closes.Count; i++)
                candles.Add(new Candle(1704067200000 + i * CandleSeries.HourMs, closes[i], closes[i], closes[i], closes[i], 1));
            CandleSeries s = CandleSeries.FromCandles(candles);
            return new TradingEnv(s, SymbolRules.BtcUsdt, 0, s.Count - 1);
        }

        [Test]
        public void TheStats_CarryTheEpisodesRiskAversionAndItsPenaltySum()
        {
            TradingEnv env = LosingEnv();
            env.ResetForEvaluation(0, new CostModel(0, 0, 0), 0.02);
            env.Step(TradeAction.Buy, 1f);
            while (!env.Done) env.Step(TradeAction.Hold, 0f);
            Assert.Greater(env.RiskPenaltySum, 0, "premise: the episode lost");

            EpisodeStats stats = EpisodeStats.From(env);
            Assert.AreEqual(0.02, stats.RiskAversion);
            Assert.AreEqual(env.RiskPenaltySum, stats.RiskPenalty);
            // 两步的 r = 100 × ln(99.9 ÷ 100) 和 100 × ln(99.8 ÷ 99.9)，罚款 0.02 × r²
            double r1 = 100 * System.Math.Log(0.999), r2 = 100 * System.Math.Log(99.8 / 99.9);
            Assert.AreEqual(0.02 * (r1 * r1 + r2 * r2), stats.RiskPenalty, 1e-12);
            Assert.AreEqual(3, stats.Steps);
        }

        [Test]
        public void WithoutRiskAversion_TheStatsSayZero()
        {
            TradingEnv env = LosingEnv();
            env.ResetForEvaluation(0, new CostModel(0, 0, 0));
            env.Step(TradeAction.Buy, 1f);
            while (!env.Done) env.Step(TradeAction.Hold, 0f);

            EpisodeStats stats = EpisodeStats.From(env);
            Assert.AreEqual(0.0, stats.RiskAversion);
            Assert.AreEqual(0.0, stats.RiskPenalty);
        }
    }
}
