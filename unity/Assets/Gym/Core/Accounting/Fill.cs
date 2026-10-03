namespace Gym.Core.Accounting
{
    /// <summary>A filled order as the account booked it.</summary>
    public readonly struct Fill
    {
        public readonly long Units;
        /// <summary>Fill price after slippage.</summary>
        public readonly double Price;
        /// <summary>Order value N = units × step × price, before fees.</summary>
        public readonly double Notional;
        /// <summary>Fee F = N × FeeRate + FixedFee.</summary>
        public readonly double Fee;

        public Fill(long units, double price, double notional, double fee)
        {
            Units = units;
            Price = price;
            Notional = notional;
            Fee = fee;
        }
    }
}
