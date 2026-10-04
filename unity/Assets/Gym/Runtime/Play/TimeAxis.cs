using System;
using System.Collections.Generic;
using System.Globalization;

namespace Gym.Runtime.Play
{
    /// <summary>K 线图下面标哪些时间。纯函数，不碰画面。</summary>
    public static class TimeAxis
    {
        /// <summary>
        /// 开盘时间是 UTC 0 点的每根 candle 标「月-日」（如 "01-03"）。一根都没有时，在最旧的那根下面标
        /// 「月-日 时:分」，免得整条时间轴是空的（按现在 64 根小时 candle 的图表，画面上总有 0 点，这只是兜底）。
        /// </summary>
        /// <param name="openTimesUtc">可见 candle 的开盘时间，从旧到新。</param>
        public static List<TimeLabel> Pick(IReadOnlyList<DateTime> openTimesUtc)
        {
            var labels = new List<TimeLabel>();
            for (int i = 0; i < openTimesUtc.Count; i++)
            {
                DateTime t = openTimesUtc[i];
                if (t.TimeOfDay == TimeSpan.Zero) labels.Add(new TimeLabel(i, t.ToString("MM-dd", CultureInfo.InvariantCulture)));
            }
            if (labels.Count == 0 && openTimesUtc.Count > 0)
                labels.Add(new TimeLabel(0, openTimesUtc[0].ToString("MM-dd HH:mm", CultureInfo.InvariantCulture)));
            return labels;
        }
    }
}
