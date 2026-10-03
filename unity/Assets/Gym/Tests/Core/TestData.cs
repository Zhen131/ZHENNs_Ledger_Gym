using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Gym.Core.Env;
using Gym.Core.Market;
using UnityEngine;

namespace Gym.Tests.Core
{
    /// <summary>共用的测试数据：仓库里提交的 BTCUSDT 数据，和几段小的合成序列。</summary>
    static class TestData
    {
        public static string DataDir => Path.Combine(Application.streamingAssetsPath, "Gym", "data");
        public static string BtcCsvPath => Path.Combine(DataDir, "BTCUSDT-1h.csv");
        public static string BtcManifestPath => Path.Combine(DataDir, "BTCUSDT-1h.manifest.json");

        /// <summary>2024-01-01T00:00:00Z.</summary>
        public const long SyntheticStartMs = 1704067200000;

        static CandleSeries btc;

        /// <summary>仓库里提交的 BTCUSDT 序列，每次跑测试只解析一次。</summary>
        public static CandleSeries Btc => btc ??= CandleSeries.Parse(File.ReadAllText(BtcCsvPath));

        public static long ManifestLong(string key)
        {
            Match m = Regex.Match(File.ReadAllText(BtcManifestPath), "\"" + key + "\"\\s*:\\s*(\\d+)");
            if (!m.Success) throw new InvalidOperationException($"{key} not found in manifest");
            return long.Parse(m.Groups[1].Value);
        }

        public static string ManifestString(string key)
        {
            Match m = Regex.Match(File.ReadAllText(BtcManifestPath), "\"" + key + "\"\\s*:\\s*\"([^\"]*)\"");
            if (!m.Success) throw new InvalidOperationException($"{key} not found in manifest");
            return m.Groups[1].Value;
        }

        /// <summary>小时序列，open 和 close 由传入的函数给出。</summary>
        public static CandleSeries Synthetic(int count, Func<int, double> close, Func<int, double> open = null)
        {
            var candles = new List<Candle>(count);
            for (int i = 0; i < count; i++)
            {
                double c = close(i);
                double o = open != null ? open(i) : c;
                candles.Add(new Candle(SyntheticStartMs + i * CandleSeries.HourMs, o, Math.Max(o, c), Math.Min(o, c), c, 1));
            }
            return CandleSeries.FromCandles(candles);
        }

        /// <summary>随机游走，每小时的步幅最多 ±<paramref name="maxMove"/>。</summary>
        public static CandleSeries RandomWalk(int count, int seed, double maxMove, double start = 100)
        {
            var random = new System.Random(seed);
            var closes = new double[count];
            double price = start;
            for (int i = 0; i < count; i++)
            {
                price *= 1 + (random.NextDouble() * 2 - 1) * maxMove;
                closes[i] = price;
            }
            return Synthetic(count, i => closes[i], i => i == 0 ? closes[0] : closes[i - 1]);
        }

        public static string CsvOf(IEnumerable<string> rows) =>
            CandleSeries.Header + "\n" + string.Join("\n", rows) + "\n";

        public static TradingEnv TrainEnv(int episodeLength = TradingEnv.TrainingEpisodeLength) =>
            TradingEnv.ForSegment(Btc, SymbolRules.BtcUsdt, DefaultSplits.Train, episodeLength: episodeLength);

        public static bool SameBits(double a, double b) =>
            BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b);

        public static bool SameBits(float a, float b) =>
            BitConverter.ToInt32(BitConverter.GetBytes(a), 0) == BitConverter.ToInt32(BitConverter.GetBytes(b), 0);
    }
}
