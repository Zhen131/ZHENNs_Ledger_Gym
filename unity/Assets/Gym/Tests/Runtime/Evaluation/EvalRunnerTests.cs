using System;
using Gym.Core.Accounting;
using Gym.Core.Env;
using Gym.Core.Evaluation;
using Gym.Runtime.Configuration;
using Gym.Runtime.Evaluation;
using NUnit.Framework;

namespace Gym.Tests.Runtime.Evaluation
{
    public class EvalRunnerTests
    {
        [Test]
        public void WithoutEvalModeOrOutputFolder_TheArgumentsAreRefused()
        {
            string none = EvalRunner.CheckArguments(new[] { "GymEval" });
            StringAssert.Contains("missing -gymMode eval, -gymSegment <segment> and -gymOut <folder>", none);
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
        public void WithoutAnEvaluationSegment_TheArgumentsAreRefused()
        {
            // 如果放行没有 -gymSegment 的命令行，player 会退回到测试段并写进 -gymOut，
            // 而在 PC 上 -gymOut 就是只追加的评估流水。
            string noSegment = EvalRunner.CheckArguments(new[] { "GymEval", "-gymMode", "eval", "-gymOut", "x" });
            Assert.IsNotNull(noSegment);
            StringAssert.Contains("missing -gymSegment <segment>.", noSegment);
            StringAssert.Contains("a segment name from gym-config.json", noSegment);
            StringAssert.Contains("Nothing was written", noSegment);
            StringAssert.Contains("Example: GymEval -batchmode -nographics -gymMode eval -gymSegment validation", noSegment);
            // CheckArguments 拿不到配置，只查给没给；段名对不对由 GymConfigLoader 在读配置时查，
            // train 不是评估段，在那里被拒绝（player 退出码 1，什么都不写）。
            string[] train = { "GymEval", "-gymMode", "eval", "-gymSegment", "train", "-gymOut", "x" };
            Assert.IsNull(EvalRunner.CheckArguments(train));
            var refused = Assert.Throws<GymConfigException>(() =>
                GymConfigLoader.Load(GymConfigLoader.DefaultConfigPath, GymConfigLoader.DefaultSymbolsPath, train));
            StringAssert.Contains("-gymSegment train is not an evaluation segment; use validation, validation-bear, validation-recovery or test",
                refused.Message);
            Assert.IsNull(EvalRunner.CheckArguments(new[] { "GymEval", "-gymMode", "eval", "-gymSegment", "validation", "-gymOut", "x" }));
            Assert.IsNull(EvalRunner.CheckArguments(new[] { "GymEval", "-gymMode", "eval", "-gymSegment", "validation-bear", "-gymOut", "x" }));
            Assert.IsNull(EvalRunner.CheckArguments(new[] { "GymEval", "-gymMode", "eval", "-gymSegment", "test", "-gymOut", "x" }));
        }

        [Test]
        public void AgentDetails_CarryNoMachinePaths()
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
                RecordFileAssert.NoMachinePath(json, model);
                StringAssert.Contains("\"model_file\": \"TradingAgent.onnx\"", json);
                StringAssert.Contains("\"data_file\": \"data/BTCUSDT-1h.csv\"", json);
            }
        }

        // ---- 观测集（13 批）：明细 JSON、流水的 notes、模型维数核对

