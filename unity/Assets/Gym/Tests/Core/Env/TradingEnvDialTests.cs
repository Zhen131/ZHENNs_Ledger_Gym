using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Gym.Core.Accounting;
using Gym.Core.Env;
using Gym.Core.Market;
using NUnit.Framework;
using UnityEngine;

namespace Gym.Tests.Core.Env
{
    /// <summary>
    /// 旋钮模式的环境（23 批）。下单模式由 TradingEnvTests 原样锁着（那个文件本批不改）。
    /// </summary>
    public class TradingEnvDialTests
    {
        static readonly SymbolRules Btc = SymbolRules.BtcUsdt;
        static readonly double[] Levels = { 0.3, 0.5, 0.7 };
        static readonly CostModel Fee = new CostModel(0.001);

        static DialSettings Dial(int hours = 24, double band = 0.05) => new DialSettings(Levels, hours, band);

        /// <summary>2022 年全年（配置里 validation-bear 的日期）：第 6 步②用的段。</summary>
        static readonly SegmentSpec Bear2022 = SegmentSpec.Parse("validation-bear", "2022-01-01", "2022-12-31");

        /// <summary>第 n 次决策（从 0 数）选的档：每 5 次换一档，0、1、2 轮着来。</summary>
        static int Schedule(int decision) => decision / 5 % 3;

        static string R(double v) => v.ToString("R", CultureInfo.InvariantCulture);

        // ① 一步走的根数

        [Test]
        public void OneDecision_WalksDecisionHoursCandles_AndTheLastOneWalksWhatIsLeft()
        {
            // 评估从 32 起到 199：167 根 = 6 × 24 + 23
            CandleSeries s = TestData.RandomWalk(200, 7, 0.01);
            var env = new TradingEnv(s, Btc, 0, 199, dial: Dial());
            env.ResetForEvaluation(0, Fee);
            Assert.AreEqual(ActionMode.Dial, env.ActionMode);
            Assert.AreEqual(0, env.Decisions);
            Assert.AreEqual(0, env.LastDecisionHours);
            var hours = new List<int>();
            DialStepResult last = default;
            while (!env.Done)
            {
                last = env.StepDial(1);
                hours.Add(last.Hours);
                Assert.AreEqual(last.Hours, env.LastDecisionHours);
            }
            CollectionAssert.AreEqual(new[] { 24, 24, 24, 24, 24, 24, 23 }, hours);
            Assert.AreEqual(7, env.Decisions);
            Assert.AreEqual(23, env.LastDecisionHours);
            Assert.AreEqual(167, env.StepCount);
            Assert.AreEqual(168, env.EquityCurve.Count);
            Assert.IsTrue(last.Done);
            Assert.AreEqual(EndReason.SegmentEnd, last.Reason);
            Assert.Throws<InvalidOperationException>(() => env.StepDial(1));
        }

        [Test]
        public void ATrainingEpisode_EndsAfterItsLength_WhichMayCutTheLastDecision()
        {
            CandleSeries s = TestData.RandomWalk(3000, 8, 0.01);
            var env = new TradingEnv(s, Btc, 0, 2999, episodeLength: 50, dial: Dial());
            Assert.AreEqual(50, env.EpisodeLength);
            env.ResetForTraining(3, Fee);
            Assert.AreEqual(24, env.StepDial(0).Hours);
            Assert.AreEqual(24, env.StepDial(0).Hours);
            DialStepResult end = env.StepDial(0);
            Assert.AreEqual(2, end.Hours);
            Assert.IsTrue(end.Done);
            Assert.AreEqual(EndReason.EpisodeLength, end.Reason);
            Assert.AreEqual(3, env.Decisions);
            Assert.AreEqual(2, env.LastDecisionHours);
            Assert.AreEqual(50, env.StepCount);
        }

        // ② 得分等于逐小时之和，曲线和下单模式逐小时重放规则一致

