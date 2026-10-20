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
        public void PoliciesArgument_DefaultsToAllThreeAndRejectsUnknownNames()
        {
            CollectionAssert.AreEquivalent(new[] { "buyhold", "cash", "random" }, EvalTools.ParsePolicies(null));
            CollectionAssert.AreEquivalent(new[] { "random" }, EvalTools.ParsePolicies("random"));
            CollectionAssert.AreEquivalent(new[] { "cash", "random" }, EvalTools.ParsePolicies(" Random , cash "));
            Assert.Throws<ArgumentException>(() => EvalTools.ParsePolicies("random,momentum"));
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
            Assert.AreEqual(4, files.Length, "log.csv and one JSON per policy"); // 买入持有、一直拿现金、随机
            foreach (string file in files)
            {
                string text = File.ReadAllText(file);
                RecordFileAssert.NoMachinePath(text, file);
                if (file.EndsWith(".json")) StringAssert.Contains("\"data_file\": \"data/BTCUSDT-1h.csv\"", text, file);
            }
        }
    }
}
