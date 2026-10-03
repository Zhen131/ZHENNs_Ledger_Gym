using System.Collections;
using Gym.Runtime.Agents;
using NUnit.Framework;
using Unity.MLAgents;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Gym.Tests.PlayMode
{
    public class TrainingSceneTests
    {
        const string TrainingScenePath = "Assets/Gym/Scenes/Training.unity";

        [TearDown]
        public void TearDown()
        {
            if (Academy.IsInitialized) Academy.Instance.AutomaticSteppingEnabled = true;
        }

        // ---- sixteen agents, one full episode each

        [UnityTest]
        public IEnumerator SixteenAgents_EachFinishAnEpisodeWithin750Steps()
        {
            using var logs = new LogGuard();
            yield return SceneManager.LoadSceneAsync(TrainingScenePath, LoadSceneMode.Single);
            Academy.Instance.AutomaticSteppingEnabled = false;
            yield return null;

            TradingAgent[] agents = Object.FindObjectsByType<TradingAgent>(FindObjectsSortMode.None);
            Assert.AreEqual(16, agents.Length);

            for (int i = 0; i < 750; i++) Academy.Instance.EnvironmentStep();

            foreach (TradingAgent agent in agents)
            {
                Assert.GreaterOrEqual(agent.FinishedEpisodes, 1, agent.name);
                Assert.GreaterOrEqual(agent.CompletedEpisodes, 1, agent.name);
                Assert.AreEqual(720, agent.LastEpisodeStats.Steps, agent.name);
                Assert.AreNotEqual(0, agent.MasterSeed, agent.name);
            }
            logs.AssertNoErrors();
            Debug.Log($"{logs.InfoCount} info/warning lines, no errors");
        }
    }
}
