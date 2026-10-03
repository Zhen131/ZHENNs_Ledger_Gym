using System;
using Gym.Core.Accounting;
using Gym.Core.Env;
using Gym.Core.Market;
using NUnit.Framework;

namespace Gym.Tests.Core.Env
{
    public class ObservationBuilderTests
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
    }
}
