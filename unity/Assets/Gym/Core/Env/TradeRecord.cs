using Gym.Core.Accounting;

namespace Gym.Core.Env
{
    public readonly struct TradeRecord
    {
        /// <summary>下单时是 episode 的第几个 step（从 0 开始数）。</summary>
        public readonly int Step;
        /// <summary>订单在哪一根 candle 的 open 成交（t + 1）。</summary>
        public readonly int CandleIndex;
        /// <summary><see cref="TradeAction.Buy"/> 或 <see cref="TradeAction.Sell"/>。</summary>
        public readonly TradeAction Side;
        public readonly double Fraction;
        public readonly long Units;
        /// <summary>算上 slippage 之后的 fill 价格。</summary>
        public readonly double Price;
        public readonly double Notional;
        public readonly double Fee;

        public TradeRecord(int step, int candleIndex, TradeAction side, double fraction, Fill fill)
        {
            Step = step;
            CandleIndex = candleIndex;
            Side = side;
            Fraction = fraction;
            Units = fill.Units;
            Price = fill.Price;
            Notional = fill.Notional;
            Fee = fill.Fee;
        }
    }
}
