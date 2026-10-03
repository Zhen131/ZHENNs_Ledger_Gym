using System;
using System.Collections.Generic;
using Gym.Core;
using NUnit.Framework;

namespace Gym.Tests.EditMode
{
    public class ObservationTests
    {
        static readonly SymbolRules Btc = SymbolRules.BtcUsdt;

        // ---- T-6 observation

        [Test]
        public void T06_HandCheckedValues()
        {
            // close[k] = 100 + k. At t = 32, close[t] = 132 and close[t − i] = 132 − i, so
            // obs[i − 1] = tanh(10 × ((132 − i) ÷ 132 − 1)) = tanh(−10 i ÷ 132).
            CandleSeries s = TestData.Synthetic(41, k => 100 + k);
            var obs = new float[ObservationBuilder.Size];
            var flat = new Account(Btc, new CostModel(), 10_000);
            ObservationBuilder.Write(s, 32, flat, 0, obs);
            Assert.AreEqual(35, obs.Length);
            for (int i = 1; i <= 32; i++)
                Assert.AreEqual((float)Math.Tanh(-10.0 * i / 132), obs[i - 1], 1e-7, $"obs[{i - 1}]");
            Assert.AreEqual(0f, obs[32]);
            Assert.AreEqual(0f, obs[33]);
            Assert.AreEqual(0f, obs[34]);

            // 10 BTC (1,000,000 units) bought at 120, 5,000 cash, price 132, fee 0.1 %:
            //   position ratio = 1320 ÷ (5000 + 1320)
            //   unrealised = (10 × 132 × 0.999 − 10 × 120) ÷ (10 × 120) = 0.0989
            //   steps since trade 360 → 360 ÷ 720 = 0.5
            var held = new Account(Btc, new CostModel(), 5_000, coinUnits: 1_000_000, avgCost: 120);
            ObservationBuilder.Write(s, 32, held, 360, obs);
            Assert.AreEqual((float)(1320.0 / 6320.0), obs[32], 1e-7);
            Assert.AreEqual((float)Math.Tanh(10 * 0.0989), obs[33], 1e-6);
            Assert.AreEqual(0.5f, obs[34]);
            ObservationBuilder.Write(s, 32, held, 5000, obs);
            Assert.AreEqual(1f, obs[34]);
        }

        [Test]
        public void T06_EveryValueStaysInsideMinusOneToOne()
        {
            CandleSeries s = TestData.Btc;
            var obs = new float[ObservationBuilder.Size];
            var random = new System.Random(6);
            for (int n = 0; n < 2000; n++)
            {
                int t = random.Next(32, s.Count);
                var account = new Account(Btc, new CostModel(), random.NextDouble() * 10_000,
                    coinUnits: random.Next(0, 100_000), avgCost: s.CloseAt(t) * (0.2 + random.NextDouble() * 2));
                ObservationBuilder.Write(s, t, account, random.Next(0, 2000), obs);
                for (int k = 0; k < obs.Length; k++)
                    Assert.That(obs[k], Is.InRange(-1f, 1f), $"t={t}, obs[{k}]");
            }
        }

        [Test]
        public void T06_IndexBeforeThirtyTwoThrows()
        {
            CandleSeries s = TestData.Synthetic(40, k => 100);
            var obs = new float[ObservationBuilder.Size];
            var a = new Account(Btc, new CostModel(), 10_000);
            Assert.Throws<ArgumentOutOfRangeException>(() => ObservationBuilder.Write(s, 31, a, 0, obs));
            Assert.Throws<ArgumentOutOfRangeException>(() => ObservationBuilder.Write(s, 40, a, 0, obs));
            Assert.Throws<ArgumentException>(() => ObservationBuilder.Write(s, 32, a, 0, new float[34]));
            Assert.DoesNotThrow(() => ObservationBuilder.Write(s, 32, a, 0, obs));
        }

        // ---- T-7 no look-ahead

        [Test]
        public void T07_FutureCandlesDoNotChangeTheObservation()
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
            envA.Reset(3, true, new CostModel());
            envB.Reset(3, true, new CostModel());
            var actions = new System.Random(5);
            var obsA = new float[ObservationBuilder.Size];
            var obsB = new float[ObservationBuilder.Size];
            while (true)
            {
                envA.WriteObservation(obsA);
                envB.WriteObservation(obsB);
                for (int k = 0; k < obsA.Length; k++)
                    Assert.IsTrue(TestData.SameBits(obsA[k], obsB[k]), $"t={envA.T}, obs[{k}]");
                Assert.AreEqual(envA.BuyEnabled, envB.BuyEnabled);
                Assert.AreEqual(envA.SellEnabled, envB.SellEnabled);
                if (envA.T == t) break;
                int branch = actions.Next(3);
                float x = (float)(actions.NextDouble() * 2 - 1);
                envA.Step(branch, x);
                envB.Step(branch, x);
                Assert.IsTrue(TestData.SameBits(envA.CurrentEquity, envB.CurrentEquity));
            }
            Assert.AreEqual(t, envA.T);
        }

        [Test]
        public void T07_OrdersFillAtTheNextOpen()
        {
            // open[k] = close[k − 1] × 1.01, so open[t + 1] differs from close[t].
            CandleSeries s = TestData.Synthetic(80, k => 100 + k, k => k == 0 ? 100 : (100 + k - 1) * 1.01);
            var env = new TradingEnv(s, Btc, 0, 79);
            env.Reset(0, true, new CostModel(0.001, 0, 0.0005));
            Assert.AreEqual(32, env.T);
            StepResult r = env.Step(ActionCodec.Buy, 1f);
            Assert.IsTrue(r.Traded);
            TradeRecord trade = env.Trades[0];
            Assert.AreEqual(33, trade.CandleIndex);
            Assert.AreEqual(s.OpenAt(33) * 1.0005, trade.Price, 1e-9);
            Assert.AreNotEqual(s.CloseAt(32), s.OpenAt(33));

            env.Step(ActionCodec.Sell, 1f);
            TradeRecord sell = env.Trades[1];
            Assert.AreEqual(34, sell.CandleIndex);
            Assert.AreEqual(s.OpenAt(34) * (1 - 0.0005), sell.Price, 1e-9);
        }
    }
}
