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
            tempDir = Path.Combine(Path.GetTempPath(), "gym-config-test-" + Guid.NewGuid().ToString("N"));
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
            Assert.AreEqual(s.PlayStart, s.Series.OpenTimeUtc(s.PlayStartIndex)); // 试玩从配置里的起点开始
            Assert.IsEmpty(s.Warnings);
            Assert.AreEqual(GymMode.Train, s.Mode);
        }

        [Test]
        public void TestSegmentOverlappingTraining_IsAnError()
        {
            GymConfig config = DefaultConfig();
            config.test = new DateRange { start = "2021-06-01", end = "2021-12-31" }; // 落在训练段（到 2021-12-31）里
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
            config.playStart = "2025-01-01"; // 在验证段里，不在训练段里
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
            config.extraSegments = new NamedDateRange[0]; // 只看训练段和测试段；额外段会和这里的训练段重叠
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
            Assert.AreEqual(2, e.Errors.Count, string.Join("; ", e.Errors)); // 未知的模式，加一个不合法的数字
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
            // 如果只靠 CostModel，异常要等 Agent 重置时才抛出，评估 player 遇到 -gymFeeRate 1.5 就会卡住；
            // 所以 loader 在载入时就像其他配置错误一样报告它。
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

        // ---- 额外的评估段（extraSegments）

        static GymSettings LoadDefault(params string[] args) =>
            GymConfigLoader.Load(GymConfigLoader.DefaultConfigPath, GymConfigLoader.DefaultSymbolsPath, args);

        static string[] Eval(string segment) => new[] { "x", "-gymMode", "eval", "-gymSegment", segment };

        [Test]
        public void DefaultConfig_HasAShorterTrainingSegmentAndTwoMoreValidationSegments()
        {
            GymSettings s = LoadDefault();
            Assert.AreEqual("train 2017-08-17..2021-12-31", s.Train.ToString());
            Assert.AreEqual("validation 2024-09-01..2025-08-31", s.Validation.ToString());
            Assert.AreEqual("test 2025-09-01..2026-08-31", s.Test.ToString());
            Assert.AreEqual(2, s.ExtraSegments.Count);
            Assert.AreEqual("validation-bear 2022-01-01..2022-12-31", s.ExtraSegments[0].ToString());
            Assert.AreEqual("validation-recovery 2023-01-01..2024-08-31", s.ExtraSegments[1].ToString());
            CollectionAssert.AreEqual(new[] { "validation", "validation-bear", "validation-recovery", "test" }, s.EvaluationSegmentNames);
            Assert.AreEqual(new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc), s.PlayStart);
            Assert.IsTrue(s.PlayStart >= s.Train.StartDate && s.PlayStart <= s.Train.EndDate, "playStart lies in the training segment");
        }

        [TestCase("validation", "2024-09-01", "2025-08-31")]
        [TestCase("validation-bear", "2022-01-01", "2022-12-31")]
        [TestCase("Validation-Recovery", "2023-01-01", "2024-08-31")]
        [TestCase("test", "2025-09-01", "2026-08-31")]
        public void EvalMode_AcceptsEveryConfiguredEvaluationSegment(string name, string start, string end)
        {
            GymSettings s = LoadDefault(Eval(name));
            Assert.AreEqual(GymMode.Eval, s.Mode);
            Assert.AreEqual($"{name.ToLowerInvariant()} {start}..{end}", s.EvalSegment.ToString());
            Assert.IsTrue(s.TryGetEvaluationSegment(name, out SegmentSpec found));
            Assert.AreEqual(s.EvalSegment.ToString(), found.ToString());
        }

        [TestCase("validation-bull")]
        [TestCase("train")]
        [TestCase("bear")]
        public void EvalMode_RefusesAnUnknownSegmentAndListsTheNames(string name)
        {
            var e = Assert.Throws<GymConfigException>(() => LoadDefault(Eval(name)));
            Assert.AreEqual(1, e.Errors.Count, string.Join("; ", e.Errors));
            StringAssert.StartsWith($"-gymSegment {name} is not an evaluation segment", e.Errors[0]);
            StringAssert.EndsWith("use validation, validation-bear, validation-recovery or test", e.Errors[0]);
            Assert.IsFalse(LoadDefault().TryGetEvaluationSegment(name, out _));
        }

        [Test]
        public void TrainMode_RefusesAnExtraSegment()
        {
            var e = Assert.Throws<GymConfigException>(() => LoadDefault("x", "-gymMode", "train", "-gymSegment", "validation-bear"));
            Assert.AreEqual(1, e.Errors.Count, string.Join("; ", e.Errors));
            StringAssert.Contains("needs -gymMode eval", e.Errors[0]);
        }

        [TestCase("Bear", "lower-case letters, digits and hyphens")]
        [TestCase("bear_2022", "lower-case letters, digits and hyphens")]
        [TestCase("", "needs a name")]
        [TestCase("train", "is taken by the train segment")]
        [TestCase("validation", "is taken by the validation segment")]
        [TestCase("test", "is taken by the test segment")]
        [TestCase("validation-bear", "is used twice")]
        public void ExtraSegmentNames_AreChecked(string name, string expected)
        {
            GymConfig config = DefaultConfig();
            // 第二项换成要检查的名字，日期沿用 validation-recovery 的，这样只有名字这一个问题
            config.extraSegments = new[]
            {
                config.extraSegments[0],
                new NamedDateRange { name = name, start = "2023-01-01", end = "2024-08-31" },
            };
            var e = Assert.Throws<GymConfigException>(() =>
                GymConfigLoader.Load(WriteConfig(config), GymConfigLoader.DefaultSymbolsPath));
            Assert.AreEqual(1, e.Errors.Count, string.Join("; ", e.Errors));
            StringAssert.StartsWith("extraSegments[1]", e.Errors[0]);
            StringAssert.Contains(expected, e.Errors[0]);
        }

        [TestCase("2021-12-31", "2022-06-30", "overlap: train 2017-08-17..2021-12-31 and extra ")]
        [TestCase("2022-06-01", "2022-12-31", "overlap: validation-bear 2022-01-01..2022-12-31 and extra ")]
        [TestCase("2024-08-01", "2024-09-30", "overlap: validation 2024-09-01..2025-08-31 and extra ")]
        [TestCase("2026-08-01", "2026-09-30", "out of range: extra ")]
        public void ExtraSegmentsTakePartInTheSplitChecks(string start, string end, string expected)
        {
            GymConfig config = DefaultConfig();
            config.extraSegments = new[]
            {
                config.extraSegments[0],
                new NamedDateRange { name = "extra", start = start, end = end },
            };
            var e = Assert.Throws<GymConfigException>(() =>
                GymConfigLoader.Load(WriteConfig(config), GymConfigLoader.DefaultSymbolsPath));
            Assert.That(e.Errors, Has.Some.StartsWith(expected), $"expected '{expected}…'; got: {string.Join("; ", e.Errors)}");
        }

        [Test]
        public void ExtraSegmentWithABadDate_IsAnErrorButItsNameIsStillKnown()
        {
            GymConfig config = DefaultConfig();
            config.extraSegments[1].end = "2024-02-30";
            var e = Assert.Throws<GymConfigException>(() =>
                GymConfigLoader.Load(WriteConfig(config), GymConfigLoader.DefaultSymbolsPath,
                    new[] { "x", "-gymMode", "eval", "-gymSegment", "validation-recovery" }));
            Assert.AreEqual(1, e.Errors.Count, string.Join("; ", e.Errors));
            StringAssert.StartsWith("validation-recovery", e.Errors[0]);
        }

        [Test]
        public void ConfigWithoutExtraSegments_StillLoads()
        {
            GymConfig config = DefaultConfig();
            config.extraSegments = null;
            string path = WriteConfig(config);
            File.WriteAllText(path, File.ReadAllText(path).Replace("\"extraSegments\"", "\"unusedField\""));
            GymSettings s = GymConfigLoader.Load(path, GymConfigLoader.DefaultSymbolsPath);
            Assert.AreEqual(0, s.ExtraSegments.Count);
            CollectionAssert.AreEqual(new[] { "validation", "test" }, s.EvaluationSegmentNames);
        }

        [Test]
        public void InitialCashAboveTheLimit_IsAConfigError()
        {
            // 从大约 1e12 USDT 起，全仓买入取整时可能超出现金并抛异常。
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
