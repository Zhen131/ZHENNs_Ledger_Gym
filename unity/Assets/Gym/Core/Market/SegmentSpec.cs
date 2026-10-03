using System;
using System.Globalization;

namespace Gym.Core.Market
{
    /// <summary>
    /// 一段 UTC 日期范围，首尾两天都包括：从 <see cref="StartDate"/> 00:00 或之后的第一根 candle，
    /// 到 <see cref="EndDate"/> 23:00 或之前的最后一根。
    /// </summary>
    public readonly struct SegmentSpec
    {
        public readonly string Name;
        public readonly DateTime StartDate;
        public readonly DateTime EndDate;

        public SegmentSpec(string name, DateTime startDate, DateTime endDate)
        {
            DateTime start = DateTime.SpecifyKind(startDate.Date, DateTimeKind.Utc);
            DateTime end = DateTime.SpecifyKind(endDate.Date, DateTimeKind.Utc);
            if (end < start)
                throw new ArgumentException($"Segment '{name}' ends ({end:yyyy-MM-dd}) before it starts ({start:yyyy-MM-dd}).");
            Name = name;
            StartDate = start;
            EndDate = end;
        }

        public static SegmentSpec Parse(string name, string startDate, string endDate) =>
            new SegmentSpec(name, ParseDate(startDate), ParseDate(endDate));

        static DateTime ParseDate(string text) =>
            DateTime.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

        public bool Overlaps(SegmentSpec other) => StartDate <= other.EndDate && other.StartDate <= EndDate;

        public int FirstIndex(CandleSeries series) => series.FirstIndexOnOrAfter(StartDate);
        public int LastIndex(CandleSeries series) => series.LastIndexOnOrBefore(EndDate);

        /// <summary>两个日期都落在这段 candle 覆盖的日子里时为 true。</summary>
        public bool IsInside(CandleSeries series) =>
            StartDate >= series.OpenTimeUtc(0).Date && EndDate <= series.OpenTimeUtc(series.Count - 1).Date;

        public override string ToString() =>
            $"{Name} {StartDate:yyyy-MM-dd}..{EndDate:yyyy-MM-dd}";
    }
}
