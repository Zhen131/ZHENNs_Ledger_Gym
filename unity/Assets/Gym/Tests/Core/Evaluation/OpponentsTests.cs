using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Gym.Core.Accounting;
using Gym.Core.Env;
using Gym.Core.Evaluation;
using Gym.Core.Market;
using NUnit.Framework;
using UnityEngine;

namespace Gym.Tests.Core.Evaluation
{
    /// <summary>第三轮的两个对手 constant_mix_50、dial_momentum_90d（23 批）。BaselinesTests 本批不改。</summary>
    public class OpponentsTests
    {
        static readonly SymbolRules Btc = SymbolRules.BtcUsdt;
        static readonly CostModel Fee = new CostModel(0.001);

        static CandleSeries Geometric(int count, double factor) =>
            TestData.Synthetic(count, k => 100 * Math.Pow(factor, k), k => 100 * Math.Pow(factor, Math.Max(0, k - 1)));

        static string R(double v) => v.ToString("R", CultureInfo.InvariantCulture);

        [Test]
        public void TheFixedParameters_AreTheOpponentsOwn()
        {
            Assert.AreEqual("constant_mix_50", Baselines.ConstantMix50Name);
            Assert.AreEqual("dial_momentum_90d", Baselines.DialMomentumName);
            Assert.AreEqual(0.05, Baselines.OpponentBand);
            Assert.AreEqual(24, Baselines.OpponentDecisionHours);
            CollectionAssert.AreEqual(new[] { 0.3, 0.5, 0.7 }, Baselines.OpponentLevels.ToArray());
            Assert.AreEqual(2160, Baselines.DialMomentumLookback);

            var env = new TradingEnv(TestData.RandomWalk(600, 1, 0.01), Btc, 0, 599);
            Baselines.RunConstantMix50(env, Fee, out TradingEnv dialEnv);
            Assert.AreEqual(ActionMode.Dial, dialEnv.ActionMode);
            CollectionAssert.AreEqual(new[] { 0.3, 0.5, 0.7 }, dialEnv.Dial.Levels.ToArray());
            Assert.AreEqual(24, dialEnv.Dial.DecisionHours);
            Assert.AreEqual(0.05, dialEnv.Dial.Band);
            Assert.AreEqual(env.First, dialEnv.First);
            Assert.AreEqual(env.Last, dialEnv.Last);
            Assert.AreEqual(env.InitialCash, dialEnv.InitialCash);
            Assert.AreSame(env.Series, dialEnv.Series);
        }

        [Test]
        public void ConstantMix50_OnAFlatMarket_TradesOnceAndSitsNearHalf()
        {
            CandleSeries flat = TestData.Synthetic(600, k => 100);
            var env = new TradingEnv(flat, Btc, 0, 599);
            EpisodeMetrics m = Baselines.RunConstantMix50(env, Fee, out TradingEnv dialEnv);
            Assert.AreEqual(1, m.Trades);
            Assert.AreEqual(1, dialEnv.CurrentLevel, "the 0.5 level");
            Assert.AreEqual(0, dialEnv.DialChanges);
            Assert.AreEqual(0.5, dialEnv.Account.PositionRatio(dialEnv.CurrentClose), 0.002);
            Assert.AreEqual((567 + 23) / 24, dialEnv.Decisions, "567 candles from 32 to 599, one decision a day");
            Assert.AreEqual(567, m.Steps);
        }

        [Test]
        public void DialMomentumLevel_FollowsTheNinetyDayTrend_AndPicksThirtyPercentWithoutHistory()
        {
            CandleSeries up = Geometric(3000, 1.0001);
            CandleSeries down = Geometric(3000, 0.9999);
            Assert.AreEqual(2, Baselines.DialMomentumLevel(up, 2500), "rising: 70 %");
            Assert.AreEqual(2, Baselines.DialMomentumLevel(up, 2160));
            Assert.AreEqual(0, Baselines.DialMomentumLevel(up, 2159), "no 2160 candles of history: 30 %, not flat cash");
            Assert.AreEqual(0, Baselines.DialMomentumLevel(down, 2500), "falling: 30 %");
            Assert.AreEqual(0, Baselines.MomentumTarget(up, 2159), "the old momentum_90d goes to cash there");
            Assert.AreEqual(1, Baselines.ConstantMix50Level(up, 2500));
        }

