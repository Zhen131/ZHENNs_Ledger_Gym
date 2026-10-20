using Gym.Core.Accounting;
using Gym.Core.Env;

namespace Gym.Runtime.Agents
{
    /// <summary>episode 结束时发给 TensorBoard 的数字，放在 Trading/… 下面。</summary>
    public readonly struct EpisodeStats
    {
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

        EpisodeStats(double ret, int trades, int rejected, double feesPaidPct, double exposure, double turnover,
            int rewardClips, double feeRate, double fixedFee, int steps, double riskAversion, double riskPenalty)
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
                env.RiskPenaltySum);
        }
    }
}
