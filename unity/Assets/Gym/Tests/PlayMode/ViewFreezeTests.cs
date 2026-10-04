using System.Collections;
using System.Linq;
using Gym.Runtime.Agents;
using Gym.Runtime.Play;
using NUnit.Framework;
using Unity.MLAgents;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Gym.Tests.PlayMode
{
    /// <summary>
    /// 画面组件新加的两样东西，在 Play scene 上测：「定格」时不理会紧接着的「一局开始」；读数面板换上别人给的
    /// 按键和状态行，不给时照原样画。
    /// </summary>
    public class ViewFreezeTests
    {
        const string PlayScenePath = "Assets/Gym/Scenes/Play.unity";

        PlayController controller;
        HudView hud;
        CandleChartView chart;
        WalletView wallet;
        AvatarView avatar;
        TradingAgent agent;

        [TearDown]
        public void TearDown()
        {
            if (Academy.IsInitialized) Academy.Instance.AutomaticSteppingEnabled = true;
        }

        IEnumerator LoadPlayScene()
        {
            yield return SceneManager.LoadSceneAsync(PlayScenePath, LoadSceneMode.Single);
            yield return null;
            controller = Object.FindFirstObjectByType<PlayController>();
            hud = Object.FindFirstObjectByType<HudView>();
            chart = Object.FindFirstObjectByType<CandleChartView>();
            wallet = Object.FindFirstObjectByType<WalletView>();
            avatar = Object.FindFirstObjectByType<AvatarView>();
            agent = controller.Agent;
            wallet.ManualClock = true;
            avatar.ManualClock = true;
        }

        void SetFrozen(bool frozen)
        {
            hud.Frozen = frozen;
            chart.Frozen = frozen;
            wallet.Frozen = frozen;
            avatar.Frozen = frozen;
        }

        static TextMesh Text(string objectName) =>
            SceneManager.GetActiveScene().GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<TextMesh>(true))
                .Single(t => t.name == objectName);

        [UnityTest]
        public IEnumerator FrozenViews_KeepTheirNumbersThroughAnEpisodeStartUntilUnfrozen()
        {
            using var logs = new LogGuard();
            yield return LoadPlayScene();
            Assert.IsFalse(hud.Frozen || chart.Frozen || wallet.Frozen || avatar.Frozen, "off by default");
            int startFirstVisible = chart.FirstVisibleIndex;
            Vector2 startStand = avatar.StandPoint;
            controller.PressBuy(0.25f);
            for (int i = 0; i < 5; i++) controller.PressHold();
            int step = hud.Shown.Step;
            double cash = hud.Shown.Cash;
            int firstVisible = chart.FirstVisibleIndex;
            double pocket = wallet.ShownCash;
            Vector2 stand = avatar.StandPoint;
            string cashText = Text("Value Cash").text;

            SetFrozen(true);
            controller.Restart(); // 环境回到开局，发出「一局开始」
            Assert.AreEqual(agent.Settings.PlayStartIndex, agent.Env.CurrentIndex, "premise: the environment did start over");
            Assert.AreEqual(step, hud.Shown.Step, "the readout keeps its numbers");
            Assert.AreEqual(cash, hud.Shown.Cash);
            Assert.AreEqual(cashText, Text("Value Cash").text);
            Assert.AreEqual(firstVisible, chart.FirstVisibleIndex, "the chart stays put");
            Assert.AreEqual(pocket, wallet.ShownCash, "the pocket keeps its cash");
            Assert.AreEqual(stand, avatar.StandPoint, "the avatar stays on the old candle");
            Object.FindFirstObjectByType<PlayLanguageSwitch>().Toggle();
            Assert.AreEqual(step, hud.Shown.Step, "switching the language does not unfreeze the numbers");

            SetFrozen(false);
            Assert.AreEqual(step, hud.Shown.Step, "unfreezing alone redraws nothing");
            controller.Restart();
            Assert.AreEqual(0, hud.Shown.Step, "the next episode start reaches the readout again");
            Assert.AreEqual(agent.Settings.Config.initialCash, hud.Shown.Cash);
            Assert.AreEqual(startFirstVisible, chart.FirstVisibleIndex);
            Assert.AreEqual(agent.Settings.Config.initialCash, wallet.ShownCash);
            Assert.AreEqual(startStand, avatar.StandPoint);
            logs.AssertNoErrors();
        }

        /// <summary>Agent 的「一局开始」现在挂着几个监听（事件背后的委托是 Agent 的私有字段）。</summary>
        int EpisodeStartListeners()
        {
            var field = typeof(TradingAgent).GetField(nameof(TradingAgent.EpisodeStarted),
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var handlers = (System.Delegate)field.GetValue(agent);
            return handlers == null ? 0 : handlers.GetInvocationList().Length;
        }

        [UnityTest]
        public IEnumerator FreezingTwiceThenUnfreezingTwice_LeavesTheSameListenersAsBefore()
        {
            using var logs = new LogGuard();
            yield return LoadPlayScene();
            int before = EpisodeStartListeners();
            Assert.GreaterOrEqual(before, 4, "premise: the readout, chart, pocket and avatar listen");
            SetFrozen(true);
            Assert.AreEqual(before - 4, EpisodeStartListeners(), "each frozen view stops listening");
            SetFrozen(true);
            Assert.AreEqual(before - 4, EpisodeStartListeners());
            SetFrozen(false);
            SetFrozen(false);
            Assert.AreEqual(before, EpisodeStartListeners(), "and listens exactly once again");
            logs.AssertNoErrors();
        }

        sealed class FixedControls : IReadoutControls
        {
            public string StatusLabel(PlayLanguage language) => "status label";
            public string StatusValue(PlayLanguage language) => "status value";
            public string KeysFirstLine(PlayLanguage language) => "first keys";
            public string KeysSecondLine(PlayLanguage language) => "second keys";
        }

        [UnityTest]
        public IEnumerator ReadoutControls_ReplaceTheKeysAndTheFractionRowOnlyWhileSet()
        {
            using var logs = new LogGuard();
            yield return LoadPlayScene();
            controller.PressHold();
            Assert.IsNull(hud.Controls, "the Play scene has none");
            string[] names = { "Label Fraction", "Value Fraction", "Keys label", "Keys trade", "Keys other" };
            string[] original = names.Select(n => Text(n).text).ToArray();
            CollectionAssert.AreEqual(new[]
            {
                PlayText.Get(PlayTextKey.Fraction, PlayLanguage.Chinese), "25 %", PlayText.Get(PlayTextKey.Keys, PlayLanguage.Chinese),
                PlayText.Get(PlayTextKey.KeysTrade, PlayLanguage.Chinese), PlayText.Get(PlayTextKey.KeysOther, PlayLanguage.Chinese),
            }, original, "the keyboard play panel as before");

            hud.Controls = new FixedControls();
            CollectionAssert.AreEqual(new[] { "status label", "status value", original[2], "first keys", "second keys" },
                names.Select(n => Text(n).text).ToArray());
            controller.PressHold();
            Assert.AreEqual("status value", Text("Value Fraction").text, "it stays after the next step");

            hud.Controls = null;
            CollectionAssert.AreEqual(original, names.Select(n => Text(n).text).ToArray(), "back to the keyboard play text word for word");
            logs.AssertNoErrors();
        }
    }
}
