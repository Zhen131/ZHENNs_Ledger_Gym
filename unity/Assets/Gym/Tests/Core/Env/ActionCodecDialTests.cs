using System;
using Gym.Core.Env;
using NUnit.Framework;

namespace Gym.Tests.Core.Env
{
    /// <summary>两种动作模式的形状和「档位 ↔ 目标仓位」（23 批）。下单模式的旧用例在 ActionCodecTests 里，本批不改。</summary>
    public class ActionCodecDialTests
    {
        static readonly double[] Levels = { 0.3, 0.5, 0.7 };

        [Test]
        public void TheOrderModeShape_IsStillOneContinuousActionAndAThreeWayBranch()
        {
            Assert.AreEqual(3, ActionCodec.BranchSize);
            Assert.AreEqual(1, ActionCodec.ContinuousSize);
            Assert.AreEqual(1, ActionCodec.ContinuousSizeFor(ActionMode.Orders));
            CollectionAssert.AreEqual(new[] { 3 }, ActionCodec.BranchSizesFor(ActionMode.Orders, 3));
            CollectionAssert.AreEqual(new[] { 3 }, ActionCodec.BranchSizesFor(ActionMode.Orders, 5), "the level count does not matter in order mode");
        }

        [Test]
        public void TheDialShape_IsNoContinuousActionAndOneBranchWithAChoicePerLevel()
        {
            Assert.AreEqual(0, ActionCodec.DialContinuousSize);
            Assert.AreEqual(0, ActionCodec.ContinuousSizeFor(ActionMode.Dial));
            CollectionAssert.AreEqual(new[] { 3 }, ActionCodec.BranchSizesFor(ActionMode.Dial, 3));
            CollectionAssert.AreEqual(new[] { 4 }, ActionCodec.BranchSizesFor(ActionMode.Dial, 4));
            Assert.Throws<ArgumentOutOfRangeException>(() => ActionCodec.BranchSizesFor(ActionMode.Dial, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => ActionCodec.ContinuousSizeFor((ActionMode)7));
        }

        [Test]
        public void ALevel_MapsToItsTarget_AndOutOfRangeLevelsThrow()
        {
            Assert.AreEqual(0.3, ActionCodec.DialTarget(Levels, 0));
            Assert.AreEqual(0.5, ActionCodec.DialTarget(Levels, 1));
            Assert.AreEqual(0.7, ActionCodec.DialTarget(Levels, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => ActionCodec.DialTarget(Levels, 3));
            Assert.Throws<ArgumentOutOfRangeException>(() => ActionCodec.DialTarget(Levels, -1));
            Assert.AreEqual(1, ActionCodec.DialLevelOf(Levels, 0.5));
            Assert.AreEqual(-1, ActionCodec.DialLevelOf(new[] { 0.2, 0.4, 0.6 }, 0.5));
        }

        [Test]
        public void ActionModeNamesAndParameterValues()
        {
            Assert.IsTrue(ActionModes.TryParse("orders", out ActionMode m) && m == ActionMode.Orders);
            Assert.IsTrue(ActionModes.TryParse("DIAL", out m) && m == ActionMode.Dial);
            Assert.IsFalse(ActionModes.TryParse("dail", out _));
            Assert.IsFalse(ActionModes.TryParse(null, out _));
            Assert.IsTrue(ActionModes.TryFromParameter(0, out m) && m == ActionMode.Orders);
            Assert.IsTrue(ActionModes.TryFromParameter(1, out m) && m == ActionMode.Dial);
            Assert.IsFalse(ActionModes.TryFromParameter(0.5, out _));
            Assert.IsFalse(ActionModes.TryFromParameter(2, out _));
            Assert.AreEqual("dial", ActionMode.Dial.Name());
            Assert.AreEqual(0, (int)ActionMode.Orders);
            Assert.AreEqual(1, (int)ActionMode.Dial);
        }
    }
}
