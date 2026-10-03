namespace Gym.Core.Accounting
{
    /// <summary>账户记下的一笔已成交的订单。</summary>
    public readonly struct Fill
    {
        public readonly long Units;
        /// <summary>算上 slippage 之后的 fill 价格。</summary>
        public readonly double Price;
        /// <summary>订单金额 N = units × step × price，未扣 fee。</summary>
        public readonly double Notional;
        /// <summary>收取的 fee：F = N × FeeRate + FixedFee。</summary>
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
