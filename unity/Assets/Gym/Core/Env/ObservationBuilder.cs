using System;
using Gym.Core.Accounting;
using Gym.Core.Market;

namespace Gym.Core.Env
{
    /// <summary>
    /// Agent 在决策下标 t 看到的 35 个数。只读到第 t 根 candle 为止（含第 t 根）。
    /// </summary>
    public static class ObservationBuilder
    {
        public const int Size = 35;
        public const int Lookback = 32;
        public const int StepsSinceTradeCap = 720;

        public const int PositionRatioIndex = 32;
        public const int UnrealizedReturnIndex = 33;
        public const int StepsSinceTradeIndex = 34;

        public static void Write(CandleSeries series, int decisionIndex, Account account, int stepsSinceTrade, float[] dst, int offset = 0)
        {
            if (series == null) throw new ArgumentNullException(nameof(series));
            if (account == null) throw new ArgumentNullException(nameof(account));
            if (dst == null) throw new ArgumentNullException(nameof(dst));
            if (decisionIndex < Lookback || decisionIndex >= series.Count)
                throw new ArgumentOutOfRangeException(nameof(decisionIndex), decisionIndex, $"Must be in [{Lookback}, {series.Count - 1}].");
            if (offset < 0 || dst.Length - offset < Size)
                throw new ArgumentException($"Need {Size} floats from offset {offset}.", nameof(dst));
            if (stepsSinceTrade < 0) throw new ArgumentOutOfRangeException(nameof(stepsSinceTrade));

            double now = series.CloseAt(decisionIndex);
            for (int i = 1; i <= Lookback; i++)
                dst[offset + i - 1] = (float)Math.Tanh(10 * (series.CloseAt(decisionIndex - i) / now - 1));
            dst[offset + PositionRatioIndex] = (float)account.PositionRatio(now);
            dst[offset + UnrealizedReturnIndex] = (float)Math.Tanh(10 * account.UnrealizedReturn(now));
            dst[offset + StepsSinceTradeIndex] = (float)Math.Min((double)stepsSinceTrade / StepsSinceTradeCap, 1.0);
        }
    }
}
