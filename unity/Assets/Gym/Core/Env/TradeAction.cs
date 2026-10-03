namespace Gym.Core.Env
{
    /// <summary>
    /// The discrete part of an action. The numbers are the indices of ML-Agents'
    /// discrete branch, which only knows integers.
    /// </summary>
    public enum TradeAction
    {
        Hold = 0,
        Buy = 1,
        Sell = 2,
    }
}
