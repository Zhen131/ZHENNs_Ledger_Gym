using System;
using System.IO;
using Gym.Core.Market;
using Gym.Runtime.Configuration;
using NUnit.Framework;
using UnityEngine;

namespace Gym.Tests.Runtime.Configuration
{
    public class GymConfigLoaderTests
    {
        string tempDir;

        [SetUp]
        public void SetUp()
        {
            tempDir = Path.Combine(Path.GetTempPath(), "gym-e1-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }

        static GymConfig DefaultConfig() =>
            JsonUtility.FromJson<GymConfig>(File.ReadAllText(GymConfigLoader.DefaultConfigPath));

        string WriteConfig(GymConfig config)
        {
            config.dataFile = Path.Combine(GymConfigLoader.DefaultDirectory, "data", "BTCUSDT-1h.csv");
            string path = Path.Combine(tempDir, "gym-config.json");
            File.WriteAllText(path, JsonUtility.ToJson(config, true));
            return path;
        }

        // ---- E-1 configuration

        [Test]
        public void DefaultFiles_LoadWithoutErrorsOrWarnings()
        {
            GymSettings s = GymConfigLoader.Load(GymConfigLoader.DefaultConfigPath, GymConfigLoader.DefaultSymbolsPath);
            Assert.AreEqual("BTCUSDT", s.Config.symbol);
            Assert.AreEqual(10_000, s.Config.initialCash);
            Assert.AreEqual(720, s.Config.episodeLength);
            Assert.AreEqual(0.5, s.Config.randomInitialPositionShare);
            Assert.AreEqual("BTCUSDT", s.Rules.Symbol);
            Assert.AreEqual(5.0, s.Rules.MinNotional);
            Assert.AreEqual(0.00001m, s.Rules.StepSize);
            Assert.AreEqual(DefaultSplits.Train.StartDate, s.Train.StartDate);
            Assert.AreEqual(DefaultSplits.Test.EndDate, s.Test.EndDate);
            Assert.AreEqual(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), s.Series.OpenTimeUtc(s.PlayStartIndex));
            Assert.IsEmpty(s.Warnings);
            Assert.AreEqual(GymMode.Train, s.Mode);
        }

        [Test]
        public void TestSegmentOverlappingTraining_IsAnError()
        {
            GymConfig config = DefaultConfig();
            config.test = new DateRange { start = "2024-06-01", end = "2026-08-31" };
            var e = Assert.Throws<GymConfigException>(() =>
                GymConfigLoader.Load(WriteConfig(config), GymConfigLoader.DefaultSymbolsPath));
            StringAssert.Contains("overlap", e.Message);
        }

        [Test]
        public void UnknownSymbol_IsAnError()
        {
            GymConfig config = DefaultConfig();
            config.symbol = "BTCUSD";
            var e = Assert.Throws<GymConfigException>(() =>
                GymConfigLoader.Load(WriteConfig(config), GymConfigLoader.DefaultSymbolsPath));
            StringAssert.Contains("BTCUSD is not listed", e.Message);
        }

        [Test]
        public void SeveralProblems_AreReportedTogether()
        {
            GymConfig config = DefaultConfig();
            config.initialCash = 0;
            config.playStart = "2025-01-01"; // validation segment, not training
            config.validation = new DateRange { start = "2024-09-01", end = "bad" };
            var e = Assert.Throws<GymConfigException>(() =>
                GymConfigLoader.Load(WriteConfig(config), GymConfigLoader.DefaultSymbolsPath));
            Assert.GreaterOrEqual(e.Errors.Count, 2);
            StringAssert.Contains("initialCash", e.Message);
            StringAssert.Contains("validation", e.Message);
        }

        [Test]
        public void PlayStartOutsideTraining_IsAnError()
        {
            GymConfig config = DefaultConfig();
            config.playStart = "2025-01-01";
            var e = Assert.Throws<GymConfigException>(() =>
                GymConfigLoader.Load(WriteConfig(config), GymConfigLoader.DefaultSymbolsPath));
            StringAssert.Contains("playStart", e.Message);
        }

        [Test]
        public void TestBeforeTraining_IsOnlyAWarning()
        {
            GymConfig config = DefaultConfig();
            config.train = new DateRange { start = "2019-01-01", end = "2024-08-31" };
            config.test = new DateRange { start = "2017-08-17", end = "2018-12-31" };
            GymSettings s = GymConfigLoader.Load(WriteConfig(config), GymConfigLoader.DefaultSymbolsPath);
            Assert.AreEqual(1, s.Warnings.Count);
            StringAssert.Contains(SplitValidator.BackwardTestWarning, s.Warnings[0]);
        }

        [Test]
        public void ModeSegmentAndCostArguments_AreReadAndChecked()
        {
            GymSettings train = GymConfigLoader.Load(GymConfigLoader.DefaultConfigPath,
                GymConfigLoader.DefaultSymbolsPath, new[] { "x", "-gymMode", "train", "-gymSegment", "train" });
            Assert.AreEqual(GymMode.Train, train.Mode);

            GymSettings test = GymConfigLoader.Load(GymConfigLoader.DefaultConfigPath,
                GymConfigLoader.DefaultSymbolsPath, new[] { "x", "-gymMode", "eval" });
            Assert.AreEqual(GymMode.Eval, test.Mode);
            Assert.AreEqual("test", test.EvalSegment.Name);

            GymSettings validation = GymConfigLoader.Load(GymConfigLoader.DefaultConfigPath,
                GymConfigLoader.DefaultSymbolsPath, new[] { "x", "-gymMode", "eval", "-gymSegment", "validation",
                    "-gymFeeRate", "0.003", "-gymFixedFee", "1", "-gymSlippage", "0.0005" });
            Assert.AreEqual("validation", validation.EvalSegment.Name);
            Assert.AreEqual(0.003, validation.FeeRateArg);
            Assert.AreEqual(1.0, validation.FixedFeeArg);
            Assert.AreEqual(0.0005, validation.SlippageArg);
            Assert.IsNull(test.FeeRateArg);

            var e = Assert.Throws<GymConfigException>(() => GymConfigLoader.Load(GymConfigLoader.DefaultConfigPath,
                GymConfigLoader.DefaultSymbolsPath, new[] { "x", "-gymMode", "play", "-gymSegment", "test", "-gymFeeRate", "abc" }));
            Assert.AreEqual(2, e.Errors.Count, string.Join("; ", e.Errors)); // unknown mode, bad number
            var f = Assert.Throws<GymConfigException>(() => GymConfigLoader.Load(GymConfigLoader.DefaultConfigPath,
                GymConfigLoader.DefaultSymbolsPath, new[] { "x", "-gymMode", "eval", "-gymSegment", "train" }));
            StringAssert.Contains("not an evaluation segment", f.Message);

            Assert.AreEqual("cfg.json", CommandLineArgs.ValueOf(new[] { "app", "-gymConfig", "cfg.json" }, "-gymConfig"));
            Assert.IsNull(CommandLineArgs.ValueOf(new[] { "app", "-gymConfig" }, "-gymConfig"));
        }

        [TestCase("-gymFeeRate", "1.5")]
        [TestCase("-gymFeeRate", "-0.001")]
        [TestCase("-gymSlippage", "0.1")]
        [TestCase("-gymFixedFee", "-1")]
        public void CostArgumentOutsideTheCostModelRange_IsAConfigError(string name, string value)
        {
            // 05D M-1: the evaluation player hung on -gymFeeRate 1.5 because CostModel threw
            // only when the agent reset. Now the loader reports it like any other config error.
            var e = Assert.Throws<GymConfigException>(() => GymConfigLoader.Load(GymConfigLoader.DefaultConfigPath,
                GymConfigLoader.DefaultSymbolsPath, new[] { "x", "-gymMode", "eval", name, value }));
            Assert.AreEqual(1, e.Errors.Count, string.Join("; ", e.Errors));
            StringAssert.StartsWith($"{name} {value} is out of range", e.Errors[0]);
        }

        [Test]
        public void CostArgumentsAtTheEdgesOfTheRanges_AreAccepted()
        {
            GymSettings s = GymConfigLoader.Load(GymConfigLoader.DefaultConfigPath, GymConfigLoader.DefaultSymbolsPath,
                new[] { "x", "-gymMode", "eval", "-gymFeeRate", "0.999", "-gymSlippage", "0.0999", "-gymFixedFee", "0" });
            Assert.AreEqual(0.999, s.FeeRateArg);
            Assert.AreEqual(0.0999, s.SlippageArg);
            Assert.AreEqual(0.0, s.FixedFeeArg);
        }

        [Test]
        public void InitialCashAboveTheLimit_IsAConfigError()
        {
            // 08D N-2: from about 1e12 USDT on, an all-in buy could round past the cash and throw.
            GymConfig config = DefaultConfig();
            config.initialCash = 1e13;
            var e = Assert.Throws<GymConfigException>(() =>
                GymConfigLoader.Load(WriteConfig(config), GymConfigLoader.DefaultSymbolsPath));
            Assert.AreEqual(1, e.Errors.Count, string.Join("; ", e.Errors));
            StringAssert.StartsWith("initialCash must be <= 1000000000", e.Errors[0]);
            StringAssert.Contains("double", e.Errors[0]);

            config.initialCash = GymConfigLoader.MaxInitialCash;
            Assert.AreEqual(1e9, GymConfigLoader.Load(WriteConfig(config), GymConfigLoader.DefaultSymbolsPath).Config.initialCash);
            Assert.AreEqual(10_000, GymConfigLoader.Load(GymConfigLoader.DefaultConfigPath, GymConfigLoader.DefaultSymbolsPath).Config.initialCash);
        }
    }
}
