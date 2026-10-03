using System.Collections.Generic;
using System.IO;
using Gym.Core.Market;

namespace Gym.Runtime.Configuration
{
    /// <summary>
    /// 解析好的 candle 文件，按完整路径存放。进程里的每个 Agent 共用同一份只读的 <see cref="CandleSeries"/>。
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
