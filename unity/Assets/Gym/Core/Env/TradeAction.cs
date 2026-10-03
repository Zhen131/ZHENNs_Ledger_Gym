namespace Gym.Core.Env
{
    /// <summary>
    /// action 的离散部分。这些数值就是 ML-Agents 离散 branch 的下标，那个 branch 只认整数。
    /// </summary>
    public enum TradeAction
    {
        Hold = 0,
        Buy = 1,
        Sell = 2,
    }
}
