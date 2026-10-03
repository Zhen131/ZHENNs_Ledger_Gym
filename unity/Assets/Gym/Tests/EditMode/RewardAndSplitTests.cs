using System;
using Gym.Core;
using NUnit.Framework;

namespace Gym.Tests.EditMode
{
    public class RewardAndSplitTests
    {
        static readonly SymbolRules Btc = SymbolRules.BtcUsdt;

        // ---- T-8 reward

        [Test]
        public void T08_RewardFormula()
        {
            double r = RewardFunction.Compute(10_000, 10_050, out bool clipped);
            Assert.AreEqual(100 * Math.Log(1.005), r, 1e-12);
            Assert.IsFalse(clipped);
            Assert.AreEqual(1, RewardFunction.Compute(10_000, 10_200, out clipped));
            Assert.IsTrue(clipped);
            Assert.AreEqual(-1, RewardFunction.Compute(10_000, 9_800, out clipped));
            Assert.IsTrue(clipped);
            Assert.AreEqual(0, RewardFunction.Compute(10_000, 10_000, out clipped));
            Assert.IsFalse(clipped);
            Assert.Throws<ArgumentOutOfRangeException>(() => RewardFunction.Compute(0, 1, out _));
        }

        [Test]
        public void T08_UnclippedEpisodeRewardsSumToTheLogReturn()
        {
            // Hourly moves of at most 0.3 % keep every step inside ±1.
            CandleSeries s = TestData.RandomWalk(800, 8, 0.003);
            var env = new TradingEnv(s, Btc, 0, s.Count - 1);
            env.Reset(0, true, new CostModel());
            var random = new System.Random(80);
            double sum = 0;
            while (!env.Done)
                sum += env.Step(random.Next(3), (float)(random.NextDouble() * 2 - 1)).Reward;

            Assert.AreEqual(0, env.ClippedRewards);
            Assert.Greater(env.Account.Trades, 10, "the episode should actually trade");
            double first = env.EquityCurve[0], last = env.EquityCurve[env.EquityCurve.Count - 1];
            double expected = 100 * Math.Log(last / first);
            Assert.Greater(Math.Abs(expected), 0.01, "premise: the episode moved equity");
            Assert.That(Math.Abs(sum - expected) / Math.Abs(expected), Is.LessThanOrEqualTo(1e-6));
            Assert.AreEqual(sum, env.RewardSum, 1e-12);
        }

        [Test]
        public void T08_ClippedStepsAreCounted()
        {
            // Price 100, jumps to 105 at 50, back to 100 at 60, to 103 at 70.
            // Fully invested from step 1, each jump moves equity by more than 1 % → 3 clipped steps.
            CandleSeries s = TestData.Synthetic(100, k => k < 50 ? 100 : k < 60 ? 105 : k < 70 ? 100 : 103);
            var env = new TradingEnv(s, Btc, 0, 99);
            env.Reset(0, true, new CostModel());
            Assert.IsTrue(env.Step(ActionCodec.Buy, 1f).Traded);
            int clippedSeen = 0;
            while (!env.Done)
            {
                StepResult r = env.Step(ActionCodec.Hold, 0f);
                if (r.RewardClipped)
                {
                    clippedSeen++;
                    Assert.AreEqual(1, Math.Abs(r.Reward));
                }
            }
            Assert.AreEqual(3, env.ClippedRewards);
            Assert.AreEqual(3, clippedSeen);
        }

        // ---- T-9 split validation

        static SegmentSpec Seg(string name, string from, string to) => SegmentSpec.Parse(name, from, to);

        [Test]
        public void T09_DefaultSplitsAreValidOnTheRealData()
        {
            SplitReport report = SplitValidator.Validate(DefaultSplits.Train, DefaultSplits.Validation, DefaultSplits.Test,
                TestData.Btc, TradingEnv.TrainingEpisodeLength);
            Assert.IsEmpty(report.Errors, string.Join("; ", report.Errors));
            Assert.IsEmpty(report.Warnings, string.Join("; ", report.Warnings));
            Assert.IsTrue(report.IsValid);
        }

        [Test]
        public void T09_OverlapIsAnError()
        {
            SplitReport report = SplitValidator.Validate(DefaultSplits.Train,
                Seg("validation", "2024-08-31", "2025-08-31"), DefaultSplits.Test, TestData.Btc, 720);
            Assert.IsFalse(report.IsValid);
            StringAssert.Contains("overlap", report.Errors[0]);
        }

        // 01D-5: every way two segments can overlap is refused, not only train and validation sharing a day.

