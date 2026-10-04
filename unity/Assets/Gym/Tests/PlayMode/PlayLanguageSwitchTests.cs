using System.Collections;
using System.Linq;
using System.Text.RegularExpressions;
using Gym.Runtime.Play;
using NUnit.Framework;
using Unity.MLAgents;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Gym.Tests.PlayMode
{
    public class PlayLanguageSwitchTests
    {
        const string PlayScenePath = "Assets/Gym/Scenes/Play.unity";
        static readonly Regex Chinese = new Regex(@"[\u3400-\u9FFF]");

        [TearDown]
        public void TearDown()
        {
            if (Academy.IsInitialized) Academy.Instance.AutomaticSteppingEnabled = true;
        }

        static Vector2 ScreenPointOf(Vector2 world)
        {
            Vector3 p = Camera.main.WorldToScreenPoint(new Vector3(world.x, world.y, 0));
            return new Vector2(p.x, p.y);
        }

        /// <summary>
        /// 两种语言写法一样的片段：数字、空格和标点、$ + - % / : 、以及 UTC、BTC、USDT。
        /// 一行里去掉这些以后，中文画面不许剩下英文字母；英文画面不许有任何汉字或全角标点。
        /// </summary>
        static readonly Regex SameInBothLanguages = new Regex(@"UTC|BTC|USDT|[0-9\s.,:%$+\-/()]");
        static readonly Regex ChineseOrFullWidth = new Regex(@"[\u3000-\u303F\u3400-\u9FFF\uFF00-\uFFEF]");
        static readonly Regex Letters = new Regex(@"[A-Za-z]");

        /// <summary>
        /// scene 里所有看得见的字，按行拆开。画面的字是运行时建的、带「不存进 scene」标记的物体，
        /// FindObjectsByType 不返回它们，所以从各个根物体往下找。
        /// </summary>
        static string[] LinesOnScreen() =>
            SceneManager.GetActiveScene().GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<TextMesh>(false))
                .Where(t => t.GetComponent<MeshRenderer>().enabled)
                .SelectMany(t => t.text.Split('\n'))
                .Where(line => line.Trim().Length > 0)
                .ToArray();

        static void AssertEveryLineIs(PlayLanguage language, string when)
        {
            string[] lines = LinesOnScreen();
            Assert.Greater(lines.Length, 30, $"{when}: premise: the panels are on screen");
            foreach (string line in lines)
            {
                if (language == PlayLanguage.English)
                    Assert.IsFalse(ChineseOrFullWidth.IsMatch(line), $"{when}: '{line}' is not English");
                else
                    Assert.IsTrue(Chinese.IsMatch(line) || !Letters.IsMatch(SameInBothLanguages.Replace(line, "")), $"{when}: '{line}' is not Chinese");
            }
        }

        [UnityTest]
        public IEnumerator SwitchingLanguage_ChangesEveryTextOnScreenButNotTheSnapshotText()
        {
            using var logs = new LogGuard();
            yield return SceneManager.LoadSceneAsync(PlayScenePath, LoadSceneMode.Single);
            yield return null;
            var language = Object.FindFirstObjectByType<PlayLanguageSwitch>();
            var controller = Object.FindFirstObjectByType<PlayController>();
            var hud = Object.FindFirstObjectByType<HudView>();
            var wallet = Object.FindFirstObjectByType<WalletView>();
            wallet.ManualClock = true;
            controller.PressBuy(0.25f);
            controller.PressHold();
            controller.PressSell(0.5f);
            Assert.IsNotNull(wallet.ActiveFloat, "premise: a floating amount is on screen too");
            AssertEveryLineIs(PlayLanguage.Chinese, "at the start");

            Assert.IsTrue(language.Click(ScreenPointOf(language.ButtonArea.center)));
            Assert.AreEqual(PlayLanguage.English, language.Current);
            AssertEveryLineIs(PlayLanguage.English, "after clicking the button");
            Assert.AreEqual("SELL 50%: filled", hud.Shown.LastAction);

            language.Toggle(); // 按 L 走的就是这个方法
            Assert.AreEqual(PlayLanguage.Chinese, language.Current);
            AssertEveryLineIs(PlayLanguage.Chinese, "after pressing L");
            Assert.AreEqual("SELL 50%: filled", hud.Shown.LastAction, "the snapshot text stays English");

            controller.SelectFraction(3);
            controller.PressHold();
            AssertEveryLineIs(PlayLanguage.Chinese, "after another step");
            logs.AssertNoErrors();
        }

        [UnityTest]
        public IEnumerator ClickingTheButtonOrPressingL_SwitchesBetweenChineseAndEnglish()
        {
            using var logs = new LogGuard();
            yield return SceneManager.LoadSceneAsync(PlayScenePath, LoadSceneMode.Single);
            yield return null;
            var language = Object.FindFirstObjectByType<PlayLanguageSwitch>();
            Assert.IsNotNull(language);
            Assert.AreEqual(PlayLanguage.Chinese, language.Current, "Chinese by default");
            StringAssert.IsMatch(Chinese.ToString(), language.Label.text);

            Assert.IsTrue(language.Click(ScreenPointOf(language.ButtonArea.center)), "a click on the button hits it");
            Assert.AreEqual(PlayLanguage.English, language.Current);
            Assert.AreEqual("Language: English (L)", language.Label.text);

            Assert.IsFalse(language.Click(ScreenPointOf(language.ButtonArea.center + new Vector2(0, -1))), "a click below misses");
            Assert.AreEqual(PlayLanguage.English, language.Current);

            language.Toggle(); // 按 L 走的就是这个方法
            Assert.AreEqual(PlayLanguage.Chinese, language.Current);
            StringAssert.IsMatch(Chinese.ToString(), language.Label.text);
            logs.AssertNoErrors();
        }
    }
}
