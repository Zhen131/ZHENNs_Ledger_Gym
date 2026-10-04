using System;
using System.IO;
using System.Text.RegularExpressions;
using Gym.Runtime.Watch;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Gym.Tests.Runtime.Watch
{
    /// <summary>观战记住上一个模型的那份记录。全程用临时的记录位置，不碰工程里真的那份。</summary>
    public class WatchModelMemoryTests
    {
        string folder;

        [SetUp]
        public void SetUp()
        {
            folder = Path.Combine(Path.GetTempPath(), "gym-watch-memory-tests-" + Guid.NewGuid().ToString("N"));
            WatchModelMemory.Location = Path.Combine(folder, "nested", WatchModelMemory.FileName);
        }

        [TearDown]
        public void TearDown()
        {
            WatchModelMemory.Location = null;
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }

        [Test]
        public void TheDefaultLocation_IsInThisProjectsUserSettingsFolder()
        {
            WatchModelMemory.Location = null;
            string projectFolder = Path.GetDirectoryName(Application.dataPath);
            Assert.AreEqual(Path.Combine(projectFolder, "UserSettings", "Gym", "watch-model.json"), WatchModelMemory.Location);
        }

        [Test]
        public void NothingRemembered_ReadsAsNullWithoutWarnings()
        {
            Assert.IsNull(WatchModelMemory.Read());
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ARecordThatWasWritten_IsReadBack()
        {
            WatchModelMemory.Write(new WatchModelRecord { model_asset = "Assets/Gym/Models/Imported/Watch-run.onnx", source_file = "/somewhere/run/TradingAgent.onnx" });
            Assert.IsTrue(File.Exists(WatchModelMemory.Location), "the folder is created when needed");
            WatchModelRecord back = WatchModelMemory.Read();
            Assert.AreEqual("Assets/Gym/Models/Imported/Watch-run.onnx", back.model_asset);
            Assert.AreEqual("/somewhere/run/TradingAgent.onnx", back.source_file);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ABrokenRecord_ReadsAsNullWithAWarning()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(WatchModelMemory.Location));
            File.WriteAllText(WatchModelMemory.Location, "{ this is not json");
            Assert.IsNull(WatchModelMemory.Read());
            LogAssert.Expect(LogType.Warning, new Regex("remembered watch model"));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ARecordWithoutAModel_ReadsAsNullWithAWarning()
        {
            WatchModelMemory.Write(new WatchModelRecord { model_asset = "", source_file = "x" });
            Assert.IsNull(WatchModelMemory.Read());
            LogAssert.Expect(LogType.Warning, new Regex("names no model"));
            LogAssert.NoUnexpectedReceived();
        }
    }
}
