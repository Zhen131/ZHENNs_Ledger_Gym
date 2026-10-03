using System;
using System.Linq;
using Gym.Core.Evaluation;
using NUnit.Framework;

namespace Gym.Tests.Core.Evaluation
{
    public class MetricsTests
    {
        // ---- hand-checked metrics

        [Test]
        public void ShortCurve_GivesTheHandCheckedMetrics()
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
        public void Percentiles_InterpolateLinearly()
        {
            double[] values = { 5, 1, 4, 2, 3 };
            Assert.AreEqual(3, Metrics.Median(values));
            Assert.AreEqual(1.2, Metrics.Percentile(values, 5), 1e-12);
            Assert.AreEqual(4.8, Metrics.Percentile(values, 95), 1e-12);
            Assert.AreEqual(2.5, Metrics.Median(new double[] { 1, 2, 3, 4 }));
            Assert.IsNaN(Metrics.Median(new double[0]));
        }

        // ---- edge cases

        [Test]
        public void FlatOrOnePointCurve_GivesZeroSharpeWithoutCrashing()
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
    }
}
