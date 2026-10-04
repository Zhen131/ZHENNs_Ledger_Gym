using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gym.Editor;
using Gym.Runtime.Agents;
using Gym.Runtime.Configuration;
using Gym.Runtime.Play;
using Gym.Runtime.Watch;
using NUnit.Framework;
using Unity.MLAgents.Policies;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Gym.Tests.Editor
{
    /// <summary>提交进仓库的 Watch scene：挂了什么、Agent 怎么设、文件里有没有模型、在不在打包清单里。</summary>
    public class WatchSceneTests
    {
        const string PlayScenePath = "Assets/Gym/Scenes/Play.unity";
        const string WatchScenePath = "Assets/Gym/Scenes/Watch.unity";

        static List<T> ComponentsIn<T>(Scene scene) where T : Component =>
            scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true)).ToList();

        static void WithScene(string path, System.Action<Scene> check)
        {
            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                check(scene);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void TheGeneratorWritesTheWatchSceneWhereTheMenuOpensIt()
        {
            Assert.AreEqual(WatchScenePath, GymSceneBuilder.WatchScenePath);
            Assert.IsTrue(File.Exists(WatchScenePath));
        }

        [Test]
        public void WatchScene_HasOneAgentThatIsOffAndSetUpLikeTheEvaluationPlayerWithoutAModel()
        {
            WithScene(WatchScenePath, scene =>
            {
                List<TradingAgent> agents = ComponentsIn<TradingAgent>(scene);
                Assert.AreEqual(1, agents.Count);
                TradingAgent agent = agents[0];
                Assert.IsFalse(agent.gameObject.activeSelf, "off until a model is attached");
                Assert.AreEqual(AgentStartMode.Evaluation, agent.StartMode, "all cash, from the segment start, no randomness");

                var behavior = agent.GetComponent<BehaviorParameters>();
                var reference = new GameObject("reference").AddComponent<BehaviorParameters>();
                try
                {
                    BuildScript.UseEvaluationInference(reference);
                    Assert.AreEqual(reference.BehaviorType, behavior.BehaviorType);
                    Assert.AreEqual(reference.DeterministicInference, behavior.DeterministicInference);
                    Assert.AreEqual(reference.InferenceDevice, behavior.InferenceDevice);
                }
                finally
                {
                    Object.DestroyImmediate(reference.gameObject);
                }
                Assert.AreEqual(BehaviorType.InferenceOnly, behavior.BehaviorType);
                Assert.IsTrue(behavior.DeterministicInference);
                Assert.AreEqual(InferenceDevice.Burst, behavior.InferenceDevice);
                Assert.IsNull(new SerializedObject(behavior).FindProperty("m_Model").objectReferenceValue, "no model in the scene");
            });
        }

        [Test]
        public void WatchScene_HasTheSameViewsAsThePlaySceneWiredToOneWatchControllerAndOneCamera()
        {
            var playCounts = new Dictionary<string, int>();
            WithScene(PlayScenePath, scene =>
            {
                playCounts["language"] = ComponentsIn<PlayLanguageSwitch>(scene).Count;
                playCounts["readout"] = ComponentsIn<HudView>(scene).Count;
                playCounts["chart"] = ComponentsIn<CandleChartView>(scene).Count;
                playCounts["wallet"] = ComponentsIn<WalletView>(scene).Count;
                playCounts["avatar"] = ComponentsIn<AvatarView>(scene).Count;
                playCounts["frame rate"] = ComponentsIn<FrameRateLimiter>(scene).Count;
            });
            WithScene(WatchScenePath, scene =>
            {
                Assert.AreEqual(playCounts["language"], ComponentsIn<PlayLanguageSwitch>(scene).Count, "language");
                Assert.AreEqual(playCounts["readout"], ComponentsIn<HudView>(scene).Count, "readout");
                Assert.AreEqual(playCounts["chart"], ComponentsIn<CandleChartView>(scene).Count, "chart");
                Assert.AreEqual(playCounts["wallet"], ComponentsIn<WalletView>(scene).Count, "wallet");
                Assert.AreEqual(playCounts["avatar"], ComponentsIn<AvatarView>(scene).Count, "avatar");
                Assert.AreEqual(playCounts["frame rate"], ComponentsIn<FrameRateLimiter>(scene).Count, "frame rate");
                Assert.IsTrue(playCounts.Values.All(n => n == 1), "premise: one of each in the Play scene");
                Assert.AreEqual(30, ComponentsIn<FrameRateLimiter>(scene)[0].TargetFrameRate);

                Assert.AreEqual(0, ComponentsIn<PlayController>(scene).Count, "no keyboard play");
                List<WatchController> controllers = ComponentsIn<WatchController>(scene);
                Assert.AreEqual(1, controllers.Count);
                WatchController c = controllers[0];
                TradingAgent agent = ComponentsIn<TradingAgent>(scene)[0];
                Assert.AreSame(agent, c.Agent);
                Assert.AreSame(ComponentsIn<PlayLanguageSwitch>(scene)[0], c.Language);
                Assert.AreSame(ComponentsIn<HudView>(scene)[0], c.Hud);
                Assert.AreSame(ComponentsIn<CandleChartView>(scene)[0], c.Chart);
                Assert.AreSame(ComponentsIn<WalletView>(scene)[0], c.Wallet);
                Assert.AreSame(ComponentsIn<AvatarView>(scene)[0], c.Avatar);
                Assert.AreSame(ComponentsIn<WatchNotice>(scene).Single(), c.Notice);
                Assert.AreSame(c.Language, c.Notice.Language);
                Assert.IsNull(c.Hud.Controller, "the readout has no keyboard controller");
                Assert.AreSame(agent, c.Hud.Agent, "readout");
                Assert.AreSame(agent, c.Chart.Agent, "chart");
                Assert.AreSame(agent, c.Wallet.Agent, "wallet");
                Assert.AreSame(agent, c.Avatar.Agent, "avatar");
                Assert.AreSame(c.Chart, c.Avatar.Chart);

                List<Camera> cameras = ComponentsIn<Camera>(scene);
                Assert.AreEqual(1, cameras.Count);
                Assert.IsTrue(cameras[0].orthographic);
                Assert.AreEqual(0, ComponentsIn<TextMesh>(scene).Count, "text is made when the scene runs, not saved");
            });
        }

        [Test]
        public void TheWatchController_StartsOnTheValidationSegmentWithTheAgentsDefaultCostsAtFiveStepsPerSecond()
        {
            WithScene(WatchScenePath, scene =>
            {
                WatchController c = ComponentsIn<WatchController>(scene).Single();
                TradingAgent agent = c.Agent;
                Assert.AreEqual(EvaluationSegment.Validation, c.Segment);
                Assert.AreEqual(agent.DefaultFeeRate, c.FeeRate);
                Assert.AreEqual(agent.DefaultFixedFee, c.FixedFee);
                Assert.AreEqual(agent.DefaultSlippage, c.Slippage);
                Assert.AreEqual(0.001, c.FeeRate);
                Assert.AreEqual(0.0, c.FixedFee);
                Assert.AreEqual(0.0, c.Slippage);
                CollectionAssert.AreEqual(WatchController.DefaultStepsPerSecond, c.StepsPerSecondChoices);
                Assert.AreEqual(5f, c.StepsPerSecondChoices[c.StartSpeedIndex]);
            });
        }

        [Test]
        public void TheWatchSceneFile_ReferencesNoModelAndIsNotInTheBuildList()
        {
            string text = File.ReadAllText(WatchScenePath);
            StringAssert.DoesNotContain(".onnx", text);
            foreach (string guid in AssetDatabase.FindAssets("t:ModelAsset"))
                StringAssert.DoesNotContain(guid, text, AssetDatabase.GUIDToAssetPath(guid));
            Assert.IsFalse(EditorBuildSettings.scenes.Any(s => s.path == WatchScenePath), "the Watch scene is editor only");
        }
    }
}
