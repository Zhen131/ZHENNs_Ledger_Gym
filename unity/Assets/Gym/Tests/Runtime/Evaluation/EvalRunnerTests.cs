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
        public void WithoutAnEvaluationSegment_TheArgumentsAreRefused()
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
    }
}
