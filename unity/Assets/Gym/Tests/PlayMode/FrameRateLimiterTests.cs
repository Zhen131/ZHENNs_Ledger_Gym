using System.Collections;
using Gym.Runtime.Play;
using NUnit.Framework;
using Unity.MLAgents;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Gym.Tests.PlayMode
{
    public class FrameRateLimiterTests
    {
        const string PlayScenePath = "Assets/Gym/Scenes/Play.unity";
        // 和 limiter 要设的值不一样，这样才看得出「恢复」。
        const int FrameRateBefore = 123;
        const int VSyncBefore = 1;

        int savedFrameRate;
        int savedVSync;

        [SetUp]
        public void SetUp()
        {
            savedFrameRate = Application.targetFrameRate;
            savedVSync = QualitySettings.vSyncCount;
        }

        [TearDown]
        public void TearDown()
        {
            Application.targetFrameRate = savedFrameRate;
            QualitySettings.vSyncCount = savedVSync;
            if (Academy.IsInitialized) Academy.Instance.AutomaticSteppingEnabled = true;
        }

        static int emptyScenes;

        /// <summary>
        /// 换到一个空 scene，把别的都卸掉。前一个测试留下的 Play scene 里也有 limiter，它卸载时会把它自己记下的
        /// 旧值写回去，盖掉这里设的「之前」的值。
        /// </summary>
        static IEnumerator LeaveOnlyAnEmptyScene()
        {
            Scene empty = SceneManager.CreateScene($"Empty scene {++emptyScenes} for the frame rate test");
            SceneManager.SetActiveScene(empty);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene != empty) yield return SceneManager.UnloadSceneAsync(scene);
            }
        }

        [UnityTest]
        public IEnumerator PlayScene_LimitsTheFrameRateAndRestoresTheOldSettingsWhenDisabledOrUnloaded()
        {
            using var logs = new LogGuard();
            yield return LeaveOnlyAnEmptyScene();
            Assert.IsNull(Object.FindFirstObjectByType<FrameRateLimiter>(), "premise: no limiter is loaded");
            Application.targetFrameRate = FrameRateBefore;
            QualitySettings.vSyncCount = VSyncBefore;
            Assert.AreEqual(VSyncBefore, QualitySettings.vSyncCount, "premise: vSync can be set here");

            yield return SceneManager.LoadSceneAsync(PlayScenePath, LoadSceneMode.Single);
            yield return null;
            var limiter = Object.FindFirstObjectByType<FrameRateLimiter>();
            Assert.IsNotNull(limiter);
            Assert.AreEqual(FrameRateLimiter.DefaultFrameRate, limiter.TargetFrameRate);
            Assert.AreEqual(limiter.TargetFrameRate, Application.targetFrameRate);
            Assert.AreEqual(0, QualitySettings.vSyncCount);

            limiter.enabled = false;
            Assert.AreEqual(FrameRateBefore, Application.targetFrameRate, "after disabling");
            Assert.AreEqual(VSyncBefore, QualitySettings.vSyncCount, "after disabling");

            limiter.enabled = true;
            Assert.AreEqual(limiter.TargetFrameRate, Application.targetFrameRate, "enabled again");
            Assert.AreEqual(0, QualitySettings.vSyncCount, "enabled again");

            yield return LeaveOnlyAnEmptyScene();
            Assert.AreEqual(FrameRateBefore, Application.targetFrameRate, "after unloading");
            Assert.AreEqual(VSyncBefore, QualitySettings.vSyncCount, "after unloading");
            logs.AssertNoErrors();
        }
    }
}
