using System;
using System.Linq;
using Gym.Core.Env;
using Gym.Runtime.Play;
using NUnit.Framework;
using UnityEngine;

namespace Gym.Tests.Runtime.Play
{
    /// <summary>小人的四种动作：一半时各是什么样、做完回到原处、新动作打断旧动作。时钟由测试拨。</summary>
    public class AvatarAnimationTests
    {
        static readonly AvatarMotionStyle Style = new AvatarMotionStyle(0.6f, 0.3f, 0.15f, 15f, 0.12f);

        static AvatarPose HalfWay(AvatarMotion motion)
        {
            var moves = new AvatarAnimation(Style);
            moves.Start(motion);
            moves.Advance(Style.Seconds / 2);
            Assert.AreEqual(motion, moves.Motion, "premise: still moving half way");
            Assert.AreEqual(0.5f, moves.Progress, 1e-6);
            return moves.Pose;
        }

        [Test]
        public void HalfWay_HoldBuyAndSellHopWhileRejectedShakesWithoutHopping()
        {
            AvatarPose hold = HalfWay(AvatarMotion.Hold);
            AvatarPose buy = HalfWay(AvatarMotion.Buy);
            AvatarPose sell = HalfWay(AvatarMotion.Sell);
            AvatarPose rejected = HalfWay(AvatarMotion.Rejected);

            foreach (AvatarPose hop in new[] { hold, buy, sell })
            {
                Assert.AreEqual(Style.HopHeight, hop.Offset.y, 1e-5, "at the top of the hop");
                Assert.AreEqual(0f, hop.Offset.x, "no sideways move");
            }
            Assert.AreEqual(1f, hold.Scale);
            Assert.AreEqual(0f, hold.TiltDegrees);
            Assert.AreEqual(1f + Style.BuyGrowth, buy.Scale, 1e-5, "only a buy grows");
            Assert.AreEqual(0f, buy.TiltDegrees);
            Assert.AreEqual(1f, sell.Scale);
            Assert.AreEqual(Style.SellLeanDegrees, sell.TiltDegrees, 1e-4, "only a sell leans");
            Assert.AreEqual(0f, rejected.Offset.y, "a rejected order does not hop");
            Assert.AreEqual(Style.ShakeDistance, Math.Abs(rejected.Offset.x), 1e-5, "it is shaken to the side");
            Assert.AreEqual(1f, rejected.Scale);
            Assert.AreEqual(0f, rejected.TiltDegrees);

            AvatarPose[] all = { hold, buy, sell, rejected };
            for (int i = 0; i < all.Length; i++)
            for (int j = i + 1; j < all.Length; j++)
                Assert.IsFalse(Same(all[i], all[j]), $"poses {i} and {j} differ");
        }

        static bool Same(AvatarPose a, AvatarPose b) =>
            a.Offset == b.Offset && Mathf.Approximately(a.Scale, b.Scale) && Mathf.Approximately(a.TiltDegrees, b.TiltDegrees);

        [Test]
        public void EveryMotion_LeavesTheRestPoseOnTheWayAndComesBackWhenDone()
        {
            foreach (AvatarMotion motion in new[] { AvatarMotion.Hold, AvatarMotion.Buy, AvatarMotion.Sell, AvatarMotion.Rejected })
            {
                var moves = new AvatarAnimation(Style);
                moves.Start(motion);
                Assert.IsTrue(Same(AvatarPose.Rest, moves.Pose), $"{motion} starts at rest");
                bool leftRest = false;
                for (int i = 0; i < 5; i++)
                {
                    moves.Advance(Style.Seconds / 6);
                    leftRest |= !Same(AvatarPose.Rest, moves.Pose);
                }
                Assert.IsTrue(leftRest, $"{motion} moves");
                moves.Advance(Style.Seconds / 6 + 0.001f);
                Assert.AreEqual(AvatarMotion.Stand, moves.Motion, $"{motion} is over");
                Assert.IsTrue(Same(AvatarPose.Rest, moves.Pose), $"{motion} ends at rest");
            }
        }

        [Test]
        public void ANewMotionHalfWayThroughAnother_ReplacesItAndStartsFromZero()
        {
            var moves = new AvatarAnimation(Style);
            moves.Start(AvatarMotion.Buy);
            moves.Advance(Style.Seconds * 0.4f);
            moves.Start(AvatarMotion.Hold);
            Assert.AreEqual(AvatarMotion.Hold, moves.Motion);
            Assert.AreEqual(0f, moves.Progress);
            Assert.IsTrue(Same(AvatarPose.Rest, moves.Pose));
            moves.Advance(Style.Seconds * 0.7f);
            Assert.AreEqual(AvatarMotion.Hold, moves.Motion, "the new motion runs its full length, not what was left of the old one");
        }

        [Test]
        public void Standing_DoesNotMoveWhenTheClockRuns()
        {
            var moves = new AvatarAnimation(Style);
            moves.Advance(10f);
            Assert.AreEqual(AvatarMotion.Stand, moves.Motion);
            Assert.IsTrue(Same(AvatarPose.Rest, moves.Pose));
        }

        [Test]
        public void MotionFor_PicksTheMotionFromTheStepResult()
        {
            StepResult filled = new StepResult(0, false, false, EndReason.None, true, false);
            StepResult rejected = new StepResult(0, false, false, EndReason.None, false, true);
            StepResult nothing = new StepResult(0, false, false, EndReason.None, false, false);
            Assert.AreEqual(AvatarMotion.Buy, AvatarAnimation.MotionFor(TradeAction.Buy, filled));
            Assert.AreEqual(AvatarMotion.Sell, AvatarAnimation.MotionFor(TradeAction.Sell, filled));
            Assert.AreEqual(AvatarMotion.Rejected, AvatarAnimation.MotionFor(TradeAction.Buy, rejected));
            Assert.AreEqual(AvatarMotion.Rejected, AvatarAnimation.MotionFor(TradeAction.Sell, rejected));
            Assert.AreEqual(AvatarMotion.Hold, AvatarAnimation.MotionFor(TradeAction.Hold, nothing));
        }

        [Test]
        public void AMotionThatTakesNoTime_IsRefused()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new AvatarAnimation(new AvatarMotionStyle(0f, 0.3f, 0.1f, 10f, 0.1f)));
        }

        [Test]
        public void TheFourMotions_AreAllTheMotionsThereAreBesidesStanding()
        {
            CollectionAssert.AreEquivalent(
                new[] { AvatarMotion.Stand, AvatarMotion.Hold, AvatarMotion.Buy, AvatarMotion.Sell, AvatarMotion.Rejected },
                Enum.GetValues(typeof(AvatarMotion)).Cast<AvatarMotion>());
        }
    }
}
