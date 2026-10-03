using System;
using System.IO;
using Gym.Core.Accounting;
using Gym.Core.Env;
using Gym.Core.Evaluation;
using Gym.Editor;
using Gym.Runtime;
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
        public void Q03_PoliciesArgumentDefaultsToAllThreeAndRejectsUnknownNames()
        {
            CollectionAssert.AreEquivalent(new[] { "buyhold", "cash", "random" }, EvalTools.ParsePolicies(null));
            CollectionAssert.AreEquivalent(new[] { "random" }, EvalTools.ParsePolicies("random"));
            CollectionAssert.AreEquivalent(new[] { "cash", "random" }, EvalTools.ParsePolicies(" Random , cash "));
            Assert.Throws<ArgumentException>(() => EvalTools.ParsePolicies("random,momentum"));
            Assert.Throws<ArgumentException>(() => EvalTools.ParsePolicies(","));
        }

        [Test]
        public void S1_TheEvaluationPlayerNeedsEvalModeAndAnOutputFolder()
        {
            string none = EvalRunner.CheckArguments(new[] { "GymEval" });
            StringAssert.Contains("missing -gymMode eval, -gymSegment validation|test and -gymOut <folder>", none);
            StringAssert.Contains("Nothing was written", none);
            StringAssert.Contains("missing -gymOut <folder>.",
                EvalRunner.CheckArguments(new[] { "GymEval", "-gymMode", "eval", "-gymSegment", "validation" }));
            StringAssert.Contains("missing -gymMode eval.",
                EvalRunner.CheckArguments(new[] { "GymEval", "-gymSegment", "validation", "-gymOut", "evaluations/smoke" }));
            StringAssert.Contains("missing -gymMode eval.",
                EvalRunner.CheckArguments(new[] { "GymEval", "-gymMode", "train", "-gymSegment", "validation", "-gymOut", "evaluations/smoke" }));
            StringAssert.Contains("missing -gymOut <folder>.",
                EvalRunner.CheckArguments(new[] { "GymEval", "-gymMode", "eval", "-gymSegment", "validation", "-gymOut" }));
            Assert.IsNull(EvalRunner.CheckArguments(new[] { "GymEval", "-gymMode", "EVAL", "-gymSegment", "Test", "-gymOut", "evaluations/smoke" }));
        }

        [Test]
        public void N1_TheEvaluationPlayerNeedsTheSegmentToo()
        {
            // 08D N-1: without -gymSegment the player fell back to the test segment and wrote to -gymOut,
            // which on the PC is the append-only log.
            string noSegment = EvalRunner.CheckArguments(new[] { "GymEval", "-gymMode", "eval", "-gymOut", "x" });
            Assert.IsNotNull(noSegment);
            StringAssert.Contains("missing -gymSegment validation|test.", noSegment);
            StringAssert.Contains("Nothing was written", noSegment);
            StringAssert.Contains("Example: GymEval -batchmode -nographics -gymMode eval -gymSegment validation", noSegment);
            StringAssert.Contains("missing -gymSegment validation|test.",
                EvalRunner.CheckArguments(new[] { "GymEval", "-gymMode", "eval", "-gymSegment", "train", "-gymOut", "x" }));
            Assert.IsNull(EvalRunner.CheckArguments(new[] { "GymEval", "-gymMode", "eval", "-gymSegment", "validation", "-gymOut", "x" }));
            Assert.IsNull(EvalRunner.CheckArguments(new[] { "GymEval", "-gymMode", "eval", "-gymSegment", "test", "-gymOut", "x" }));
        }

        // ---- 05D M-2: committed evaluation files must not carry a machine's paths

        static void AssertNoMachinePath(string text, string what)
        {
            StringAssert.DoesNotContain("/Users/", text, what);
            StringAssert.DoesNotContain("/home/", text, what);
            StringAssert.DoesNotContain(":\\", text, what);   // C:\ as raw text
            StringAssert.DoesNotContain("\\\\", text, what);  // any backslash once JSON-escaped, \\server too
            StringAssert.DoesNotContain(Path.GetFullPath("."), text, what);
        }

        [Test]
        public void M2_PathsForRecordsDropMachineFolders()
        {
            Assert.AreEqual("data/BTCUSDT-1h.csv", GymConfigLoader.PathForRecords("data/BTCUSDT-1h.csv"));
            Assert.AreEqual("data/BTCUSDT-1h.csv", GymConfigLoader.PathForRecords(@"data\BTCUSDT-1h.csv"));
            Assert.AreEqual("BTCUSDT-1h.csv", GymConfigLoader.PathForRecords("/Users/someone/Gym/data/BTCUSDT-1h.csv"));
            Assert.AreEqual("BTCUSDT-1h.csv", GymConfigLoader.PathForRecords(@"C:\Users\someone\Gym\data\BTCUSDT-1h.csv"));
            Assert.AreEqual("BTCUSDT-1h.csv", GymConfigLoader.PathForRecords(@"\\server\share\BTCUSDT-1h.csv"));
            Assert.AreEqual("BTCUSDT-1h.csv", GymConfigLoader.PathForRecords("~/Gym/BTCUSDT-1h.csv"));
            Assert.AreEqual("", GymConfigLoader.PathForRecords(null));
            Assert.AreEqual("TradingAgent.onnx", GymConfigLoader.FileNameOnly(@"C:\Users\someone\results\run\TradingAgent.onnx"));
            Assert.AreEqual("TradingAgent.onnx", GymConfigLoader.FileNameOnly("/Users/someone/results/run/TradingAgent.onnx"));
            Assert.AreEqual("", GymConfigLoader.FileNameOnly(null));
        }

        [Test]
        public void M2_BaselineFilesCarryNoMachinePaths()
        {
            EvalTools.Run(new[] { "x", "-gymSegment", "validation", "-gymFeeRates", "0.001", "-gymRandomSeeds", "2", "-gymOut", tempDir });
            string[] files = Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories);
            Assert.AreEqual(4, files.Length, "log.csv and one JSON per policy"); // buy-and-hold, cash, random
            foreach (string file in files)
            {
                string text = File.ReadAllText(file);
                AssertNoMachinePath(text, file);
                if (file.EndsWith(".json")) StringAssert.Contains("\"data_file\": \"data/BTCUSDT-1h.csv\"", text, file);
            }
        }

        [Test]
        public void M2_AgentDetailsCarryNoMachinePaths()
        {
            GymSettings s = GymConfigLoader.Load(GymConfigLoader.DefaultConfigPath, GymConfigLoader.DefaultSymbolsPath,
                new[] { "x", "-gymMode", "eval", "-gymSegment", "validation" });
            TradingEnv env = TradingEnv.ForSegment(s.Series, s.Rules, s.EvalSegment, s.Config.initialCash, 0, 0);
            env.ResetForEvaluation(0, new CostModel());
            env.Step(TradeAction.Hold, 0f);
            var record = new EvaluationRecord
            {
                TimestampUtc = DateTime.UtcNow, Kind = EvaluationRecord.AgentKind, Policy = "agent",
                ModelRunId = "run", ModelSha256 = "abc", Symbol = s.Rules.Symbol,
            };
            foreach (string model in new[]
                     {
                         "/Users/someone/Gym/results/run/TradingAgent.onnx",
                         @"C:\Users\someone\Gym\results\run\TradingAgent.onnx",
                         @"\\server\share\run\TradingAgent.onnx",
                     })
            {
                var info = new EvalBuildInfo { run_id = "run", model_sha256 = "abc", model_file = model, built_at_utc = "2026-10-02T00:00:00Z" };
                string json = JsonWriter.Serialize(EvalRunner.Details(record, info, s, env, Metrics.From(env), "InferenceOnly", true, "Burst"));
                AssertNoMachinePath(json, model);
                StringAssert.Contains("\"model_file\": \"TradingAgent.onnx\"", json);
                StringAssert.Contains("\"data_file\": \"data/BTCUSDT-1h.csv\"", json);
            }
        }
    }
}
