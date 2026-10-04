using System.Collections.Generic;
using System.Linq;
using Gym.Runtime.Play;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Gym.Tests.Editor
{
    /// <summary>Play scene 上那些只给人看的组件：挂在哪、挂了几个、默认值是什么。</summary>
    public class PlaySceneViewsTests
    {
        const string TrainingScenePath = "Assets/Gym/Scenes/Training.unity";
        const string PlayScenePath = "Assets/Gym/Scenes/Play.unity";
        const string EvalScenePath = "Assets/Gym/Scenes/Eval.unity";

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
        public void PlayScene_HasOneFrameRateLimiterAtThirtyFramesPerSecond()
        {
            WithScene(PlayScenePath, scene =>
            {
                List<FrameRateLimiter> limiters = ComponentsIn<FrameRateLimiter>(scene);
                Assert.AreEqual(1, limiters.Count);
                Assert.AreEqual(30, limiters[0].TargetFrameRate);
            });
        }

        [Test]
        public void PlayScene_HasOneLanguageSwitchThatStartsInChinese()
        {
            WithScene(PlayScenePath, scene =>
            {
                List<PlayLanguageSwitch> switches = ComponentsIn<PlayLanguageSwitch>(scene);
                Assert.AreEqual(1, switches.Count);
                Assert.AreEqual(PlayLanguage.Chinese, switches[0].DefaultLanguage);
            });
        }

        [Test]
        public void PlayScene_ChartFillsItsBoxInTheLayoutWithFivePriceLinesWanted()
        {
            WithScene(PlayScenePath, scene =>
            {
                CandleChartView chart = ComponentsIn<CandleChartView>(scene)[0];
                Assert.AreEqual(PlayLayout.Chart.center.x, chart.transform.position.x, 1e-5);
                Assert.AreEqual(PlayLayout.Chart.center.y, chart.transform.position.y, 1e-5);
                Assert.AreEqual(PlayLayout.Chart.width, chart.Width, 1e-5);
                Assert.AreEqual(PlayLayout.Chart.height, chart.Height, 1e-5);
                Assert.AreEqual(5, chart.DesiredPriceLines);
            });
        }

        [Test]
        public void SavedPlayScene_HoldsNoTextMeshOrFont()
        {
            // 字体是运行时按名字找的系统字体；存进 scene 就会变成丢失的引用。
            WithScene(PlayScenePath, scene => Assert.AreEqual(0, ComponentsIn<TextMesh>(scene).Count));
        }

        [Test]
        public void TrainingAndEvalScenes_HaveNoFrameRateLimiter()
        {
            WithScene(TrainingScenePath, scene => Assert.AreEqual(0, ComponentsIn<FrameRateLimiter>(scene).Count, "Training"));
            WithScene(EvalScenePath, scene => Assert.AreEqual(0, ComponentsIn<FrameRateLimiter>(scene).Count, "Eval"));
        }
    }
}
