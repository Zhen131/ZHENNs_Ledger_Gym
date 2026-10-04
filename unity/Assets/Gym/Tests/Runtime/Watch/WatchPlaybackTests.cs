using System;
using System.Collections.Generic;
using System.Linq;
using Gym.Runtime.Watch;
using NUnit.Framework;
using UnityEngine;

namespace Gym.Tests.Runtime.Watch
{
    /// <summary>观战的播放逻辑，用替身代替模型和 Agent，时钟由测试拨（一帧按 1/30 秒）。</summary>
    public class WatchPlaybackTests
    {
        const double Frame = 1.0 / 30;
        static readonly float[] Speeds = { 1, 2, 5, 10, 20, 50, 100, 200, 500 };

        static WatchPlayback NewPlayback(ScriptedWatchTarget target, int speedIndex = 2) => new WatchPlayback(target, Speeds, speedIndex);

        static void RunFrames(WatchPlayback playback, double seconds)
        {
            int frames = (int)Math.Round(seconds / Frame);
            for (int i = 0; i < frames; i++) playback.Advance(Frame);
        }

        // ---- 暂停、单步

        [Test]
        public void ByDefault_ItPlaysFiveStepsPerSecond()
        {
            var target = new ScriptedWatchTarget(10_000);
            WatchPlayback playback = NewPlayback(target);
            Assert.AreEqual(5f, playback.StepsPerSecond);
            Assert.IsFalse(playback.Paused);
            RunFrames(playback, 2);
            Assert.AreEqual(10, target.Steps);
        }

        [Test]
        public void WhilePaused_TheClockTakesNoSteps()
        {
            var target = new ScriptedWatchTarget(10_000);
            WatchPlayback playback = NewPlayback(target);
            RunFrames(playback, 1);
            int before = target.Steps;
            playback.TogglePause();
            Assert.IsTrue(playback.Paused);
            RunFrames(playback, 3);
            Assert.AreEqual(before, target.Steps);
            playback.TogglePause();
            Assert.IsFalse(playback.Paused);
            RunFrames(playback, 1);
            Assert.AreEqual(before + 5, target.Steps, "and it carries on after resuming");
        }

        [Test]
        public void StepOnceWhilePaused_TakesExactlyOneStep()
        {
            var target = new ScriptedWatchTarget(10_000);
            WatchPlayback playback = NewPlayback(target);
            playback.TogglePause();
            for (int i = 1; i <= 3; i++)
            {
                Assert.IsTrue(playback.StepOnce());
                Assert.AreEqual(i, target.Steps);
                Assert.AreEqual(i, playback.StepsTaken);
            }
        }

        [Test]
        public void StepOnceWhilePlaying_DoesNothing()
        {
            var target = new ScriptedWatchTarget(10_000);
            WatchPlayback playback = NewPlayback(target);
            Assert.IsFalse(playback.StepOnce());
            Assert.AreEqual(0, target.Steps);
        }

        // ---- 快慢

        [Test]
        public void FasterAndSlower_StayInsideTheTable()
        {
            var target = new ScriptedWatchTarget(10_000);
            WatchPlayback playback = NewPlayback(target);
            for (int i = 0; i < Speeds.Length + 3; i++) playback.Faster();
            Assert.AreEqual(Speeds.Length - 1, playback.SpeedIndex);
            Assert.AreEqual(Speeds.Last(), playback.StepsPerSecond);
            for (int i = 0; i < Speeds.Length + 3; i++) playback.Slower();
            Assert.AreEqual(0, playback.SpeedIndex);
            Assert.AreEqual(Speeds.First(), playback.StepsPerSecond);
        }

        /// <summary>
        /// 在速度 S 下按 1/30 秒一帧拨过 T 秒，走 S × T 步。每帧欠的步数带着小数往下攒，总数是 S × T 向下取整
        /// （差一点点就够一步的也算够，抵掉小数相加的舍入误差）；合同允许边界上差一步，这里另外查实际没有差。
        /// </summary>
        [Test]
        public void AtEverySpeed_TheClockGivesSpeedTimesSecondsSteps()
        {
            var lines = new List<string>();
            for (int index = 0; index < Speeds.Length; index++)
            foreach (double seconds in new[] { 1.0, 3.0, 7.0 })
            {
                var target = new ScriptedWatchTarget(int.MaxValue);
                WatchPlayback playback = NewPlayback(target, index);
                RunFrames(playback, seconds);
                double expected = Speeds[index] * seconds;
                lines.Add($"{Speeds[index]} steps/s x {seconds} s: {target.Steps} steps (S x T = {expected})");
                Assert.LessOrEqual(Math.Abs(target.Steps - expected), 1, lines.Last());
                Assert.AreEqual(Math.Floor(expected + 1e-6), target.Steps, lines.Last());
            }
            Debug.Log(string.Join("\n", lines));
        }

