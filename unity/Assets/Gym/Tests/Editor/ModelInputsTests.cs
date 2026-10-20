using System;
using System.IO;
using Gym.Core.Env;
using Gym.Editor;
using Gym.Runtime.Agents;
using Gym.Runtime.Evaluation;
using Gym.Runtime.Watch;
using Gym.Tests.Runtime.Watch;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Gym.Tests.Editor
{
    /// <summary>
    /// 从真模型读向量观测输入维数，并用它走一遍评估包和观战的核对。要模型：从 GYM_WATCH_MODEL 拿（第一轮的模型是 35 维），
    /// 没给就报告为忽略，和另外三个要模型的测试一样。比较本身是纯函数，不要模型的测试在 ObservationSetTests、EvalRunnerTests。
    /// </summary>
    public class ModelInputsTests
    {
        string folder;
        GameObject agentObject;

        [SetUp]
        public void SetUp()
        {
            folder = Path.Combine(Path.GetTempPath(), "gym-model-inputs-tests-" + Guid.NewGuid().ToString("N"));
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

        [Test]
        public void AnImportedModel_ReportsItsInputSize_AndIsRefusedByEveryOtherObservationSet()
        {
            string model = ModelForTests.PathOrIgnore();
            string asset = WatchSetup.Remember(model);
            int size = ModelInputs.VectorObservationSizeAt(asset);
            TestContext.WriteLine($"{model}: {size} vector observation inputs");
            Debug.Log($"[Gym] model inputs test: {model} takes {size} vector observations");
            Assert.That(size, Is.EqualTo(35).Or.EqualTo(41).Or.EqualTo(43), "a model of one of the three observation sets");

            var info = new EvalBuildInfo { run_id = "test", vector_observation_size = size };
            foreach (ObservationSet set in ObservationSets.All)
            {
                bool fits = size == set.Size();
                string evaluation = EvalRunner.CheckModelInput(info, set, out string notChecked);
                Assert.IsNull(notChecked);
                Assert.AreEqual(fits, evaluation == null, $"evaluation player on {set.Name()}: {evaluation}");
                if (!fits) StringAssert.Contains($"takes {size} observations but the observation set {set.Name()} has {set.Size()}", evaluation);

                string watch = WatchSetup.FitProblem(asset, set);
                Assert.AreEqual(fits, watch == null, $"watch on {set.Name()}: {watch}");

                agentObject = new GameObject("Agent under test");
                var agent = agentObject.AddComponent<TradingAgent>();
                agent.ObservationSetOverride = set;
                bool attached = WatchController.ModelSource.TryAttach(agent, asset, out string problem);
                Assert.AreEqual(fits, attached, $"attach on {set.Name()}: {problem}");
                if (!fits) StringAssert.Contains($"has {set.Size()}", problem);
                Object.DestroyImmediate(agentObject);
                agentObject = null;
            }
        }
    }
}
