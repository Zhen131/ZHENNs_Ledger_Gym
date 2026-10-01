using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Gym.Core;
using Gym.Runtime;
using UnityEditor;
using UnityEngine;

namespace Gym.Editor
{
    /// <summary>
    /// Baseline evaluation from the command line (04B §4.1):
    ///
    ///   Unity -batchmode -nographics -projectPath . -executeMethod Gym.Editor.EvalTools.RunBaselines
    ///         -gymSegment test -gymFeeRates 0,0.001,0.003 -gymRandomSeeds 100 -gymOut evaluations
    ///         [-gymPolicies buyhold,cash,random] -quit
    ///
    /// For every fee rate it runs the chosen policies (default all three: buy-and-hold, cash
    /// and the random policy over seeds 0 … n−1, mixed by SeedMixer) through TradingEnv in
    /// evaluation mode, appends one row per policy to log.csv and writes a detail JSON per
    /// policy under runs/.
    /// </summary>
    public static class EvalTools
    {
        public const string FeeRatesArg = "-gymFeeRates";
        public const string RandomSeedsArg = "-gymRandomSeeds";
        public const string OutArg = "-gymOut";
        public const string PoliciesArg = "-gymPolicies";
        public static readonly string[] PolicyNames = { "buyhold", "cash", "random" };

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static void RunBaselines()
        {
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                string segmentName = GymConfigLoader.GetArg(args, GymConfigLoader.SegmentArg) ?? "test";
                double[] feeRates = (GymConfigLoader.GetArg(args, FeeRatesArg) ?? "0,0.001,0.003")
                    .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => double.Parse(x.Trim(), NumberStyles.Float, Inv)).ToArray();
                int seeds = int.Parse(GymConfigLoader.GetArg(args, RandomSeedsArg) ?? "100", Inv);
                string outDir = Path.GetFullPath(GymConfigLoader.GetArg(args, OutArg) ?? "evaluations");
                string configPath = GymConfigLoader.GetArg(args, GymConfigLoader.ConfigArg) ?? GymConfigLoader.DefaultConfigPath;
                if (seeds < 1) throw new ArgumentOutOfRangeException(RandomSeedsArg, seeds, "Need at least one seed.");
                HashSet<string> policies = ParsePolicies(GymConfigLoader.GetArg(args, PoliciesArg));

                GymSettings s = GymConfigLoader.Load(configPath, GymConfigLoader.DefaultSymbolsPath,
                    new[] { GymConfigLoader.ModeArg, "eval", GymConfigLoader.SegmentArg, segmentName });
                SegmentSpec segment = s.EvalSegment;
                var env = TradingEnv.ForSegment(s.Series, s.Rules, segment, s.Config.initialCash, 0, 0);
                JsonObject market = Market(env);
                Debug.Log($"[Gym] baselines on {segment}: {Json(market)}");

                foreach (double fee in feeRates)
                {
                    var cost = new CostModel(fee, 0, 0);

                    if (policies.Contains("buyhold"))
                    {
                        EpisodeMetrics hold = Baselines.RunBuyAndHold(env, cost);
                        Write(outDir, s, segment, cost, market, Baselines.BuyAndHoldName, hold, 1, "", null);
                        Debug.Log($"[Gym] fee {fee}: buy_and_hold {hold.TotalReturn:P2}");
                    }

                    if (policies.Contains("cash"))
                    {
                        EpisodeMetrics cash = Baselines.RunCash(env, cost);
                        Write(outDir, s, segment, cost, market, Baselines.CashName, cash, 1, "", null);
                        Debug.Log($"[Gym] fee {fee}: cash {cash.TotalReturn:P2}");
                    }

                    if (policies.Contains("random"))
                    {
                        var runs = new List<(int seed, EpisodeMetrics metrics)>();
                        for (int seed = 0; seed < seeds; seed++) runs.Add((seed, Baselines.RunRandom(env, cost, seed)));
                        EpisodeMetrics median = MedianOf(runs.Select(r => r.metrics).ToList());
                        double p5 = Metrics.Percentile(runs.Select(r => r.metrics.TotalReturn), 5);
                        double p95 = Metrics.Percentile(runs.Select(r => r.metrics.TotalReturn), 95);
                        string notes = $"seeds mixed (Q03); medians over seeds 0-{seeds - 1}; " +
                                       $"total_return p5={EvaluationLog.Number(p5)} p95={EvaluationLog.Number(p95)}";
                        Write(outDir, s, segment, cost, market, Baselines.RandomName, median, seeds, notes, runs);
                        Debug.Log($"[Gym] fee {fee}: random median {median.TotalReturn:P2} (p5 {p5:P2}, p95 {p95:P2})");
                    }
                }

