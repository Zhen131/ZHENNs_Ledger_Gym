using System;
using System.Collections.Generic;
using Gym.Core.Env;

namespace Gym.Core.Market
{
    public static class SplitValidator
    {
        public const string BackwardTestWarning = "backward test";

        /// <summary>只检查训练段、验证段和测试段。</summary>
        public static SplitReport Validate(SegmentSpec train, SegmentSpec validation, SegmentSpec test,
            CandleSeries series, int episodeLength) =>
            Validate(train, validation, test, Array.Empty<SegmentSpec>(), series, episodeLength);

        /// <summary>
        /// 连同额外的评估段一起检查：每一段都要落在数据里，任何两段（训练段、验证段、测试段和额外段之间，
        /// 以及额外段彼此之间）都不许重叠。
        /// </summary>
        public static SplitReport Validate(SegmentSpec train, SegmentSpec validation, SegmentSpec test,
            IReadOnlyList<SegmentSpec> extra, CandleSeries series, int episodeLength)
        {
            if (series == null) throw new ArgumentNullException(nameof(series));
            if (extra == null) throw new ArgumentNullException(nameof(extra));
            var report = new SplitReport();
            var segments = new List<SegmentSpec> { train, validation, test };
            segments.AddRange(extra);

            for (int i = 0; i < segments.Count; i++)
                for (int j = i + 1; j < segments.Count; j++)
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
        /// 从第一个有完整 lookback 窗口的下标（max(first, 32)）到最后一根 bar，一共几根 bar，首尾都算。
        /// 一个 L 个 step 的 episode 需要 L + 1 根。
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
