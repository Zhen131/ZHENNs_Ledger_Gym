using System;
using System.Collections;
using Gym.Core.Accounting;
using Gym.Core.Env;
using Gym.Runtime.Agents;
using Gym.Runtime.Configuration;
using Gym.Runtime.Play;
using NUnit.Framework;
using Unity.MLAgents;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Gym.Tests.PlayMode
{
    public class PlaySceneTests
    {
        const string PlayScenePath = "Assets/Gym/Scenes/Play.unity";

        PlayController controller;
        HudView hud;
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
            Assert.IsNotNull(controller);
            Assert.IsNotNull(hud);
            agent = controller.Agent;
            Assert.IsNotNull(agent);
            Assert.IsFalse(Academy.Instance.AutomaticSteppingEnabled, "PlayController should switch off automatic stepping");
        }

        static bool SameBits(double a, double b) => BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b);

        static void AssertHudMatches(HudSnapshot shown, TradingEnv reference, string when)
        {
            Assert.IsTrue(shown.Ready, when);
            Assert.IsTrue(SameBits(reference.Account.Cash, shown.Cash), $"{when}: cash {shown.Cash:R} vs {reference.Account.Cash:R}");
            Assert.AreEqual(reference.Account.CoinUnits, shown.CoinUnits, $"{when}: coin units");
            Assert.IsTrue(SameBits(reference.Account.FeesPaid, shown.FeesPaid), $"{when}: fees {shown.FeesPaid:R} vs {reference.Account.FeesPaid:R}");
            Assert.AreEqual(reference.Account.Trades, shown.Trades, $"{when}: trades");
            Assert.AreEqual(reference.Account.Rejected, shown.Rejected, $"{when}: rejected");
            Assert.AreEqual(reference.Series.OpenTimeUtc(reference.CurrentIndex), shown.TimeUtc, $"{when}: candle");
        }

        // ---- HUD 和单独运行的 TradingEnv 逐位相同

        [UnityTest]
        public IEnumerator BuyHoldSell_HudMatchesAStandaloneEnvBitForBit()
        {
            using var logs = new LogGuard();
            yield return LoadPlayScene();

            GymSettings s = agent.Settings;
            GymConfig c = s.Config;
            var reference = TradingEnv.ForSegment(s.Series, s.Rules, s.Train, c.initialCash, c.episodeLength, c.randomInitialPositionShare);
            reference.Reset(new CostModel(agent.DefaultFeeRate, agent.DefaultFixedFee, agent.DefaultSlippage), s.PlayStartIndex);
            Assert.AreEqual(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), s.Series.OpenTimeUtc(reference.CurrentIndex));
            Assert.AreEqual(reference.CurrentIndex, agent.Env.CurrentIndex);

            controller.PressBuy(0.25f);
            reference.Step(TradeAction.Buy, ActionCodec.FromFraction(0.25f));
            AssertHudMatches(hud.Shown, reference, "after buy 25 %");
            Assert.AreEqual(1, reference.Account.Trades, "premise: the buy fills");
            StringAssert.Contains("BUY 25%: filled", hud.Shown.LastAction);

            controller.PressHold();
            reference.Step(TradeAction.Hold, ActionCodec.FromFraction(controller.SelectedFraction));
            AssertHudMatches(hud.Shown, reference, "after hold");
            Assert.AreEqual("HOLD", hud.Shown.LastAction);

            controller.PressSell(0.5f);
            reference.Step(TradeAction.Sell, ActionCodec.FromFraction(0.5f));
            AssertHudMatches(hud.Shown, reference, "after sell 50 %");
            Assert.AreEqual(2, reference.Account.Trades, "premise: the sell fills");
            Assert.AreEqual(3, agent.Env.StepCount);

            var chart = Object.FindFirstObjectByType<CandleChartView>();
            Assert.AreEqual(CandleChartView.VisibleCandles, chart.DrawnCandles);
            Assert.AreEqual(2, chart.DrawnMarkers);
            logs.AssertNoErrors();
        }

        // ---- 没有 coin 时卖出会被拒绝，其他什么都不变

        [UnityTest]
        public IEnumerator SellWhileFlat_IsRejected()
        {
            using var logs = new LogGuard();
            yield return LoadPlayScene();
            controller.PressHold(); // 确保 episode 已经开始
            HudSnapshot before = hud.Shown;
            Assert.AreEqual(0, before.CoinUnits);

            controller.PressSell(1f);
            HudSnapshot after = hud.Shown;
            Assert.AreEqual(before.Rejected + 1, after.Rejected);
            Assert.IsTrue(SameBits(before.Cash, after.Cash));
            Assert.AreEqual(0, after.CoinUnits);
            Assert.AreEqual(0, after.Trades);
            StringAssert.Contains("REJECTED", after.LastAction);
            Assert.IsTrue(agent.LastResult.Rejected);
            logs.AssertNoErrors();
        }

        [UnityTest]
        public IEnumerator Restart_GoesBackToPlayStart()
        {
            using var logs = new LogGuard();
            yield return LoadPlayScene();
            controller.PressBuy(1f);
            controller.PressHold();
            controller.Restart();
            Assert.AreEqual(agent.Settings.PlayStartIndex, agent.Env.CurrentIndex);
            Assert.AreEqual(0, hud.Shown.CoinUnits);
            Assert.AreEqual(agent.Settings.Config.initialCash, hud.Shown.Cash);
            controller.PressHold();
            Assert.AreEqual(1, agent.Env.StepCount);
            logs.AssertNoErrors();
        }
    }
}
