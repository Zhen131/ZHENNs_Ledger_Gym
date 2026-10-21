#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Gym.Core.Env;
using Gym.Runtime.Agents;
using Gym.Runtime.Configuration;
using NUnit.Framework;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Gym.Tests.PlayMode
{
    /// <summary>
    /// 旋钮模式的 Agent：预制体里是下单模式的「1 个连续 + 3 档」，Agent 在 OnEnable 里、ML-Agents 建执行器之前把本实例的
    /// 动作形状改成「0 个连续 + 一个 3 档分支」。主判据看执行器层面：<see cref="Agent.GetStoredActionBuffers"/> 的长度，
    /// 以及 OnActionReceived 之后（Stepped 事件里）读到的同样两个长度。PlayMode 测试改不了命令行、也没有训练器送 action_mode，
    /// 所以用 <see cref="TradingAgent.ActionModeOverride"/>；命令行这一路用共用设置里解析好的 <see cref="GymSettings.ActionMode"/>
    /// 临时代替（测试完还原）。
    /// </summary>
    public class DialAgentTests
    {
        const string PrefabPath = "Assets/Gym/Prefabs/TradingAgent.prefab";

        GameObject holder;
        GymSettings settings;
        ActionMode savedMode;

        [TearDown]
        public void TearDown()
        {
            if (settings != null) settings.ActionMode = savedMode;
            if (holder != null) Object.Destroy(holder);
            if (Academy.IsInitialized) Academy.Instance.AutomaticSteppingEnabled = true;
        }

        TradingAgent Make(string name, AgentStartMode mode, ActionMode? overrideMode)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, PrefabPath);
            holder = new GameObject("Dial agent test");
            holder.SetActive(false); // Agent 在启用时初始化：先设好覆盖再启用
            GameObject agentObject = Object.Instantiate(prefab, holder.transform);
            agentObject.name = name;
            var agent = agentObject.GetComponent<TradingAgent>();
            agent.StartMode = mode;
            agent.ActionModeOverride = overrideMode;
            return agent;
        }

        static void AssertPrefabIsOrderMode(string what)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            ActionSpec spec = prefab.GetComponent<BehaviorParameters>().BrainParameters.ActionSpec;
            Assert.AreEqual(1, spec.NumContinuousActions, what);
            CollectionAssert.AreEqual(new[] { 3 }, spec.BranchSizes, what);
        }

        [UnityTest]
        public IEnumerator DialOverride_GivesTheActuatorsNoContinuousActionAndOneBranch_AndAnEpisodeOfDecisions()
        {
            using var logs = new LogGuard();
            AssertPrefabIsOrderMode("premise: the prefab is in order mode");
            TradingAgent agent = Make("TradingAgent (dial)", AgentStartMode.Training, ActionMode.Dial);
            var behavior = agent.GetComponent<BehaviorParameters>();
            Assert.AreEqual(1, behavior.BrainParameters.ActionSpec.NumContinuousActions, "premise: the instance starts in order mode too");

            var seen = new List<(int continuous, int discrete, int level, int hours)>();
            agent.Stepped += a =>
            {
                ActionBuffers stored = a.GetStoredActionBuffers();
                seen.Add((stored.ContinuousActions.Length, stored.DiscreteActions.Length, a.LastDialResult.Level, a.LastDialResult.Hours));
            };

            Academy.Instance.AutomaticSteppingEnabled = false;
            holder.SetActive(true);
            yield return null;
            Assert.IsNotNull(agent.Env, "the agent initialised");
            Assert.AreEqual(ActionMode.Dial, agent.ActionMode);
            Assert.AreEqual(ActionMode.Dial, agent.Env.ActionMode);
            Assert.AreEqual(0, behavior.BrainParameters.ActionSpec.NumContinuousActions, "the instance's spec was changed");
            CollectionAssert.AreEqual(new[] { 3 }, behavior.BrainParameters.ActionSpec.BranchSizes);

            // 执行器层面的形状：LazyInitialize 末尾按执行器的形状分配的缓冲区
            ActionBuffers buffers = agent.GetStoredActionBuffers();
            Assert.AreEqual(0, buffers.ContinuousActions.Length, "stored continuous actions");
            Assert.AreEqual(1, buffers.DiscreteActions.Length, "stored discrete branches");

            CollectionAssert.AreEqual(new[] { 0.3, 0.5, 0.7 }, agent.Env.Dial.Levels.ToArray());
            Assert.AreEqual(24, agent.Env.Dial.DecisionHours);
            Assert.AreEqual(0.05, agent.Env.Dial.Band);
            Assert.AreEqual(2160, agent.Env.EpisodeLength, "dial episodes are dialEpisodeLength candles long");

            const int steps = 95; // 一局 2160 根 = 90 次决策，再多走几步进下一局
            for (int i = 0; i < steps; i++) Academy.Instance.EnvironmentStep();

            Assert.GreaterOrEqual(seen.Count, 91, "OnActionReceived ran once per environment step");
            Assert.IsTrue(seen.All(s => s.continuous == 0 && s.discrete == 1),
                "after every OnActionReceived the stored buffers have 0 continuous actions and 1 discrete branch");
            Assert.IsTrue(seen.All(s => s.level == 1), "the heuristic picks the 0.5 level");
            Assert.IsTrue(seen.Take(90).All(s => s.hours == 24), "each of the first episode's 90 decisions walks 24 candles");

            Assert.GreaterOrEqual(agent.FinishedEpisodes, 1, "an episode finished");
            EpisodeStats stats = agent.LastEpisodeStats;
            Assert.AreEqual(90, stats.Decisions, "Trading/Decisions");
            Assert.Greater(stats.Decisions, 0);
            Assert.AreEqual(0, stats.DialChanges, "the heuristic never changes level");
            Assert.AreEqual(2160, stats.Steps, "the episode walked 2160 candles");
            AssertPrefabIsOrderMode("the prefab asset is still in order mode");

            Debug.Log($"[Gym] dial agent test: {agent.name}, BrainParameters.ActionSpec continuous " +
                      $"{behavior.BrainParameters.ActionSpec.NumContinuousActions}, branches [{string.Join(", ", behavior.BrainParameters.ActionSpec.BranchSizes)}]; " +
                      $"stored action buffers continuous {buffers.ContinuousActions.Length}, discrete {buffers.DiscreteActions.Length}; " +
                      $"{seen.Count} OnActionReceived calls; first episode: {stats.Decisions} decisions, {stats.DialChanges} level changes, " +
                      $"{stats.Steps} candles, return {stats.Return.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}");
            logs.AssertNoErrors();
        }

        [UnityTest]
        public IEnumerator TheSettingsActionMode_StandsForTheCommandLine_AndReachesTheAgent()
        {
            using var logs = new LogGuard();
            settings = GymConfigLoader.LoadForRuntime();
            savedMode = settings.ActionMode;
            Assert.AreEqual(ActionMode.Orders, savedMode, "premise: the test process has no -gymActionMode and the config says orders");
            settings.ActionMode = ActionMode.Dial; // 代替 -gymActionMode dial
            TradingAgent agent = Make("TradingAgent (dial from the settings)", AgentStartMode.Evaluation, null);
            Academy.Instance.AutomaticSteppingEnabled = false;
            holder.SetActive(true);
            yield return null;
            Assert.AreEqual(ActionMode.Dial, agent.ActionMode);
            Assert.AreEqual(0, agent.GetStoredActionBuffers().ContinuousActions.Length);
            Assert.AreEqual(1, agent.GetStoredActionBuffers().DiscreteActions.Length);
            Academy.Instance.EnvironmentStep();
            Assert.AreEqual(24, agent.Env.StepCount, "one decision walked 24 candles");
            Assert.AreEqual(1, agent.Env.Decisions);
            Assert.Greater(agent.Env.CurrentEquity, 0);
            logs.AssertNoErrors();
        }

        [UnityTest]
        public IEnumerator OrderModeByDefault_KeepsThePrefabShape()
        {
            using var logs = new LogGuard();
            TradingAgent agent = Make("TradingAgent (orders)", AgentStartMode.Training, null);
            Academy.Instance.AutomaticSteppingEnabled = false;
            holder.SetActive(true);
            yield return null;
            Assert.AreEqual(ActionMode.Orders, agent.ActionMode);
            Assert.AreEqual(ActionMode.Orders, agent.Env.ActionMode);
            Assert.AreEqual(1, agent.GetStoredActionBuffers().ContinuousActions.Length);
            Assert.AreEqual(1, agent.GetStoredActionBuffers().DiscreteActions.Length);
            Assert.AreEqual(720, agent.Env.EpisodeLength);
            for (int i = 0; i < 5; i++) Academy.Instance.EnvironmentStep();
            Assert.AreEqual(5, agent.Env.StepCount, "one candle per decision");
            Assert.AreEqual(5, agent.Env.Decisions);
            Assert.AreEqual(0, agent.Env.DialChanges);
            logs.AssertNoErrors();
        }

        [UnityTest]
        public IEnumerator ThePlaySceneInDialMode_FailsWithAClearError()
        {
            TradingAgent agent = Make("TradingAgent (play, dial)", AgentStartMode.PlayFromConfig, ActionMode.Dial);
            // LogAssert 用正则时丢掉 RegexOptions，所以跨行用 (?s)
            LogAssert.Expect(LogType.Error, new Regex(@"(?s)\[Gym\].*the play scene does not support the dial action mode"));
            LogAssert.Expect(LogType.Exception, new Regex(@"(?s)GymConfigException.*the play scene does not support the dial action mode"));
            Academy.Instance.AutomaticSteppingEnabled = false;
            holder.SetActive(true);
            yield return null;
            Assert.IsNull(agent.Env, "the agent did not start: no silent run in order mode");
            Assert.AreEqual(1, agent.GetComponent<BehaviorParameters>().BrainParameters.ActionSpec.NumContinuousActions,
                "the shape was not changed either");
        }
    }
}
#endif