        [Test]
        public void DialMomentum_HoldsSeventyOnARise_ThirtyOnAFall_AndChangesWhenTheTrendTurns()
        {
            var rising = new TradingEnv(Geometric(3000, 1.0001), Btc, 2200, 2999);
            Baselines.RunDialMomentum(rising, Fee, out TradingEnv r);
            Assert.AreEqual(2, r.CurrentLevel);
            Assert.AreEqual(0, r.DialChanges);
            Assert.Greater(r.Decisions, 30);
            Assert.AreEqual(0.7, r.Account.PositionRatio(r.CurrentClose), 0.06);

            var falling = new TradingEnv(Geometric(3000, 0.9999), Btc, 2200, 2999);
            Baselines.RunDialMomentum(falling, Fee, out TradingEnv f);
            Assert.AreEqual(0, f.CurrentLevel);
            Assert.AreEqual(0, f.DialChanges);
            Assert.AreEqual(0.3, f.Account.PositionRatio(f.CurrentClose), 0.06);

            // 从下标 0 开始：前 2160 根没有历史（30 %），之后涨势里换到 70 %：一次换档
            var fromStart = new TradingEnv(Geometric(3000, 1.0001), Btc, 0, 2999);
            Baselines.RunDialMomentum(fromStart, Fee, out TradingEnv s);
            Assert.AreEqual(1, s.DialChanges);
            Assert.AreEqual(2, s.CurrentLevel);

            // 先涨 3000 根、再跌：涨的时候 70 %，跌破 90 天前的价以后换到 30 %，只换一次
            double Close(int k) => k < 3000 ? 100 * Math.Pow(1.0002, k) : 100 * Math.Pow(1.0002, 3000) * Math.Pow(0.9998, k - 3000);
            CandleSeries turn = TestData.Synthetic(7000, Close, k => k == 0 ? Close(0) : Close(k - 1));
            var turning = new TradingEnv(turn, Btc, 2200, 6999);
            Baselines.RunDialMomentum(turning, Fee, out TradingEnv t);
            Assert.AreEqual(1, t.DialChanges);
            Assert.AreEqual(0, t.CurrentLevel);
            Assert.Greater(t.Trades.Count(x => x.Side == TradeAction.Sell && x.CandleIndex > 3000), 0, "it sold down after the turn");
        }

        static readonly object[] Opponents =
        {
            new object[] { Baselines.ConstantMix50Name },
            new object[] { Baselines.DialMomentumName },
        };

        static EpisodeMetrics Run(string name, TradingEnv env, out TradingEnv dialEnv) =>
            name == Baselines.ConstantMix50Name
                ? Baselines.RunConstantMix50(env, Fee, out dialEnv)
                : Baselines.RunDialMomentum(env, Fee, out dialEnv);

        [TestCaseSource(nameof(Opponents))]
        public void Opponent_DoesNotLookAhead(string name)
        {
            // 照 Rule_DoesNotLookAhead：从下标 k 起改 K 线，决策下标小于 k 的每一笔方向和比例都不变，成交价不比
            const int count = 6000, first = 2200, k = 4000;
            CandleSeries original = TestData.RandomWalk(count, 2026, 0.01);
            double Changed(int i) => i < k ? original.CloseAt(i) : original.CloseAt(i) * 1.7 * Math.Pow(0.9995, i - k);
            CandleSeries changed = TestData.Synthetic(count, Changed, i => i == 0 ? Changed(0) : Changed(i - 1));
            Assert.IsTrue(TestData.SameBits(original.OpenAt(k - 1), changed.OpenAt(k - 1)), "premise: candles before k are the same");

            for (int t = first; t < k; t++)
                Assert.AreEqual(Baselines.DialMomentumLevel(original, t), Baselines.DialMomentumLevel(changed, t), $"level at {t} < k");

            Run(name, new TradingEnv(original, Btc, first, count - 1), out TradingEnv a);
            Run(name, new TradingEnv(changed, Btc, first, count - 1), out TradingEnv b);
            List<TradeRecord> before = a.Trades.Where(r => r.CandleIndex - 1 < k).ToList();
            List<TradeRecord> beforeChanged = b.Trades.Where(r => r.CandleIndex - 1 < k).ToList();
            Assert.Greater(before.Count, 0, $"premise: {name} trades before k");
            Assert.AreEqual(before.Count, beforeChanged.Count, $"{name}: orders before k");
            for (int i = 0; i < before.Count; i++)
            {
                Assert.AreEqual(before[i].CandleIndex, beforeChanged[i].CandleIndex, $"{name}: order {i} decision index");
                Assert.AreEqual(before[i].Side, beforeChanged[i].Side, $"{name}: order {i} side");
                Assert.IsTrue(TestData.SameBits(before[i].Fraction, beforeChanged[i].Fraction), $"{name}: order {i} fraction");
            }
            bool differsLater = a.Trades.Count != b.Trades.Count ||
                                a.Trades.Zip(b.Trades, (x, y) => x.CandleIndex != y.CandleIndex || x.Side != y.Side).Any(d => d);
            Assert.IsTrue(differsLater, $"premise: {name} reacts to the changed candles");
        }

