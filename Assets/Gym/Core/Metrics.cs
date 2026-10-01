using System;
using System.Collections.Generic;

namespace Gym.Core
{
    /// <summary>The evaluation numbers of one episode (04B §1).</summary>
    public readonly struct EpisodeMetrics
    {
        public readonly double TotalReturn;
        public readonly double MaxDrawdown;
        public readonly double SharpeAnnualized;
        public readonly int Trades;
        public readonly int Rejected;
        public readonly double Turnover;
        public readonly double FeesPaid;
        public readonly double FeesPct;
        public readonly double Exposure;
        public readonly int Steps;
        public readonly double InitialEquity;
        public readonly double FinalEquity;

        public EpisodeMetrics(double totalReturn, double maxDrawdown, double sharpeAnnualized, int trades, int rejected,
            double turnover, double feesPaid, double feesPct, double exposure, int steps, double initialEquity, double finalEquity)
        {
            TotalReturn = totalReturn;
            MaxDrawdown = maxDrawdown;
            SharpeAnnualized = sharpeAnnualized;
            Trades = trades;
            Rejected = rejected;
            Turnover = turnover;
            FeesPaid = feesPaid;
            FeesPct = feesPct;
            Exposure = exposure;
            Steps = steps;
            InitialEquity = initialEquity;
            FinalEquity = finalEquity;
        }
    }

    /// <summary>
    /// Metrics over an equity curve (the start plus one value per step, at closes)
    /// and the account counters.
    /// </summary>
    public static class Metrics
    {
        /// <summary>Hourly candles: 24 × 365 steps per year.</summary>
        public const double StepsPerYear = 8760;

        /// <summary>Final ÷ initial − 1. 0 for an empty curve.</summary>
        public static double TotalReturn(IReadOnlyList<double> curve)
        {
            if (curve == null || curve.Count == 0) return 0;
            return curve[curve.Count - 1] / curve[0] - 1;
        }

        /// <summary>Largest fall from any peak to a later low, as a positive fraction in [0, 1].</summary>
        public static double MaxDrawdown(IReadOnlyList<double> curve)
        {
            if (curve == null || curve.Count == 0) return 0;
            double peak = curve[0];
            double worst = 0;
            for (int i = 1; i < curve.Count; i++)
            {
                double value = curve[i];
                if (value > peak) peak = value;
                else if (peak > 0) worst = Math.Max(worst, (peak - value) / peak);
            }
            return worst;
        }

        /// <summary>
        /// Mean of the per-step log returns ÷ their sample standard deviation × √8760.
        /// 0 when there are fewer than two returns or the deviation is 0.
        /// </summary>
        public static double SharpeAnnualized(IReadOnlyList<double> curve)
        {
            if (curve == null || curve.Count < 3) return 0;
            int n = curve.Count - 1;
            double sum = 0;
            for (int i = 1; i <= n; i++) sum += Math.Log(curve[i] / curve[i - 1]);
            double mean = sum / n;
            double squares = 0;
            for (int i = 1; i <= n; i++)
            {
                double d = Math.Log(curve[i] / curve[i - 1]) - mean;
                squares += d * d;
            }
            double std = Math.Sqrt(squares / (n - 1));
            if (!(std > 0)) return 0;
            return mean / std * Math.Sqrt(StepsPerYear);
        }

        /// <summary>Mean of the curve, for turnover. 0 for an empty curve.</summary>
        public static double MeanEquity(IReadOnlyList<double> curve)
        {
            if (curve == null || curve.Count == 0) return 0;
            double sum = 0;
            for (int i = 0; i < curve.Count; i++) sum += curve[i];
            return sum / curve.Count;
        }

        /// <summary>
        /// Percentile with linear interpolation between the closest ranks
        /// (rank = p ÷ 100 × (n − 1), as numpy's default). NaN for no values.
        /// </summary>
        public static double Percentile(IEnumerable<double> values, double percent)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            if (!(percent >= 0 && percent <= 100)) throw new ArgumentOutOfRangeException(nameof(percent));
            var sorted = new List<double>(values);
            if (sorted.Count == 0) return double.NaN;
            sorted.Sort();
            double rank = percent / 100 * (sorted.Count - 1);
            int below = (int)Math.Floor(rank);
            int above = Math.Min(below + 1, sorted.Count - 1);
            return sorted[below] + (sorted[above] - sorted[below]) * (rank - below);
        }

        public static double Median(IEnumerable<double> values) => Percentile(values, 50);

        /// <summary>All metrics for a curve and the counters of the account that produced it.</summary>
        public static EpisodeMetrics Compute(IReadOnlyList<double> curve, int trades, int rejected,
            double turnoverNotional, double feesPaid, int holdingSteps, int steps)
        {
            if (curve == null) throw new ArgumentNullException(nameof(curve));
            double initial = curve.Count > 0 ? curve[0] : 0;
            double final = curve.Count > 0 ? curve[curve.Count - 1] : 0;
            double meanEquity = MeanEquity(curve);
            return new EpisodeMetrics(
                TotalReturn(curve),
                MaxDrawdown(curve),
                SharpeAnnualized(curve),
                trades,
                rejected,
                meanEquity > 0 ? turnoverNotional / meanEquity : 0,
                feesPaid,
                initial > 0 ? feesPaid / initial * 100 : 0,
                steps > 0 ? (double)holdingSteps / steps : 0,
                steps,
                initial,
                final);
        }

        /// <summary>Metrics of the episode an environment has just run.</summary>
        public static EpisodeMetrics From(TradingEnv env)
        {
            if (env == null) throw new ArgumentNullException(nameof(env));
            Account a = env.Account ?? throw new InvalidOperationException("The environment has not been reset.");
            return Compute(env.EquityCurve, a.Trades, a.Rejected, a.TurnoverNotional, a.FeesPaid, env.HoldingSteps, env.StepCount);
        }
    }
}
