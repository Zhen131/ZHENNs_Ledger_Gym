using System;
using Gym.Core.Accounting;
using Gym.Core.Env;
using Gym.Core.Evaluation;
using Gym.Runtime.Configuration;
using Gym.Runtime.Evaluation;
using NUnit.Framework;

namespace Gym.Tests.Runtime.Evaluation
{
    /// <summary>评估记录里的动作模式（24 批）：agent 行 notes 末尾那一段，和明细 JSON 的七个键。下单模式的老用例在 EvalRunnerTests。</summary>
    public class EvalRecordActionTests
    {
        static readonly double[] Levels = { 0.3, 0.5, 0.7 };

        [Test]
        public void TheActionPartOfTheNotes_IsFixedInOrder()
        {
            Assert.AreEqual("mode=orders", ActionRecord.Notes(ActionMode.Orders, null, 0));
            var dial = new DialSettings(Levels, 24, 0.05);
            Assert.AreEqual("mode=dial; band=0.05; hours=24; changes=0", ActionRecord.Notes(ActionMode.Dial, dial, 0));
            Assert.AreEqual("mode=dial; band=0.07; hours=12; changes=14", ActionRecord.Notes(ActionMode.Dial, new DialSettings(Levels, 12, 0.07), 14));
            Assert.Throws<ArgumentNullException>(() => ActionRecord.Notes(ActionMode.Dial, null, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => ActionRecord.Notes((ActionMode)5, dial, 0));
        }

        [Test]
        public void AgentNotes_InDialMode_EndWithBandHoursAndChanges()
        {
            var dial = new DialSettings(Levels, 24, 0.05);
            Assert.AreEqual("deterministic=True; device=Burst; steps=365; obs=long; mode=dial; band=0.05; hours=24; changes=7",
                EvalRunner.AgentNotes(true, "Burst", 365, ObservationSet.Long, ActionMode.Dial, dial, 7));
            Assert.AreEqual("deterministic=True; device=Burst; steps=8759; obs=base; mode=orders",
                EvalRunner.AgentNotes(true, "Burst", 8759, ObservationSet.Base, ActionMode.Orders, null, 0));
            StringAssert.DoesNotContain(",", EvalRunner.AgentNotes(true, "Burst", 1, ObservationSet.LongTa, ActionMode.Dial, dial, 3),
                "notes stay one CSV cell");
        }

        static GymSettings Settings(string segment) =>
            GymConfigLoader.Load(GymConfigLoader.DefaultConfigPath, GymConfigLoader.DefaultSymbolsPath,
                new[] { "x", "-gymMode", "eval", "-gymSegment", segment });

        static EvaluationRecord Record(GymSettings s) => new EvaluationRecord
        {
            TimestampUtc = DateTime.UtcNow, Kind = EvaluationRecord.AgentKind, Policy = "agent",
            ModelRunId = "run", ModelSha256 = "abc", Symbol = s.Rules.Symbol,
        };

        static string DetailsJson(GymSettings s, TradingEnv env) =>
            JsonWriter.Serialize(EvalRunner.Details(Record(s), new EvalBuildInfo { run_id = "run", model_file = "TradingAgent.onnx" }, s, env,
                Metrics.From(env), "InferenceOnly", true, "Burst"));

        [Test]
        public void OrderModeDetails_SayOrdersOneHourPerDecisionAndNoChanges()
        {
            GymSettings s = Settings("validation");
            TradingEnv env = TradingEnv.ForSegment(s.Series, s.Rules, s.EvalSegment, s.Config.initialCash, 0, 0);
            env.ResetForEvaluation(0, new CostModel());
            for (int i = 0; i < 5; i++) env.Step(TradeAction.Hold, 0f);
            string json = DetailsJson(s, env);
            StringAssert.Contains("\"action_mode\": \"orders\"", json);
            StringAssert.Contains("\"decision_hours\": 1", json);
            StringAssert.Contains("\"rebalance_band\": null", json);
            StringAssert.Contains("\"dial_levels\": []", json);
            StringAssert.Contains("\"dial_changes\": 0", json);
            StringAssert.Contains("\"decisions\": 5", json, "decisions equal the steps in order mode");
            StringAssert.Contains("\"last_decision_hours\": 1", json);
            // 七个键排在观测集之后、分段之前
            int obs = json.IndexOf("\"history_length\"", StringComparison.Ordinal);
            int mode = json.IndexOf("\"action_mode\"", StringComparison.Ordinal);
            int last = json.IndexOf("\"last_decision_hours\"", StringComparison.Ordinal);
            int segment = json.IndexOf("\"segment\"", StringComparison.Ordinal);
            Assert.That(obs < mode && mode < last && last < segment, json);
        }

        [Test]
        public void DialModeDetails_RecordTheBandLevelsChangesAndDecisions()
        {
            GymSettings s = Settings("validation-bear");
            var dial = new DialSettings(Levels, 24, 0.05);
            TradingEnv env = TradingEnv.ForSegment(s.Series, s.Rules, s.EvalSegment, s.Config.initialCash, 0, 0, ObservationSet.Base, dial);
            env.ResetForEvaluation(0, new CostModel(0.001, 0, 0));
            foreach (int level in new[] { 1, 1, 2, 0, 0 }) env.StepDial(level);
            Assert.AreEqual(2, env.DialChanges);
            string json = DetailsJson(s, env);
            StringAssert.Contains("\"action_mode\": \"dial\"", json);
            StringAssert.Contains("\"decision_hours\": 24", json);
            StringAssert.Contains("\"rebalance_band\": 0.05", json);
            StringAssert.Contains("\"dial_changes\": 2", json);
            StringAssert.Contains("\"decisions\": 5", json);
            StringAssert.Contains("\"last_decision_hours\": 24", json);
            StringAssert.Contains("0.3", json);
            Assert.AreEqual("mode=dial; band=0.05; hours=24; changes=2", ActionRecord.Notes(env));
        }
    }
}
