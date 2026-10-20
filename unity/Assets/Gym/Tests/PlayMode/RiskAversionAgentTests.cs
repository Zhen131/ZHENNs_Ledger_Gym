#if UNITY_EDITOR
using System;
using System.Collections;
using Gym.Core.Env;
using Gym.Runtime.Agents;
using Gym.Runtime.Configuration;
using NUnit.Framework;
using Unity.MLAgents;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Gym.Tests.PlayMode
{
    /// <summary>
    /// 风险系数从 Agent 到环境：Inspector 默认值和命令行这两条路（训练器的环境参数 risk_aversion 在 Mac 上送不进来，
    /// 只能在 PC 冒烟时看 Trading/RiskAversion 曲线）。PlayMode 测试改不了进程的命令行，所以把共用设置里
    /// 解析好的 <see cref="GymSettings.RiskAversionArg"/> 临时设成 0.01，代替 -gymRiskAversion 0.01，测试完还原。
    /// </summary>
    public class RiskAversionAgentTests
    {
        const string PrefabPath = "Assets/Gym/Prefabs/TradingAgent.prefab";

        GameObject first;
        GameObject second;
        GymSettings settings;
        double? savedArg;

        [TearDown]
        public void TearDown()
        {
            if (settings != null) settings.RiskAversionArg = savedArg;
            if (first != null) Object.Destroy(first);
            if (second != null) Object.Destroy(second);
            if (Academy.IsInitialized) Academy.Instance.AutomaticSteppingEnabled = true;
        }

        /// <summary>在没启用的 <paramref name="holder"/> 下放一个预制体实例；<paramref name="inspectorDefault"/> 为 null 时保留预制体的值。</summary>
        static TradingAgent Make(GameObject holder, string name, AgentStartMode mode, double? inspectorDefault)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, PrefabPath);
            GameObject agentObject = Object.Instantiate(prefab, holder.transform);
            agentObject.name = name;
            var agent = agentObject.GetComponent<TradingAgent>();
            agent.StartMode = mode;
            if (inspectorDefault.HasValue)
            {
                var serialized = new SerializedObject(agent);
                serialized.FindProperty("defaultRiskAversion").doubleValue = inspectorDefault.Value;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            return agent;
        }

        /// <summary>按资金曲线重算本局的罚款：每一步 λ × min(r, 0)²，r 是不加罚款的原得分。</summary>
        static void AssertPenaltiesMatchTheEquityCurve(TradingEnv env)
        {
            double expected = 0;
            int steps = 0;
            for (int i = 1; i < env.EquityCurve.Count; i++)
            {
                double r = RewardFunction.Compute(env.EquityCurve[i - 1], env.EquityCurve[i], out _);
                double penalty = env.RiskAversion * (Math.Min(r, 0) * Math.Min(r, 0));
                expected += penalty;
                if (penalty > 0) steps++;
            }
            Assert.AreEqual(expected, env.RiskPenaltySum, 1e-12, "penalty sum");
            Assert.AreEqual(steps, env.RiskPenaltySteps, "penalised steps");
        }

        [UnityTest]
        public IEnumerator TheInspectorDefault_ThenTheCommandLineValue_ReachTheEnvironmentAtEveryReset()
        {
            using var logs = new LogGuard();
            settings = GymConfigLoader.LoadForRuntime();
            savedArg = settings.RiskAversionArg;
            Assert.IsNull(savedArg, "premise: the test process has no -gymRiskAversion");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.AreEqual(0.0, prefab.GetComponent<TradingAgent>().DefaultRiskAversion, "premise: the prefab says 0");

            // 1) 没有训练器、没有命令行：用 Inspector 默认值
            first = new GameObject("Risk aversion test, defaults");
            first.SetActive(false); // Agent 在启用时初始化：先设好默认值再启用
            TradingAgent prefabDefault = Make(first, "TradingAgent (play, prefab default)", AgentStartMode.PlayFromConfig, null);
            TradingAgent play = Make(first, "TradingAgent (play, default 0.02)", AgentStartMode.PlayFromConfig, 0.02);
            TradingAgent training = Make(first, "TradingAgent (training, default 0.02)", AgentStartMode.Training, 0.02);
            Academy.Instance.AutomaticSteppingEnabled = false;
            first.SetActive(true);
            yield return null;

            Assert.AreEqual(0.0, prefabDefault.Env.RiskAversion, "Reset (Play) with the prefab's 0");
            Assert.AreEqual(0.02, play.Env.RiskAversion, "Reset (Play) with the Inspector default");
            const int steps = 30;
            for (int i = 0; i < steps; i++) Academy.Instance.EnvironmentStep();
            Assert.Greater(training.Env.StepCount, 0, "the training agent took steps");
            Assert.AreEqual(0.02, training.Env.RiskAversion, "ResetForTraining with the Inspector default");
            AssertPenaltiesMatchTheEquityCurve(training.Env);
            double trainingPenalty = training.Env.RiskPenaltySum;
            int trainingStart = training.Env.StartIndex;
            AssertPenaltiesMatchTheEquityCurve(play.Env);
            Assert.AreEqual(0.0, prefabDefault.Env.RiskPenaltySum);

            // 2) 命令行的值盖过 Inspector 默认值：新建的 Agent 和已有 Agent 的下一局都用它
            settings.RiskAversionArg = 0.01;
            second = new GameObject("Risk aversion test, command line");
            second.SetActive(false);
            TradingAgent fromArg = Make(second, "TradingAgent (play, default 0.02, argument 0.01)", AgentStartMode.PlayFromConfig, 0.02);
            second.SetActive(true);
            yield return null;
            Assert.AreEqual(0.01, fromArg.Env.RiskAversion, "the command line beats the Inspector default");

            training.EndEpisode(); // 下一局开始时重新读
            Academy.Instance.EnvironmentStep();
            Assert.AreEqual(0.01, training.Env.RiskAversion, "read again at the start of the next episode");
            Assert.AreEqual(0.02, play.Env.RiskAversion, "an episode that is running keeps its value");

            Debug.Log($"[Gym] risk aversion test: training agent from index {trainingStart}, {steps} steps at lambda 0.02, " +
                      $"penalty sum {trainingPenalty.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}; " +
                      $"Play agents 0 (prefab), 0.02 (Inspector), 0.01 (argument over Inspector); training agent's next episode 0.01");
            logs.AssertNoErrors();
        }
    }
}
#endif
