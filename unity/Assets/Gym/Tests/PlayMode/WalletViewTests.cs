using System;
using System.Collections;
using Gym.Core.Accounting;
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
    public class WalletViewTests
    {
        const string PlayScenePath = "Assets/Gym/Scenes/Play.unity";

        PlayController controller;
        WalletView wallet;
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
            wallet = Object.FindFirstObjectByType<WalletView>();
            Assert.IsNotNull(wallet);
            agent = controller.Agent;
            wallet.ManualClock = true;
        }

        static bool SameBits(double a, double b) => BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b);

        /// <summary>TextMesh 把颜色存成每通道 8 位，读回来的值和设进去的差不到 1/255。</summary>
        static void AssertSameColor(Color expected, Color actual, string what)
        {
            const float tolerance = 1f / 255;
            Assert.AreEqual(expected.r, actual.r, tolerance, what + " (red)");
            Assert.AreEqual(expected.g, actual.g, tolerance, what + " (green)");
            Assert.AreEqual(expected.b, actual.b, tolerance, what + " (blue)");
        }

        void AssertWalletMatchesTheAccount(string when)
        {
            Account a = agent.Env.Account;
            double unrealized = a.UnrealizedPnl(agent.Env.CurrentClose);
            Assert.IsTrue(SameBits(a.Cash, wallet.ShownCash), $"{when}: cash {wallet.ShownCash:R} vs {a.Cash:R}");
            Assert.IsTrue(SameBits(a.RealizedPnl, wallet.ShownRealizedPnl), $"{when}: realized {wallet.ShownRealizedPnl:R} vs {a.RealizedPnl:R}");
            Assert.IsTrue(SameBits(unrealized, wallet.ShownUnrealizedPnl), $"{when}: unrealized {wallet.ShownUnrealizedPnl:R} vs {unrealized:R}");
            Assert.AreEqual(MoneyText.Amount(a.Cash), wallet.CashText.text, when);
            Assert.AreEqual(MoneyText.Signed(a.RealizedPnl), wallet.RealizedText.text, when);
            Assert.AreEqual(MoneyText.Signed(unrealized), wallet.UnrealizedText.text, when);
            AssertSameColor(MoneyText.IsGainOrZero(unrealized) ? PlayPalette.Gain : PlayPalette.Loss, wallet.UnrealizedText.color, $"{when}: unrealized colour");
            AssertSameColor(MoneyText.IsGainOrZero(a.RealizedPnl) ? PlayPalette.Gain : PlayPalette.Loss, wallet.RealizedText.color, $"{when}: realized colour");
        }

        [UnityTest]
        public IEnumerator AfterEachStep_ThePocketShowsTheAccountsCashAndProfitAndLoss()
        {
            using var logs = new LogGuard();
            yield return LoadPlayScene();
            AssertWalletMatchesTheAccount("at the start");
            controller.PressBuy(0.5f);
            AssertWalletMatchesTheAccount("after buy 50 %");
            for (int i = 0; i < 6; i++)
            {
                controller.PressHold();
                AssertWalletMatchesTheAccount($"after hold {i + 1}");
            }
            controller.PressSell(0.5f);
            AssertWalletMatchesTheAccount("after sell 50 %");
            Assert.AreNotEqual(0, agent.Env.Account.RealizedPnl, "premise: the sell realized something");
            controller.PressSell(1f);
            AssertWalletMatchesTheAccount("after selling everything");
            Assert.AreEqual(0, wallet.ShownUnrealizedPnl);
            logs.AssertNoErrors();
        }

        [UnityTest]
        public IEnumerator BuyAndSell_FloatTheCashChangeWhileHoldAndRejectedDoNot()
        {
            using var logs = new LogGuard();
            yield return LoadPlayScene();
            float half = wallet.FloatSeconds / 2;

            controller.PressHold();
            Assert.IsNull(wallet.ActiveFloat, "hold: no float");
            controller.PressSell(1f);
            Assert.IsTrue(agent.LastResult.Rejected, "premise: selling while flat is rejected");
            Assert.IsNull(wallet.ActiveFloat, "rejected: no float");

            // 买入：红色 "-$…"，金额是现金少了多少；往上飘、变透明，到时间消失。
            double cashBefore = agent.Env.Account.Cash;
            controller.PressBuy(0.25f);
            double spent = cashBefore - agent.Env.Account.Cash;
            Assert.Greater(spent, 0, "premise: the buy filled");
            FloatingAmount buy = wallet.ActiveFloat;
            Assert.IsNotNull(buy);
            Assert.AreEqual(MoneyText.Spent(spent), buy.Text);
            StringAssert.StartsWith("-$", buy.Text);
            Assert.AreEqual(buy.Text, wallet.FloatText.text);
            Assert.IsTrue(wallet.FloatText.gameObject.activeSelf);
            AssertSameColor(PlayPalette.Loss, wallet.FloatText.color, "red");
            float y0 = buy.Y, a0 = wallet.FloatText.color.a;
            wallet.Advance(half);
            Assert.Greater(buy.Y, y0, "moves up");
            Assert.Less(wallet.FloatText.color.a, a0, "fades");
            Assert.AreEqual(buy.Y, wallet.FloatText.transform.position.y, 1e-5, "the text is where the animation says");
            wallet.Advance(half + 0.01f);
            Assert.IsNull(wallet.ActiveFloat, "gone after its time");
            Assert.IsFalse(wallet.FloatText.gameObject.activeSelf);

            // 卖出：绿色 "+$…"，金额是现金多了多少；往下掉，到口袋处消失。
            cashBefore = agent.Env.Account.Cash;
            controller.PressSell(1f);
            double received = agent.Env.Account.Cash - cashBefore;
            Assert.Greater(received, 0, "premise: the sell filled");
            FloatingAmount sell = wallet.ActiveFloat;
            Assert.IsNotNull(sell);
            Assert.AreEqual(MoneyText.Received(received), sell.Text);
            StringAssert.StartsWith("+$", sell.Text);
            AssertSameColor(PlayPalette.Gain, wallet.FloatText.color, "green");
            y0 = sell.Y;
            wallet.Advance(half);
            Assert.Less(sell.Y, y0, "moves down");
            Assert.Greater(sell.Y, wallet.PocketTop, "still above the pocket half way");
            wallet.Advance(half - 0.001f);
            Assert.AreEqual(wallet.PocketTop, sell.Y, 0.01, "reaches the pocket");
            wallet.Advance(0.01f);
            Assert.IsNull(wallet.ActiveFloat, "gone at the pocket");

            controller.PressHold();
            Assert.IsNull(wallet.ActiveFloat, "hold after the sell: no float");
            logs.AssertNoErrors();
        }

        [UnityTest]
        public IEnumerator ANewTradeWhileAnAmountIsStillFloating_ReplacesIt()
        {
            using var logs = new LogGuard();
            yield return LoadPlayScene();
            controller.PressBuy(0.25f);
            FloatingAmount first = wallet.ActiveFloat;
            wallet.Advance(0.1f);
            controller.PressBuy(0.25f);
            Assert.IsNotNull(wallet.ActiveFloat);
            Assert.AreNotSame(first, wallet.ActiveFloat);
            Assert.AreEqual(0f, wallet.ActiveFloat.Age);
            logs.AssertNoErrors();
        }
    }
}
