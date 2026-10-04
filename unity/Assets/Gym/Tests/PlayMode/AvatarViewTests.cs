using System.Collections;
using System.Collections.Generic;
using Gym.Core.Env;
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
    /// <summary>Play scene 里的头像框小人：站在哪、四种动作、提示字、被新的一步打断、重来。时钟由测试拨。</summary>
    public class AvatarViewTests
    {
        const string PlayScenePath = "Assets/Gym/Scenes/Play.unity";

        PlayController controller;
        AvatarView avatar;
        CandleChartView chart;
        PlayLanguageSwitch language;
        TradingAgent agent;
        Camera view;

        [TearDown]
        public void TearDown()
        {
            if (Academy.IsInitialized) Academy.Instance.AutomaticSteppingEnabled = true;
            if (view != null) view.ResetAspect();
        }

        IEnumerator LoadPlayScene()
        {
            yield return SceneManager.LoadSceneAsync(PlayScenePath, LoadSceneMode.Single);
            yield return null;
            controller = Object.FindFirstObjectByType<PlayController>();
            avatar = Object.FindFirstObjectByType<AvatarView>();
            chart = Object.FindFirstObjectByType<CandleChartView>();
            language = Object.FindFirstObjectByType<PlayLanguageSwitch>();
            Assert.IsNotNull(avatar, "the Play scene has an avatar");
            agent = controller.Agent;
            Assert.AreSame(agent, avatar.Agent);
            Assert.AreSame(chart, avatar.Chart);
            avatar.ManualClock = true;
            view = Camera.main;
            view.aspect = 16f / 9f; // 版面按 16:9 设计；命令行下没有窗口，相机的宽高比要自己定
        }

        bool InCamera(Rect area)
        {
            foreach (Vector2 corner in new[] { area.min, area.max, new Vector2(area.xMin, area.yMax), new Vector2(area.xMax, area.yMin) })
            {
                Vector3 p = view.WorldToViewportPoint(new Vector3(corner.x, corner.y, 0));
                if (p.x < 0 || p.x > 1 || p.y < 0 || p.y > 1) return false;
            }
            return true;
        }

        static bool NewestIsTheHighestVisible(TradingEnv env)
        {
            int last = env.CurrentIndex;
            for (int i = System.Math.Max(0, last - CandleChartView.VisibleCandles + 1); i < last; i++)
                if (env.Series[i].High > env.Series[last].High) return false;
            return true;
        }

        static void AssertSameColor(Color expected, Color actual, string what)
        {
            const float tolerance = 1f / 255; // TextMesh 按每通道 8 位存颜色
            Assert.AreEqual(expected.r, actual.r, tolerance, what + " (red)");
            Assert.AreEqual(expected.g, actual.g, tolerance, what + " (green)");
            Assert.AreEqual(expected.b, actual.b, tolerance, what + " (blue)");
        }

        void FinishTheMotion() => avatar.Advance(avatar.MotionSeconds + 0.01f);

        AvatarPose HalfWay() => Advanced(avatar.MotionSeconds / 2);

        AvatarPose Advanced(float seconds)
        {
            avatar.Advance(seconds);
            return avatar.Pose;
        }

        // ---- 位置

        [UnityTest]
        public IEnumerator AfterEveryStep_TheAvatarStandsAboveTheNewestCandleInsideTheCamera()
        {
            using var logs = new LogGuard();
            yield return LoadPlayScene();
            int newHighs = 0, hintsChecked = 0;
            for (int i = 0; i < 200; i++)
            {
                if (i % 20 == 5) controller.PressBuy(0.25f);
                else if (i % 20 == 15) controller.PressSell(0.5f);
                else controller.PressHold();
                TradingEnv env = agent.Env;
                string when = $"after step {i + 1}";
                Vector3 high = chart.LatestHighPosition(env);
                Assert.AreEqual(high.x, avatar.StandPoint.x, 1e-5, when);
                Assert.AreEqual(high.y, avatar.StandPoint.y, 1e-5, when);
                Assert.Greater(avatar.ReachArea.yMin, high.y, $"{when}: nothing of the avatar reaches down to the newest candle's high");
                Assert.GreaterOrEqual(avatar.RestFrame.xMin, chart.WorldArea.xMin - 1e-4f, $"{when}: inside the chart on the left");
                Assert.LessOrEqual(avatar.RestFrame.xMax, chart.WorldArea.xMax + 1e-4f, $"{when}: not over the price labels");
                Assert.IsTrue(InCamera(avatar.ReachArea), $"{when}: the whole frame, hopping or not, is on screen ({avatar.ReachArea})");
                if (avatar.HintText.gameObject.activeSelf)
                {
                    Bounds b = avatar.HintText.GetComponent<MeshRenderer>().bounds;
                    Assert.IsTrue(InCamera(new Rect(b.min, b.size)), $"{when}: the hint is on screen");
                    Assert.Greater(b.min.y, avatar.ReachArea.yMax - 1e-4f, $"{when}: the hint sits above the highest hop");
                    hintsChecked++;
                }
                if (NewestIsTheHighestVisible(env)) newHighs++;
            }
            Assert.Greater(newHighs, 0, "premise: some step made a new high in the visible range");
            Assert.Greater(hintsChecked, 0, "premise: some step showed a hint");
            Debug.Log($"[Gym] avatar placement: 200 steps, {newHighs} new visible highs, {hintsChecked} hints");
            logs.AssertNoErrors();
        }

        // ---- 四种动作

        [UnityTest]
        public IEnumerator BuyHoldSellAndRejected_EachHaveTheirOwnMotionHintAndColour()
        {
            using var logs = new LogGuard();
            yield return LoadPlayScene();
            Assert.AreEqual(PlayLanguage.Chinese, language.Current, "premise: Chinese first");
            var halfWay = new List<(AvatarMotion motion, AvatarPose pose)>();

            controller.PressBuy(0.25f);
            Assert.AreEqual(AvatarMotion.Buy, avatar.Motion);
            Assert.AreEqual("买入 25%", avatar.HintText.text);
            Assert.IsTrue(avatar.HintText.gameObject.activeSelf);
            AssertSameColor(chart.BuyColor, avatar.HintText.color, "buy hint");
            Assert.AreEqual(chart.BuyColor, avatar.BorderColor);
            AvatarPose pose = HalfWay();
            Assert.Greater(pose.Offset.y, 0f, "a buy hops");
            Assert.Greater(pose.Scale, 1f, "and grows");
            halfWay.Add((AvatarMotion.Buy, pose));
            FinishTheMotion();
            Assert.AreEqual(AvatarMotion.Stand, avatar.Motion);
            Assert.AreEqual(Vector2.zero, avatar.Pose.Offset, "back where it stood");
            Assert.AreEqual(1f, avatar.Pose.Scale);
            Assert.IsFalse(avatar.HintText.gameObject.activeSelf, "the hint goes with the motion");

            controller.PressHold();
            Assert.AreEqual(AvatarMotion.Hold, avatar.Motion);
            Assert.IsFalse(avatar.HintText.gameObject.activeSelf, "holding shows no hint");
            pose = HalfWay();
            Assert.Greater(pose.Offset.y, 0f, "a hold hops");
            Assert.AreEqual(1f, pose.Scale);
            Assert.AreEqual(0f, pose.TiltDegrees);
            halfWay.Add((AvatarMotion.Hold, pose));
            FinishTheMotion();
            Assert.AreEqual(Vector2.zero, avatar.Pose.Offset);

            controller.PressSell(0.5f);
            Assert.AreEqual(AvatarMotion.Sell, avatar.Motion);
            Assert.AreEqual("卖出 50%", avatar.HintText.text);
            AssertSameColor(chart.SellColor, avatar.HintText.color, "sell hint");
            language.Toggle();
            Assert.AreEqual("SELL 50%", avatar.HintText.text, "the hint follows the language");
            language.Toggle();
            Assert.AreEqual("卖出 50%", avatar.HintText.text);
            pose = HalfWay();
            Assert.Greater(pose.Offset.y, 0f, "a sell hops");
            Assert.Greater(pose.TiltDegrees, 0f, "and leans");
            halfWay.Add((AvatarMotion.Sell, pose));
            FinishTheMotion();
            Assert.AreEqual(Vector2.zero, avatar.Pose.Offset);
            Assert.AreEqual(0f, avatar.Pose.TiltDegrees);

            controller.PressSell(1f);
            Assert.IsTrue(agent.LastResult.Traded, "premise: selling the rest fills");
            controller.PressSell(0.5f);
            Assert.IsTrue(agent.LastResult.Rejected, "premise: selling with no coin is rejected");
            Assert.AreEqual(AvatarMotion.Rejected, avatar.Motion);
            Assert.AreEqual("被拒", avatar.HintText.text);
            AssertSameColor(PlayPalette.Rejected, avatar.HintText.color, "rejected hint");
            language.Toggle();
            Assert.AreEqual("REJECTED", avatar.HintText.text);
            pose = HalfWay();
            Assert.AreEqual(0f, pose.Offset.y, "a rejected order does not hop");
            Assert.Greater(Mathf.Abs(pose.Offset.x), 0f, "it shakes sideways");
            halfWay.Add((AvatarMotion.Rejected, pose));
            FinishTheMotion();
            Assert.AreEqual(Vector2.zero, avatar.Pose.Offset);

            for (int i = 0; i < halfWay.Count; i++)
            for (int j = i + 1; j < halfWay.Count; j++)
            {
                AvatarPose a = halfWay[i].pose, b = halfWay[j].pose;
                bool same = a.Offset == b.Offset && Mathf.Approximately(a.Scale, b.Scale) && Mathf.Approximately(a.TiltDegrees, b.TiltDegrees);
                Assert.IsFalse(same, $"{halfWay[i].motion} and {halfWay[j].motion} look different half way");
            }
            logs.AssertNoErrors();
        }

        // ---- 打断和重来

        [UnityTest]
        public IEnumerator ANewStepHalfWayThroughAMotion_StartsTheNewMotionFromTheBeginning()
        {
            using var logs = new LogGuard();
            yield return LoadPlayScene();
            controller.PressBuy(0.25f);
            avatar.Advance(avatar.MotionSeconds / 2);
            Assert.AreEqual(0.5f, avatar.MotionProgress, 1e-5, "premise: half way through the buy");
            controller.PressHold();
            Assert.AreEqual(AvatarMotion.Hold, avatar.Motion, "switched at once, not queued");
            Assert.AreEqual(0f, avatar.MotionProgress, "from the beginning");
            Assert.IsFalse(avatar.HintText.gameObject.activeSelf, "the buy hint is gone");
            avatar.Advance(avatar.MotionSeconds * 0.9f);
            Assert.AreEqual(AvatarMotion.Hold, avatar.Motion, "the hold runs its full length");
            logs.AssertNoErrors();
        }

        [UnityTest]
        public IEnumerator Restarting_PutsTheAvatarBackToStandingOnTheFirstCandle()
        {
            using var logs = new LogGuard();
            yield return LoadPlayScene();
            Vector2 start = avatar.StandPoint;
            Assert.IsTrue(avatar.Placed, "it stands on the first candle before any step");
            controller.PressBuy(0.25f);
            controller.PressHold();
            controller.PressSell(0.5f);
            avatar.Advance(avatar.MotionSeconds / 3);
            controller.Restart();
            Assert.AreEqual(AvatarMotion.Stand, avatar.Motion);
            Assert.AreEqual(Vector2.zero, avatar.Pose.Offset);
            Assert.IsFalse(avatar.HintText.gameObject.activeSelf);
            Assert.AreEqual(start, avatar.StandPoint, "back on the first candle");
            logs.AssertNoErrors();
        }
    }
}
