#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using Gym.Core.Env;
using Gym.Runtime.Agents;
using Gym.Runtime.Play;
using Gym.Runtime.Watch;
using NUnit.Framework;
using Unity.MLAgents;
using Unity.MLAgents.Policies;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Gym.Tests.PlayMode
{
    /// <summary>
    /// Watch scene 在旋钮模式下：读数面板「上一步」那一行画成目标仓位和换档次数，行数不变（证明画面接上了；数字对不对
    /// 由 EditMode 的 HudDialReadoutTests 证明）。Mac 上没有旋钮形状的模型，所以不挂模型：Agent 启用前设
    /// <see cref="TradingAgent.ActionModeOverride"/>、改成 HeuristicOnly（旋钮模式下启发式永远选 50% 那一档），
    /// 直接调 <see cref="WatchController.StartWatching"/>。不依赖环境变量，不会被忽略。
    /// </summary>
    public class WatchDialReadoutTests
    {
        const string WatchScenePath = "Assets/Gym/Scenes/Watch.unity";

        string folder;

        [SetUp]
        public void SetUp()
        {
            folder = Path.Combine(Path.GetTempPath(), "gym-watch-dial-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            WatchModelMemory.Location = Path.Combine(folder, WatchModelMemory.FileName);
        }

        [TearDown]
        public void TearDown()
        {
            WatchModelMemory.Location = null;
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            if (Academy.IsInitialized) Academy.Instance.AutomaticSteppingEnabled = true;
        }

        static TextMesh Text(string objectName) =>
            SceneManager.GetActiveScene().GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<TextMesh>(true))
                .Single(t => t.name == objectName);

        /// <summary>按路径加载 Watch scene；播放的时钟在 sceneLoaded 里就归测试管（和 WatchModelTests 一样）。</summary>
        static IEnumerator LoadWatchScene()
        {
            UnityAction<Scene, LoadSceneMode> takeTheClock = (scene, mode) =>
            {
                foreach (GameObject root in scene.GetRootGameObjects())
                foreach (WatchController c in root.GetComponentsInChildren<WatchController>(true))
                    c.ManualClock = true;
            };
            SceneManager.sceneLoaded += takeTheClock;
            try
            {
                yield return EditorSceneManager.LoadSceneAsyncInPlayMode(WatchScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            }
            finally
            {
                SceneManager.sceneLoaded -= takeTheClock;
            }
            for (int i = 0; i < 3; i++) yield return null;
        }

        [UnityTest]
        public IEnumerator InDialMode_TheLastActionRowShowsTheTargetPositionAndTheDialChanges()
        {
            using var logs = new LogGuard();
            yield return LoadWatchScene();
            var controller = Object.FindFirstObjectByType<WatchController>();
            var hud = Object.FindFirstObjectByType<HudView>();
            var language = Object.FindFirstObjectByType<PlayLanguageSwitch>();
            TradingAgent agent = controller.Agent;
            Assert.IsTrue(controller.ManualClock, "the test holds the clock");
            Assert.IsFalse(controller.IsWatching, "premise: no model was remembered, so the controller did not start");
            Assert.IsFalse(agent.gameObject.activeSelf, "premise: the agent is still off, so its action mode can still be set");
            Assert.IsTrue(hud.ShowsDialTarget, "the Watch scene switches the dial row on");

            agent.ActionModeOverride = ActionMode.Dial;
            agent.GetComponent<BehaviorParameters>().BehaviorType = BehaviorType.HeuristicOnly;
            controller.StartWatching();
            yield return null;
            Assert.IsTrue(controller.IsWatching);
            Assert.AreEqual(ActionMode.Dial, agent.Env.ActionMode, "the agent is in dial mode");
            Assert.AreEqual(HudReadout.Rows.Length * 2 + 3, hud.GetComponentsInChildren<TextMesh>(true).Length,
                "no row was added: a name and a value per row, and the three key lines");
            Assert.AreEqual(PlayLanguage.Chinese, language.Current);
            Assert.AreEqual("目标仓位", Text("Label LastAction").text);
            Assert.AreEqual("还没有｜换档 0 次", Text("Value LastAction").text, "before the first decision");

            WatchPlayback playback = controller.Playback;
            if (!playback.Paused) playback.TogglePause();
            for (int i = 0; i < 3; i++) Assert.IsTrue(playback.StepOnce(), $"step {i + 1}");
            int hours = agent.Env.Dial.DecisionHours;
            Assert.AreEqual(3, agent.Env.Decisions, "three decisions");
            Assert.AreEqual(3 * hours, agent.Env.StepCount, "each decision walks the decision hours");
            Assert.AreEqual(0.5, hud.Shown.DialTarget, "the heuristic picks the 50% level");
            Assert.AreEqual(0, hud.Shown.DialChanges, "and never changes it");
            Assert.AreEqual("目标仓位", Text("Label LastAction").text);
            Assert.AreEqual("50%｜换档 0 次", Text("Value LastAction").text, "the panel draws the row");

            language.Toggle();
            Assert.AreEqual("Target position", Text("Label LastAction").text);
            Assert.AreEqual("50% | changes: 0", Text("Value LastAction").text, "in English");
            Assert.AreEqual(controller.StatusLabel(PlayLanguage.English), Text("Label Fraction").text, "the watch status row is still there");
            logs.AssertNoErrors();
        }
    }
}
#endif
