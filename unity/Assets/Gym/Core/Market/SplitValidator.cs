using System;

namespace Gym.Core
{
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
