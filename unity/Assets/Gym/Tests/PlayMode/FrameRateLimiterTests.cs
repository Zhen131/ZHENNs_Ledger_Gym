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

        [UnityTest]
        public IEnumerator PlayScene_LimitsTheFrameRateAndRestoresTheOldSettingsWhenDisabledOrUnloaded()
        {
            using var logs = new LogGuard();
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

            Scene play = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(SceneManager.CreateScene("Empty scene after Play"));
            yield return SceneManager.UnloadSceneAsync(play);
            Assert.AreEqual(FrameRateBefore, Application.targetFrameRate, "after unloading");
            Assert.AreEqual(VSyncBefore, QualitySettings.vSyncCount, "after unloading");
            logs.AssertNoErrors();
        }
    }
}
