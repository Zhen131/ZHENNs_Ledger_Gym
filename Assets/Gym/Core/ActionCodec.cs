using System;

namespace Gym.Core
{
    /// <summary>
    /// The hybrid action: a discrete choice (hold / buy / sell) plus one
    /// continuous number a ∈ [−1, 1] that sets the fraction, and the action mask.
    /// </summary>
    public static class ActionCodec
    {
        public const int Hold = 0;
        public const int Buy = 1;
        public const int Sell = 2;
        public const int BranchSize = 3;
        public const int ContinuousSize = 1;

        /// <summary>Fraction = clamp((a + 1) ÷ 2, 0, 1). NaN counts as 0.</summary>
        public static double Fraction(float a)
        {
            double f = ((double)a + 1) / 2;
            if (double.IsNaN(f)) return 0;
            return Math.Max(0, Math.Min(1, f));
        }

        /// <summary>The continuous action that produces <paramref name="fraction"/>.</summary>
        public static float FromFraction(double fraction) => (float)(Math.Max(0, Math.Min(1, fraction)) * 2 - 1);

        public static bool HoldEnabled => true;

        /// <summary>Buy is allowed iff Cash ≥ MinNotional × (1 + FeeRate) × (1 + Slippage) + FixedFee.</summary>
        public static bool BuyEnabled(Account account, double price)
        {
            CostModel cost = account.Cost;
            return account.Cash >= account.Rules.MinNotional * (1 + cost.FeeRate) * (1 + cost.Slippage) + cost.FixedFee;
        }

        /// <summary>Sell is allowed iff the account holds coin and quantity × P × (1 − Slippage) ≥ MinNotional.</summary>
        public static bool SellEnabled(Account account, double price) =>
            account.CoinUnits > 0 &&
            account.Quantity * price * (1 - account.Cost.Slippage) >= account.Rules.MinNotional;

        public static bool IsEnabled(int branch, Account account, double price)
        {
            switch (branch)
            {
                case Hold: return HoldEnabled;
                case Buy: return BuyEnabled(account, price);
                case Sell: return SellEnabled(account, price);
                default: throw new ArgumentOutOfRangeException(nameof(branch), branch, "Must be 0, 1 or 2.");
            }
        }
    }
}
