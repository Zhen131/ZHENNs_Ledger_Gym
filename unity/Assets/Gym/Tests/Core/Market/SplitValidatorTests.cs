using Gym.Core.Env;
using Gym.Core.Market;
using NUnit.Framework;

namespace Gym.Tests.Core.Market
{
    public class SplitValidatorTests
    {
        // ---- split validation

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

        // Every way two segments can overlap is refused, not only train and validation sharing a day.

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
        public void TestBeforeTraining_IsOnlyAWarning()
        {
            SplitReport report = SplitValidator.Validate(Seg("train", "2019-01-01", "2024-08-31"),
                DefaultSplits.Validation, Seg("test", "2017-08-17", "2018-12-31"), TestData.Btc, 720);
            Assert.IsTrue(report.IsValid, string.Join("; ", report.Errors));
            Assert.AreEqual(1, report.Warnings.Count);
            StringAssert.Contains(SplitValidator.BackwardTestWarning, report.Warnings[0]);
        }
    }
}
