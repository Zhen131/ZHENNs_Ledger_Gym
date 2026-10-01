namespace Gym.Core
{
    /// <summary>One hourly candle. Times are milliseconds since the Unix epoch, UTC.</summary>
    public readonly struct Candle
    {
        public readonly long OpenTimeMs;
        public readonly double Open;
        public readonly double High;
        public readonly double Low;
        public readonly double Close;
        public readonly double Volume;

        public Candle(long openTimeMs, double open, double high, double low, double close, double volume)
        {
            OpenTimeMs = openTimeMs;
            Open = open;
            High = high;
            Low = low;
            Close = close;
            Volume = volume;
        }
    }
}
