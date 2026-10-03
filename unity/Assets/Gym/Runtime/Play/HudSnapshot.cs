using System;

namespace Gym.Runtime
{
    /// <summary>The numbers the HUD shows, captured right after each step.</summary>
    public struct HudSnapshot
    {
        public bool Ready;
        public DateTime TimeUtc;
        public double Close;
        public double Cash;
        public long CoinUnits;
        public double Quantity;
        public double PositionRatio;
        public double Equity;
        public double Return;
        public double FeesPaid;
        public int Trades;
        public int Rejected;
        public string LastAction;
        public double FeeRate;
        public double FixedFee;
        public double Slippage;
        public int Step;
    }
}
