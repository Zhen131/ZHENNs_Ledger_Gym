using System;

namespace Gym.Core.Market
{
    /// <summary>
    /// 一个 symbol 的交易所规则。coin 数量总是存成整数个 <see cref="StepSize"/> 单位（<c>CoinUnits</c>），
    /// 所以数量正好是 units × StepSize。
    /// </summary>
    public sealed class SymbolRules
    {
        /// <summary>Binance 现货 BTCUSDT：最小订单 5 USDT，数量步长 0.00001 BTC。</summary>
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

        /// <summary>最小订单金额，单位 USDT。</summary>
        public double MinNotional { get; }

        /// <summary>coin 数量的最小增量。</summary>
        public decimal StepSize { get; }

        /// <summary>若干个 step 单位对应的 coin 数量。</summary>
        public double Quantity(long units) => (double)(units * StepSize);
    }
}
