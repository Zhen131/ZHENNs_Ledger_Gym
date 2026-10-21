using Gym.Core.Accounting;
using Gym.Core.Env;
using Gym.Core.Market;
using Gym.Runtime.Agents;
using System.Collections.Generic;
using NUnit.Framework;

namespace Gym.Tests.Runtime.Agents
{
    /// <summary>发给 TensorBoard 的 Trading/DialChanges 和 Trading/Decisions 取自环境的哪两个数（23 批）。</summary>
    public class EpisodeStatsDialTests
    {
        /// <summary>400 根小时线，收盘在 100 上下来回摆（±2 %），open 等于上一根的收盘。</summary>
        static CandleSeries Wobble()
        {
            var candles = new List<Candle>();
            double previous = 100;
            for (int i = 0; i < 400; i++)
            {
                double close = 100 * (1 + 0.02 * System.Math.Sin(i * 0.3));
                candles.Add(new Candle(1704067200000 + i * CandleSeries.HourMs, previous, System.Math.Max(previous, close),
                    System.Math.Min(previous, close), close, 1));
                previous = close;
            }
            return CandleSeries.FromCandles(candles);
        }

        [Test]
        public void TheKeys_AreTradingDialChangesAndTradingDecisions()
        {
            Assert.AreEqual("Trading/DialChanges", EpisodeStats.DialChangesKey);
            Assert.AreEqual("Trading/Decisions", EpisodeStats.DecisionsKey);
        }

        [Test]
        public void InDialMode_TheStatsCarryTheLevelChangesAndTheDecisions()
        {
            CandleSeries s = Wobble();
            var env = new TradingEnv(s, SymbolRules.BtcUsdt, 0, 399, dial: new DialSettings(new[] { 0.3, 0.5, 0.7 }, 24, 0.05));
            env.ResetForEvaluation(0, new CostModel(0.001));
            foreach (int level in new[] { 0, 2, 2, 1 }) env.StepDial(level);
            EpisodeStats stats = EpisodeStats.From(env);
            Assert.AreEqual(2, stats.DialChanges);
            Assert.AreEqual(4, stats.Decisions);
            Assert.AreEqual(96, stats.Steps);
        }

        [Test]
        public void InOrderMode_ThereAreNoLevelChanges_AndEveryStepIsADecision()
        {
            CandleSeries s = Wobble();
            var env = new TradingEnv(s, SymbolRules.BtcUsdt, 0, 399);
            env.ResetForEvaluation(0, new CostModel(0.001));
            for (int i = 0; i < 30; i++) env.Step(TradeAction.Hold, 0f);
            EpisodeStats stats = EpisodeStats.From(env);
            Assert.AreEqual(0, stats.DialChanges);
            Assert.AreEqual(30, stats.Decisions);
            Assert.AreEqual(30, stats.Steps);
        }
    }
}
