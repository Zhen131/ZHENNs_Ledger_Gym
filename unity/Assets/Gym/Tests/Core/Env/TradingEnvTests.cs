using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Gym.Core.Accounting;
using Gym.Core.Env;
using Gym.Core.Market;
using NUnit.Framework;

namespace Gym.Tests.Core.Env
{
    public class TradingEnvTests
    {
        static readonly SymbolRules Btc = SymbolRules.BtcUsdt;

        // ---- T-10 one episode

        [Test]
        public void TrainingEpisode_EndsAtStep720()
        {
            TradingEnv env = TestData.TrainEnv();
            env.ResetForTraining(42, new CostModel());
            for (int i = 1; i < 720; i++)
            {
                StepResult r = env.Step(TradeAction.Hold, 0f);
                Assert.IsFalse(r.Done, $"done early at step {i}");
            }
            StepResult end = env.Step(TradeAction.Hold, 0f);
            Assert.IsTrue(end.Done);
            Assert.AreEqual(EndReason.EpisodeLength, end.Reason);
            Assert.AreEqual(720, env.StepCount);
            Assert.AreEqual(env.StartIndex + 720, env.CurrentIndex);
            Assert.AreEqual(721, env.EquityCurve.Count);
            Assert.Throws<InvalidOperationException>(() => env.Step(TradeAction.Hold, 0f));
        }

        [Test]
        public void TrainingStartsForSeedsZeroTo999_AreLegalAndAboutHalfHoldCoin()
        {
            TradingEnv env = TestData.TrainEnv();
            int lo = Math.Max(env.First, ObservationBuilder.Lookback), hi = env.Last - 720;
            int withCoin = 0;
            for (int seed = 0; seed < 1000; seed++)
            {
                env.ResetForTraining(seed, new CostModel());
                Assert.That(env.StartIndex, Is.InRange(lo, hi), $"seed {seed}");
                Assert.GreaterOrEqual(env.Account.Cash, 0);
                Assert.AreEqual(env.InitialCash, env.CurrentEquity, 1e-6, "no fee on the initial position");
                if (env.StartedWithCoin)
                {
                    withCoin++;
                    Assert.AreEqual(env.CurrentClose, env.Account.AvgCost);
                }
                else
                {
                    Assert.AreEqual(env.InitialCash, env.Account.Cash);
                }
            }
            Assert.That(withCoin, Is.InRange(400, 600));
            TestContext.WriteLine($"T-10: {withCoin} of 1000 training starts held coin");
        }

        [Test]
        public void EvaluationEpisode_RunsTheWholeTestSegmentFromCash()
        {
            CandleSeries s = TestData.Btc;
            TradingEnv env = TradingEnv.ForSegment(s, Btc, DefaultSplits.Test);
            env.ResetForEvaluation(123, new CostModel());
            int first = s.FirstIndexOnOrAfter(new DateTime(2025, 9, 1));
            int last = s.LastIndexOnOrBefore(new DateTime(2026, 8, 31));
            Assert.AreEqual(new DateTime(2025, 9, 1, 0, 0, 0, DateTimeKind.Utc), s.OpenTimeUtc(env.StartIndex));
            Assert.AreEqual(first, env.StartIndex);
            Assert.AreEqual(10_000, env.Account.Cash);
            Assert.AreEqual(0, env.Account.CoinUnits);
            int steps = 0;
            StepResult r = default;
            while (!env.Done)
            {
                r = env.Step(TradeAction.Hold, 0f);
                steps++;
            }
            Assert.AreEqual(EndReason.SegmentEnd, r.Reason);
            Assert.AreEqual(last, env.CurrentIndex);
            Assert.AreEqual(new DateTime(2026, 8, 31, 23, 0, 0, DateTimeKind.Utc), s.OpenTimeUtc(env.CurrentIndex));
            Assert.AreEqual(last - first, steps);
            Assert.AreEqual(last - first + 1, env.EquityCurve.Count);
        }

        [Test]
        public void SameSeedAndActions_GiveIdenticalEquityObservationsAndTrades()
        {
            var costs = new CostModel(0.001, 0.5, 0.0005);
            List<double> RunOnce(out List<float> observations, out List<TradeRecord> trades)
            {
                TradingEnv env = TestData.TrainEnv();
                env.ResetForTraining(2026, costs);
                var actions = new System.Random(77);
                var obs = new float[ObservationBuilder.Size];
                observations = new List<float>();
                while (!env.Done)
                {
                    env.WriteObservation(obs);
                    observations.AddRange(obs);
                    env.Step((TradeAction)actions.Next(3), (float)(actions.NextDouble() * 2 - 1));
                }
                trades = new List<TradeRecord>(env.Trades);
                return new List<double>(env.EquityCurve);
            }

            List<double> a = RunOnce(out List<float> obsA, out List<TradeRecord> tradesA);
            List<double> b = RunOnce(out List<float> obsB, out List<TradeRecord> tradesB);
            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++) Assert.IsTrue(TestData.SameBits(a[i], b[i]), $"equity[{i}]");
            Assert.AreEqual(obsA.Count, obsB.Count);
            for (int i = 0; i < obsA.Count; i++) Assert.IsTrue(TestData.SameBits(obsA[i], obsB[i]), $"obs[{i}]");
            Assert.AreEqual(tradesA.Count, tradesB.Count);
            Assert.Greater(tradesA.Count, 0);
            for (int i = 0; i < tradesA.Count; i++)
            {
                Assert.AreEqual(tradesA[i].Units, tradesB[i].Units);
                Assert.IsTrue(TestData.SameBits(tradesA[i].Fee, tradesB[i].Fee));
            }
        }