        [TestCase(ObservationSet.Base, "base", 32)]
        [TestCase(ObservationSet.Long, "long", 2160)]
        [TestCase(ObservationSet.LongTa, "long-ta", 2160)]
        public void AgentDetails_RecordTheObservationSetAndItsHistoryLength(ObservationSet set, string name, int history)
        {
            GymSettings s = GymConfigLoader.Load(GymConfigLoader.DefaultConfigPath, GymConfigLoader.DefaultSymbolsPath,
                new[] { "x", "-gymMode", "eval", "-gymSegment", "validation-bear", "-gymObs", name });
            Assert.AreEqual(set, s.ObservationSet);
            TradingEnv env = TradingEnv.ForSegment(s.Series, s.Rules, s.EvalSegment, s.Config.initialCash, 0, 0, s.ObservationSet);
            env.ResetForEvaluation(0, new CostModel());
            env.Step(TradeAction.Hold, 0f);
            var record = new EvaluationRecord
            {
                TimestampUtc = DateTime.UtcNow, Kind = EvaluationRecord.AgentKind, Policy = "agent",
                ModelRunId = "run", ModelSha256 = "abc", Symbol = s.Rules.Symbol,
            };
            var info = new EvalBuildInfo { run_id = "run", model_sha256 = "abc", model_file = "TradingAgent.onnx", vector_observation_size = set.Size() };
            string json = JsonWriter.Serialize(EvalRunner.Details(record, info, s, env, Metrics.From(env), "InferenceOnly", true, "Burst"));
            StringAssert.Contains($"\"observation_set\": \"{name}\"", json);
            StringAssert.Contains($"\"history_length\": {history}", json);
        }

        [Test]
        public void AgentNotes_EndWithTheObservationSet()
        {
            Assert.AreEqual("deterministic=True; device=Burst; steps=8759; obs=base",
                EvalRunner.AgentNotes(true, "Burst", 8759, ObservationSet.Base));
            Assert.AreEqual("deterministic=True; device=Burst; steps=8759; obs=long",
                EvalRunner.AgentNotes(true, "Burst", 8759, ObservationSet.Long));
            Assert.AreEqual("deterministic=False; device=CPU; steps=10; obs=long-ta",
                EvalRunner.AgentNotes(false, "CPU", 10, ObservationSet.LongTa));
            StringAssert.DoesNotContain(",", EvalRunner.AgentNotes(true, "Burst", 1, ObservationSet.LongTa), "notes stay one CSV cell");
        }

        [Test]
        public void ModelInputCheck_RefusesAModelWhoseInputSizeIsNotTheSetsSize()
        {
            var first = new EvalBuildInfo { run_id = "round-1", vector_observation_size = 35 };
            Assert.IsNull(EvalRunner.CheckModelInput(first, ObservationSet.Base, out string notChecked));
            Assert.IsNull(notChecked);

            string refused = EvalRunner.CheckModelInput(first, ObservationSet.Long, out notChecked);
            Assert.IsNull(notChecked);
            Assert.AreEqual("the model takes 35 observations but the observation set long has 41; use the observation set the model " +
                            "was trained with, or a model trained on long (from build-info.json, model run round-1). Nothing was written.",
                refused);
            StringAssert.Contains("has 43", EvalRunner.CheckModelInput(first, ObservationSet.LongTa, out _));
            Assert.IsNull(EvalRunner.CheckModelInput(new EvalBuildInfo { vector_observation_size = 41 }, ObservationSet.Long, out _));
            StringAssert.Contains("takes 41 observations but the observation set base has 35",
                EvalRunner.CheckModelInput(new EvalBuildInfo { vector_observation_size = 41 }, ObservationSet.Base, out _));
        }

        [Test]
        public void ModelInputCheck_SaysWhyItDidNotCheck()
        {
            Assert.IsNull(EvalRunner.CheckModelInput(null, ObservationSet.Long, out string noFile));
            Assert.AreEqual("no build-info.json, so the model's input size is not checked against the observation set long", noFile);
            Assert.IsNull(EvalRunner.CheckModelInput(new EvalBuildInfo { run_id = "old" }, ObservationSet.Base, out string oldBuild));
            StringAssert.StartsWith("build-info.json does not record the model's input size", oldBuild);
            // 13 批之前打的包：JSON 里没有这个字段，读出来是 0
            EvalBuildInfo parsed = UnityEngine.JsonUtility.FromJson<EvalBuildInfo>("{\"run_id\": \"old\", \"model_sha256\": \"abc\"}");
            Assert.AreEqual(0, parsed.vector_observation_size);
        }
    }
}
