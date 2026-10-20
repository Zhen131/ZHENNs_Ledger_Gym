using Gym.Core.Env;
using Gym.Core.Market;
using NUnit.Framework;

namespace Gym.Tests.Core.Market
{
    public class SplitValidatorTests
    {
        // ---- 检查分段划分

        static SegmentSpec Seg(string name, string from, string to) => SegmentSpec.Parse(name, from, to);

        [Test]
        public void DefaultSplitsOnTheCommittedData_AreValidWithoutWarnings()
        {
            SplitReport report = SplitValidator.Validate(DefaultSplits.Train, DefaultSplits.Validation, DefaultSplits.Test,
                TestData.Btc, TradingEnv.TrainingEpisodeLength);
            Assert.IsEmpty(report.Errors, string.Join("; ", report.Errors));
            Assert.IsEmpty(report.Warnings, string.Join("; ", report.Warnings));
            Assert.IsTrue(report.IsValid);
        }

        [Test]
        public void ValidationOverlappingTraining_IsAnError()
        {
            SplitReport report = SplitValidator.Validate(DefaultSplits.Train,
                Seg("validation", "2024-08-31", "2025-08-31"), DefaultSplits.Test, TestData.Btc, 720);
            Assert.IsFalse(report.IsValid);
            StringAssert.Contains("overlap", report.Errors[0]);
        }

        // 两个分段重叠的每一种情况都会被拒绝，不只是训练段和验证段共用一天这一种。

        static void AssertOverlap(SplitReport report, string first, string second)
        {
            Assert.IsFalse(report.IsValid);
            Assert.IsTrue(report.Errors.Exists(e => e.StartsWith($"overlap: {first} ") && e.Contains($" and {second} ")),
                $"expected an overlap of {first} and {second}; got: {string.Join("; ", report.Errors)}");
        }

        [Test]
        public void ValidationAndTestSharingADay_IsAnError()
        {
            SplitReport report = SplitValidator.Validate(DefaultSplits.Train, DefaultSplits.Validation,
                Seg("test", "2025-08-31", "2026-08-31"), TestData.Btc, 720);
            AssertOverlap(report, "validation", "test");
            Assert.AreEqual(1, report.Errors.Count, string.Join("; ", report.Errors));
        }

        [Test]
        public void TestInsideTraining_IsAnError()
        {
            SplitReport report = SplitValidator.Validate(DefaultSplits.Train, DefaultSplits.Validation,
                Seg("test", "2020-01-01", "2020-12-31"), TestData.Btc, 720);
            AssertOverlap(report, "train", "test");
            Assert.AreEqual(1, report.Errors.Count, string.Join("; ", report.Errors));
        }

        [Test]
        public void TestEqualToValidation_IsAnError()
        {
            SplitReport report = SplitValidator.Validate(DefaultSplits.Train, DefaultSplits.Validation,
                Seg("test", "2024-09-01", "2025-08-31"), TestData.Btc, 720);
            AssertOverlap(report, "validation", "test");
            Assert.AreEqual(1, report.Errors.Count, string.Join("; ", report.Errors));
        }

        [Test]
        public void ValidationContainingTest_IsAnError()
        {
            SplitReport report = SplitValidator.Validate(DefaultSplits.Train,
                Seg("validation", "2024-09-01", "2026-08-31"), DefaultSplits.Test, TestData.Btc, 720);
            AssertOverlap(report, "validation", "test");
            Assert.AreEqual(1, report.Errors.Count, string.Join("; ", report.Errors));
        }

        [Test]
        public void TrainingContainingBothOthers_IsAnErrorForEach()
        {
            SplitReport report = SplitValidator.Validate(Seg("train", "2017-08-17", "2026-08-31"),
                DefaultSplits.Validation, DefaultSplits.Test, TestData.Btc, 720);
            AssertOverlap(report, "train", "validation");
            AssertOverlap(report, "train", "test");
            Assert.AreEqual(2, report.Errors.Count, string.Join("; ", report.Errors));
        }

        [Test]
        public void SegmentOutsideTheData_IsAnError()
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
        public void TrainingSegmentShorterThanAnEpisode_IsAnError()
        {
            // 2024-08-01 00:00 .. 2024-08-30 23:00 = 720 根 bar；一个 episode 需要 721 根。
            SplitReport shortOne = SplitValidator.Validate(Seg("train", "2024-08-01", "2024-08-30"),
                DefaultSplits.Validation, DefaultSplits.Test, TestData.Btc, 720);
            Assert.IsFalse(shortOne.IsValid);
            StringAssert.Contains("too short", shortOne.Errors[0]);

            SplitReport justEnough = SplitValidator.Validate(Seg("train", "2024-08-01", "2024-08-31"),
                DefaultSplits.Validation, DefaultSplits.Test, TestData.Btc, 720);
            Assert.IsTrue(justEnough.IsValid, string.Join("; ", justEnough.Errors));
        }