                Debug.Log($"[Gym] baselines written to {Path.Combine(outDir, EvaluationLog.FileName)}");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Gym] baselines failed: {e}");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
        }

        static void Write(string outDir, GymSettings s, SegmentSpec segment, CostModel cost, JsonObject market,
            string policy, EpisodeMetrics metrics, int seeds, string notes, List<(int seed, EpisodeMetrics metrics)> perSeed)
        {
            DateTime now = DateTime.UtcNow;
            var record = new EvaluationRecord
            {
                TimestampUtc = now,
                Kind = EvaluationRecord.BaselineKind,
                Policy = policy,
                Symbol = s.Rules.Symbol,
                Seeds = seeds,
                Notes = notes,
            };
            record.SetSegment(segment);
            record.SetCost(cost);
            record.SetMetrics(metrics);
            if (perSeed != null)
            {
                // Medians of integer counts can be halves; keep them exact.
                record.Trades = Metrics.Median(perSeed.Select(r => (double)r.metrics.Trades));
                record.Rejected = Metrics.Median(perSeed.Select(r => (double)r.metrics.Rejected));
            }

            var details = new JsonObject
            {
                { "timestamp_utc", now },
                { "kind", record.Kind },
                { "policy", policy },
                { "generated_by", "Gym.Editor.EvalTools.RunBaselines" },
                { "symbol", s.Rules.Symbol },
                { "data_file", s.DataPath },
                { "segment", SegmentJson(segment, s.Series) },
                { "market", market },
                { "cost", new JsonObject { { "fee_rate", cost.FeeRate }, { "fixed_fee", cost.FixedFee }, { "slippage", cost.Slippage } } },
                { "initial_cash", s.Config.initialCash },
                { "seeds", seeds },
            };
            if (perSeed == null)
            {
                details.Add("metrics", EvaluationLog.MetricsJson(metrics));
            }
            else
            {
                details.Add("summary", new JsonObject
                {
                    { "aggregation", "median over seeds" },
                    { "total_return", record.TotalReturn },
                    { "max_drawdown", record.MaxDrawdown },
                    { "sharpe", record.Sharpe },
                    { "trades", record.Trades },
                    { "rejected", record.Rejected },
                    { "turnover", record.Turnover },
                    { "fees_paid", record.FeesPaid },
                    { "fees_pct", record.FeesPct },
                    { "exposure", record.Exposure },
                    { "total_return_p5", Metrics.Percentile(perSeed.Select(r => r.metrics.TotalReturn), 5) },
                    { "total_return_p95", Metrics.Percentile(perSeed.Select(r => r.metrics.TotalReturn), 95) },
                });
                details.Add("per_seed", perSeed.Select(r =>
                {
                    JsonObject m = EvaluationLog.MetricsJson(r.metrics);
                    var row = new JsonObject { { "seed", r.seed } };
                    foreach (KeyValuePair<string, object> pair in m) row.Add(pair.Key, pair.Value);
                    return row;
                }).ToList());
            }

            EvaluationLog.Append(outDir, record);
            EvaluationLog.WriteRunDetails(outDir, now, policy, segment.Name, details);
        }

        /// <summary>-gymPolicies: a comma-separated subset of buyhold, cash, random; all three when absent.</summary>
        public static HashSet<string> ParsePolicies(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return new HashSet<string>(PolicyNames);
            var chosen = new HashSet<string>();
            foreach (string part in text.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string name = part.Trim().ToLowerInvariant();
                if (!PolicyNames.Contains(name))
                    throw new ArgumentException($"{PoliciesArg}: unknown policy '{part.Trim()}'; use {string.Join(", ", PolicyNames)}");
                chosen.Add(name);
            }
            if (chosen.Count == 0) throw new ArgumentException($"{PoliciesArg} names no policy");
            return chosen;
        }

        /// <summary>Median of every metric; counts are rounded medians (exact halves go in the log row).</summary>
        static EpisodeMetrics MedianOf(IReadOnlyList<EpisodeMetrics> runs)
        {
            double M(Func<EpisodeMetrics, double> f) => Metrics.Median(runs.Select(f));
            return new EpisodeMetrics(
                M(r => r.TotalReturn), M(r => r.MaxDrawdown), M(r => r.SharpeAnnualized),
                (int)Math.Round(M(r => r.Trades)), (int)Math.Round(M(r => r.Rejected)),
                M(r => r.Turnover), M(r => r.FeesPaid), M(r => r.FeesPct), M(r => r.Exposure),
                runs[0].Steps, M(r => r.InitialEquity), M(r => r.FinalEquity));
        }

        public static JsonObject SegmentJson(SegmentSpec segment, CandleSeries series)
        {
            int first = segment.FirstIndex(series);
            int last = segment.LastIndex(series);
            int start = Math.Max(first, ObservationBuilder.Lookback);
            return new JsonObject
            {
                { "name", segment.Name },
                { "start_date", segment.StartDate.ToString("yyyy-MM-dd", Inv) },
                { "end_date", segment.EndDate.ToString("yyyy-MM-dd", Inv) },
                { "first_candle_utc", series.OpenTimeUtc(start) },
                { "last_candle_utc", series.OpenTimeUtc(last) },
                { "steps", last - start },
            };
        }

        /// <summary>The segment's price move: from the close the evaluation starts at to the last close.</summary>
        public static JsonObject Market(TradingEnv env)
        {
            int start = Math.Max(env.First, ObservationBuilder.Lookback);
            CandleSeries s = env.Series;
            double startClose = s.CloseAt(start), endClose = s.CloseAt(env.Last);
            return new JsonObject
            {
                { "start_candle_utc", s.OpenTimeUtc(start) },
                { "start_open", s.OpenAt(start) },
                { "start_close", startClose },
                { "end_candle_utc", s.OpenTimeUtc(env.Last) },
                { "end_close", endClose },
                { "change", endClose / startClose - 1 },
            };
        }

        static string Json(JsonObject o) => EvaluationLog.Json(o).Replace("\n", " ").Replace("  ", "");
    }
}
