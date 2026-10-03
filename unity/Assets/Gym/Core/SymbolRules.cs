using System;

namespace Gym.Core
{
    /// <summary>
    /// Exchange rules for one symbol. Coin amounts are always stored as a whole
    /// number of <see cref="StepSize"/> units (<c>CoinUnits</c>), so the
    /// quantity is exactly units × StepSize.
    /// </summary>
    public sealed class SymbolRules
    {
        /// <summary>Binance spot BTCUSDT: 5 USDT minimum order, 0.00001 BTC step.</summary>
        public static readonly SymbolRules BtcUsdt = new SymbolRules("BTCUSDT", 5.0, 0.00001m);

        public SymbolRules(string symbol, double minNotional, decimal stepSize)
        {
            if (string.IsNullOrEmpty(symbol)) throw new ArgumentException("Symbol is required.", nameof(symbol));
            if (!(minNotional >= 0) || double.IsInfinity(minNotional))
                throw new ArgumentOutOfRangeException(nameof(minNotional), minNotional, "Must be >= 0.");
            if (stepSize <= 0) throw new ArgumentOutOfRangeException(nameof(stepSize), stepSize, "Must be > 0.");
            Symbol = symbol;
            MinNotional = minNotional;
            StepSize = stepSize;
        }

        public string Symbol { get; }

        /// <summary>Smallest order value in USDT.</summary>
        public double MinNotional { get; }

        /// <summary>Smallest coin increment.</summary>
        public decimal StepSize { get; }

        /// <summary>Coin quantity for a number of step units.</summary>
        public double Quantity(long units) => (double)(units * StepSize);
    }
}
