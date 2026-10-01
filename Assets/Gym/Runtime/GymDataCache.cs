using System.Collections.Generic;
using System.IO;
using Gym.Core;

namespace Gym.Runtime
{
    /// <summary>
    /// Parsed candle files keyed by full path. Every agent in the process shares
    /// the same read-only <see cref="CandleSeries"/>.
    /// </summary>
    public static class GymDataCache
    {
        static readonly object Gate = new object();
        static readonly Dictionary<string, CandleSeries> Cache = new Dictionary<string, CandleSeries>();

        public static CandleSeries Get(string path)
        {
            string fullPath = Path.GetFullPath(path);
            lock (Gate)
            {
                if (Cache.TryGetValue(fullPath, out CandleSeries series)) return series;
                series = CandleSeries.Parse(File.ReadAllText(fullPath));
                Cache[fullPath] = series;
                return series;
            }
        }

        public static int Count
        {
            get { lock (Gate) return Cache.Count; }
        }
    }
}
