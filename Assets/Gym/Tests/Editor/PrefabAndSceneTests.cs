using System.Collections.Generic;
using System.Linq;
using Gym.Runtime;
using NUnit.Framework;
using Unity.MLAgents;
using Unity.MLAgents.Policies;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Gym.Tests.Editor
{
    public class PrefabAndSceneTests
    {
        const string PrefabPath = "Assets/Gym/Prefabs/TradingAgent.prefab";
        const string TrainingScenePath = "Assets/Gym/Scenes/Training.unity";

        static List<T> ComponentsIn<T>(Scene scene) where T : Component =>
            scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true)).ToList();

        // ---- E-2 prefab

        [Test]
        public void E02_PrefabHasTheContractedBehaviour()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, PrefabPath);

            var behavior = prefab.GetComponent<BehaviorParameters>();
            Assert.AreEqual("TradingAgent", behavior.BehaviorName);
            Assert.AreEqual(BehaviorType.Default, behavior.BehaviorType);
            Assert.AreEqual(35, behavior.BrainParameters.VectorObservationSize);
            Assert.AreEqual(1, behavior.BrainParameters.NumStackedVectorObservations);
            Assert.AreEqual(1, behavior.BrainParameters.ActionSpec.NumContinuousActions);
            CollectionAssert.AreEqual(new[] { 3 }, behavior.BrainParameters.ActionSpec.BranchSizes);

            var requester = prefab.GetComponent<DecisionRequester>();
            Assert.IsNotNull(requester);
            Assert.AreEqual(1, requester.DecisionPeriod);
            Assert.IsFalse(requester.TakeActionsBetweenDecisions);

            var agent = prefab.GetComponent<TradingAgent>();
            Assert.AreEqual(0, agent.MaxStep);
            Assert.AreEqual(AgentStartMode.Training, agent.StartMode);
            Assert.AreEqual(0.001, agent.DefaultFeeRate);
            Assert.AreEqual(0.0, agent.DefaultFixedFee);
            Assert.AreEqual(0.0, agent.DefaultSlippage);
        }

        // ---- E-3 scenes

        [Test]
        public void E03_TrainingSceneHasSixteenDefaultAgents()
        {
            Scene scene = EditorSceneManager.OpenScene(TrainingScenePath, OpenSceneMode.Additive);
            try
            {
                List<TradingAgent> agents = ComponentsIn<TradingAgent>(scene);
                Assert.AreEqual(16, agents.Count);
                CollectionAssert.AreEquivalent(Enumerable.Range(0, 16), agents.Select(a => a.AgentIndex));
                foreach (TradingAgent agent in agents)
                {
                    Assert.AreEqual(BehaviorType.Default, agent.GetComponent<BehaviorParameters>().BehaviorType);
                    Assert.AreEqual(AgentStartMode.Training, agent.StartMode);
                    Assert.AreEqual(PrefabPath, PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(agent.gameObject));
                }
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void E03_TrainingSceneIsFirstInBuildSettings()
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            Assert.GreaterOrEqual(scenes.Length, 1);
            Assert.AreEqual(TrainingScenePath, scenes[0].path);
            Assert.IsTrue(scenes[0].enabled);
        }
    }
}
