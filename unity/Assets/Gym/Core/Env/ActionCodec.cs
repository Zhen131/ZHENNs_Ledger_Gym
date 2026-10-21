using System;
using System.Collections.Generic;
using Gym.Core.Accounting;

namespace Gym.Core.Env
{
    /// <summary>
    /// 下单模式的混合 action：一个离散选择（不动／买入／卖出），加上一个决定比例的连续数 a ∈ [−1, 1]；
    /// 还有 action mask。旋钮模式的 action 是 0 个连续数加一个「档位个数」档的离散分支（遮罩全开），见 <see cref="DialTarget"/>。
    /// </summary>
    public static class ActionCodec
    {
        /// <summary>下单模式离散分支的档数（不动、买、卖）。</summary>
        public const int BranchSize = 3;
        /// <summary>下单模式连续数的个数。</summary>
        public const int ContinuousSize = 1;
        /// <summary>旋钮模式连续数的个数。</summary>
        public const int DialContinuousSize = 0;

        /// <summary>这种模式的连续数个数：下单 1、旋钮 0。</summary>
        public static int ContinuousSizeFor(ActionMode mode) =>
            mode == ActionMode.Orders ? ContinuousSize
            : mode == ActionMode.Dial ? DialContinuousSize
            : throw new ArgumentOutOfRangeException(nameof(mode), (int)mode, "Not an action mode.");

        /// <summary>这种模式的离散分支：下单是一个 3 档分支；旋钮是一个 <paramref name="levelCount"/> 档分支。</summary>
        public static int[] BranchSizesFor(ActionMode mode, int levelCount)
        {
            if (mode == ActionMode.Orders) return new[] { BranchSize };
            if (mode != ActionMode.Dial) throw new ArgumentOutOfRangeException(nameof(mode), (int)mode, "Not an action mode.");
            if (levelCount < 2) throw new ArgumentOutOfRangeException(nameof(levelCount), levelCount, "A dial needs at least two levels.");
            return new[] { levelCount };
        }

        /// <summary>第 <paramref name="level"/> 档的目标仓位；越界时抛异常。</summary>
        public static double DialTarget(IReadOnlyList<double> levels, int level)
        {
            if (levels == null) throw new ArgumentNullException(nameof(levels));
            if (level < 0 || level >= levels.Count)
                throw new ArgumentOutOfRangeException(nameof(level), level, $"Must be in [0, {levels.Count - 1}].");
            return levels[level];
        }

        /// <summary>目标仓位正好是 <paramref name="target"/> 的那一档；没有时返回 −1。</summary>
        public static int DialLevelOf(IReadOnlyList<double> levels, double target)
        {
            if (levels == null) throw new ArgumentNullException(nameof(levels));
            for (int i = 0; i < levels.Count; i++)
                if (levels[i] == target) return i;
            return -1;
        }

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
