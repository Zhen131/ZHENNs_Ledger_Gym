using System;
using System.IO;
using Gym.Editor;
using Gym.Runtime.Agents;
using Gym.Runtime.Play;
using Gym.Runtime.Watch;
using Gym.Tests.Runtime.Watch;
using NUnit.Framework;
using Unity.MLAgents.Policies;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Gym.Tests.Editor
{
    /// <summary>
    /// 菜单「选模型来看」的后一段（导入、记住）和把模型挂到 Agent 上的那一步。记录全程写到临时位置。
    /// 要模型的几项从 GYM_WATCH_MODEL 拿模型，没给就报告为忽略。
    /// </summary>
    public class WatchSetupTests
    {
        string folder;
        GameObject agentObject;

        [SetUp]
        public void SetUp()
        {
            folder = Path.Combine(Path.GetTempPath(), "gym-watch-setup-tests-" + Guid.NewGuid().ToString("N"));
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

        TradingAgent NewAgent()
        {
            agentObject = new GameObject("Agent under test");
            return agentObject.AddComponent<TradingAgent>();
        }

        /// <summary>BehaviorParameters 上挂的模型（按序列化字段读，这个测试程序集不引用推理引擎）。</summary>
        static Object ModelOn(TradingAgent agent) =>
            new SerializedObject(agent.GetComponent<BehaviorParameters>()).FindProperty("m_Model").objectReferenceValue;

        [Test]
        public void TheEditorLoad_RegistersTheModelAttacherForTheWatchScene()
        {
            Assert.IsInstanceOf<WatchModelAttacher>(WatchController.ModelSource);
        }

        [Test]
        public void TheNoModelHint_NamesTheMenuItemThatReallyExists()
        {
            string menu = WatchSetup.MenuPath.Replace("/", " > ");
            StringAssert.Contains(menu, PlayText.Get(PlayTextKey.WatchNoModel, PlayLanguage.English));
            StringAssert.Contains(menu, PlayText.Get(PlayTextKey.WatchNoModel, PlayLanguage.Chinese));
        }

        [Test]
        public void TheImportedName_IsWatchPlusTheFolderTheModelIsIn()
        {
            Assert.AreEqual("Watch-run-7", WatchSetup.ImportedName(Path.Combine(folder, "run-7", "TradingAgent.onnx")));
            Assert.AreEqual("Watch-_golden", WatchSetup.ImportedName(Path.Combine(folder, "_golden", "model.onnx")));
        }

        [Test]
        public void RememberingAFileThatIsNotThere_ThrowsAndRemembersNothing()
        {
            Assert.Throws<FileNotFoundException>(() => WatchSetup.Remember(Path.Combine(folder, "missing", "TradingAgent.onnx")));
            Assert.IsFalse(File.Exists(WatchModelMemory.Location));
        }

        [Test]
        public void AttachingAModelThatIsGone_FailsWithAReasonThatPointsToTheMenu()
        {
            TradingAgent agent = NewAgent();
            Assert.IsFalse(WatchController.ModelSource.TryAttach(agent, "Assets/Gym/Models/Imported/does-not-exist.onnx", out string problem));
            StringAssert.Contains(WatchSetup.MenuPath, problem);
            Assert.IsNull(ModelOn(agent));
        }

        [Test]
        public void RememberingATrainedModel_ImportsItAndWritesOnlyTheTemporaryRecord()
        {
            string model = ModelForTests.PathOrIgnore();
            string realRecord = WatchModelMemory.DefaultLocation;
            DateTime? realBefore = File.Exists(realRecord) ? File.GetLastWriteTimeUtc(realRecord) : (DateTime?)null;

            string asset = WatchSetup.Remember(model);
            Assert.AreEqual($"{BuildScript.ImportedModelsFolder}/{WatchSetup.ImportedName(model)}.onnx", asset);
            Object imported = AssetDatabase.LoadMainAssetAtPath(asset);
            Assert.IsNotNull(imported);
            Assert.AreEqual("ModelAsset", imported.GetType().Name, "imported as a model");
            WatchModelRecord record = WatchModelMemory.Read();
            Assert.AreEqual(asset, record.model_asset);
            Assert.AreEqual(model, record.source_file);
            DateTime? realAfter = File.Exists(realRecord) ? File.GetLastWriteTimeUtc(realRecord) : (DateTime?)null;
            Assert.AreEqual(realBefore, realAfter, "the project's own record is untouched");

            TradingAgent agent = NewAgent();
            Assert.IsTrue(WatchController.ModelSource.TryAttach(agent, asset, out string problem), problem);
            Assert.AreSame(imported, ModelOn(agent));
        }
    }
}
