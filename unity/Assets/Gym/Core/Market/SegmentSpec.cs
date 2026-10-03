using System;
using System.Globalization;

namespace Gym.Core
{
    /// <summary>
    /// A date range in UTC, both days included: from the first candle at or
    /// after 00:00 of <see cref="StartDate"/> to the last one at or before
    /// 23:00 of <see cref="EndDate"/>.
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

        /// <summary>True when both dates fall inside the days the series covers.</summary>
        public bool IsInside(CandleSeries series) =>
            StartDate >= series.OpenTimeUtc(0).Date && EndDate <= series.OpenTimeUtc(series.Count - 1).Date;

        public override string ToString() =>
            $"{Name} {StartDate:yyyy-MM-dd}..{EndDate:yyyy-MM-dd}";
    }
}
