using System;
using System.IO;
using Gym.Editor;
using Gym.Runtime.Configuration;
using Gym.Tests.Runtime.Evaluation;
using NUnit.Framework;

namespace Gym.Tests.Editor
{
    public class EvalToolsTests
    {
        string tempDir;

        [SetUp]
        public void SetUp()
        {
            tempDir = Path.Combine(Path.GetTempPath(), "gym-eval-" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }

        [Test]
        public void PoliciesArgument_DefaultsToAllSixAndRejectsUnknownNames()
        {
            CollectionAssert.AreEquivalent(new[] { "buyhold", "cash", "random", "momentum", "sma", "voltarget" }, EvalTools.ParsePolicies(null));
            CollectionAssert.AreEquivalent(new[] { "random" }, EvalTools.ParsePolicies("random"));
            CollectionAssert.AreEquivalent(new[] { "cash", "random" }, EvalTools.ParsePolicies(" Random , cash "));
            Assert.Throws<ArgumentException>(() => EvalTools.ParsePolicies("random,martingale"));
            Assert.Throws<ArgumentException>(() => EvalTools.ParsePolicies(","));
        }

        // ---- -gymSegment 必须给；-gym* 参数转交给 GymConfigLoader

        [Test]
        public void WithoutASegment_RunRefusesAndWritesNothing()
        {
            var e = Assert.Throws<ArgumentException>(() =>
                EvalTools.Run(new[] { "x", "-gymFeeRates", "0.001", "-gymRandomSeeds", "2", "-gymOut", tempDir }));
            StringAssert.Contains("-gymSegment is required", e.Message);
            StringAssert.Contains("Nothing was written", e.Message);
            Assert.IsFalse(Directory.Exists(tempDir), "nothing in -gymOut");
        }

        [Test]
        public void AnUnknownSegment_IsRefusedByTheLoaderAndNothingIsWritten()
        {
            var e = Assert.Throws<GymConfigException>(() => EvalTools.Run(new[]
                { "x", "-gymSegment", "validation-bull", "-gymFeeRates", "0.001", "-gymRandomSeeds", "2", "-gymOut", tempDir }));
            StringAssert.Contains("-gymSegment validation-bull is not an evaluation segment", e.Message);
            StringAssert.Contains("use validation, validation-bear, validation-recovery or test", e.Message);
            Assert.IsFalse(Directory.Exists(tempDir), "nothing in -gymOut");
        }

        [Test]
        public void LoaderArgs_ForwardEveryGymArgumentAndForceEvalMode()
        {
            string[] args =
            {
                "Unity", "-batchmode", "-projectPath", "p", "-executeMethod", "Gym.Editor.EvalTools.RunBaselines",
                "-gymMode", "train", "-gymSegment", "validation-bear", "-gymFeeRates", "0,0.001", "-gymSymbol", "ETHUSDT",
                "-gymRiskAversion", "0.01", "-gymOut", "out", "-quit",
            };
            CollectionAssert.AreEqual(new[]
            {
                "-gymMode", "eval", "-gymSegment", "validation-bear", "-gymFeeRates", "0,0.001", "-gymSymbol", "ETHUSDT",
                "-gymRiskAversion", "0.01", "-gymOut", "out",
            }, EvalTools.LoaderArgs(args));

            // 缺值的参数：转交后每个名字读到的值仍和原命令行一样
            string[] odd = { "x", "-gymFoo", "-gymSegment", "validation", "-gymFeeRate", "0.003", "-gymOut" };
            string[] forwarded = EvalTools.LoaderArgs(odd);
            foreach (string name in new[] { "-gymFoo", "-gymSegment", "-gymFeeRate", "-gymOut" })
                Assert.AreEqual(CommandLineArgs.ValueOf(odd, name), CommandLineArgs.ValueOf(forwarded, name), name);
            Assert.AreEqual("eval", CommandLineArgs.ValueOf(forwarded, "-gymMode"));
        }

        [Test]
        public void Run_HandsTheOtherGymArgumentsToTheLoader()
        {
            // -gymFeeRate（单数）是 Load 认识的成本参数：转交到了，Load 才会按它的范围拒绝 1.5
            var e = Assert.Throws<GymConfigException>(() => EvalTools.Run(new[]
                { "x", "-gymSegment", "validation", "-gymFeeRate", "1.5", "-gymRandomSeeds", "1", "-gymOut", tempDir }));
            StringAssert.Contains("-gymFeeRate 1.5 is out of range", e.Message);
            Assert.IsFalse(Directory.Exists(tempDir), "nothing in -gymOut");
        }

        [Test]
        public void BaselineFiles_CarryNoMachinePaths()
        {
            EvalTools.Run(new[] { "x", "-gymSegment", "validation", "-gymFeeRates", "0.001", "-gymRandomSeeds", "2", "-gymOut", tempDir });
            string[] files = Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories);
            // log.csv，加上每个只跑一次的 policy 各一份（买入持有、一直拿现金、三条规则），随机 policy 一份
            Assert.AreEqual(1 + 5 + 1, files.Length, "log.csv and one JSON per policy");
            foreach (string file in files)
            {
                string text = File.ReadAllText(file);
                RecordFileAssert.NoMachinePath(text, file);
                if (file.EndsWith(".json")) StringAssert.Contains("\"data_file\": \"data/BTCUSDT-1h.csv\"", text, file);
            }
        }

