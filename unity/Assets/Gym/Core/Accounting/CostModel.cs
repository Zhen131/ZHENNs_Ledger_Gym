using System;

namespace Gym.Core.Accounting
{
    /// <summary>What a trade costs: a proportional fee, a fixed fee per order and price slippage.</summary>
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

        /// <summary>Fraction of the order value charged as fee (0.001 = 0.1 %).</summary>
        public double FeeRate { get; }

        /// <summary>USDT charged on every filled order.</summary>
        public double FixedFee { get; }

        /// <summary>Fraction the fill price moves against the trader (0.0005 = 5 bp).</summary>
        public double Slippage { get; }

        public override string ToString() => $"fee={FeeRate}, fixed={FixedFee}, slippage={Slippage}";
    }
}