        [TestCaseSource(nameof(Opponents))]
        public void Opponent_IgnoresTheDialSettingsAndModeOfTheEnvironmentItIsGiven(string name)
        {
            CandleSeries s = TestData.RandomWalk(5000, 11, 0.01);
            var plain = new TradingEnv(s, Btc, 2200, 4999);
            // 档位、带宽、决策根数、每局长度、带币开局的概率都和对手的不同
            var odd = new TradingEnv(s, Btc, 2200, 4999, 10_000, 100, 1.0, ObservationSet.Base,
                new DialSettings(new[] { 0.2, 0.4, 0.6, 0.8 }, 12, 0.02));
            EpisodeMetrics m1 = Run(name, plain, out TradingEnv a);
            EpisodeMetrics m2 = Run(name, odd, out TradingEnv b);
            Assert.IsTrue(TestData.SameBits(m1.TotalReturn, m2.TotalReturn), $"{name}: return");
            Assert.IsTrue(TestData.SameBits(m1.MaxDrawdown, m2.MaxDrawdown), $"{name}: drawdown");
            Assert.AreEqual(m1.Trades, m2.Trades, $"{name}: trades");
            Assert.AreEqual(a.DialChanges, b.DialChanges);
            Assert.AreEqual(a.EquityCurve.Count, b.EquityCurve.Count);
            for (int i = 0; i < a.EquityCurve.Count; i++) Assert.IsTrue(TestData.SameBits(a.EquityCurve[i], b.EquityCurve[i]));
            CollectionAssert.AreEqual(new[] { 0.3, 0.5, 0.7 }, b.Dial.Levels.ToArray(), "the opponent's own levels");
            Assert.AreEqual(0.05, b.Dial.Band);
            Assert.AreEqual(24, b.Dial.DecisionHours);
            Assert.AreEqual(0, odd.StepCount, "the given environment is not touched");
        }

        /// <summary>真实 BTC 上两段的数（记录，不是靶子），写进日志，C 报告和 22A 第二节那张表并排。</summary>
        [Test]
        public void OnRealBtc_BothOpponentsRunTheBearAndTheValidationSegments()
        {
            var segments = new[]
            {
                SegmentSpec.Parse("validation-bear", "2022-01-01", "2022-12-31"),
                SegmentSpec.Parse("validation", "2024-09-01", "2025-08-31"),
            };
            foreach (SegmentSpec segment in segments)
            {
                var env = TradingEnv.ForSegment(TestData.Btc, Btc, segment);
                EpisodeMetrics hold = Baselines.RunBuyAndHold(env, Fee);
                foreach (object[] o in Opponents)
                {
                    string name = (string)o[0];
                    EpisodeMetrics m = Run(name, env, out TradingEnv d);
                    Assert.AreEqual((d.StepCount + 23) / 24, d.Decisions, $"{name} on {segment}: one decision a day");
                    Assert.Greater(m.Trades, 0);
                    if (name == Baselines.ConstantMix50Name) Assert.AreEqual(0, d.DialChanges);
                    Assert.Less(m.MaxDrawdown, hold.MaxDrawdown, $"{name} on {segment}: never all in, so a smaller drawdown than buy and hold");
                    Debug.Log($"[Gym] opponent {name} on BTCUSDT {segment} (candles {d.StartIndex}..{d.CurrentIndex}), fee 0.001: " +
                              $"total_return {R(m.TotalReturn)}, max_drawdown {R(m.MaxDrawdown)}, trades {m.Trades}, " +
                              $"level changes {d.DialChanges}, decisions {d.Decisions}, final equity {R(m.FinalEquity)}");
                }
                Debug.Log($"[Gym] opponent reference buy_and_hold on BTCUSDT {segment}, fee 0.001: total_return {R(hold.TotalReturn)}, " +
                          $"max_drawdown {R(hold.MaxDrawdown)}");
            }
        }
    }
}
