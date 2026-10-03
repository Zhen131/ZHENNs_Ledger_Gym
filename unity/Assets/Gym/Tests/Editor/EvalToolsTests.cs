using System;
using System.IO;
using Gym.Editor;
using Gym.Tests.Runtime.Evaluation;
using NUnit.Framework;

namespace Gym.Tests.Editor
{
    public class EvalToolsTests
    {
        string tempDir;

        [SetUp]
        public void SetUp()
        {
            tempDir = Path.Combine(Path.GetTempPath(), "gym-eval-" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }

        [Test]
        public void Q03_PoliciesArgumentDefaultsToAllThreeAndRejectsUnknownNames()
        {
            CollectionAssert.AreEquivalent(new[] { "buyhold", "cash", "random" }, EvalTools.ParsePolicies(null));
            CollectionAssert.AreEquivalent(new[] { "random" }, EvalTools.ParsePolicies("random"));
            CollectionAssert.AreEquivalent(new[] { "cash", "random" }, EvalTools.ParsePolicies(" Random , cash "));
            Assert.Throws<ArgumentException>(() => EvalTools.ParsePolicies("random,momentum"));
            Assert.Throws<ArgumentException>(() => EvalTools.ParsePolicies(","));
        }

        [Test]
        public void M2_BaselineFilesCarryNoMachinePaths()
        {
            EvalTools.Run(new[] { "x", "-gymSegment", "validation", "-gymFeeRates", "0.001", "-gymRandomSeeds", "2", "-gymOut", tempDir });
            string[] files = Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories);
            Assert.AreEqual(4, files.Length, "log.csv and one JSON per policy"); // buy-and-hold, cash, random
            foreach (string file in files)
            {
                string text = File.ReadAllText(file);
                RecordFileAssert.NoMachinePath(text, file);
                if (file.EndsWith(".json")) StringAssert.Contains("\"data_file\": \"data/BTCUSDT-1h.csv\"", text, file);
            }
        }
    }
}
