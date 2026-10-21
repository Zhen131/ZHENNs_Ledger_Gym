using System;

namespace Gym.Core.Env
{
    /// <summary>
    /// 调仓规则：按目标仓位（币值 ÷ 总资产）定这一根 K 线下什么单。第二轮三条规则线用它（带宽 0.10），
    /// 旋钮模式的环境和两个对手也用它（带宽是参数）。算法就是原来 <c>Baselines.RuleOrder</c> 的那一条，一字不改。
    /// </summary>
    public static class Rebalancer
    {
        /// <summary>
        /// p = 币值 ÷ 总资产。目标 − p &gt; 带宽时买，比例（占现金）= min(1, (目标 − p) × 总资产 ÷ 现金)，现金不为正时比例为 1；
        /// p − 目标 &gt; 带宽时卖，比例（占持币）= min(1, (p − 目标) ÷ p)；其余不动（比例 0）。总资产不为正时不动。
        /// </summary>
        public static (TradeAction action, double fraction) Order(double target, double cash, double coinValue, double band)
        {
            double equity = cash + coinValue;
            if (!(equity > 0)) return (TradeAction.Hold, 0);
            double p = coinValue / equity;
            if (target - p > band)
                return (TradeAction.Buy, cash > 0 ? Math.Min(1, (target - p) * equity / cash) : 1);
            if (p - target > band)
                return (TradeAction.Sell, Math.Min(1, (p - target) / p));
            return (TradeAction.Hold, 0);
        }
    }
}
