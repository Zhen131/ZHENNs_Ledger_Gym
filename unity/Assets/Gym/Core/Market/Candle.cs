namespace Gym.Core.Market
{
    /// <summary>一根小时 candle。时间是从 Unix 纪元起算的毫秒数，UTC。</summary>
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
