namespace Gym.Core
{
    public readonly struct TradeRecord
    {
        /// <summary>Episode step (0-based) in which the order was placed.</summary>
        public readonly int Step;
        /// <summary>Candle at whose open the order filled (t + 1).</summary>
        public readonly int CandleIndex;
        /// <summary><see cref="ActionCodec.Buy"/> or <see cref="ActionCodec.Sell"/>.</summary>
        public readonly int Side;
        public readonly double Fraction;
        public readonly long Units;
        /// <summary>Fill price after slippage.</summary>
        public readonly double Price;
        public readonly double Notional;
        public readonly double Fee;

        public TradeRecord(int step, int candleIndex, int side, double fraction, Fill fill)
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