        [Test]
        public void RulePolicies_AreLoggedUnderTheirUnderscoredNames()
        {
            EvalTools.Run(new[]
            {
                "x", "-gymSegment", "validation-bear", "-gymPolicies", "momentum,sma,voltarget", "-gymFeeRates", "0,0.001",
                "-gymOut", tempDir,
            });
            string[] lines = File.ReadAllLines(Path.Combine(tempDir, "log.csv"));
            string[] header = lines[0].Split(',');
            int policy = Array.IndexOf(header, "policy"), segment = Array.IndexOf(header, "segment"), fee = Array.IndexOf(header, "fee_rate");
            var rows = new System.Collections.Generic.List<string>();
            for (int i = 1; i < lines.Length; i++)
            {
                string[] cells = lines[i].Split(',');
                rows.Add($"{cells[policy]} {cells[segment]} {cells[fee]}");
            }
            CollectionAssert.AreEqual(new[]
            {
                "momentum_90d validation-bear 0", "sma_cross validation-bear 0", "vol_target validation-bear 0",
                "momentum_90d validation-bear 0.001", "sma_cross validation-bear 0.001", "vol_target validation-bear 0.001",
            }, rows);
            string[] details = Directory.GetFiles(Path.Combine(tempDir, "runs"), "*.json");
            Assert.AreEqual(6, details.Length);
            foreach (string name in new[] { "momentum_90d", "sma_cross", "vol_target" })
                Assert.AreEqual(2, Array.FindAll(details, f => f.EndsWith($"-{name}-validation-bear.json") || f.Contains($"-{name}-validation-bear-")).Length, name);
        }

        // ---- -gymSymbol：经 LoaderArgs 转交，记录跟着当前币

        [Test]
        public void SymbolArgument_IsRecordedWithThatSymbolsDataFile()
        {
            EvalTools.Run(new[]
            {
                "x", "-gymSegment", "validation-bear", "-gymSymbol", "ETHUSDT", "-gymPolicies", "buyhold", "-gymFeeRates", "0.001",
                "-gymOut", tempDir,
            });
            string[] lines = File.ReadAllLines(Path.Combine(tempDir, "log.csv"));
            Assert.AreEqual(2, lines.Length, "header and one row");
            int symbol = Array.IndexOf(lines[0].Split(','), "symbol");
            Assert.AreEqual("ETHUSDT", lines[1].Split(',')[symbol]);
            string[] details = Directory.GetFiles(Path.Combine(tempDir, "runs"), "*.json");
            Assert.AreEqual(1, details.Length);
            string text = File.ReadAllText(details[0]);
            StringAssert.Contains("\"symbol\": \"ETHUSDT\"", text);
            StringAssert.Contains("\"data_file\": \"data/ETHUSDT-1h.csv\"", text);
            RecordFileAssert.NoMachinePath(text, details[0]);
        }

        [Test]
        public void ASymbolThatIsNotInTheConfig_IsRefusedByTheLoaderAndNothingIsWritten()
        {
            var e = Assert.Throws<GymConfigException>(() => EvalTools.Run(new[]
                { "x", "-gymSegment", "validation", "-gymSymbol", "BNBUSDT", "-gymFeeRates", "0.001", "-gymRandomSeeds", "1", "-gymOut", tempDir }));
            StringAssert.Contains("-gymSymbol BNBUSDT is not one of the symbols in the config", e.Message);
            Assert.IsFalse(Directory.Exists(tempDir), "nothing in -gymOut");
        }
    }
}
