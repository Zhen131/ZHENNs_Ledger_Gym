using System;
using Gym.Runtime.Play;
using NUnit.Framework;

namespace Gym.Tests.Runtime.Play
{
    public class PriceScaleTests
    {
        /// <summary>
        /// 想要 5 条时允许 3～7 条：1-2-5 的档位一档比一档大 2 到 2.5 倍，在两档交界处，两边的条数也差 2 到 2.5 倍，
        /// 挑离 5 近的那一档，最多差 2 条。下面的取样实测最少 3、最多 7。
        /// </summary>
        const int FewestForFive = 3;
        const int MostForFive = 7;
        const int Samples = 20_000;

        /// <summary>
        /// 取样：可见范围的宽度在 10～50,000 之间、最低价在 1,000～150,000 之间，都按对数均匀取（窄到几十、
        /// 宽到几万，价格从几千到十几万都覆盖到），种子固定。
        /// </summary>
        static (double low, double high) Sample(Random random)
        {
            double width = Math.Pow(10, 1 + random.NextDouble() * (Math.Log10(50_000) - 1));
            double low = Math.Pow(10, 3 + random.NextDouble() * (Math.Log10(150_000) - 3));
            return (low, low + width);
        }

        static bool IsOneTwoFiveStep(double step)
        {
            double exponent = Math.Floor(Math.Log10(step) + 1e-9);
            double mantissa = step / Math.Pow(10, exponent);
            return Math.Abs(mantissa - 1) < 1e-9 || Math.Abs(mantissa - 2) < 1e-9 || Math.Abs(mantissa - 5) < 1e-9;
        }

        static void AssertWellFormed(PriceTicks ticks, double low, double high, string what)
        {
            Assert.IsTrue(IsOneTwoFiveStep(ticks.Step), $"{what}: step {ticks.Step:R}");
            for (int i = 0; i < ticks.Values.Count; i++)
            {
                double v = ticks.Values[i];
                Assert.GreaterOrEqual(v, low, $"{what}: line {v:R} below the range");
                Assert.LessOrEqual(v, high, $"{what}: line {v:R} above the range");
                double multiple = v / ticks.Step;
                Assert.AreEqual(Math.Round(multiple), multiple, 1e-9, $"{what}: {v:R} is not a multiple of {ticks.Step:R}");
                if (i > 0) Assert.AreEqual(ticks.Step, v - ticks.Values[i - 1], ticks.Step * 1e-9, $"{what}: lines are one step apart");
            }
        }

        [Test]
        public void ManyRandomRanges_GiveOneTwoFiveStepsAndLinesInsideTheRange()
        {
            var random = new Random(20261004);
            int fewest = int.MaxValue, most = 0;
            for (int n = 0; n < Samples; n++)
            {
                (double low, double high) = Sample(random);
                PriceTicks ticks = PriceScale.Compute(low, high, CandleChartView.DefaultPriceLines);
                string what = $"[{low:R}, {high:R}]";
                AssertWellFormed(ticks, low, high, what);
                Assert.That(ticks.Values.Count, Is.InRange(FewestForFive, MostForFive), what);
                fewest = Math.Min(fewest, ticks.Values.Count);
                most = Math.Max(most, ticks.Values.Count);
            }
            TestContext.Out.WriteLine($"{Samples} ranges, desired 5 lines: fewest {fewest}, most {most}");
        }

        [Test]
        public void OtherDesiredCounts_StillGiveWellFormedLines([Values(1, 3, 4, 6, 8, 10)] int desired)
        {
            var random = new Random(desired);
            for (int n = 0; n < 2_000; n++)
            {
                (double low, double high) = Sample(random);
                PriceTicks ticks = PriceScale.Compute(low, high, desired);
                AssertWellFormed(ticks, low, high, $"desired {desired}, [{low:R}, {high:R}]");
                Assert.GreaterOrEqual(ticks.Values.Count, 1);
            }
        }

        [Test]
        public void TypicalChartRange_GivesRoundThousands()
        {
            PriceTicks ticks = PriceScale.Compute(41_830.5, 46_210.2, 5);
            Assert.AreEqual(1_000, ticks.Step);
            CollectionAssert.AreEqual(new double[] { 42_000, 43_000, 44_000, 45_000, 46_000 }, ticks.Values);
        }

        [Test]
        public void LinesOnTheEdges_AreKept()
        {
            PriceTicks ticks = PriceScale.Compute(40_000, 45_000, 5);
            Assert.AreEqual(1_000, ticks.Step);
            Assert.AreEqual(40_000, ticks.Values[0]);
            Assert.AreEqual(45_000, ticks.Values[ticks.Values.Count - 1]);
        }

        [Test]
        public void SmallSteps_StayExactMultiples()
        {
            PriceTicks ticks = PriceScale.Compute(0.11, 0.97, 5);
            AssertWellFormed(ticks, 0.11, 0.97, "below one");
            Assert.AreEqual(0.2, ticks.Step, 1e-12);
        }

        [Test]
        public void EmptyOrBrokenRange_GivesNoLines()
        {
            Assert.AreEqual(0, PriceScale.Compute(100, 100, 5).Values.Count);
            Assert.AreEqual(0, PriceScale.Compute(200, 100, 5).Values.Count);
            Assert.AreEqual(0, PriceScale.Compute(double.NaN, 100, 5).Values.Count);
            Assert.AreEqual(0, PriceScale.Compute(0, double.PositiveInfinity, 5).Values.Count);
            Assert.AreEqual(0, PriceScale.Compute(0, 100, 0).Values.Count);
        }

        [Test]
        public void Labels_HaveThousandsSeparatorsAndOnlyTheDecimalsTheStepNeeds()
        {
            Assert.AreEqual("42,000", PriceScale.Label(42_000, 1_000));
            Assert.AreEqual("42,550", PriceScale.Label(42_550, 50));
            Assert.AreEqual("105,000", PriceScale.Label(105_000, 5_000));
            Assert.AreEqual("1.5", PriceScale.Label(1.5, 0.5));
            Assert.AreEqual("0.40", PriceScale.Label(0.4, 0.02));
            Assert.AreEqual("0.05", PriceScale.Label(0.05, 0.05));
        }
    }
}
