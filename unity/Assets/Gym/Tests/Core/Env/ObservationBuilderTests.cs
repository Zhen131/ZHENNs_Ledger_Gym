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

        // ---- observation

        [Test]
        public void SyntheticSeries_GivesTheHandCheckedValues()
        {
            // close[k] = 100 + k。在 t = 32 时，close[t] = 132，close[t − i] = 132 − i，所以
            // obs[i − 1] = tanh(10 × ((132 − i) ÷ 132 − 1)) = tanh(−10 i ÷ 132)。
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

            // 在 120 买入的 10 BTC（1,000,000 个单位），现金 5,000，价格 132，fee 0.1 %：
            //   持仓比例 = 1320 ÷ (5000 + 1320)
            //   未实现收益率 = (10 × 132 × 0.999 − 10 × 120) ÷ (10 × 120) = 0.0989
            //   距上次交易 360 个 step → 360 ÷ 720 = 0.5
            var held = new Account(Btc, new CostModel(), 5_000, coinUnits: 1_000_000, avgCost: 120);
            ObservationBuilder.Write(s, 32, held, 360, obs);
            Assert.AreEqual((float)(1320.0 / 6320.0), obs[32], 1e-7);
            Assert.AreEqual((float)Math.Tanh(10 * 0.0989), obs[33], 1e-6);
            Assert.AreEqual(0.5f, obs[34]);
            ObservationBuilder.Write(s, 32, held, 5000, obs);
            Assert.AreEqual(1f, obs[34]);
        }

        [Test]
        public void RandomStates_KeepEveryValueInsideMinusOneToOne()
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
        public void IndexBeforeThirtyTwoOrPastTheEnd_Throws()
        {
            CandleSeries s = TestData.Synthetic(40, k => 100);
            var obs = new float[ObservationBuilder.Size];
            var a = new Account(Btc, new CostModel(), 10_000);
            Assert.Throws<ArgumentOutOfRangeException>(() => ObservationBuilder.Write(s, 31, a, 0, obs));
            Assert.Throws<ArgumentOutOfRangeException>(() => ObservationBuilder.Write(s, 40, a, 0, obs));
            Assert.Throws<ArgumentException>(() => ObservationBuilder.Write(s, 32, a, 0, new float[34]));
            Assert.DoesNotThrow(() => ObservationBuilder.Write(s, 32, a, 0, obs));
        }

        // ---- 观测集（13 批）：三档的长度，base 和改动前逐位相同

        [Test]
        public void ThreeSets_HaveTheirSizesAndHistoryLengths_AndShareThePriceWindow()
        {
            Assert.AreEqual(32, ObservationBuilder.PriceWindow);
            Assert.AreEqual(32, ObservationBuilder.PositionRatioIndex);
            Assert.AreEqual(33, ObservationBuilder.UnrealizedReturnIndex);
            Assert.AreEqual(34, ObservationBuilder.StepsSinceTradeIndex);
            Assert.AreEqual(35, ObservationSet.Base.Size());
            Assert.AreEqual(41, ObservationSet.Long.Size());
            Assert.AreEqual(43, ObservationSet.LongTa.Size());
            Assert.AreEqual(32, ObservationSet.Base.HistoryLength());
            Assert.AreEqual(2160, ObservationSet.Long.HistoryLength());
            Assert.AreEqual(2160, ObservationSet.LongTa.HistoryLength());
            Assert.AreEqual(ObservationBuilder.Size, ObservationSet.Base.Size(), "base is the old 35");
            Assert.AreEqual(ObservationBuilder.Lookback, ObservationSet.Base.HistoryLength(), "base keeps the old 32");
            CollectionAssert.AreEqual(new[] { 35, 36, 37, 38, 39, 40, 41, 42 }, new[]
            {
                ObservationBuilder.Return1DayIndex, ObservationBuilder.Return1WeekIndex, ObservationBuilder.Return30DayIndex,
                ObservationBuilder.Return90DayIndex, ObservationBuilder.VolatilityIndex, ObservationBuilder.DrawdownIndex,
                ObservationBuilder.MacdIndex, ObservationBuilder.RsiIndex,
            });
        }

        /// <summary>改动前 ObservationBuilder.Write 的原样副本（13 批之前的 35 个数），用来锁死 base 档逐位不变。</summary>
        static void WriteAsBefore13(CandleSeries series, int decisionIndex, Account account, int stepsSinceTrade, float[] dst)
        {
            double now = series.CloseAt(decisionIndex);
            for (int i = 1; i <= 32; i++)
                dst[i - 1] = (float)Math.Tanh(10 * (series.CloseAt(decisionIndex - i) / now - 1));
            dst[32] = (float)account.PositionRatio(now);
            dst[33] = (float)Math.Tanh(10 * account.UnrealizedReturn(now));
            dst[34] = (float)Math.Min((double)stepsSinceTrade / 720, 1.0);
        }

        [Test]
        public void Base_IsBitForBitTheSameThroughTheOldEntryTheNewEntryAndThePreviousCode()
        {
            CandleSeries s = TestData.Btc;
            var before = new float[35];
            var oldEntry = new float[ObservationBuilder.Size];
            var newEntry = new float[ObservationSet.Base.Size()];
            var longer = new float[ObservationSet.LongTa.Size()];
            var random = new System.Random(13);
            for (int n = 0; n < 3000; n++)
            {
                int t = n == 0 ? 32 : n == 1 ? s.Count - 1 : random.Next(32, s.Count);
                var account = new Account(Btc, new CostModel(), random.NextDouble() * 10_000,
                    coinUnits: random.Next(0, 100_000), avgCost: s.CloseAt(t) * (0.2 + random.NextDouble() * 2));
                int steps = random.Next(0, 2000);
                WriteAsBefore13(s, t, account, steps, before);
                ObservationBuilder.Write(s, t, account, steps, oldEntry);
                ObservationBuilder.Write(ObservationSet.Base, s, t, account, steps, newEntry);
                for (int k = 0; k < 35; k++)
                {
                    Assert.IsTrue(TestData.SameBits(before[k], oldEntry[k]), $"t={t}, obs[{k}]: old entry vs the code before 13");
                    Assert.IsTrue(TestData.SameBits(before[k], newEntry[k]), $"t={t}, obs[{k}]: new entry vs the code before 13");
                }
                if (t < ObservationBuilder.LongHistory) continue;
                foreach (ObservationSet set in new[] { ObservationSet.Long, ObservationSet.LongTa })
                {
                    ObservationBuilder.Write(set, s, t, account, steps, longer);
                    for (int k = 0; k < 35; k++)
                        Assert.IsTrue(TestData.SameBits(before[k], longer[k]), $"t={t}, {set.Name()} obs[{k}] is the base value");
                }
            }
        }

        // ---- long：35～40 号的手算用例

        /// <summary>
        /// c[i] = 100 × exp(μ i + a × (i mod 2))：小时对数收益在 μ + a 和 μ − a 之间交替，720 个里各一半，
        /// 所以平均是 μ、总体标准差恰好是 a（σ 不为零、能手算）；h 是偶数时 ln(c[t] ÷ c[t − h]) = μ h。
        /// </summary>
        static CandleSeries Alternating(int count, double mu, double a) =>
            TestData.Synthetic(count, i => 100 * Math.Exp(mu * i + a * (i % 2)));

        [Test]
        public void Long_OnAlternatingHoursWithDrift_GivesTheHandCheckedValues()
        {
            const double mu = 0.0002, a = 0.01;
            CandleSeries s = Alternating(2400, mu, a);
            var flat = new Account(Btc, new CostModel(), 10_000);
            var obs = new float[ObservationSet.Long.Size()];

            // σ：720 个收益（不是 719、721 个）的总体标准差（除以 720）就是 a。
            Assert.AreEqual(a, ObservationBuilder.Volatility(s, 2301), 1e-12);
            Assert.AreEqual(a, ObservationBuilder.Volatility(s, 2300), 1e-12);

            // z = μ h ÷ (a √h) = μ √h ÷ a；值 = tanh(z ÷ 3)。
            //   h = 24：tanh(0.0002 × 4.89898 ÷ 0.03) = tanh(0.032660) = 0.032648
            //   h = 168：tanh(0.086410) = 0.086195；h = 720：tanh(0.178885) = 0.177001；h = 2160：tanh(0.309839) = 0.300290
            // 把 √h 写成 h（h = 24 时 tanh(0.16) = 0.158649）、漏掉 ÷ 3（tanh(0.097980) = 0.097667）都过不了。
            ObservationBuilder.Write(ObservationSet.Long, s, 2301, flat, 0, obs);
            int[] hours = { 24, 168, 720, 2160 };
            double[] expected = { 0.032648, 0.086195, 0.177001, 0.300290 };
            for (int k = 0; k < 4; k++)
            {
                Assert.AreEqual(Math.Tanh(mu * Math.Sqrt(hours[k]) / a / 3), obs[35 + k], 1e-6, $"obs[{35 + k}] formula");
                Assert.AreEqual(expected[k], obs[35 + k], 1e-6, $"obs[{35 + k}] by hand");
            }
            // 39：tanh(ln(0.01 ÷ 0.0065)) = tanh(0.430783) = 0.405975。
            Assert.AreEqual(0.405975, obs[39], 1e-6);
            // 40：t = 2301 是奇数，c[t] 就是过去 2160 根的最高价，回撤 0。
            Assert.AreEqual(0f, obs[40]);

            // t = 2300 是偶数：最高价是 c[t − 1] = 100 × exp(μ (t − 1) + a)，回撤 = 1 − exp(μ − a) = 1 − exp(−0.0098) = 0.009752。
            ObservationBuilder.Write(ObservationSet.Long, s, 2300, flat, 0, obs);
            Assert.AreEqual(1 - Math.Exp(mu - a), obs[40], 1e-7);
            Assert.AreEqual(0.009752, obs[40], 1e-6);
            for (int k = 0; k < 4; k++)
                Assert.AreEqual(Math.Tanh(mu * Math.Sqrt(hours[k]) / a / 3), obs[35 + k], 1e-6, $"even t, obs[{35 + k}]");
        }

        [Test]
        public void Long_OnFlatPrices_UsesTheVolatilityFloor()
        {
            CandleSeries s = TestData.Synthetic(2200, k => 250);
            var obs = new float[ObservationSet.Long.Size()];
            ObservationBuilder.Write(ObservationSet.Long, s, 2160, new Account(Btc, new CostModel(), 10_000), 0, obs);
            Assert.AreEqual(0, ObservationBuilder.Volatility(s, 2160));
            for (int k = 35; k <= 38; k++) Assert.AreEqual(0f, obs[k], $"obs[{k}]: ln(1) = 0 over σ = 1e-6");
            // σ 抬到 1e-6：tanh(ln(1e-6 ÷ 0.0065)) = tanh(−8.779557) = −0.99999995，离 −1 不到 1e-7。
            Assert.AreEqual((float)Math.Tanh(Math.Log(1e-6 / 0.0065)), obs[39]);
            Assert.AreEqual(-1f, obs[39], 1e-6);
            Assert.AreEqual(0f, obs[40]);
        }

        [Test]
        public void Long_Drawdown_LooksBackOver2160CandlesIncludingT()
        {
            const int t = 2300;
            var obs = new float[ObservationSet.Long.Size()];
            var flat = new Account(Btc, new CostModel(), 10_000);
            // t − 2159 在窗口里（最旧的一根）：高点 200，c[t] = 100，回撤 0.5。
            CandleSeries inside = TestData.Synthetic(2400, i => i == t - 2159 ? 200 : 100);
            ObservationBuilder.Write(ObservationSet.Long, inside, t, flat, 0, obs);
            Assert.AreEqual(0.5f, obs[40]);
            // t − 2160 已经在窗口外。
            CandleSeries outside = TestData.Synthetic(2400, i => i == t - 2160 ? 200 : 100);
            ObservationBuilder.Write(ObservationSet.Long, outside, t, flat, 0, obs);
            Assert.AreEqual(0f, obs[40]);
            // 90 天里最高价是 120，现在 90：回撤 (120 − 90) ÷ 120 = 0.25。
            CandleSeries lower = TestData.Synthetic(2400, i => i == t ? 90 : i == t - 1000 ? 120 : 100);
            ObservationBuilder.Write(ObservationSet.Long, lower, t, flat, 0, obs);
            Assert.AreEqual(0.25f, obs[40]);
        }

        // ---- long-ta：41、42 号的手算用例

        /// <summary>
        /// 按天采样的序列 d[k] = c[t − 24k] 取 <paramref name="days"/> 里的值（k 超出时取最后一个）；同一天里别的小时
        /// 取 <paramref name="otherHours"/>（为 null 时也取那一天的值），它们不该影响 41、42 号。
        /// </summary>
        static CandleSeries Daily(int t, double[] days, Func<int, double> otherHours = null) =>
            TestData.Synthetic(t + 1, i =>
            {
                int back = t - i;
                int k = Math.Min((back + 23) / 24, days.Length - 1);
                if (back % 24 != 0 && otherHours != null) return otherHours(i);
                return days[k];
            });

        [Test]
        public void LongTa_Macd_IsHandChecked()
        {
            const int t = 2200;
            const double p = 100, delta = 0.1;
            var obs = new float[ObservationSet.LongTa.Size()];
            var flat = new Account(Btc, new CostModel(), 10_000);

            // 只有最新一天 d[0] = p (1 + δ)，更早的都是 p：EMA12 = p + (2/13) p δ，EMA26 = p + (2/27) p δ，
            // macd = (2/13 − 2/27) δ ÷ (1 + δ) = 0.0797721 × 0.1 ÷ 1.1 = 0.00725201，值 = tanh(0.145040) = 0.144032。
            var newest = new double[91];
            for (int k = 0; k < newest.Length; k++) newest[k] = k == 0 ? p * (1 + delta) : p;
            CandleSeries a = Daily(t, newest);
            Assert.AreEqual((2.0 / 13 - 2.0 / 27) * delta / (1 + delta), ObservationBuilder.Macd(a, t), 1e-12);
            ObservationBuilder.Write(ObservationSet.LongTa, a, t, flat, 0, obs);
            Assert.AreEqual(0.144032, obs[41], 1e-6);

            // 只有最旧一天 d[89] = p (1 + δ)：两条均线都从它起算，再被 89 个 p 拉回去，
            // macd = δ × ((11/13)^89 − (25/27)^89) = 0.1 × (3.49e-7 − 1.05995e-3) = −1.0596e-4；顺序反了会得到正数。
            var oldest = new double[91];
            for (int k = 0; k < oldest.Length; k++) oldest[k] = k == 89 ? p * (1 + delta) : p;
            CandleSeries b = Daily(t, oldest);
            double expected = delta * (Math.Pow(11.0 / 13, 89) - Math.Pow(25.0 / 27, 89));
            Assert.AreEqual(expected, ObservationBuilder.Macd(b, t), 1e-12);
            Assert.AreEqual(-1.0596e-4, expected, 1e-8);
            ObservationBuilder.Write(ObservationSet.LongTa, b, t, flat, 0, obs);
            Assert.AreEqual((float)Math.Tanh(20 * expected), obs[41], 1e-7);

            // 第 90 天（k = 90）以前的、以及同一天里别的小时的价格都不进 MACD。
            var older = (double[])newest.Clone();
            older[90] = 5 * p;
            CandleSeries c = Daily(t, older, i => 1 + (i * 7919 % 1000));
            Assert.AreEqual(ObservationBuilder.Macd(a, t), ObservationBuilder.Macd(c, t));
        }

        [Test]
        public void LongTa_MacdOnARisingSeries_IsPositive()
        {
            CandleSeries s = TestData.Synthetic(2400, i => 100 * Math.Pow(1.0002, i));
            var obs = new float[ObservationSet.LongTa.Size()];
            ObservationBuilder.Write(ObservationSet.LongTa, s, 2399, new Account(Btc, new CostModel(), 10_000), 0, obs);
            Assert.Greater(ObservationBuilder.Macd(s, 2399), 0);
            Assert.Greater(obs[41], 0f);
        }

        /// <summary>从最旧（d[15]）往新一天天加上 changes[k] = d[k] − d[k + 1]（k = 14 … 0），得到 d[0] … d[15]。</summary>
        static double[] DaysFromChanges(double start, double[] changes)
        {
            var days = new double[changes.Length + 1];
            days[changes.Length] = start;
            for (int k = changes.Length - 1; k >= 0; k--) days[k] = days[k + 1] + changes[k];
            return days;
        }

        [Test]
        public void LongTa_Rsi_IsHandChecked()
        {
            const int t = 2200;
            var obs = new float[ObservationSet.LongTa.Size()];
            var flat = new Account(Btc, new CostModel(), 10_000);

            // 最近 14 个日变化：10 个 +2、4 个 −1；第 15 个（d[14] − d[15]）是 −50，不在窗口里。
            // G = 20 ÷ 14，L = 4 ÷ 14，RSI = 100 − 100 ÷ (1 + 5) = 83.3333，值 = 0.666667。
            var changes = new double[15];
            for (int k = 0; k < 14; k++) changes[k] = k < 10 ? 2 : -1;
            changes[14] = -50;
            CandleSeries s = Daily(t, DaysFromChanges(300, changes), i => 1);
            Assert.AreEqual(100 - 100 / 6.0, ObservationBuilder.Rsi(s, t), 1e-9);
            ObservationBuilder.Write(ObservationSet.LongTa, s, t, flat, 0, obs);
            Assert.AreEqual(2 / 3.0, obs[42], 1e-6);

            // 连涨 14 天：L = 0、G > 0，RSI = 100，值 = 1（第 15 天跌多少都不算）。
            var up = new double[15];
            for (int k = 0; k < 14; k++) up[k] = 3;
            up[14] = -80;
            ObservationBuilder.Write(ObservationSet.LongTa, Daily(t, DaysFromChanges(300, up)), t, flat, 0, obs);
            Assert.AreEqual(1f, obs[42]);

            // 14 天不动：G = L = 0，RSI = 50，值 = 0。
            ObservationBuilder.Write(ObservationSet.LongTa, Daily(t, DaysFromChanges(300, new double[15])), t, flat, 0, obs);
            Assert.AreEqual(0f, obs[42]);

            // 连跌 14 天：G = 0，RSI = 100 − 100 ÷ 1 = 0，值 = −1。
            var down = new double[15];
            for (int k = 0; k < 14; k++) down[k] = -3;
            ObservationBuilder.Write(ObservationSet.LongTa, Daily(t, DaysFromChanges(300, down)), t, flat, 0, obs);
            Assert.AreEqual(-1f, obs[42]);
        }

        // ---- 不偷看未来：每一档改掉 t 之后的 K 线，观测逐位不变

        [TestCase(ObservationSet.Base)]
        [TestCase(ObservationSet.Long)]
        [TestCase(ObservationSet.LongTa)]
        public void FutureCandles_DoNotChangeTheObservationOfThisSet(ObservationSet set)
        {
            const int count = 2700;
            CandleSeries a = TestData.RandomWalk(count, 21, 0.02);
            var obsA = new float[set.Size()];
            var obsB = new float[set.Size()];
            var random = new System.Random(77);
            foreach (int t in new[] { set.HistoryLength(), 2300, 2500, count - 2 })
            {
                var noisy = new System.Collections.Generic.List<Candle>();
                for (int i = 0; i < count; i++)
                {
                    if (i <= t) { noisy.Add(a[i]); continue; }
                    double o = 1 + random.NextDouble() * 1000, c = 1 + random.NextDouble() * 1000;
                    noisy.Add(new Candle(a[i].OpenTimeMs, o, Math.Max(o, c) + 1, Math.Min(o, c) * 0.5, c, random.NextDouble()));
                }
                CandleSeries b = CandleSeries.FromCandles(noisy);
                var accountA = new Account(Btc, new CostModel(), 4_000, coinUnits: 30_000, avgCost: a.CloseAt(t) * 0.9);
                var accountB = new Account(Btc, new CostModel(), 4_000, coinUnits: 30_000, avgCost: a.CloseAt(t) * 0.9);
                ObservationBuilder.Write(set, a, t, accountA, 17, obsA);
                ObservationBuilder.Write(set, b, t, accountB, 17, obsB);
                for (int k = 0; k < obsA.Length; k++)
                    Assert.IsTrue(TestData.SameBits(obsA[k], obsB[k]), $"{set.Name()} t={t}, obs[{k}]: {obsA[k]} vs {obsB[k]}");
            }
        }

        [Test]
        public void DecisionIndexBeforeTheHistoryLengthOrAShortArray_Throws()
        {
            CandleSeries s = TestData.Synthetic(2300, k => 100 + k % 7);
            var a = new Account(Btc, new CostModel(), 10_000);
            foreach (ObservationSet set in new[] { ObservationSet.Long, ObservationSet.LongTa })
            {
                var obs = new float[set.Size()];
                Assert.Throws<ArgumentOutOfRangeException>(() => ObservationBuilder.Write(set, s, 2159, a, 0, obs), set.Name());
                Assert.Throws<ArgumentOutOfRangeException>(() => ObservationBuilder.Write(set, s, 32, a, 0, obs), set.Name());
                Assert.Throws<ArgumentOutOfRangeException>(() => ObservationBuilder.Write(set, s, 2300, a, 0, obs), set.Name());
                Assert.Throws<ArgumentException>(() => ObservationBuilder.Write(set, s, 2160, a, 0, new float[set.Size() - 1]), set.Name());
                Assert.DoesNotThrow(() => ObservationBuilder.Write(set, s, 2160, a, 0, obs), set.Name());
            }
            Assert.Throws<ArgumentOutOfRangeException>(() => ObservationBuilder.Write(ObservationSet.Base, s, 31, a, 0, new float[35]));
            Assert.DoesNotThrow(() => ObservationBuilder.Write(ObservationSet.Base, s, 32, a, 0, new float[35]));
            Assert.Throws<ArgumentException>(() => ObservationBuilder.Write(ObservationSet.Long, s, 2160, a, 0, new float[35]),
                "a base-sized array is too short for long");
        }

        // ---- 真实 BTC 数据：和另一份独立的计算（numpy）对得上，值都在范围里

        [Test]
        public void RealBtc_AtTheStartOfValidationBear_MatchesAnIndependentComputation()
        {
            CandleSeries s = TestData.Btc;
            int t = s.FirstIndexOnOrAfter(new DateTime(2022, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            Assert.AreEqual(38348, t);
            Assert.AreEqual(46656.13, s.CloseAt(t));
            var obs = new float[ObservationSet.LongTa.Size()];
            ObservationBuilder.Write(ObservationSet.LongTa, s, t, new Account(Btc, new CostModel(), 10_000), 0, obs);
            // 13C 报告里的 numpy 脚本（ddof=0 的标准差、逐个按第 1 节的公式）在同一个下标算出的值。
            double[] numpy =
            {
                -0.08719155675196329, -0.32135853019107197, -0.344091510721907, -0.01355277584065578,
                0.0654271289633978, 0.32021533448077766, -0.49825120983970944, 0.030324410590706067,
            };
            for (int k = 0; k < numpy.Length; k++)
                Assert.AreEqual(numpy[k], obs[35 + k], 1e-6, $"obs[{35 + k}]");
            Assert.AreEqual(0.006940146644002364, ObservationBuilder.Volatility(s, t), 1e-15);
            TestContext.WriteLine($"BTCUSDT {s.OpenTimeUtc(t):yyyy-MM-dd HH:mm} UTC, index {t}, close {s.CloseAt(t)}: " +
                                  string.Join(", ", Array.ConvertAll(obs, v => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture))));
        }

        [Test]
        public void RandomStates_KeepEveryLongTaValueInsideItsRange()
        {
            CandleSeries s = TestData.Btc;
            var obs = new float[ObservationSet.LongTa.Size()];
            var random = new System.Random(42);
            for (int n = 0; n < 1000; n++)
            {
                int t = random.Next(ObservationBuilder.LongHistory, s.Count);
                var account = new Account(Btc, new CostModel(), random.NextDouble() * 10_000,
                    coinUnits: random.Next(0, 100_000), avgCost: s.CloseAt(t) * (0.2 + random.NextDouble() * 2));
                ObservationBuilder.Write(ObservationSet.LongTa, s, t, account, random.Next(0, 2000), obs);
                for (int k = 0; k < obs.Length; k++)
                    Assert.That(obs[k], Is.InRange(-1f, 1f), $"t={t}, obs[{k}]");
                Assert.That(obs[40], Is.GreaterThanOrEqualTo(0f).And.LessThan(1f), $"t={t}, drawdown");
            }
        }
    }
}
