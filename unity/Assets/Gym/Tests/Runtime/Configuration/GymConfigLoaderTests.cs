using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Gym.Core.Env;
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
            config.symbols = new SymbolDataEntry[0]; // 只看主币；晚上市的币的数据里没有这里的测试段
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

        // ---- 多币（symbols）和 -gymSymbol

        /// <summary>默认配置里的币，按配置里的顺序：主币 BTC、ETH、ADA、只考不训的 DOGE、第五只。</summary>
        static readonly string[] DefaultSymbols = { "BTCUSDT", "ETHUSDT", "ADAUSDT", "DOGEUSDT", "XRPUSDT" };

        [Test]
        public void DefaultConfig_ListsFiveSymbolsAndLoadsEachOne()
        {
            GymSettings s = LoadDefault();
            CollectionAssert.AreEqual(DefaultSymbols, s.SymbolNames);
            Assert.AreEqual("BTCUSDT", s.Current.Symbol, "without -gymSymbol the current symbol is the main one");
            Assert.AreSame(s.Current.Series, s.Series);
            Assert.AreSame(s.Current.Rules, s.Rules);
            Assert.AreEqual("data/BTCUSDT-1h.csv", s.DataFile);
            SymbolTable table = JsonUtility.FromJson<SymbolTable>(File.ReadAllText(GymConfigLoader.DefaultSymbolsPath));
            foreach (string name in DefaultSymbols)
            {
                Assert.IsTrue(s.TryGetSymbol(name, out SymbolSettings symbol), name);
                SymbolEntry entry = Array.Find(table.symbols, e => e.symbol == name);
                Assert.IsNotNull(entry, $"{name} has rules in symbols.json");
                Assert.AreEqual(name, symbol.Rules.Symbol);
                Assert.AreEqual(entry.minNotional, symbol.Rules.MinNotional, name);
                Assert.AreEqual(decimal.Parse(entry.stepSize, CultureInfo.InvariantCulture), symbol.Rules.StepSize, name);
                Assert.AreEqual($"data/{name}-1h.csv", symbol.DataFile);
                StringAssert.EndsWith($"{name}-1h.csv", symbol.DataPath);
                Assert.Greater(symbol.Series.Count, 50_000, name);
                Assert.AreEqual(name == "DOGEUSDT", symbol.EvalOnly, $"{name}: only DOGE is evalOnly");
                Assert.AreEqual(s.Train.EndDate, symbol.Train.EndDate, name);
            }
            Assert.IsFalse(s.TryGetSymbol("SOLUSDT", out _));
            Assert.IsEmpty(s.Warnings);
        }

        [TestCase("ETHUSDT")]
        [TestCase("adausdt")]
        [TestCase("DOGEUSDT")]
        [TestCase("XRPUSDT")]
        public void SymbolArgument_MakesThatSymbolTheCurrentOne(string name)
        {
            GymSettings s = LoadDefault("x", "-gymMode", "eval", "-gymSegment", "validation-bear", "-gymSymbol", name);
            string upper = name.ToUpperInvariant();
            Assert.IsTrue(s.TryGetSymbol(upper, out SymbolSettings symbol));
            Assert.AreSame(symbol, s.Current);
            Assert.AreEqual(upper, s.Rules.Symbol);
            Assert.AreSame(symbol.Series, s.Series);
            Assert.AreEqual(symbol.DataPath, s.DataPath);
            Assert.AreEqual($"data/{upper}-1h.csv", s.DataFile);
            Assert.AreEqual(symbol.Train.ToString(), s.Train.ToString());
            Assert.AreEqual("validation-bear 2022-01-01..2022-12-31", s.EvalSegment.ToString());
            Assert.AreEqual(s.PlayStart, s.Series.OpenTimeUtc(s.PlayStartIndex), "the play start index is found in this symbol's candles");
            Assert.AreEqual("BTCUSDT", s.Config.symbol, "the main symbol stays the same");
            Assert.IsEmpty(s.Warnings);
        }

        [TestCase("SOLUSDT")]
        [TestCase("BTC")]
        [TestCase("BNBUSDT")] // 在 symbols.json 里有规则，但不是配置里的币
        public void SymbolArgument_RefusesANameThatIsNotInTheConfigAndListsTheNames(string name)
        {
            var e = Assert.Throws<GymConfigException>(() =>
                LoadDefault("x", "-gymMode", "eval", "-gymSegment", "validation", "-gymSymbol", name));
            Assert.AreEqual(1, e.Errors.Count, string.Join("; ", e.Errors));
            StringAssert.StartsWith($"-gymSymbol {name} is not one of the symbols in the config", e.Errors[0]);
            StringAssert.EndsWith("use " + string.Join(", ", DefaultSymbols, 0, DefaultSymbols.Length - 1) + " or " + DefaultSymbols[DefaultSymbols.Length - 1], e.Errors[0]);
        }

        [Test]
        public void ALateListedSymbol_HasItsTrainingSegmentClippedToItsFirstCandleAndStillPassesTheSplitChecks()
        {
            GymSettings s = LoadDefault();
            SegmentSpec configured = SegmentSpec.Parse("train", DefaultConfig().train.start, DefaultConfig().train.end);
            int clipped = 0;
            foreach (SymbolSettings symbol in s.Symbols)
            {
                DateTime firstDay = symbol.Series.OpenTimeUtc(0).Date;
                if (firstDay > configured.StartDate)
                {
                    clipped++;
                    Assert.IsTrue(symbol.TrainClipped, symbol.Symbol);
                    Assert.AreEqual(firstDay, symbol.Train.StartDate, $"{symbol.Symbol}: starts on the day of its first candle");
                }
                else
                {
                    Assert.IsFalse(symbol.TrainClipped, symbol.Symbol);
                    Assert.AreEqual(configured.ToString(), symbol.Train.ToString(), symbol.Symbol);
                }
                Assert.AreEqual(configured.EndDate, symbol.Train.EndDate, symbol.Symbol);
                Assert.IsTrue(symbol.Train.IsInside(symbol.Series), symbol.Symbol);
                SplitReport report = SplitValidator.Validate(symbol.Train, s.Validation, s.Test, s.ExtraSegments,
                    symbol.Series, s.Config.episodeLength);
                Assert.IsEmpty(report.Errors, $"{symbol.Symbol}: {string.Join("; ", report.Errors)}");
            }
            Assert.GreaterOrEqual(clipped, 2, "premise: ADA and DOGE start after 2017-08-17");
            Assert.AreEqual(clipped, s.Notes.Count, "one note per clipped symbol");
            foreach (string note in s.Notes) StringAssert.Contains("is clipped to train ", note);
            Assert.IsEmpty(s.Warnings, "clipping is not a warning");
            Assert.AreEqual(configured.ToString(), s.Train.ToString(), "the main symbol is not clipped");

            GymSettings ada = LoadDefault("x", "-gymSymbol", "ADAUSDT");
            Assert.AreEqual("train 2018-04-17..2021-12-31", ada.Train.ToString(), "ADA's first candle is 2018-04-17 04:00 UTC");
            Assert.AreEqual(ada.PlayStart, ada.Series.OpenTimeUtc(ada.PlayStartIndex));
        }

        [Test]
        public void AnEvaluationSegmentOutsideASymbolsData_IsAnError()
        {
            // 配置旁边放一份只到 2023-12-31 的 ETH 数据：验证段（2024-09～2025-08）、测试段和 validation-recovery 都不在里面
            string dataDir = Path.Combine(tempDir, "data");
            Directory.CreateDirectory(dataDir);
            const long cut = 1704067200000; // 2024-01-01 00:00 UTC
            string[] lines = File.ReadAllLines(Path.Combine(GymConfigLoader.DefaultDirectory, "data", "ETHUSDT-1h.csv"));
            File.WriteAllLines(Path.Combine(dataDir, "ETHUSDT-1h.csv"),
                lines.Where((line, i) => i == 0 || long.Parse(line.Split(',')[0], CultureInfo.InvariantCulture) < cut));
            string path = WriteConfig(DefaultConfig());

            var e = Assert.Throws<GymConfigException>(() => GymConfigLoader.Load(path, GymConfigLoader.DefaultSymbolsPath));
            string all = string.Join("; ", e.Errors);
            Assert.That(e.Errors, Has.Some.StartsWith("ETHUSDT: out of range: validation 2024-09-01..2025-08-31"), all);
            Assert.That(e.Errors, Has.Some.StartsWith("ETHUSDT: out of range: test 2025-09-01..2026-08-31"), all);
            Assert.That(e.Errors, Has.All.StartsWith("ETHUSDT: "), "the other symbols' data covers every segment: " + all);

            // 当前币就是它时，同样的问题不带币名前缀
            var f = Assert.Throws<GymConfigException>(() =>
                GymConfigLoader.Load(path, GymConfigLoader.DefaultSymbolsPath, new[] { "x", "-gymSymbol", "ETHUSDT" }));
            Assert.That(f.Errors, Has.Some.StartsWith("out of range: validation 2024-09-01..2025-08-31"), string.Join("; ", f.Errors));
        }

        [TestCase("twice", "symbols[2]: ethusdt is listed twice")]
        [TestCase("no data file", "symbols[1]: ETHUSDT needs a dataFile")]
        [TestCase("no name", "symbols[1] needs a symbol")]
        [TestCase("main missing", "symbol BTCUSDT is not listed in the symbols of the config")]
        [TestCase("main elsewhere", "symbols[0]: BTCUSDT is the main symbol, so its dataFile must be the same file as dataFile")]
        public void SymbolsList_IsChecked(string problem, string expected)
        {
            GymConfig config = DefaultConfig();
            var btc = new SymbolDataEntry { symbol = "BTCUSDT", dataFile = "data/BTCUSDT-1h.csv" };
            var eth = new SymbolDataEntry { symbol = "ETHUSDT", dataFile = "data/ETHUSDT-1h.csv" };
            switch (problem)
            {
                case "twice": config.symbols = new[] { btc, eth, new SymbolDataEntry { symbol = "ethusdt", dataFile = "data/ETHUSDT-1h.csv" } }; break;
                case "no data file": config.symbols = new[] { btc, new SymbolDataEntry { symbol = "ETHUSDT", dataFile = "" } }; break;
                case "no name": config.symbols = new[] { btc, new SymbolDataEntry { symbol = "", dataFile = "data/ETHUSDT-1h.csv" } }; break;
                case "main missing": config.symbols = new[] { eth }; break;
                case "main elsewhere": config.symbols = new[] { new SymbolDataEntry { symbol = "BTCUSDT", dataFile = "data/ETHUSDT-1h.csv" }, eth }; break;
            }
            var e = Assert.Throws<GymConfigException>(() =>
                GymConfigLoader.Load(WriteConfig(config), GymConfigLoader.DefaultSymbolsPath));
            Assert.AreEqual(1, e.Errors.Count, string.Join("; ", e.Errors));
            StringAssert.StartsWith(expected, e.Errors[0]);
        }

        [Test]
        public void WithoutASymbolsList_OnlyTheMainSymbolIsKnown()
        {
            GymConfig config = DefaultConfig();
            config.symbols = new SymbolDataEntry[0];
            string path = WriteConfig(config);
            GymSettings s = GymConfigLoader.Load(path, GymConfigLoader.DefaultSymbolsPath);
            CollectionAssert.AreEqual(new[] { "BTCUSDT" }, s.SymbolNames);
            Assert.AreEqual(s.Series, s.Current.Series);
            var e = Assert.Throws<GymConfigException>(() =>
                GymConfigLoader.Load(path, GymConfigLoader.DefaultSymbolsPath, new[] { "x", "-gymSymbol", "ETHUSDT" }));
            StringAssert.EndsWith("use BTCUSDT", e.Errors[0]);
        }

        // ---- 观测集（13 批）：配置的 observationSet、命令行 -gymObs

        [Test]
        public void ObservationSet_IsBaseByDefaultAndReadFromTheConfig()
        {
            Assert.AreEqual(ObservationSet.Base, LoadDefault().ObservationSet, "the default config has no observationSet");
            Assert.IsTrue(string.IsNullOrEmpty(DefaultConfig().observationSet), "premise: gym-config.json leaves it out");
            foreach ((string name, ObservationSet expected) in new[]
                     {
                         ("long", ObservationSet.Long), ("long-ta", ObservationSet.LongTa), ("LONG-TA", ObservationSet.LongTa),
                         ("base", ObservationSet.Base), ("", ObservationSet.Base),
                     })
            {
                GymConfig config = DefaultConfig();
                config.observationSet = name;
                GymSettings s = GymConfigLoader.Load(WriteConfig(config), GymConfigLoader.DefaultSymbolsPath);
                Assert.AreEqual(expected, s.ObservationSet, $"observationSet '{name}'");
                Assert.IsEmpty(s.Warnings);
            }
        }

        [Test]
        public void ObsArgument_OverridesTheConfig()
        {
            Assert.AreEqual(ObservationSet.Long, LoadDefault("x", "-gymObs", "long").ObservationSet);
            Assert.AreEqual(ObservationSet.LongTa, LoadDefault("x", "-gymObs", "Long-TA").ObservationSet);
            Assert.AreEqual(ObservationSet.Long,
                LoadDefault("x", "-gymMode", "eval", "-gymSegment", "validation-bear", "-gymObs", "long").ObservationSet);
            GymConfig config = DefaultConfig();
            config.observationSet = "long";
            string path = WriteConfig(config);
            Assert.AreEqual(ObservationSet.Base,
                GymConfigLoader.Load(path, GymConfigLoader.DefaultSymbolsPath, new[] { "x", "-gymObs", "base" }).ObservationSet);
            Assert.AreEqual(ObservationSet.Long, GymConfigLoader.Load(path, GymConfigLoader.DefaultSymbolsPath).ObservationSet);
        }

        [TestCase("longer")]
        [TestCase("ta")]
        [TestCase("1")]
        public void UnknownObservationSetNames_AreRefused(string name)
        {
            var fromArgs = Assert.Throws<GymConfigException>(() => LoadDefault("x", "-gymObs", name));
            Assert.AreEqual(1, fromArgs.Errors.Count, fromArgs.Message);
            Assert.AreEqual($"-gymObs {name} is not an observation set; use base, long or long-ta", fromArgs.Errors[0]);

            GymConfig config = DefaultConfig();
            config.observationSet = name;
            string path = WriteConfig(config);
            var fromConfig = Assert.Throws<GymConfigException>(() => GymConfigLoader.Load(path, GymConfigLoader.DefaultSymbolsPath));
            Assert.AreEqual(1, fromConfig.Errors.Count, fromConfig.Message);
            Assert.AreEqual($"observationSet '{name}' is not an observation set; use base, long or long-ta", fromConfig.Errors[0]);
            // 命令行给了对的名字，配置里错的名字照样报
            var both = Assert.Throws<GymConfigException>(() =>
                GymConfigLoader.Load(path, GymConfigLoader.DefaultSymbolsPath, new[] { "x", "-gymObs", "long" }));
            Assert.AreEqual(fromConfig.Errors[0], both.Errors[0]);
        }

        [Test]
        public void TheDefaultConfig_WorksWithEverySet()
        {
            GymSettings s = LoadDefault();
            foreach (ObservationSet set in ObservationSets.All)
                CollectionAssert.IsEmpty(GymConfigLoader.ProblemsWithObservationSet(s, set), set.Name());
            foreach (string symbol in s.SymbolNames)
                Assert.DoesNotThrow(() => LoadDefault("x", "-gymObs", "long-ta", "-gymSymbol", symbol), symbol);
        }

        [Test]
        public void TheHistoryDependentChecks_UseTheHistoryLengthOfTheSet()
        {
            // 训练段 2017-08-17～2017-11-30 有 2540 根（下标 0～2539）：base 从 32 起够一个 720 步的 episode，
            // long 从 2160 起只剩 380 根；playStart 2017-10-01 是第 1076 根，在 base 下够靠后，在 long 下早于 2160。
            GymConfig config = DefaultConfig();
            config.symbols = new SymbolDataEntry[0]; // 只看主币；晚上市的币的数据里没有这里的训练段
            config.train = new DateRange { start = "2017-08-17", end = "2017-11-30" };
            config.playStart = "2017-10-01";
            string path = WriteConfig(config);
            GymSettings s = GymConfigLoader.Load(path, GymConfigLoader.DefaultSymbolsPath);
            Assert.AreEqual(ObservationSet.Base, s.ObservationSet);
            CollectionAssert.IsEmpty(GymConfigLoader.ProblemsWithObservationSet(s, ObservationSet.Base));

            // 训练器的 obs_set 换成 long 时，Agent 拿到的就是这几条（不改已经载入的设置）
            var problems = GymConfigLoader.ProblemsWithObservationSet(s, ObservationSet.Long);
            CollectionAssert.AreEqual(new[]
            {
                "too short: train 2017-08-17..2017-11-30 has 380 decision bars, an episode needs 721",
                "playStart 2017-10-01 must fall inside the training segment train 2017-08-17..2017-11-30",
            }, problems);
            Assert.AreEqual(ObservationSet.Base, s.ObservationSet, "the loaded settings are unchanged");

            // 配置或 -gymObs 直接选 long 时，载入就报同样的两条
            var e = Assert.Throws<GymConfigException>(() =>
                GymConfigLoader.Load(path, GymConfigLoader.DefaultSymbolsPath, new[] { "x", "-gymObs", "long" }));
            CollectionAssert.AreEqual(problems, e.Errors);
            config.observationSet = "long-ta";
            var fromConfig = Assert.Throws<GymConfigException>(() => GymConfigLoader.Load(WriteConfig(config), GymConfigLoader.DefaultSymbolsPath));
            CollectionAssert.AreEqual(problems, fromConfig.Errors);
        }

        // ---- 风险系数（-gymRiskAversion，14 批）

        [Test]
        public void RiskAversionArgument_IsReadAndIsNullWithoutIt()
        {
            Assert.AreEqual("-gymRiskAversion", GymConfigLoader.RiskAversionArg);
            Assert.IsNull(LoadDefault().RiskAversionArg, "without the argument the agent falls back to its default");
            Assert.IsNull(LoadDefault("x", "-gymMode", "eval").RiskAversionArg);
            Assert.AreEqual(0.01, LoadDefault("x", "-gymRiskAversion", "0.01").RiskAversionArg);
            Assert.AreEqual(0.005, LoadDefault("x", "-gymMode", "eval", "-gymSegment", "validation", "-gymRiskAversion", "0.005").RiskAversionArg);
            Assert.AreEqual(0.0, LoadDefault("x", "-gymRiskAversion", "0").RiskAversionArg);
            Assert.AreEqual(0.02, LoadDefault("x", "-gymRiskAversion", "2e-2").RiskAversionArg, "invariant-culture number");
            // 播放器接受任何有限的非负数；[0, 100] 只是 check_configs.py 对配置文件的检查
            Assert.AreEqual(250.0, LoadDefault("x", "-gymRiskAversion", "250").RiskAversionArg);
        }

        [TestCase("-0.001", "is out of range")]
        [TestCase("-1", "is out of range")]
        [TestCase("abc", "is not a number")]
        [TestCase("NaN", "is not a number")]
        [TestCase("Infinity", "is not a number")]
        public void NegativeOrNonNumericRiskAversion_IsAConfigError(string value, string reason)
        {
            var e = Assert.Throws<GymConfigException>(() => LoadDefault("x", "-gymRiskAversion", value));
            Assert.AreEqual(1, e.Errors.Count, string.Join("; ", e.Errors));
            StringAssert.StartsWith("-gymRiskAversion ", e.Errors[0]);
            StringAssert.Contains(value, e.Errors[0]);
            StringAssert.Contains(reason, e.Errors[0]);
            var inEval = Assert.Throws<GymConfigException>(() => LoadDefault("x", "-gymMode", "eval", "-gymSegment", "validation", "-gymRiskAversion", value));
            Assert.AreEqual(1, inEval.Errors.Count, string.Join("; ", inEval.Errors));
        }
    }
}