        [Test]
        public void TestBeforeTraining_IsOnlyAWarning()
        {
            SplitReport report = SplitValidator.Validate(Seg("train", "2019-01-01", "2024-08-31"),
                DefaultSplits.Validation, Seg("test", "2017-08-17", "2018-12-31"), TestData.Btc, 720);
            Assert.IsTrue(report.IsValid, string.Join("; ", report.Errors));
            Assert.AreEqual(1, report.Warnings.Count);
            StringAssert.Contains(SplitValidator.BackwardTestWarning, report.Warnings[0]);
        }

        // ---- 额外的评估段

        static readonly SegmentSpec ShortTrain = Seg("train", "2017-08-17", "2021-12-31");
        static readonly SegmentSpec Bear = Seg("validation-bear", "2022-01-01", "2022-12-31");
        static readonly SegmentSpec Recovery = Seg("validation-recovery", "2023-01-01", "2024-08-31");

        [Test]
        public void ConfiguredSegmentsWithTheTwoExtraOnes_AreValidWithoutWarnings()
        {
            SplitReport report = SplitValidator.Validate(ShortTrain, DefaultSplits.Validation, DefaultSplits.Test,
                new[] { Bear, Recovery }, TestData.Btc, 720);
            Assert.IsEmpty(report.Errors, string.Join("; ", report.Errors));
            Assert.IsEmpty(report.Warnings, string.Join("; ", report.Warnings));
        }

        [Test]
        public void NoExtraSegments_GivesTheSameReportAsTheThreeSegmentCheck()
        {
            SplitReport three = SplitValidator.Validate(Seg("train", "2017-08-17", "2026-08-31"),
                DefaultSplits.Validation, Seg("test", "2025-09-01", "2026-09-30"), TestData.Btc, 720);
            SplitReport none = SplitValidator.Validate(Seg("train", "2017-08-17", "2026-08-31"),
                DefaultSplits.Validation, Seg("test", "2025-09-01", "2026-09-30"), new SegmentSpec[0], TestData.Btc, 720);
            CollectionAssert.AreEqual(three.Errors, none.Errors);
            CollectionAssert.AreEqual(three.Warnings, none.Warnings);
        }

        [Test]
        public void ExtraSegmentOverlappingTrainingValidationOrTest_IsAnError()
        {
            AssertOverlap(SplitValidator.Validate(DefaultSplits.Train, DefaultSplits.Validation, DefaultSplits.Test,
                new[] { Bear }, TestData.Btc, 720), "train", "validation-bear");
            AssertOverlap(SplitValidator.Validate(ShortTrain, DefaultSplits.Validation, DefaultSplits.Test,
                new[] { Seg("late", "2025-06-01", "2025-08-31") }, TestData.Btc, 720), "validation", "late");
            SplitReport report = SplitValidator.Validate(ShortTrain, DefaultSplits.Validation, DefaultSplits.Test,
                new[] { Seg("end", "2026-06-01", "2026-08-31") }, TestData.Btc, 720);
            AssertOverlap(report, "test", "end");
            Assert.AreEqual(1, report.Errors.Count, string.Join("; ", report.Errors));
        }

        [Test]
        public void ExtraSegmentsOverlappingEachOther_IsAnError()
        {
            SplitReport report = SplitValidator.Validate(ShortTrain, DefaultSplits.Validation, DefaultSplits.Test,
                new[] { Bear, Seg("bear-tail", "2022-12-31", "2023-03-31"), Recovery }, TestData.Btc, 720);
            AssertOverlap(report, "validation-bear", "bear-tail");
            AssertOverlap(report, "bear-tail", "validation-recovery");
            Assert.AreEqual(2, report.Errors.Count, string.Join("; ", report.Errors));
        }

        [Test]
        public void ExtraSegmentOutsideTheData_IsAnError()
        {
            SplitReport report = SplitValidator.Validate(ShortTrain, DefaultSplits.Validation, DefaultSplits.Test,
                new[] { Bear, Seg("future", "2026-09-01", "2026-12-31") }, TestData.Btc, 720);
            Assert.AreEqual(1, report.Errors.Count, string.Join("; ", report.Errors));
            StringAssert.StartsWith("out of range: future 2026-09-01..2026-12-31", report.Errors[0]);
        }
    }
}
