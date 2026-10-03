using System;
using System.Collections.Generic;
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

    /// <summary>The three segments of PRD D-14.</summary>
    public static class DefaultSplits
    {
        public static readonly SegmentSpec Train = SegmentSpec.Parse("train", "2017-08-17", "2024-08-31");
        public static readonly SegmentSpec Validation = SegmentSpec.Parse("validation", "2024-09-01", "2025-08-31");
        public static readonly SegmentSpec Test = SegmentSpec.Parse("test", "2025-09-01", "2026-08-31");
    }

    public sealed class SplitReport
    {
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Warnings = new List<string>();
        public bool IsValid => Errors.Count == 0;
    }

    public static class SplitValidator
    {
        public const string BackwardTestWarning = "backward test";

        public static SplitReport Validate(SegmentSpec train, SegmentSpec validation, SegmentSpec test,
            CandleSeries series, int episodeLength)
        {
            if (series == null) throw new ArgumentNullException(nameof(series));
            var report = new SplitReport();
            var segments = new[] { train, validation, test };

            for (int i = 0; i < segments.Length; i++)
                for (int j = i + 1; j < segments.Length; j++)
                    if (segments[i].Overlaps(segments[j]))
                        report.Errors.Add($"overlap: {segments[i]} and {segments[j]}");

            foreach (SegmentSpec segment in segments)
            {
                if (!segment.IsInside(series))
                    report.Errors.Add(
                        $"out of range: {segment} (data covers {series.OpenTimeUtc(0):yyyy-MM-dd HH:mm}" +
                        $"..{series.OpenTimeUtc(series.Count - 1):yyyy-MM-dd HH:mm} UTC)");
            }

            if (train.IsInside(series))
            {
                int decisions = TrainingDecisionBars(train, series);
                int needed = Math.Max(episodeLength, 1) + 1;
                if (decisions < needed)
                    report.Errors.Add($"too short: {train} has {decisions} decision bars, an episode needs {needed}");
            }

            if (test.StartDate < train.EndDate)
                report.Warnings.Add($"{BackwardTestWarning}: {test} starts before {train} ends");

            return report;
        }

        /// <summary>
        /// Bars from the first index that has a full look-back window
        /// (max(first, 32)) to the last bar, inclusive. An episode of L steps
        /// needs L + 1 of them.
        /// </summary>
        public static int TrainingDecisionBars(SegmentSpec segment, CandleSeries series)
        {
            int first = segment.FirstIndex(series);
            int last = segment.LastIndex(series);
            if (first < 0 || last < 0) return 0;
            return Math.Max(0, last - Math.Max(first, ObservationBuilder.Lookback) + 1);
        }
    }
}
