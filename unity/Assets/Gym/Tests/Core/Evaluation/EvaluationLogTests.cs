using System;
using System.IO;
using System.Linq;
using Gym.Core.Evaluation;
using NUnit.Framework;

namespace Gym.Tests.EditMode
{
    public class EvaluationLogTests
    {
        // ---- M-4 append-only log

        string dir;

        [SetUp]
        public void SetUp()
        {
            dir = Path.Combine(Path.GetTempPath(), "gym-m4-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }

        static EvaluationRecord Record(string policy, double ret) => new EvaluationRecord
        {
            TimestampUtc = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc),
            Kind = EvaluationRecord.BaselineKind,
            Policy = policy,
            Symbol = "BTCUSDT",
            Segment = "test",
            SegmentStart = new DateTime(2025, 9, 1),
            SegmentEnd = new DateTime(2026, 8, 31),
            FeeRate = 0.001,
            TotalReturn = ret,
            Notes = "p5=-0.1, p95=0.2",
        };

        [Test]
        public void M04_SecondAppendKeepsEveryEarlierByte()
        {
            string path = EvaluationLog.Append(dir, Record("cash", 0));
            byte[] first = File.ReadAllBytes(path);
            EvaluationLog.Append(dir, Record("buy_and_hold", -0.25));
            byte[] second = File.ReadAllBytes(path);
            Assert.Greater(second.Length, first.Length);
            CollectionAssert.AreEqual(first, second.Take(first.Length).ToArray());

            string[] lines = File.ReadAllText(path).TrimEnd('\n').Split('\n');
            Assert.AreEqual(3, lines.Length);
            Assert.AreEqual(EvaluationLog.Header, lines[0]);
            Assert.AreEqual(23, EvaluationLog.Columns.Length);
            StringAssert.Contains("\"p5=-0.1, p95=0.2\"", lines[2], "notes with a comma are quoted");
            StringAssert.StartsWith("2026-10-01T12:00:00.000Z,baseline,buy_and_hold,,,BTCUSDT,test,2025-09-01,2026-08-31,0.001,0,0,1,-0.25,", lines[2]);
        }

        [Test]
        public void M04_ForeignHeaderIsRefusedAndLeftUntouched()
        {
            string path = Path.Combine(dir, EvaluationLog.FileName);
            File.WriteAllText(path, "timestamp_utc,policy,total_return\n2026-01-01,x,0.1\n");
            byte[] before = File.ReadAllBytes(path);
            var e = Assert.Throws<InvalidDataException>(() => EvaluationLog.Append(dir, Record("cash", 0)));
            StringAssert.Contains("Nothing was written", e.Message);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(path));
        }

        [Test]
        public void M04_MissingFinalNewlineIsAddedByAppendingOnly()
        {
            string path = Path.Combine(dir, EvaluationLog.FileName);
            string existing = EvaluationLog.Header + "\n" + EvaluationLog.FormatRow(Record("cash", 0));
            File.WriteAllText(path, existing); // no trailing newline
            byte[] before = File.ReadAllBytes(path);
            EvaluationLog.Append(dir, Record("random", 0.05));
            byte[] after = File.ReadAllBytes(path);
            CollectionAssert.AreEqual(before, after.Take(before.Length).ToArray());
            Assert.AreEqual(3, File.ReadAllText(path).TrimEnd('\n').Split('\n').Length);
        }

        [Test]
        public void M04_RunDetailsNeverOverwrite()
        {
            var when = new DateTime(2026, 10, 1, 12, 0, 0, 123, DateTimeKind.Utc);
            var details = new JsonObject { { "policy", "cash" }, { "metrics", EvaluationLog.MetricsJson(default) }, { "seeds", new[] { 1, 2 } } };
            string a = EvaluationLog.WriteRunDetails(dir, when, "cash", "test", details);
            string b = EvaluationLog.WriteRunDetails(dir, when, "cash", "test", details);
            Assert.AreNotEqual(a, b);
            Assert.AreEqual("20261001T120000123Z-cash-test.json", Path.GetFileName(a));
            string json = File.ReadAllText(a);
            StringAssert.Contains("\"policy\": \"cash\"", json);
            StringAssert.Contains("\"total_return\": 0", json);
            StringAssert.Contains("\"seeds\": [", json);
        }
    }
}
