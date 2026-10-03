using System;
using Gym.Core.Accounting;

namespace Gym.Core.Env
{
    /// <summary>
    /// 混合 action：一个离散选择（不动／买入／卖出），加上一个决定比例的连续数 a ∈ [−1, 1]；
    /// 还有 action mask。
    /// </summary>
    public static class ActionCodec
    {
        public const int BranchSize = 3;
        public const int ContinuousSize = 1;

        /// <summary>Fraction = clamp((a + 1) ÷ 2, 0, 1)。NaN 按 0 算。</summary>
        public static double Fraction(float continuousAction)
        {
            double f = ((double)continuousAction + 1) / 2;
            if (double.IsNaN(f)) return 0;
            return Math.Max(0, Math.Min(1, f));
        }

        /// <summary>能得到 <paramref name="fraction"/> 的那个连续 action。</summary>
        public static float FromFraction(double fraction) => (float)(Math.Max(0, Math.Min(1, fraction)) * 2 - 1);

        public static bool HoldEnabled => true;

        /// <summary>当且仅当 Cash ≥ MinNotional × (1 + FeeRate) × (1 + Slippage) + FixedFee 时允许买入。</summary>
        public static bool BuyEnabled(Account account, double price)
        {
            CostModel cost = account.Cost;
            return account.Cash >= account.Rules.MinNotional * (1 + cost.FeeRate) * (1 + cost.Slippage) + cost.FixedFee;
        }

        /// <summary>当且仅当账户持有 coin，并且 quantity × P × (1 − Slippage) ≥ MinNotional 时允许卖出。</summary>
        public static bool SellEnabled(Account account, double price) =>
            account.CoinUnits > 0 &&
            account.Quantity * price * (1 - account.Cost.Slippage) >= account.Rules.MinNotional;

        public static bool IsEnabled(TradeAction action, Account account, double price)
        {
            switch (action)
            {
                case TradeAction.Hold: return HoldEnabled;
                case TradeAction.Buy: return BuyEnabled(account, price);
                case TradeAction.Sell: return SellEnabled(account, price);
                default: throw new ArgumentOutOfRangeException(nameof(action), (int)action, "Must be 0, 1 or 2.");
            }
        }
    }
}