        [Test]
        public void OnRealBtc_ADialStepIsTheSumOfItsHours_AndTheCurveMatchesTheOrderModeReplayingTheRule()
        {
            var dialEnv = TradingEnv.ForSegment(TestData.Btc, Btc, Bear2022, dial: Dial());
            var orderEnv = TradingEnv.ForSegment(TestData.Btc, Btc, Bear2022);
            dialEnv.ResetForEvaluation(7, Fee); // 两边都全现金、从段首开始；seed 只是记录
            orderEnv.ResetForEvaluation(7, Fee);
            Assert.AreEqual(dialEnv.StartIndex, orderEnv.StartIndex);

            int decision = 0;
            while (!dialEnv.Done)
            {
                int level = Schedule(decision++);
                int stepsBefore = dialEnv.StepCount;
                DialStepResult r = dialEnv.StepDial(level);

                // 下单模式那边：这一步的每一小时按同一个目标、同一带宽重放规则
                double replayed = 0;
                for (int h = 0; h < r.Hours; h++)
                {
                    Account a = orderEnv.Account;
                    double close = orderEnv.CurrentClose;
                    (TradeAction action, double fraction) = Rebalancer.Order(Levels[level], a.Cash, a.Quantity * close, 0.05);
                    if ((action == TradeAction.Buy && !orderEnv.BuyEnabled) || (action == TradeAction.Sell && !orderEnv.SellEnabled))
                        action = TradeAction.Hold;
                    replayed += orderEnv.Step(action, action == TradeAction.Hold ? 0f : ActionCodec.FromFraction(fraction)).Reward;
                }
                Assert.IsTrue(TestData.SameBits(replayed, r.Reward), $"decision {decision}: reward = sum of the replayed hours");

                // 逐小时 RewardFunction.Compute 的和（按资金曲线重算）
                double fromCurve = 0;
                for (int i = stepsBefore + 1; i <= dialEnv.StepCount; i++)
                    fromCurve += RewardFunction.Compute(dialEnv.EquityCurve[i - 1], dialEnv.EquityCurve[i], out _);
                Assert.IsTrue(TestData.SameBits(fromCurve, r.Reward), $"decision {decision}: reward = sum of RewardFunction.Compute");
            }
            Assert.IsTrue(orderEnv.Done);
            Assert.AreEqual(orderEnv.StepCount, dialEnv.StepCount);
            Assert.AreEqual(orderEnv.EquityCurve.Count, dialEnv.EquityCurve.Count);
            for (int i = 0; i < dialEnv.EquityCurve.Count; i++)
                Assert.IsTrue(TestData.SameBits(orderEnv.EquityCurve[i], dialEnv.EquityCurve[i]), $"equity {i}");
            Assert.AreEqual(orderEnv.Trades.Count, dialEnv.Trades.Count);
            Assert.IsTrue(TestData.SameBits(orderEnv.RewardSum, dialEnv.RewardSum));
            Assert.Greater(dialEnv.Trades.Count, 10, "premise: the rule traded");
            Assert.Greater(dialEnv.DialChanges, 5, "premise: the schedule changed level");

            Debug.Log($"[Gym] dial replay test: segment {Bear2022} (candles {dialEnv.StartIndex}..{dialEnv.CurrentIndex}), fee 0.001, " +
                      $"levels by decision n: (n / 5) % 3, {dialEnv.Decisions} decisions, {dialEnv.DialChanges} level changes, " +
                      $"{dialEnv.Trades.Count} trades; final equity dial {R(dialEnv.EquityCurve[dialEnv.EquityCurve.Count - 1])}, " +
                      $"orders replay {R(orderEnv.EquityCurve[orderEnv.EquityCurve.Count - 1])}; reward sum dial {R(dialEnv.RewardSum)}, " +
                      $"orders {R(orderEnv.RewardSum)}");
        }

        // ③ 换档计数

        [Test]
        public void LevelChanges_AreCountedByHand_AndTheFirstDecisionIsNotAChange()
        {
            CandleSeries s = TestData.RandomWalk(400, 9, 0.01);
            var env = new TradingEnv(s, Btc, 0, 399, dial: Dial());
            env.ResetForEvaluation(0, Fee);
            int[] levels = { 1, 1, 2, 0, 0 };
            bool[] changed = levels.Select(l => env.StepDial(l).Changed).ToArray();
            CollectionAssert.AreEqual(new[] { false, false, true, true, false }, changed);
            Assert.AreEqual(2, env.DialChanges);
            Assert.AreEqual(5, env.Decisions);
            Assert.AreEqual(0, env.CurrentLevel);

            env.ResetForEvaluation(0, Fee); // 新的一局从零数
            Assert.AreEqual(0, env.DialChanges);
            Assert.AreEqual(0, env.Decisions);
            Assert.AreEqual(-1, env.CurrentLevel);
            Assert.IsFalse(env.StepDial(2).Changed, "the first decision of an episode is not a change");
        }

        // ④ 开局

