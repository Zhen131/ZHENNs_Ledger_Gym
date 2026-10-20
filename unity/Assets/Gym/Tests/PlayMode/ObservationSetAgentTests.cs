#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using Gym.Core.Env;
using Gym.Runtime.Agents;
using NUnit.Framework;
using Unity.MLAgents;
using Unity.MLAgents.Policies;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Gym.Tests.PlayMode
{
    /// <summary>
    /// 运行时改观测长度：预制体里写的是 35，Agent 在 Initialize 里（ML-Agents 建传感器之前）把它改成当前档的大小。
    /// PlayMode 测试改不了命令行、也没有训练器发 obs_set，所以用 <see cref="TradingAgent.ObservationSetOverride"/> 进 long 档。
    /// </summary>
    public class ObservationSetAgentTests
    {
        const string PrefabPath = "Assets/Gym/Prefabs/TradingAgent.prefab";

        GameObject holder;

        [TearDown]
        public void TearDown()
        {
            if (holder != null) Object.Destroy(holder);
            if (Academy.IsInitialized) Academy.Instance.AutomaticSteppingEnabled = true;
        }

        /// <summary>ML-Agents 的 VectorSensor 在观测个数和长度不一致时打的警告（截断或补零）。</summary>
        static bool IsSizeMismatchWarning(string message) =>
            message.Contains("observations") && message.Contains("vector observation size");

        [UnityTest]
        public IEnumerator TrainingModeOnTheLongSet_CollectsFortyOneObservationsWithoutASizeWarning()
        {
            using var logs = new LogGuard();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, PrefabPath);
            Assert.AreEqual(35, prefab.GetComponent<BehaviorParameters>().BrainParameters.VectorObservationSize,
                "premise: the prefab says 35");

            holder = new GameObject("Observation set test");
            holder.SetActive(false); // Agent 在启用时初始化：先设好覆盖再启用
            GameObject agentObject = Object.Instantiate(prefab, holder.transform);
            agentObject.name = "TradingAgent (long)";
            var agent = agentObject.GetComponent<TradingAgent>();
            agent.StartMode = AgentStartMode.Training;
            agent.ObservationSetOverride = ObservationSet.Long;
            var behavior = agentObject.GetComponent<BehaviorParameters>();
            Assert.AreEqual(35, behavior.BrainParameters.VectorObservationSize, "premise: the instance starts at 35 too");

            Academy.Instance.AutomaticSteppingEnabled = false;
            holder.SetActive(true);
            yield return null;
            Assert.IsNotNull(agent.Env, "the agent initialised");
            Assert.AreEqual(AgentStartMode.Training, agent.EffectiveMode);
            Assert.AreEqual(ObservationSet.Long, agent.ObservationSet);
            Assert.AreEqual(41, behavior.BrainParameters.VectorObservationSize, "the instance's size was changed before the sensor was built");

            const int steps = 20;
            for (int i = 0; i < steps; i++) Academy.Instance.EnvironmentStep();

            int collected = agent.GetObservations().Count;
            Assert.AreEqual(41, collected, "the agent collected 41 observations");
            Assert.GreaterOrEqual(agent.Env.StartIndex, 2160, "a long training episode starts after 2160 candles of history");
            Assert.Greater(agent.Env.StepCount, 0, "the agent took steps");
            float[] observation = agent.GetObservations().ToArray();
            Assert.That(observation, Has.All.InRange(-1f, 1f));
            Assert.AreEqual(35, prefab.GetComponent<BehaviorParameters>().BrainParameters.VectorObservationSize,
                "the prefab asset still says 35");

            string[] mismatches = logs.Seen.Where(l => l.type == LogType.Warning && IsSizeMismatchWarning(l.message))
                .Select(l => l.message).ToArray();
            CollectionAssert.IsEmpty(mismatches, "ML-Agents warned about the observation size");
            Debug.Log($"[Gym] observation set test: {agent.name} on {agent.ObservationSet.Name()}, " +
                      $"BehaviorParameters.VectorObservationSize {behavior.BrainParameters.VectorObservationSize}, " +
                      $"collected {collected} observations after {steps} environment steps, episode start index {agent.Env.StartIndex}, " +
                      $"size-mismatch warnings {mismatches.Length}; obs[35..40] = " +
                      string.Join(", ", observation.Skip(35).Select(v => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture))));
            logs.AssertNoErrors();
        }
    }
}
#endif