        // ---- T-11 cash and coin never go negative

        [Test]
        public void RandomActions_NeverMakeCashOrCoinNegative()
        {
            var costs = new[]
            {
                new CostModel(),
                new CostModel(0.001, 1, 0.0005),
                new CostModel(0.0025, 5, 0.002),
                new CostModel(0, 0.1, 0.0999),
            };
            TradingEnv env = TestData.TrainEnv();
            var random = new System.Random(11);
            long steps = 0;
            for (int episode = 0; episode < 200; episode++)
            {
                env.ResetForTraining(episode, costs[episode % costs.Length]);
                while (!env.Done)
                {
                    env.Step((TradeAction)random.Next(3), (float)(random.NextDouble() * 2.4 - 1.2));
                    steps++;
                    Account a = env.Account;
                    if (a.Cash < 0 || a.CoinUnits < 0 || double.IsNaN(a.Cash) || double.IsNaN(env.CurrentEquity))
                        Assert.Fail($"episode {episode}, step {env.StepCount}: cash={a.Cash}, units={a.CoinUnits}");
                }
            }
            Assert.AreEqual(200L * 720, steps);
        }

        // ---- T-12 speed (no hard limit; reported)

        [Test]
        public void HundredThousandSteps_TimingIsReported()
        {
            TradingEnv env = TestData.TrainEnv();
            var random = new System.Random(12);
            var obs = new float[ObservationBuilder.Size];
            int seed = 0;
            env.ResetForTraining(seed++, new CostModel());
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < 100_000; i++)
            {
                if (env.Done) env.ResetForTraining(seed++, new CostModel());
                env.WriteObservation(obs);
                env.Step((TradeAction)random.Next(3), (float)(random.NextDouble() * 2 - 1));
            }
            watch.Stop();
            double ms = watch.Elapsed.TotalMilliseconds;
            string line = $"T-12: 100000 steps (with observations, {seed} resets) in {ms:F1} ms = {100_000 / (ms / 1000):F0} steps/s";
            TestContext.WriteLine(line);
            UnityEngine.Debug.Log(line);
            Assert.Pass(line);
        }

        [Test]
        public void ResetAtAGivenCandle_StartsInCashAndRunsToTheEnd()
        {
            CandleSeries s = TestData.RandomWalk(300, 21, 0.01);
            var env = new TradingEnv(s, Btc, 10, 250);
            env.Reset(new CostModel(), 100);
            Assert.AreEqual(100, env.StartIndex);
            Assert.AreEqual(100, env.CurrentIndex);
            Assert.IsTrue(env.Evaluation);
            Assert.IsFalse(env.StartedWithCoin);
            Assert.AreEqual(env.InitialCash, env.Account.Cash);
            Assert.AreEqual(1, env.EquityCurve.Count);
            int steps = 0;
            while (!env.Done)
            {
                env.Step(TradeAction.Hold, 0f);
                steps++;
            }
            Assert.AreEqual(150, steps);
            Assert.AreEqual(EndReason.SegmentEnd, env.EndReason);

            // Same start and actions as an evaluation episode that happens to start there.
            var a = new TradingEnv(s, Btc, 100, 250);
            var b = new TradingEnv(s, Btc, 10, 250);
            a.ResetForEvaluation(0, new CostModel());
            b.Reset(new CostModel(), 100);
            var random = new System.Random(4);
            while (!a.Done)
            {
                int branch = random.Next(3);
                float x = (float)(random.NextDouble() * 2 - 1);
                a.Step((TradeAction)branch, x);
                b.Step((TradeAction)branch, x);
                Assert.IsTrue(TestData.SameBits(a.CurrentEquity, b.CurrentEquity));
            }
            Assert.IsTrue(b.Done);

            Assert.Throws<ArgumentOutOfRangeException>(() => env.Reset(new CostModel(), 31));
            Assert.Throws<ArgumentOutOfRangeException>(() => env.Reset(new CostModel(), 250));
            Assert.Throws<ArgumentNullException>(() => env.Reset(null, 100));
        }

        [Test]
        public void BadSegmentOrMissingReset_Throws()
        {
            CandleSeries s = TestData.Synthetic(100, k => 100);
            Assert.Throws<ArgumentOutOfRangeException>(() => new TradingEnv(s, Btc, 0, 32));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TradingEnv(s, Btc, 50, 40));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TradingEnv(s, Btc, 0, 100));
            var env = new TradingEnv(s, Btc, 0, 99, episodeLength: 720);
            Assert.Throws<InvalidOperationException>(() => env.Step(0, 0f));
            Assert.Throws<InvalidOperationException>(() => env.ResetForTraining(0, new CostModel()));
            Assert.DoesNotThrow(() => env.ResetForEvaluation(0, new CostModel()));
        }

        // ---- T-7 no look-ahead

        [Test]
        public void FutureCandles_DoNotChangeTheObservation()
        {
            const int count = 200, t = 120;
            CandleSeries a = TestData.RandomWalk(count, 7, 0.02);
            var noisy = new List<Candle>();
            var random = new System.Random(99);
            for (int i = 0; i < count; i++)
            {
                if (i <= t) { noisy.Add(a[i]); continue; }
                double o = 1 + random.NextDouble() * 1000, c = 1 + random.NextDouble() * 1000;
                noisy.Add(new Candle(a[i].OpenTimeMs, o, Math.Max(o, c) + 1, Math.Min(o, c) * 0.5, c, random.NextDouble()));
            }
            CandleSeries b = CandleSeries.FromCandles(noisy);

            var envA = new TradingEnv(a, Btc, 0, count - 1);
            var envB = new TradingEnv(b, Btc, 0, count - 1);
            envA.ResetForEvaluation(3, new CostModel());
            envB.ResetForEvaluation(3, new CostModel());
            var actions = new System.Random(5);
            var obsA = new float[ObservationBuilder.Size];
            var obsB = new float[ObservationBuilder.Size];
            while (true)
            {
                envA.WriteObservation(obsA);
                envB.WriteObservation(obsB);
                for (int k = 0; k < obsA.Length; k++)
                    Assert.IsTrue(TestData.SameBits(obsA[k], obsB[k]), $"t={envA.CurrentIndex}, obs[{k}]");
                Assert.AreEqual(envA.BuyEnabled, envB.BuyEnabled);
                Assert.AreEqual(envA.SellEnabled, envB.SellEnabled);
                if (envA.CurrentIndex == t) break;
                int branch = actions.Next(3);
                float x = (float)(actions.NextDouble() * 2 - 1);
                envA.Step((TradeAction)branch, x);
                envB.Step((TradeAction)branch, x);
                Assert.IsTrue(TestData.SameBits(envA.CurrentEquity, envB.CurrentEquity));
            }
            Assert.AreEqual(t, envA.CurrentIndex);
        }

        [Test]
        public void Orders_FillAtTheNextOpen()
        {
            // open[k] = close[k − 1] × 1.01, so open[t + 1] differs from close[t].
            CandleSeries s = TestData.Synthetic(80, k => 100 + k, k => k == 0 ? 100 : (100 + k - 1) * 1.01);
            var env = new TradingEnv(s, Btc, 0, 79);
            env.ResetForEvaluation(0, new CostModel(0.001, 0, 0.0005));
            Assert.AreEqual(32, env.CurrentIndex);
            StepResult r = env.Step(TradeAction.Buy, 1f);
            Assert.IsTrue(r.Traded);
            TradeRecord trade = env.Trades[0];
            Assert.AreEqual(33, trade.CandleIndex);
            Assert.AreEqual(s.OpenAt(33) * 1.0005, trade.Price, 1e-9);
            Assert.AreNotEqual(s.CloseAt(32), s.OpenAt(33));

            env.Step(TradeAction.Sell, 1f);
            TradeRecord sell = env.Trades[1];
            Assert.AreEqual(34, sell.CandleIndex);
            Assert.AreEqual(s.OpenAt(34) * (1 - 0.0005), sell.Price, 1e-9);
        }

        // ---- R-2 training starts for seeds 0..999 are no longer a lattice

        [Test]
        public void TrainingStartsForConsecutiveSeeds_AreSpread()
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
                env.ResetForTraining(seed, new CostModel());
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
        public void SameSeedWithCosts_GivesTheSameStartAndEquityCurve()
        {
            TradingEnv a = TestData.TrainEnv(), b = TestData.TrainEnv();
            var cost = new CostModel(0.001, 0.5, 0.0005);
            a.ResetForTraining(12345, cost);
            b.ResetForTraining(12345, cost);
            Assert.AreEqual(a.StartIndex, b.StartIndex);
            Assert.AreEqual(a.StartedWithCoin, b.StartedWithCoin);
            Assert.IsTrue(TestData.SameBits(a.Account.Cash, b.Account.Cash));
            var actions = new Random(3);
            while (!a.Done)
            {
                int branch = actions.Next(3);
                float x = (float)(actions.NextDouble() * 2 - 1);
                a.Step((TradeAction)branch, x);
                b.Step((TradeAction)branch, x);
            }
            Assert.AreEqual(a.EquityCurve.Count, b.EquityCurve.Count);
            for (int i = 0; i < a.EquityCurve.Count; i++)
                Assert.IsTrue(TestData.SameBits(a.EquityCurve[i], b.EquityCurve[i]), $"equity[{i}]");
        }
    }
}