        [Test]
        public void ATrainingStartWithCoin_SitsAtOneOfTheLevels_AndACashStartHasNone()
        {
            var segment = SegmentSpec.Parse("train", "2017-08-17", "2021-12-31");
            var dialEnv = TradingEnv.ForSegment(TestData.Btc, Btc, segment, episodeLength: 720, dial: Dial());
            var orderEnv = TradingEnv.ForSegment(TestData.Btc, Btc, segment, episodeLength: 720);
            var hits = new int[Levels.Length];
            int withCoin = 0;
            for (int seed = 0; seed < 600; seed++)
            {
                dialEnv.ResetForTraining(seed, Fee);
                orderEnv.ResetForTraining(seed, Fee);
                // 同一 seed、同一每局长度：起点和带不带币和下单模式一样（取数的次序和次数没动）
                Assert.AreEqual(orderEnv.StartIndex, dialEnv.StartIndex, $"seed {seed}: start");
                Assert.AreEqual(orderEnv.StartedWithCoin, dialEnv.StartedWithCoin, $"seed {seed}: holds coin");
                double p = dialEnv.Account.PositionRatio(dialEnv.CurrentClose);
                if (!dialEnv.StartedWithCoin)
                {
                    Assert.AreEqual(0, p, $"seed {seed}: a cash start");
                    continue;
                }
                withCoin++;
                int k = Array.FindIndex(Levels, level => Math.Abs(p - level) < 1e-3);
                Assert.GreaterOrEqual(k, 0, $"seed {seed}: position {R(p)} is not at a level");
                // 只因最小下单单位（0.00001 BTC）向下取整而略低于档位
                Assert.LessOrEqual(p, Levels[k] + 1e-12, $"seed {seed}");
                hits[k]++;
            }
            Assert.Greater(withCoin, 200, "premise: about half start with coin");
            Assert.IsTrue(hits.All(h => h > 30), $"each level is picked: {string.Join(", ", hits)}");
        }

        [Test]
        public void EvaluationAndPlayStarts_AreInCash_AndTheFirstDecisionBuysToTheTarget()
        {
            var env = TradingEnv.ForSegment(TestData.Btc, Btc, Bear2022, dial: Dial());
            env.ResetForEvaluation(0, Fee);
            Assert.AreEqual(0, env.Account.CoinUnits);
            DialStepResult r = env.StepDial(2);
            Assert.IsTrue(r.Traded);
            Assert.AreEqual(0.7, env.Account.PositionRatio(env.CurrentClose), 0.05, "after the first decision the position is near 70 %");
            env.Reset(Fee, env.FirstDecisionIndex + 10);
            Assert.AreEqual(0, env.Account.CoinUnits);
        }

        // ⑥ seed 可重复

        [Test]
        public void SameSeedAndLevels_GiveIdenticalCurvesAndTrades()
        {
            var segment = SegmentSpec.Parse("train", "2017-08-17", "2021-12-31");
            var a = TradingEnv.ForSegment(TestData.Btc, Btc, segment, episodeLength: 2160, dial: Dial());
            var b = TradingEnv.ForSegment(TestData.Btc, Btc, segment, episodeLength: 2160, dial: Dial());
            for (int seed = 0; seed < 5; seed++)
            {
                a.ResetForTraining(seed, Fee);
                b.ResetForTraining(seed, Fee);
                int n = 0;
                while (!a.Done)
                {
                    int level = Schedule(n++);
                    DialStepResult ra = a.StepDial(level), rb = b.StepDial(level);
                    Assert.IsTrue(TestData.SameBits(ra.Reward, rb.Reward));
                }
                Assert.IsTrue(b.Done);
                Assert.AreEqual(90, a.Decisions, "a 2160-candle episode has 90 daily decisions");
                Assert.AreEqual(a.EquityCurve.Count, b.EquityCurve.Count);
                for (int i = 0; i < a.EquityCurve.Count; i++) Assert.IsTrue(TestData.SameBits(a.EquityCurve[i], b.EquityCurve[i]));
                Assert.AreEqual(a.Trades.Count, b.Trades.Count);
                for (int i = 0; i < a.Trades.Count; i++)
                {
                    Assert.AreEqual(a.Trades[i].CandleIndex, b.Trades[i].CandleIndex);
                    Assert.AreEqual(a.Trades[i].Units, b.Trades[i].Units);
                }
            }
        }

        // ⑦ 规则不前视

