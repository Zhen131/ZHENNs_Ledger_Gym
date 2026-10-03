using System;
using System.IO;
using System.Linq;
using Gym.Core.Accounting;
using Gym.Core.Env;
using Gym.Core.Evaluation;
using Gym.Core.Market;
using NUnit.Framework;

namespace Gym.Tests.EditMode
{
    public class EvaluationTests
    {
        static readonly SymbolRules Btc = SymbolRules.BtcUsdt;

        // ---- M-1 hand-checked metrics

        [Test]
        public void M01_MetricsOfAShortCurve()
        {
            // Curve 100 → 110 → 99 → 121.
            //   return = 121 ÷ 100 − 1 = 0.21
            //   max drawdown: peak 110, later low 99 → (110 − 99) ÷ 110 = 0.1
            //   log returns r = [ln 1.1, ln 0.9, ln(121/99)] = [0.0953102, −0.1053605, 0.2006707]
            //   mean = ln(1.21) ÷ 3 = 0.0635401
            //   sample std = √(Σ(r − mean)² ÷ 2) = 0.1554695
            //   Sharpe = 0.0635401 ÷ 0.1554695 × √8760 = 38.252055
            double[] curve = { 100, 110, 99, 121 };
            Assert.AreEqual(0.21, Metrics.TotalReturn(curve), 1e-12);
            Assert.AreEqual(0.1, Metrics.MaxDrawdown(curve), 1e-12);
            double[] r = { Math.Log(1.1), Math.Log(0.9), Math.Log(121.0 / 99) };
            double mean = r.Average();
            double std = Math.Sqrt(r.Sum(x => (x - mean) * (x - mean)) / 2);
            Assert.AreEqual(mean / std * Math.Sqrt(8760), Metrics.SharpeAnnualized(curve), 1e-9);
            Assert.AreEqual(38.252055, Metrics.SharpeAnnualized(curve), 1e-6);

            // Turnover = notional ÷ mean equity = 215 ÷ 107.5 = 2; fees % = 1 ÷ 100 × 100 = 1; exposure = 2 ÷ 3.
            EpisodeMetrics m = Metrics.Compute(curve, trades: 2, rejected: 1, turnoverNotional: 215, feesPaid: 1, holdingSteps: 2, steps: 3);
            Assert.AreEqual(2.0, m.Turnover, 1e-12);
            Assert.AreEqual(1.0, m.FeesPct, 1e-12);
            Assert.AreEqual(2.0 / 3, m.Exposure, 1e-12);
            Assert.AreEqual(2, m.Trades);
            Assert.AreEqual(1, m.Rejected);
            Assert.AreEqual(100, m.InitialEquity);
            Assert.AreEqual(121, m.FinalEquity);
        }

        [Test]
        public void M01_PercentilesInterpolateLinearly()
        {
            double[] values = { 5, 1, 4, 2, 3 };
            Assert.AreEqual(3, Metrics.Median(values));
            Assert.AreEqual(1.2, Metrics.Percentile(values, 5), 1e-12);
            Assert.AreEqual(4.8, Metrics.Percentile(values, 95), 1e-12);
            Assert.AreEqual(2.5, Metrics.Median(new double[] { 1, 2, 3, 4 }));
            Assert.IsNaN(Metrics.Median(new double[0]));
        }

        // ---- M-2 edge cases

        [Test]
        public void M02_FlatCurveHasZeroSharpeAndOnePointDoesNotCrash()
        {
            Assert.AreEqual(0, Metrics.SharpeAnnualized(new double[] { 100, 100, 100, 100 }));
            Assert.AreEqual(0, Metrics.SharpeAnnualized(new double[] { 100, 101 }));
            double[] one = { 100 };
            Assert.AreEqual(0, Metrics.TotalReturn(one));
            Assert.AreEqual(0, Metrics.MaxDrawdown(one));
            Assert.AreEqual(0, Metrics.SharpeAnnualized(one));
            EpisodeMetrics m = Metrics.Compute(one, 0, 0, 0, 0, 0, 0);
            Assert.AreEqual(0, m.Exposure);
            Assert.AreEqual(0, m.Turnover);
            Assert.AreEqual(0, Metrics.TotalReturn(new double[0]));
            Assert.AreEqual(0, Metrics.MaxDrawdown(new double[] { 100, 120, 130 }));
        }

        // ---- M-3 baselines

        [Test]
        public void M03_BuyAndHoldOnARisingMarketEarnsTheMoveLessOneFee()
        {
            // close[k] = 100 × 1.001^k, open[k] = close[k − 1]. Evaluation starts at t = 32 and
            // buys at open[33] = close[32]; the account ends at close[199].
            //   expected = (close[199] ÷ close[32]) ÷ (1 + fee) − 1   (fee 0.1 % paid once on the buy)
            CandleSeries s = TestData.Synthetic(200, k => 100 * Math.Pow(1.001, k), k => k == 0 ? 100 : 100 * Math.Pow(1.001, k - 1));
            var env = new TradingEnv(s, Btc, 0, 199);
            var cost = new CostModel(0.001);
            EpisodeMetrics m = Baselines.RunBuyAndHold(env, cost);
            double move = s.CloseAt(199) / s.CloseAt(32);
            Assert.AreEqual(move / 1.001 - 1, m.TotalReturn, 1e-5);
            Assert.AreEqual(1, m.Trades);
            Assert.AreEqual(167, m.Steps);
            Assert.AreEqual(1.0, m.Exposure);
            Assert.AreEqual(0, m.MaxDrawdown, 1e-12);

            EpisodeMetrics cash = Baselines.RunCash(env, cost);
            Assert.AreEqual(0, cash.TotalReturn);
            Assert.AreEqual(0, cash.Trades);
            Assert.AreEqual(0, cash.FeesPaid);
            Assert.AreEqual(0, cash.Exposure);
        }

        [Test]
        public void M03_RandomBaselineIsReproduciblePerSeed()
        {
            CandleSeries s = TestData.RandomWalk(600, 33, 0.01);
            var env = new TradingEnv(s, Btc, 0, 599);
            var cost = new CostModel(0.001, 0.5, 0.0005);
            EpisodeMetrics a = Baselines.RunRandom(env, cost, 7);
            EpisodeMetrics b = Baselines.RunRandom(env, cost, 7);
            Assert.IsTrue(TestData.SameBits(a.TotalReturn, b.TotalReturn));
            Assert.IsTrue(TestData.SameBits(a.SharpeAnnualized, b.SharpeAnnualized));
            Assert.IsTrue(TestData.SameBits(a.FeesPaid, b.FeesPaid));
            Assert.AreEqual(a.Trades, b.Trades);
            Assert.AreEqual(a.Rejected, b.Rejected);
            Assert.Greater(a.Trades, 10);
            EpisodeMetrics c = Baselines.RunRandom(env, cost, 8);
            Assert.IsFalse(TestData.SameBits(a.TotalReturn, c.TotalReturn), "a different seed should trade differently");
        }

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