        [Test]
        public void ManyStepsPerFrame_AreTakenWithinTheSameFrame()
        {
            var target = new ScriptedWatchTarget(int.MaxValue);
            WatchPlayback playback = NewPlayback(target, Speeds.Length - 1);
            playback.Advance(Frame);
            Assert.AreEqual(16, target.Steps, "500 steps/s at 30 frames/s is 16 or 17 steps a frame");
        }

        [Test]
        public void ALongPauseOfTheEditor_DoesNotRushAheadMoreThanAQuarterSecond()
        {
            var target = new ScriptedWatchTarget(int.MaxValue);
            WatchPlayback playback = NewPlayback(target, 4); // 20 steps/s
            playback.Advance(10);
            Assert.AreEqual(5, target.Steps);
        }

        // ---- 重来、走到头

        [Test]
        public void Restart_GoesBackToTheStartOfTheSegment()
        {
            var target = new ScriptedWatchTarget(10_000);
            WatchPlayback playback = NewPlayback(target);
            RunFrames(playback, 4);
            Assert.AreEqual(20, target.Position);
            playback.Restart();
            Assert.AreEqual(0, target.Position);
            Assert.AreEqual(1, target.Restarts);
            Assert.AreEqual(0, playback.StepsTaken);
            RunFrames(playback, 1);
            Assert.AreEqual(5, target.Position);
        }

        [Test]
        public void ReachingTheEnd_StopsAndFreezesBeforeTheSegmentStartsOver()
        {
            var target = new ScriptedWatchTarget(12);
            WatchPlayback playback = NewPlayback(target, 3); // 10 steps/s
            playback.FrozenChanged += frozen => target.Log.Add($"frozen {frozen}");
            int changes = 0;
            playback.Changed += () => changes++;
            Assert.DoesNotThrow(() => RunFrames(playback, 5));
            Assert.IsTrue(playback.Finished);
            Assert.IsTrue(playback.Frozen);
            Assert.AreEqual(12, target.Steps, "it stops at the end instead of starting the segment again");
            Assert.AreEqual(12, playback.StepsTaken);
            Assert.AreEqual(1, changes, "the screen hears about the end once");
            CollectionAssert.AreEqual(new[] { "step", "finished", "frozen True", "episode started" }, target.Log.Skip(11).ToArray(),
                "frozen before the environment's own episode start, so the screen keeps the last numbers");

            playback.TogglePause();
            playback.TogglePause();
            Assert.IsFalse(playback.StepOnce(), "no single step past the end either");
            RunFrames(playback, 2);
            Assert.AreEqual(12, target.Steps);
        }

        [Test]
        public void RestartAfterTheEnd_UnfreezesBeforeTheTargetStartsOverAndPlaysAgain()
        {
            var target = new ScriptedWatchTarget(12);
            WatchPlayback playback = NewPlayback(target, 3);
            playback.FrozenChanged += frozen => target.Log.Add($"frozen {frozen}");
            RunFrames(playback, 5);
            target.Log.Clear();
            playback.Restart();
            CollectionAssert.AreEqual(new[] { "frozen False", "restart", "episode started" }, target.Log);
            Assert.IsFalse(playback.Finished);
            Assert.IsFalse(playback.Frozen);
            RunFrames(playback, 0.5);
            Assert.AreEqual(5, target.Position);
        }

        [Test]
        public void ASpeedTableThatIsEmptyOrNotSlowToFast_IsRefused()
        {
            var target = new ScriptedWatchTarget(10);
            Assert.Throws<ArgumentException>(() => new WatchPlayback(target, new float[0], 0));
            Assert.Throws<ArgumentException>(() => new WatchPlayback(target, new[] { 5f, 2f }, 0));
            Assert.Throws<ArgumentException>(() => new WatchPlayback(target, new[] { 0f, 2f }, 0));
            Assert.AreEqual(1, new WatchPlayback(target, new[] { 1f, 2f }, 7).SpeedIndex, "a start index past the end is clamped");
        }
    }
}
