using System;

namespace Gym.Core.Accounting
{
    /// <summary>一笔交易的成本：按比例收的 fee、每笔订单的固定 fee，以及价格 slippage。</summary>
    public sealed class CostModel
    {
        public const double DefaultFeeRate = 0.001;

        public CostModel(double feeRate = DefaultFeeRate, double fixedFee = 0, double slippage = 0)
        {
            if (!(feeRate >= 0 && feeRate < 1))
                throw new ArgumentOutOfRangeException(nameof(feeRate), feeRate, "Must be in [0, 1).");
            if (!(fixedFee >= 0) || double.IsInfinity(fixedFee))
                throw new ArgumentOutOfRangeException(nameof(fixedFee), fixedFee, "Must be a finite value >= 0.");
            if (!(slippage >= 0 && slippage < 0.1))
                throw new ArgumentOutOfRangeException(nameof(slippage), slippage, "Must be in [0, 0.1).");
            FeeRate = feeRate;
            FixedFee = fixedFee;
            Slippage = slippage;
        }

        /// <summary>订单金额里作为 fee 收取的比例（0.001 = 0.1 %）。</summary>
        public double FeeRate { get; }

        /// <summary>每笔成交的订单收取的 USDT。</summary>
        public double FixedFee { get; }

        /// <summary>fill 价格朝不利于交易者的方向偏移的比例（0.0005 = 5 bp）。</summary>
        public double Slippage { get; }

        public override string ToString() => $"fee={FeeRate}, fixed={FixedFee}, slippage={Slippage}";
    }
}
