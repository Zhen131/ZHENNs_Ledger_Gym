using System;
using System.Collections.Generic;
using System.Linq;
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

        // ---- 三条简单规则

        /// <summary>每小时乘 <paramref name="factor"/> 的序列；open[k] = close[k − 1]。</summary>
        static CandleSeries Geometric(int count, double factor) =>
            TestData.Synthetic(count, k => 100 * Math.Pow(factor, k), k => 100 * Math.Pow(factor, Math.Max(0, k - 1)));

        /// <summary>小时对数收益 +a、−a 交替的序列：任何连续偶数个收益的总体标准差都正好是 a。</summary>
        static CandleSeries Zigzag(int count, double a)
        {
            var closes = new double[count];
            closes[0] = 100;
            for (int k = 1; k < count; k++) closes[k] = closes[k - 1] * Math.Exp(k % 2 == 1 ? a : -a);
            return TestData.Synthetic(count, k => closes[k], k => k == 0 ? closes[0] : closes[k - 1]);
        }

        static readonly CostModel Fee = new CostModel(0.001);

        static void AssertOrder(TradeAction action, double fraction, (TradeAction action, double fraction) actual, string what)
        {
            Assert.AreEqual(action, actual.action, what);
            Assert.AreEqual(fraction, actual.fraction, 1e-12, what);
        }

        [Test]
        public void RuleOrder_MovesTowardTheTargetOnlyOutsideTheBand()
        {
            // 现金 6000、币值 4000：p = 0.4
            AssertOrder(TradeAction.Buy, 1, Baselines.RuleOrder(1, 6000, 4000), "target 1: min(1, 0.6 × 10000 ÷ 6000)");
            AssertOrder(TradeAction.Buy, 1.0 / 3, Baselines.RuleOrder(0.6, 6000, 4000), "target 0.6: 0.2 × 10000 ÷ 6000");
            AssertOrder(TradeAction.Hold, 0, Baselines.RuleOrder(0.45, 6000, 4000), "target 0.45: within the band");
            AssertOrder(TradeAction.Hold, 0, Baselines.RuleOrder(0.35, 6000, 4000), "target 0.35: within the band");
            AssertOrder(TradeAction.Sell, 0.375, Baselines.RuleOrder(0.25, 6000, 4000), "target 0.25: 0.15 ÷ 0.4");
            AssertOrder(TradeAction.Sell, 1, Baselines.RuleOrder(0, 6000, 4000), "target 0: sell everything");
            AssertOrder(TradeAction.Hold, 0, Baselines.RuleOrder(0, 10000, 0), "in cash, target 0");
        }

        [Test]
        public void Momentum_HoldsWhenThePriceIsAboveItsLevel90DaysAgo()
        {
            CandleSeries up = Geometric(2600, 1.0001);
            var env = new TradingEnv(up, Btc, 2160, 2599);
            EpisodeMetrics m = Baselines.RunMomentum(env, Fee);
            Assert.AreEqual(1, m.Trades);
            Assert.AreEqual(TradeAction.Buy, env.Trades[0].Side);
            Assert.AreEqual(0, env.Trades[0].Step);
            Assert.AreEqual(1.0, env.Trades[0].Fraction, 1e-6);
            Assert.AreEqual(1.0, m.Exposure);
        }

        [Test]
        public void Momentum_StaysInCashWhenThePriceIsBelowItsLevel90DaysAgo()
        {
            var env = new TradingEnv(Geometric(2600, 0.9999), Btc, 2160, 2599);
            EpisodeMetrics m = Baselines.RunMomentum(env, Fee);
            Assert.AreEqual(0, m.Trades);
            Assert.AreEqual(0, m.Exposure);
            Assert.AreEqual(0, m.TotalReturn);
        }

        [Test]
        public void Momentum_SellsEverythingWhenTheTrendTurns()
        {
            // 涨到第 2400 根，然后跌：到 close[t] ≤ close[t − 2160] 时全部卖出
            double Close(int k) => k <= 2400 ? 100 * Math.Pow(1.0001, k) : 100 * Math.Pow(1.0001, 2400) * Math.Pow(0.999, k - 2400);
            CandleSeries s = TestData.Synthetic(3000, Close, k => Close(Math.Max(0, k - 1)));
            var env = new TradingEnv(s, Btc, 2160, 2999);
            Baselines.RunMomentum(env, Fee);
            Assert.AreEqual(2, env.Trades.Count);
            Assert.AreEqual(TradeAction.Buy, env.Trades[0].Side);
            Assert.AreEqual(TradeAction.Sell, env.Trades[1].Side);
            Assert.AreEqual(1.0, env.Trades[1].Fraction, 1e-6);
            int decision = env.Trades[1].CandleIndex - 1;
            Assert.LessOrEqual(s.CloseAt(decision), s.CloseAt(decision - 2160), "sold at the first hour the trend turned");
            Assert.Greater(s.CloseAt(decision - 1), s.CloseAt(decision - 1 - 2160), "and not earlier");
            Assert.AreEqual(0, env.Account.CoinUnits);
        }

        [Test]
        public void Momentum_WithoutNinetyDaysOfHistory_StaysInCash()
        {
            CandleSeries up = Geometric(2600, 1.0001);
            Assert.AreEqual(0, Baselines.MomentumTarget(up, 2159));
            Assert.AreEqual(1, Baselines.MomentumTarget(up, 2160));
            var env = new TradingEnv(up, Btc, 0, 2150); // 走到 2150 都还没有 90 天的历史
            EpisodeMetrics m = Baselines.RunMomentum(env, Fee);
            Assert.AreEqual(0, m.Trades);
            Assert.AreEqual(0, m.Exposure);
        }

        [Test]
        public void SmaCross_HoldsWhenTheWeeklyAverageIsAboveTheMonthlyOne()
        {
            var env = new TradingEnv(Geometric(1500, 1.0002), Btc, 720, 1499);
            EpisodeMetrics m = Baselines.RunSmaCross(env, Fee);
            Assert.AreEqual(1, m.Trades);
            Assert.AreEqual(TradeAction.Buy, env.Trades[0].Side);
            Assert.AreEqual(0, env.Trades[0].Step);
            Assert.AreEqual(1.0, m.Exposure);
        }

        [Test]
        public void SmaCross_StaysInCashWhenTheWeeklyAverageIsBelowTheMonthlyOne()
        {
            var env = new TradingEnv(Geometric(1500, 0.9998), Btc, 720, 1499);
            EpisodeMetrics m = Baselines.RunSmaCross(env, Fee);
            Assert.AreEqual(0, m.Trades);
            Assert.AreEqual(0, m.Exposure);
        }

        [Test]
        public void SmaCross_WithoutAMonthOfHistory_StaysInCash()
        {
            CandleSeries up = Geometric(1500, 1.0002);
            Assert.AreEqual(0, Baselines.SmaCrossTarget(up, 718));
            Assert.AreEqual(1, Baselines.SmaCrossTarget(up, 719));
            var env = new TradingEnv(up, Btc, 0, 710);
            EpisodeMetrics m = Baselines.RunSmaCross(env, Fee);
            Assert.AreEqual(0, m.Trades);
            Assert.AreEqual(0, m.Exposure);
        }

        [Test]
        public void VolTarget_HoldsFullyWhenTheMarketIsCalm()
        {
            // σ = 0.001：0.0045 ÷ 0.001 = 4.5，封顶 1
            CandleSeries calm = Zigzag(1200, 0.001);
            Assert.AreEqual(0.001, Baselines.HourlyVolatility(calm, 1000, 720), 1e-12);
            Assert.AreEqual(1, Baselines.VolTargetTarget(calm, 1000));
            var env = new TradingEnv(calm, Btc, 720, 1199);
            EpisodeMetrics m = Baselines.RunVolTarget(env, Fee);
            Assert.AreEqual(1, m.Trades);
            Assert.AreEqual(1.0, env.Trades[0].Fraction, 1e-6);
            Assert.AreEqual(1.0, m.Exposure);
        }

        [Test]
        public void VolTarget_StaysInCashWhenTheTargetIsWithinTheBand()
        {
            // σ = 0.05：目标 0.09，离空仓不到 0.10，不买
            CandleSeries wild = Zigzag(1200, 0.05);
            Assert.AreEqual(0.09, Baselines.VolTargetTarget(wild, 1000), 1e-9);
            var env = new TradingEnv(wild, Btc, 720, 1199);
            EpisodeMetrics m = Baselines.RunVolTarget(env, Fee);
            Assert.AreEqual(0, m.Trades);
            Assert.AreEqual(0, m.Exposure);
        }

        [Test]
        public void VolTarget_HoldsLessWhenTheMarketIsMoreVolatile()
        {
            // σ = 0.009 → 目标 0.5；σ = 0.018 → 目标 0.25
            CandleSeries calmer = Zigzag(1200, 0.009), wilder = Zigzag(1200, 0.018);
            Assert.AreEqual(0.5, Baselines.VolTargetTarget(calmer, 1000), 1e-9);
            Assert.AreEqual(0.25, Baselines.VolTargetTarget(wilder, 1000), 1e-9);

            var a = new TradingEnv(calmer, Btc, 720, 1199);
            var b = new TradingEnv(wilder, Btc, 720, 1199);
            Baselines.RunVolTarget(a, Fee);
            Baselines.RunVolTarget(b, Fee);
            Assert.AreEqual(TradeAction.Buy, a.Trades[0].Side);
            Assert.AreEqual(TradeAction.Buy, b.Trades[0].Side);
            Assert.AreEqual(0.5, a.Trades[0].Fraction, 1e-6);  // 空仓时买入比例（占现金）就是目标仓位
            Assert.AreEqual(0.25, b.Trades[0].Fraction, 1e-6);
            // 买完之后的仓位约等于目标（手续费、成交价和收盘价的差让它略偏，都在 0.10 之内）
            double calmerPosition = PositionAfterFirstStep(new TradingEnv(calmer, Btc, 720, 1199));
            double wilderPosition = PositionAfterFirstStep(new TradingEnv(wilder, Btc, 720, 1199));
            Assert.AreEqual(0.5, calmerPosition, 0.02);
            Assert.AreEqual(0.25, wilderPosition, 0.02);
            Assert.Greater(calmerPosition, wilderPosition);
        }

        /// <summary>按 vol_target 走第一个 step 之后的仓位比例。</summary>
        static double PositionAfterFirstStep(TradingEnv env)
        {
            env.ResetForEvaluation(0, Fee);
            int t = env.CurrentIndex;
            (TradeAction action, double fraction) = Baselines.RuleOrder(Baselines.VolTargetTarget(env.Series, t),
                env.Account.Cash, env.Account.Quantity * env.Series.CloseAt(t));
            env.Step(action, ActionCodec.FromFraction(fraction));
            return env.Account.PositionRatio(env.CurrentClose); // 币值 ÷ 总资产，按这一步之后的收盘价
        }

        [Test]
        public void VolTarget_WithoutAMonthOfReturns_StaysInCash()
        {
            CandleSeries calm = Zigzag(1200, 0.001);
            Assert.AreEqual(0, Baselines.VolTargetTarget(calm, 719));
            Assert.AreEqual(1, Baselines.VolTargetTarget(calm, 720));
            var env = new TradingEnv(calm, Btc, 0, 715);
            EpisodeMetrics m = Baselines.RunVolTarget(env, Fee);
            Assert.AreEqual(0, m.Trades);
            Assert.AreEqual(0, m.Exposure);
        }

        // 防前视：从下标 k 起把 K 线改掉，决策下标小于 k 的每一笔下单方向和比例都不变。
        // 单子按下一根 open 成交，所以第 k − 1 根的成交价会变，不比成交价。

        static readonly object[] RuleNames =
        {
            new object[] { Baselines.MomentumName },
            new object[] { Baselines.SmaCrossName },
            new object[] { Baselines.VolTargetName },
        };

        static Func<CandleSeries, int, double> TargetOf(string rule) =>
            rule == Baselines.MomentumName ? Baselines.MomentumTarget
            : rule == Baselines.SmaCrossName ? (Func<CandleSeries, int, double>)Baselines.SmaCrossTarget
            : Baselines.VolTargetTarget;

        [TestCaseSource(nameof(RuleNames))]
        public void Rule_DoesNotLookAhead(string rule)
        {
            const int count = 6000, first = 2200, k = 4000;
            CandleSeries original = TestData.RandomWalk(count, 2026, 0.01);
            // 从第 k 根起：close × 1.7 再叠一段下跌，open 跟着 close 走
            double Changed(int i) => i < k ? original.CloseAt(i) : original.CloseAt(i) * 1.7 * Math.Pow(0.9995, i - k);
            CandleSeries changed = TestData.Synthetic(count, Changed, i => i == 0 ? Changed(0) : Changed(i - 1));
            Assert.IsTrue(TestData.SameBits(original.OpenAt(k - 1), changed.OpenAt(k - 1)), "premise: candles before k are the same");

            Func<CandleSeries, int, double> target = TargetOf(rule);
            bool differsLater = false;
            for (int t = first; t < count; t++)
            {
                double a = target(original, t), b = target(changed, t);
                if (t < k) Assert.IsTrue(TestData.SameBits(a, b), $"{rule}: target at {t} < k changed");
                else if (!TestData.SameBits(a, b)) differsLater = true;
            }
            Assert.IsTrue(differsLater, $"premise: {rule} reacts to the changed candles");

            var envA = new TradingEnv(original, Btc, first, count - 1);
            var envB = new TradingEnv(changed, Btc, first, count - 1);
            Baselines.RunRule(envA, Fee, target);
            Baselines.RunRule(envB, Fee, target);
            List<TradeRecord> before = envA.Trades.Where(r => r.CandleIndex - 1 < k).ToList();
            List<TradeRecord> beforeChanged = envB.Trades.Where(r => r.CandleIndex - 1 < k).ToList();
            Assert.Greater(before.Count, 0, $"premise: {rule} trades before k");
            Assert.AreEqual(before.Count, beforeChanged.Count, $"{rule}: orders before k");
            for (int i = 0; i < before.Count; i++)
            {
                Assert.AreEqual(before[i].CandleIndex, beforeChanged[i].CandleIndex, $"{rule}: order {i} decision index");
                Assert.AreEqual(before[i].Side, beforeChanged[i].Side, $"{rule}: order {i} side");
                Assert.IsTrue(TestData.SameBits(before[i].Fraction, beforeChanged[i].Fraction), $"{rule}: order {i} fraction");
            }
        }
    }
}
