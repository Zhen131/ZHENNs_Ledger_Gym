#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Text.RegularExpressions;
using Gym.Runtime.Agents;
using Gym.Runtime.Play;
using Gym.Runtime.Watch;
using NUnit.Framework;
using Unity.MLAgents;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Gym.Tests.PlayMode
{
    /// <summary>
    /// Watch scene 没给模型时：画面说去点哪个菜单，日志里没有错误，Agent 一直不激活。记录用临时位置造。
    /// Watch scene 不在打包清单里，所以按路径加载（只有 editor 里能这样，整个文件包在 UNITY_EDITOR 里）。
    /// </summary>
    public class WatchSceneTests
    {
        const string WatchScenePath = "Assets/Gym/Scenes/Watch.unity";

        string folder;
        WatchController controller;
        WatchNotice notice;
        PlayLanguageSwitch language;
        TradingAgent agent;

        [SetUp]
        public void SetUp()
        {
            folder = Path.Combine(Path.GetTempPath(), "gym-watch-scene-tests-" + Guid.NewGuid().ToString("N"));
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

        IEnumerator LoadWatchScene()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(WatchScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            for (int i = 0; i < 5; i++) yield return null;
            controller = Object.FindFirstObjectByType<WatchController>();
            notice = Object.FindFirstObjectByType<WatchNotice>();
            language = Object.FindFirstObjectByType<PlayLanguageSwitch>();
            agent = controller.Agent;
        }

        void AssertTheNoModelHintIsShown(string when)
        {
            Assert.IsFalse(controller.IsWatching, when);
            Assert.IsFalse(agent.gameObject.activeSelf, $"{when}: the agent stays off");
            Assert.IsNull(agent.Env, $"{when}: and never initialised");
            Assert.AreEqual(WatchNoticeKind.NoModel, notice.Shown, when);
            Assert.IsTrue(notice.ShownText.gameObject.activeInHierarchy, when);
            Assert.AreEqual(PlayText.Get(PlayTextKey.WatchNoModel, language.Current), notice.ShownText.text, $"{when}: in the current language");
        }

        [UnityTest]
        public IEnumerator WithoutAModel_TheScreenSaysWhichMenuToUseAndTheAgentStaysOff()
        {
            using var logs = new LogGuard();
            Assert.IsFalse(File.Exists(WatchModelMemory.Location), "premise: no model was ever chosen");
            yield return LoadWatchScene();
            Assert.AreEqual(PlayLanguage.Chinese, language.Current);
            AssertTheNoModelHintIsShown("at the start");
            StringAssert.Contains("Gym > Watch a Model...", notice.ShownText.text);

            language.Toggle();
            AssertTheNoModelHintIsShown("in English");
            StringAssert.StartsWith("No model chosen yet.", notice.ShownText.text);
            for (int i = 0; i < 10; i++) yield return null;
            AssertTheNoModelHintIsShown("some frames later");
            logs.AssertNoErrors();
        }

        [UnityTest]
        public IEnumerator ARememberedModelThatIsGone_ShowsTheSameHintWithAWarningAndNoError()
        {
            using var logs = new LogGuard();
            WatchModelMemory.Write(new WatchModelRecord { model_asset = "Assets/Gym/Models/Imported/Watch-gone.onnx", source_file = "/nowhere/TradingAgent.onnx" });
            yield return LoadWatchScene();
            AssertTheNoModelHintIsShown("after the warning");
            logs.AssertNoErrors();
            Assert.IsTrue(Regex.IsMatch(string.Join("\n", logs.Seen), "Watch-gone.onnx is not in the project any more"), "the warning names the missing model");
        }
    }
}
#endif
