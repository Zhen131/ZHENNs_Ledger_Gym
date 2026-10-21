using System;
using System.IO;
using Gym.Core.Env;
using Gym.Editor;
using Gym.Runtime.Agents;
using Gym.Runtime.Evaluation;
using Gym.Runtime.Watch;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Gym.Tests.Editor
{
    /// <summary>
    /// 从真模型读动作形状（24 批）。用仓库里跟踪的第二轮下单模式模型 models/round2/ppo_v2-s3-20261020/TradingAgent.onnx，
    /// 不靠环境变量、不会被忽略。导入照 WatchSetup.Remember 的做法，记录写临时位置。
    /// </summary>
    public class ModelActionsTests
    {
        const string TrackedModel = "models/round2/ppo_v2-s3-20261020/TradingAgent.onnx";

        string folder;
        GameObject agentObject;

        [SetUp]
        public void SetUp()
        {
            folder = Path.Combine(Path.GetTempPath(), "gym-model-actions-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            WatchModelMemory.Location = Path.Combine(folder, WatchModelMemory.FileName);
        }

        [TearDown]
        public void TearDown()
        {
            WatchModelMemory.Location = null;
            if (agentObject != null) Object.DestroyImmediate(agentObject);
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }

        /// <summary>仓库根目录下的那个模型（Application.dataPath 是 &lt;repo&gt;/unity/Assets）。</summary>
        static string TrackedModelPath()
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", TrackedModel));
            Assert.IsTrue(File.Exists(path), $"{path} is tracked in the repository and must exist");
            return path;
        }

        [Test]
        public void TheTrackedOrderModeModel_HasOneContinuousActionAndAThreeWayBranch_AndIsRefusedByTheDialMode()
        {
            string model = TrackedModelPath();
            string asset = WatchSetup.Remember(model);
            ActionShape shape = ModelActions.ActionShapeAt(asset);
            Debug.Log($"[Gym] model actions test: {TrackedModel} has actions {shape}");
            Assert.AreEqual(1, shape.Continuous);
            CollectionAssert.AreEqual(new[] { 3 }, shape.Branches);
            Assert.AreEqual("1 continuous + [3]", shape.ToString());

            // 评估包这一路：打包时写进 build-info.json 的就是这三个字段
            var info = new EvalBuildInfo
            {
                run_id = "ppo_v2-s3-20261020", continuous_actions = shape.Continuous,
                discrete_branches = new[] { shape.Branches[0] }, action_shape_recorded = true,
            };
            Assert.IsNull(EvalRunner.CheckModelActions(info, ActionMode.Orders, 3, out string notChecked));
            Assert.IsNull(notChecked);
            string refused = EvalRunner.CheckModelActions(info, ActionMode.Dial, 3, out notChecked);
            Assert.IsNull(notChecked);
            StringAssert.Contains("1 continuous + [3]", refused, "the model's shape");
            StringAssert.Contains("0 continuous + [3]", refused, "the dial mode's shape");
            StringAssert.Contains("Nothing was written", refused);

            // 观战这一路：新重载先查观测集（这个模型是 base，35 个数），再查动作形状；两个参数的重载照旧只查观测集
            Assert.IsNull(WatchSetup.FitProblem(asset, ObservationSet.Base));
            Assert.IsNull(WatchSetup.FitProblem(asset, ObservationSet.Base, ActionMode.Orders, 3));
            string watch = WatchSetup.FitProblem(asset, ObservationSet.Base, ActionMode.Dial, 3);
            StringAssert.Contains("1 continuous + [3]", watch);
            StringAssert.Contains("0 continuous + [3]", watch);
            StringAssert.Contains("actionMode", watch);
            StringAssert.Contains("has 41", WatchSetup.FitProblem(asset, ObservationSet.Long, ActionMode.Orders, 3), "the observation set is checked first");

            // 挂模型这一步：Agent 要用旋钮模式时不挂
            agentObject = new GameObject("Agent under test");
            var agent = agentObject.AddComponent<TradingAgent>();
            agent.ObservationSetOverride = ObservationSet.Base;
            agent.ActionModeOverride = ActionMode.Dial;
            Assert.IsFalse(WatchController.ModelSource.TryAttach(agent, asset, out string problem));
            StringAssert.Contains("0 continuous + [", problem);
            agent.ActionModeOverride = ActionMode.Orders;
            Assert.IsTrue(WatchController.ModelSource.TryAttach(agent, asset, out problem), problem);
        }
    }
}
