using System;
using System.Collections.Generic;
using Gym.Core.Accounting;
using Gym.Core.Env;

namespace Gym.Runtime.Agents
{
    /// <summary>episode 结束时发给 TensorBoard 的数字，放在 Trading/… 下面。</summary>
    public readonly struct EpisodeStats
    {
        /// <summary>每只币占比曲线的前缀：Trading/SymbolShare/&lt;币名&gt;。</summary>
        public const string SymbolSharePrefix = "Trading/SymbolShare/";
        /// <summary>TensorBoard 键：本局换档次数（下单模式下为 0）。</summary>
        public const string DialChangesKey = "Trading/DialChanges";
        /// <summary>TensorBoard 键：本局决策次数（下单模式下等于步数）。</summary>
        public const string DecisionsKey = "Trading/Decisions";

        /// <summary>Final equity ÷ initial equity − 1.</summary>
        public readonly double Return;
        public readonly int Trades;
        public readonly int Rejected;
        /// <summary>Fees paid ÷ initial equity × 100.</summary>
        public readonly double FeesPaidPct;
        /// <summary>走完时仍持有 coin 的 step 所占的比例。</summary>
        public readonly double Exposure;
        /// <summary>Traded notional ÷ initial equity.</summary>
        public readonly double Turnover;
        public readonly int RewardClips;
        public readonly double FeeRate;
        public readonly double FixedFee;
        public readonly int Steps;
        /// <summary>本局的风险系数 λ。</summary>
        public readonly double RiskAversion;
        /// <summary>本局罚款累计：Σ λ × min(r, 0)²（λ = 0 时为 0）。</summary>
        public readonly double RiskPenalty;
        /// <summary>本局换档次数：这一步的目标和上一步不同的次数，第一步不算；下单模式下为 0。</summary>
        public readonly int DialChanges;
        /// <summary>本局决策次数：旋钮模式是开口的次数，下单模式等于步数。</summary>
        public readonly int Decisions;

        EpisodeStats(double ret, int trades, int rejected, double feesPaidPct, double exposure, double turnover,
            int rewardClips, double feeRate, double fixedFee, int steps, double riskAversion, double riskPenalty,
            int dialChanges, int decisions)
        {
            Return = ret;
            Trades = trades;
            Rejected = rejected;
            FeesPaidPct = feesPaidPct;
            Exposure = exposure;
            Turnover = turnover;
            RewardClips = rewardClips;
            FeeRate = feeRate;
            FixedFee = fixedFee;
            Steps = steps;
            RiskAversion = riskAversion;
            RiskPenalty = riskPenalty;
            DialChanges = dialChanges;
            Decisions = decisions;
        }

        /// <summary>
        /// 一局结束时每只币各记一条：键是 <see cref="SymbolSharePrefix"/> 加币名，本局用的是这只记 1，不是记 0
        /// （按平均聚合，曲线就是这段时间里各币的局数占比）。按 <paramref name="symbols"/> 的顺序，比较币名时分大小写。
        /// </summary>
        public static List<(string key, float value)> SymbolShares(IReadOnlyList<string> symbols, string current)
        {
            if (symbols == null) throw new ArgumentNullException(nameof(symbols));
            var shares = new List<(string, float)>(symbols.Count);
            foreach (string symbol in symbols)
                shares.Add((SymbolSharePrefix + symbol, symbol == current ? 1f : 0f));
            return shares;
        }

        public static EpisodeStats From(TradingEnv env)
        {
            double initial = env.EquityCurve[0];
            double final = env.EquityCurve[env.EquityCurve.Count - 1];
            Account a = env.Account;
            return new EpisodeStats(
                final / initial - 1,
                a.Trades,
                a.Rejected,
                a.FeesPaid / initial * 100,
                env.StepCount > 0 ? (double)env.HoldingSteps / env.StepCount : 0,
                a.TurnoverNotional / initial,
                env.ClippedRewards,
                env.Cost.FeeRate,
                env.Cost.FixedFee,
                env.StepCount,
                env.RiskAversion,
                env.RiskPenaltySum,
                env.DialChanges,
                env.Decisions);
        }
    }
}