        static void AssertOverlap(SplitReport report, string first, string second)
        {
            Assert.IsFalse(report.IsValid);
            Assert.IsTrue(report.Errors.Exists(e => e.StartsWith($"overlap: {first} ") && e.Contains($" and {second} ")),
                $"expected an overlap of {first} and {second}; got: {string.Join("; ", report.Errors)}");
        }

        [Test]
        public void D01_5_ValidationAndTestSharingADayIsAnError()
        {
            SplitReport report = SplitValidator.Validate(DefaultSplits.Train, DefaultSplits.Validation,
                Seg("test", "2025-08-31", "2026-08-31"), TestData.Btc, 720);
            AssertOverlap(report, "validation", "test");
            Assert.AreEqual(1, report.Errors.Count, string.Join("; ", report.Errors));
        }

        [Test]
        public void D01_5_TestInsideTrainingIsAnError()
        {
            SplitReport report = SplitValidator.Validate(DefaultSplits.Train, DefaultSplits.Validation,
                Seg("test", "2020-01-01", "2020-12-31"), TestData.Btc, 720);
            AssertOverlap(report, "train", "test");
            Assert.AreEqual(1, report.Errors.Count, string.Join("; ", report.Errors));
        }

        [Test]
        public void D01_5_TestEqualToValidationIsAnError()
        {
            SplitReport report = SplitValidator.Validate(DefaultSplits.Train, DefaultSplits.Validation,
                Seg("test", "2024-09-01", "2025-08-31"), TestData.Btc, 720);
            AssertOverlap(report, "validation", "test");
            Assert.AreEqual(1, report.Errors.Count, string.Join("; ", report.Errors));
        }

        [Test]
        public void D01_5_ValidationContainingTestIsAnError()
        {
            SplitReport report = SplitValidator.Validate(DefaultSplits.Train,
                Seg("validation", "2024-09-01", "2026-08-31"), DefaultSplits.Test, TestData.Btc, 720);
            AssertOverlap(report, "validation", "test");
            Assert.AreEqual(1, report.Errors.Count, string.Join("; ", report.Errors));
        }

        [Test]
        public void D01_5_TrainingContainingBothOthersIsAnError()
        {
            SplitReport report = SplitValidator.Validate(Seg("train", "2017-08-17", "2026-08-31"),
                DefaultSplits.Validation, DefaultSplits.Test, TestData.Btc, 720);
            AssertOverlap(report, "train", "validation");
            AssertOverlap(report, "train", "test");
            Assert.AreEqual(2, report.Errors.Count, string.Join("; ", report.Errors));
        }

        [Test]
        public void T09_OutOfRangeIsAnError()
        {
            SplitReport late = SplitValidator.Validate(DefaultSplits.Train, DefaultSplits.Validation,
                Seg("test", "2025-09-01", "2026-09-30"), TestData.Btc, 720);
            Assert.IsFalse(late.IsValid);
            StringAssert.Contains("out of range", late.Errors[0]);

            SplitReport early = SplitValidator.Validate(Seg("train", "2017-08-16", "2024-08-31"),
                DefaultSplits.Validation, DefaultSplits.Test, TestData.Btc, 720);
            Assert.IsFalse(early.IsValid);
            StringAssert.Contains("out of range", early.Errors[0]);
        }

        [Test]
        public void T09_TooShortTrainingSegmentIsAnError()
        {
            // 2024-08-01 00:00 .. 2024-08-30 23:00 = 720 bars; an episode needs 721.
            SplitReport shortOne = SplitValidator.Validate(Seg("train", "2024-08-01", "2024-08-30"),
                DefaultSplits.Validation, DefaultSplits.Test, TestData.Btc, 720);
            Assert.IsFalse(shortOne.IsValid);
            StringAssert.Contains("too short", shortOne.Errors[0]);

            SplitReport justEnough = SplitValidator.Validate(Seg("train", "2024-08-01", "2024-08-31"),
                DefaultSplits.Validation, DefaultSplits.Test, TestData.Btc, 720);
            Assert.IsTrue(justEnough.IsValid, string.Join("; ", justEnough.Errors));
        }

        [Test]
        public void T09_BackwardTestIsOnlyAWarning()
        {
            SplitReport report = SplitValidator.Validate(Seg("train", "2019-01-01", "2024-08-31"),
                DefaultSplits.Validation, Seg("test", "2017-08-17", "2018-12-31"), TestData.Btc, 720);
            Assert.IsTrue(report.IsValid, string.Join("; ", report.Errors));
            Assert.AreEqual(1, report.Warnings.Count);
            StringAssert.Contains(SplitValidator.BackwardTestWarning, report.Warnings[0]);
        }
    }
}
