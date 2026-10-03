using System;
using System.Collections.Generic;
using Gym.Core.Accounting;
using Gym.Core.Env;

namespace Gym.Core.Evaluation
{
    /// <summary>
    /// 根据 equity 曲线（起点加上每个 step 一个值，都取 close 时的值）和账户计数器算出的指标。
    /// </summary>
    public static class Metrics
    {
        /// <summary>每小时一根 candle：一年 24 × 365 个 step。</summary>
        public const double StepsPerYear = 8760;

        /// <summary>Final ÷ initial − 1。曲线为空时为 0。</summary>
        public static double TotalReturn(IReadOnlyList<double> curve)
        {
            if (curve == null || curve.Count == 0) return 0;
            return curve[curve.Count - 1] / curve[0] - 1;
        }

        /// <summary>从任一高点到之后低点的最大跌幅，表示成 [0, 1] 里的正比例。</summary>
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
        /// 每个 step 的对数收益率的均值 ÷ 它们的样本标准差 × √8760。
        /// 收益率少于两个，或者标准差为 0 时，结果为 0。
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

        /// <summary>曲线的均值，算 turnover 用。曲线为空时为 0。</summary>
        public static double MeanEquity(IReadOnlyList<double> curve)
        {
            if (curve == null || curve.Count == 0) return 0;
            double sum = 0;
            for (int i = 0; i < curve.Count; i++) sum += curve[i];
            return sum / curve.Count;
        }

        /// <summary>
        /// 百分位数，在最近的两个名次之间线性插值（rank = p ÷ 100 × (n − 1)，和 numpy 的默认做法一样）。
        /// 没有值时为 NaN。
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

        /// <summary>一条曲线，加上产生它的账户的计数器，对应的全部指标。</summary>
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

        /// <summary>环境刚运行完的那个 episode 的指标。</summary>
        public static EpisodeMetrics From(TradingEnv env)
        {
            if (env == null) throw new ArgumentNullException(nameof(env));
            Account a = env.Account ?? throw new InvalidOperationException("The environment has not been reset.");
            return Compute(env.EquityCurve, a.Trades, a.Rejected, a.TurnoverNotional, a.FeesPaid, env.HoldingSteps, env.StepCount);
        }
    }
}
