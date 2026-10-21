using Gym.Core.Env;
using Gym.Runtime.Evaluation;
using NUnit.Framework;
using UnityEngine;

namespace Gym.Tests.Runtime.Evaluation
{
    /// <summary>
    /// 动作形状核对（24 批）：比较是不碰模型的纯函数（<see cref="ActionShape.ModelActionProblem"/>），评估包按
    /// build-info.json 调它（<see cref="EvalRunner.CheckModelActions"/>）。从真模型读形状的那条在 Editor 的 ModelActionsTests。
    /// </summary>
    public class ActionShapeCheckTests
    {
        static EvalBuildInfo Recorded(int continuous, params int[] branches) =>
            new EvalBuildInfo { run_id = "run", continuous_actions = continuous, discrete_branches = branches, action_shape_recorded = true };

        [Test]
        public void TheExpectedShapes_ComeFromActionCodec()
        {
            Assert.AreEqual("1 continuous + [3]", ActionShape.For(ActionMode.Orders, 3).ToString());
            Assert.AreEqual("0 continuous + [3]", ActionShape.For(ActionMode.Dial, 3).ToString());
            Assert.AreEqual("0 continuous + [4]", ActionShape.For(ActionMode.Dial, 4).ToString());
            Assert.IsTrue(new ActionShape(1, new[] { 3 }).SameAs(ActionShape.For(ActionMode.Orders, 0)));
            Assert.IsFalse(new ActionShape(1, new[] { 3, 2 }).SameAs(ActionShape.For(ActionMode.Orders, 0)));
            Assert.IsFalse(new ActionShape(0, new int[0]).SameAs(ActionShape.For(ActionMode.Dial, 3)));
        }

        [Test]
        public void TheSameShape_Passes()
        {
            Assert.IsNull(ActionShape.ModelActionProblem(new ActionShape(1, new[] { 3 }), ActionMode.Orders, 3));
            Assert.IsNull(ActionShape.ModelActionProblem(new ActionShape(0, new[] { 3 }), ActionMode.Dial, 3));

            Assert.IsNull(EvalRunner.CheckModelActions(Recorded(1, 3), ActionMode.Orders, 0, out string notChecked));
            Assert.IsNull(notChecked);
        }

        [Test]
        public void ADifferentShape_IsRefusedWithBothShapes()
        {
            Assert.AreEqual("the model's actions are 1 continuous + [3] but the action mode dial with 3 levels needs 0 continuous + [3]; " +
                            "use the action mode the model was trained with, or a model trained in dial mode",
                ActionShape.ModelActionProblem(new ActionShape(1, new[] { 3 }), ActionMode.Dial, 3));

            string refused = EvalRunner.CheckModelActions(Recorded(1, 3), ActionMode.Dial, 3, out string notChecked);
            Assert.IsNull(notChecked);
            Assert.AreEqual("the model's actions are 1 continuous + [3] but the action mode dial with 3 levels needs 0 continuous + [3]; " +
                            "use the action mode the model was trained with, or a model trained in dial mode " +
                            "(from build-info.json, model run run). Nothing was written.", refused);

            string orders = EvalRunner.CheckModelActions(Recorded(0, 3), ActionMode.Orders, 0, out notChecked);
            Assert.IsNull(notChecked);
            StringAssert.Contains("the model's actions are 0 continuous + [3] but the action mode orders needs 1 continuous + [3]", orders);
            StringAssert.Contains("Nothing was written", orders);

            StringAssert.Contains("needs 0 continuous + [4]", EvalRunner.CheckModelActions(Recorded(0, 3), ActionMode.Dial, 4, out _));
        }

        /// <summary>0 个连续数是旋钮模式的正常值：action_shape_recorded 为 true 时照常核对，不当旧包。</summary>
        [Test]
        public void NoContinuousActions_InARecordedBuild_IsCheckedLikeAnyOtherShape()
        {
            Assert.IsNull(EvalRunner.CheckModelActions(Recorded(0, 3), ActionMode.Dial, 3, out string notChecked));
            Assert.IsNull(notChecked, "a dial model is not an old build");
            Assert.IsNotNull(EvalRunner.CheckModelActions(Recorded(0, 3), ActionMode.Orders, 0, out notChecked));
            Assert.IsNull(notChecked);
            Assert.IsNotNull(EvalRunner.CheckModelActions(Recorded(0), ActionMode.Dial, 3, out notChecked), "no branch at all is refused too");
            Assert.IsNull(notChecked);
        }

        /// <summary>24 批之前打的包：没有 action_shape_recorded（读出来是 false），不查、只警告；没有 build-info.json 时也一样。</summary>
        [Test]
        public void ABuildThatDidNotRecordTheShape_IsOnlyWarnedAbout()
        {
            var old = new EvalBuildInfo { run_id = "old", vector_observation_size = 35 };
            Assert.IsNull(EvalRunner.CheckModelActions(old, ActionMode.Dial, 3, out string notChecked));
            Assert.AreEqual("build-info.json does not record the model's action shape (built before action shapes were recorded), " +
                            "so it is not checked against the action mode dial", notChecked);

            // recorded 为 false 时，哪怕记了不对的形状也不查
            var unrecorded = new EvalBuildInfo { run_id = "x", continuous_actions = 5, discrete_branches = new[] { 9 } };
            Assert.IsNull(EvalRunner.CheckModelActions(unrecorded, ActionMode.Orders, 0, out notChecked));
            Assert.IsNotNull(notChecked);

            Assert.IsNull(EvalRunner.CheckModelActions(null, ActionMode.Orders, 0, out string noFile));
            Assert.AreEqual("no build-info.json, so the model's action shape is not checked against the action mode orders", noFile);

            // 13 批、23 批打的包的 build-info.json：没有这三个字段
            EvalBuildInfo parsed = JsonUtility.FromJson<EvalBuildInfo>(
                "{\"run_id\": \"old\", \"model_sha256\": \"abc\", \"vector_observation_size\": 35}");
            Assert.IsFalse(parsed.action_shape_recorded);
            Assert.AreEqual(0, parsed.continuous_actions);
            Assert.IsNull(EvalRunner.CheckModelActions(parsed, ActionMode.Orders, 0, out notChecked));
            StringAssert.StartsWith("build-info.json does not record the model's action shape", notChecked);
        }

        /// <summary>打包时写的三个字段经 JsonUtility 来回一次不变（BuildScript 用 JsonUtility.ToJson 写）。</summary>
        [Test]
        public void TheThreeFields_RoundTripThroughBuildInfoJson()
        {
            string json = JsonUtility.ToJson(Recorded(0, 3), true);
            StringAssert.Contains("\"continuous_actions\": 0", json);
            StringAssert.Contains("\"action_shape_recorded\": true", json);
            StringAssert.Contains("\"discrete_branches\": [", json);
            EvalBuildInfo back = JsonUtility.FromJson<EvalBuildInfo>(json);
            Assert.IsTrue(back.action_shape_recorded);
            Assert.AreEqual(0, back.continuous_actions);
            CollectionAssert.AreEqual(new[] { 3 }, back.discrete_branches);
        }
    }
}
