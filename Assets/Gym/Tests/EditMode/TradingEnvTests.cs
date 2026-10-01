using System;
using System.Collections.Generic;
using System.Diagnostics;
using Gym.Core;
using NUnit.Framework;

namespace Gym.Tests.EditMode
{
    public class TradingEnvTests
    {
        static readonly SymbolRules Btc = SymbolRules.BtcUsdt;

        // ---- T-10 one episode

        [Test]
        public void T10_TrainingEpisodeEndsAtStep720()
        {
            TradingEnv env = TestData.TrainEnv();
            env.Reset(42, false, new CostModel());
            for (int i = 1; i < 720; i++)
            {
                StepResult r = env.Step(ActionCodec.Hold, 0f);
                Assert.IsFalse(r.Done, $"done early at step {i}");
            }
            StepResult end = env.Step(ActionCodec.Hold, 0f);
            Assert.IsTrue(end.Done);
            Assert.AreEqual(EndReason.EpisodeLength, end.Reason);
            Assert.AreEqual(720, env.StepCount);
            Assert.AreEqual(env.StartIndex + 720, env.T);
            Assert.AreEqual(721, env.EquityCurve.Count);
            Assert.Throws<InvalidOperationException>(() => env.Step(ActionCodec.Hold, 0f));
        }

        [Test]
        public void T10_StartsForSeedsZeroTo999AreLegalAndHalfHoldCoin()
        {
            TradingEnv env = TestData.TrainEnv();
            int lo = Math.Max(env.First, ObservationBuilder.Lookback), hi = env.Last - 720;
            int withCoin = 0;
            for (int seed = 0; seed < 1000; seed++)
            {
                env.Reset(seed, false, new CostModel());
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
        public void T10_EvaluationRunsTheWholeTestSegmentFromCash()
        {
            CandleSeries s = TestData.Btc;
            TradingEnv env = TradingEnv.ForSegment(s, Btc, DefaultSplits.Test);
            env.Reset(123, true, new CostModel());
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
                r = env.Step(ActionCodec.Hold, 0f);
                steps++;
            }
            Assert.AreEqual(EndReason.SegmentEnd, r.Reason);
            Assert.AreEqual(last, env.T);
            Assert.AreEqual(new DateTime(2026, 8, 31, 23, 0, 0, DateTimeKind.Utc), s.OpenTimeUtc(env.T));
            Assert.AreEqual(last - first, steps);
            Assert.AreEqual(last - first + 1, env.EquityCurve.Count);
        }

        [Test]
        public void T10_SameSeedAndActionsGiveIdenticalBits()
        {
            var costs = new CostModel(0.001, 0.5, 0.0005);
            List<double> RunOnce(out List<float> observations, out List<TradeRecord> trades)
            {
                TradingEnv env = TestData.TrainEnv();
                env.Reset(2026, false, costs);
                var actions = new System.Random(77);
                var obs = new float[ObservationBuilder.Size];
                observations = new List<float>();
                while (!env.Done)
                {
                    env.WriteObservation(obs);
                    observations.AddRange(obs);
                    env.Step(actions.Next(3), (float)(actions.NextDouble() * 2 - 1));
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
        public void T11_CashAndCoinNeverGoNegativeUnderRandomActions()
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
                env.Reset(episode, false, costs[episode % costs.Length]);
                while (!env.Done)
                {
                    env.Step(random.Next(3), (float)(random.NextDouble() * 2.4 - 1.2));
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
        public void T12_HundredThousandStepsTiming()
        {
            TradingEnv env = TestData.TrainEnv();
            var random = new System.Random(12);
            var obs = new float[ObservationBuilder.Size];
            int seed = 0;
            env.Reset(seed++, false, new CostModel());
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < 100_000; i++)
            {
                if (env.Done) env.Reset(seed++, false, new CostModel());
                env.WriteObservation(obs);
                env.Step(random.Next(3), (float)(random.NextDouble() * 2 - 1));
            }
            watch.Stop();
            double ms = watch.Elapsed.TotalMilliseconds;
            string line = $"T-12: 100000 steps (with observations, {seed} resets) in {ms:F1} ms = {100_000 / (ms / 1000):F0} steps/s";
            TestContext.WriteLine(line);
            UnityEngine.Debug.Log(line);
            Assert.Pass(line);
        }

        [Test]
        public void T10_ConstructorAndResetGuardRails()
        {
            CandleSeries s = TestData.Synthetic(100, k => 100);
            Assert.Throws<ArgumentOutOfRangeException>(() => new TradingEnv(s, Btc, 0, 32));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TradingEnv(s, Btc, 50, 40));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TradingEnv(s, Btc, 0, 100));
            var env = new TradingEnv(s, Btc, 0, 99, episodeLength: 720);
            Assert.Throws<InvalidOperationException>(() => env.Step(0, 0f));
            Assert.Throws<InvalidOperationException>(() => env.Reset(0, false, new CostModel()));
            Assert.DoesNotThrow(() => env.Reset(0, true, new CostModel()));
        }
    }
}
