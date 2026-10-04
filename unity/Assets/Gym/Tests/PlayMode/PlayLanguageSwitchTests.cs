using System.Collections;
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
        static readonly Regex Chinese = new Regex(@"[㐀-鿿]");

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