        [Test]
        public void TheRebalancingInsideADecision_DoesNotLookAhead()
        {
            // 从下标 k 起把 K 线改掉，决策下标小于 k 的每一笔下单方向和比例都不变；单子按下一根 open 成交，不比成交价
            const int count = 3000, first = 100, k = 1700;
            CandleSeries original = TestData.RandomWalk(count, 2026, 0.01);
            double Changed(int i) => i < k ? original.CloseAt(i) : original.CloseAt(i) * 1.7 * Math.Pow(0.9995, i - k);
            CandleSeries changed = TestData.Synthetic(count, Changed, i => i == 0 ? Changed(0) : Changed(i - 1));
            Assert.IsTrue(TestData.SameBits(original.OpenAt(k - 1), changed.OpenAt(k - 1)), "premise: candles before k are the same");

            var envA = new TradingEnv(original, Btc, first, count - 1, dial: Dial());
            var envB = new TradingEnv(changed, Btc, first, count - 1, dial: Dial());
            envA.ResetForEvaluation(0, Fee);
            envB.ResetForEvaluation(0, Fee);
            int n = 0;
            while (!envA.Done)
            {
                int level = Schedule(n++);
                envA.StepDial(level);
                envB.StepDial(level);
            }
            List<TradeRecord> before = envA.Trades.Where(r => r.CandleIndex - 1 < k).ToList();
            List<TradeRecord> beforeChanged = envB.Trades.Where(r => r.CandleIndex - 1 < k).ToList();
            Assert.Greater(before.Count, 5, "premise: trades before k");
            Assert.AreEqual(before.Count, beforeChanged.Count, "orders before k");
            for (int i = 0; i < before.Count; i++)
            {
                Assert.AreEqual(before[i].CandleIndex, beforeChanged[i].CandleIndex, $"order {i} decision index");
                Assert.AreEqual(before[i].Side, beforeChanged[i].Side, $"order {i} side");
                Assert.IsTrue(TestData.SameBits(before[i].Fraction, beforeChanged[i].Fraction), $"order {i} fraction");
            }
            Assert.AreNotEqual(envA.Trades.Count, 0);
            bool differsLater = envA.Trades.Count != envB.Trades.Count ||
                                envA.Trades.Zip(envB.Trades, (x, y) => !TestData.SameBits(x.Fraction, y.Fraction) || x.Side != y.Side).Any(d => d);
            Assert.IsTrue(differsLater, "premise: the rule reacts to the changed candles");
        }

        // 其余

        [Test]
        public void BadLevelsOrModes_AreRefused()
        {
            CandleSeries s = TestData.RandomWalk(200, 7, 0.01);
            var env = new TradingEnv(s, Btc, 0, 199, dial: Dial());
            env.ResetForEvaluation(0, Fee);
            Assert.Throws<ArgumentOutOfRangeException>(() => env.StepDial(3));
            Assert.Throws<ArgumentOutOfRangeException>(() => env.StepDial(-1));
            var orders = new TradingEnv(s, Btc, 0, 199);
            orders.ResetForEvaluation(0, Fee);
            Assert.AreEqual(ActionMode.Orders, orders.ActionMode);
            Assert.IsNull(orders.Dial);
            Assert.Throws<InvalidOperationException>(() => orders.StepDial(0));
            Assert.Throws<InvalidOperationException>(() => new TradingEnv(s, Btc, 0, 199, dial: Dial()).StepDial(0), "no reset");
        }

        [Test]
        public void InOrderMode_DecisionsAreSteps_AndThereAreNoLevelChanges()
        {
            CandleSeries s = TestData.RandomWalk(200, 7, 0.01);
            var env = new TradingEnv(s, Btc, 0, 199);
            env.ResetForEvaluation(0, Fee);
            Assert.AreEqual(0, env.LastDecisionHours);
            for (int i = 0; i < 10; i++) env.Step(TradeAction.Hold, 0f);
            Assert.AreEqual(10, env.Decisions);
            Assert.AreEqual(1, env.LastDecisionHours);
            Assert.AreEqual(0, env.DialChanges);
            Assert.AreEqual(-1, env.CurrentLevel);
        }

        [Test]
        public void DialSettings_RefuseBadValues()
        {
            Assert.Throws<ArgumentException>(() => new DialSettings(new[] { 0.5, 0.3, 0.7 }, 24, 0.05), "not increasing");
            Assert.Throws<ArgumentException>(() => new DialSettings(new[] { 0.3, 0.3 }, 24, 0.05), "not strictly increasing");
            Assert.Throws<ArgumentException>(() => new DialSettings(new[] { 0.0, 0.5 }, 24, 0.05), "0 is not inside (0, 1)");
            Assert.Throws<ArgumentException>(() => new DialSettings(new[] { 0.5, 1.0 }, 24, 0.05), "1 is not inside (0, 1)");
            Assert.Throws<ArgumentException>(() => new DialSettings(new[] { 0.5 }, 24, 0.05), "one level");
            Assert.Throws<ArgumentException>(() => new DialSettings(null, 24, 0.05), "no levels");
            Assert.Throws<ArgumentException>(() => new DialSettings(Levels, 0, 0.05), "0 hours");
            Assert.Throws<ArgumentException>(() => new DialSettings(Levels, 24, 0.6), "band 0.6");
            Assert.Throws<ArgumentException>(() => new DialSettings(Levels, 24, 0), "band 0");
            var copy = (double[])Levels.Clone();
            var dial = new DialSettings(copy, 24, 0.05);
            copy[0] = 0.1;
            Assert.AreEqual(0.3, dial.Levels[0], "the settings keep their own copy of the levels");
        }
    }
}
