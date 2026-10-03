using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Gym.Core.Accounting;
using Gym.Core.Env;
using Gym.Core.Evaluation;
using Gym.Core.Market;
using Gym.Runtime.Configuration;
using Gym.Runtime.Evaluation;
using UnityEditor;
using UnityEngine;

namespace Gym.Editor
{
    /// <summary>
    /// 从命令行跑对照组评估：
    ///
    ///   Unity -batchmode -nographics -projectPath "$PWD/unity" -executeMethod Gym.Editor.EvalTools.RunBaselines
    ///         -gymSegment test -gymFeeRates 0,0.001,0.003 -gymRandomSeeds 100 -gymOut "$PWD/evaluations"
    ///         [-gymPolicies buyhold,cash,random] -quit
    ///
    /// 对每一个费率，让选中的 policy（默认三个都跑：买入持有、一直拿现金，以及 seed 0 … n−1 上的
    /// 随机 policy，seed 经 SeedMixer 打散）在评估模式下走 TradingEnv；每个 policy 往 log.csv 追加一行，
    /// 并在 runs/ 下写一份明细 JSON。
    /// </summary>
    public static class EvalTools
    {
        public const string FeeRatesArg = "-gymFeeRates";
        public const string RandomSeedsArg = "-gymRandomSeeds";
        public const string OutArg = EvalRunner.OutArg;
        public const string PoliciesArg = "-gymPolicies";
        public const string BuyAndHoldPolicy = "buyhold";
        public const string CashPolicy = "cash";
        public const string RandomPolicy = "random";
        public static readonly string[] PolicyNames = { BuyAndHoldPolicy, CashPolicy, RandomPolicy };

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static void RunBaselines()
        {
            try
            {
                Run(Environment.GetCommandLineArgs());
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Gym] baselines failed: {e}");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
        }

        /// <summary>按给定的参数做 <see cref="RunBaselines"/> 的活；有任何问题就抛异常。返回评估流水的路径。</summary>
        public static string Run(string[] args)
        {
            string segmentName = CommandLineArgs.ValueOf(args, GymConfigLoader.SegmentArg) ?? SegmentNames.Test;
            double[] feeRates = ParseFeeRates(CommandLineArgs.ValueOf(args, FeeRatesArg) ?? "0,0.001,0.003");
            int seeds = int.Parse(CommandLineArgs.ValueOf(args, RandomSeedsArg) ?? "100", Inv);
            string outDir = Path.GetFullPath(CommandLineArgs.ValueOf(args, OutArg) ?? Path.Combine(RepositoryRoot(), "evaluations"));
            string configPath = CommandLineArgs.ValueOf(args, GymConfigLoader.ConfigArg) ?? GymConfigLoader.DefaultConfigPath;
            if (seeds < 1) throw new ArgumentOutOfRangeException(RandomSeedsArg, seeds, "Need at least one seed.");
            HashSet<string> policies = ParsePolicies(CommandLineArgs.ValueOf(args, PoliciesArg));

            GymSettings settings = GymConfigLoader.Load(configPath, GymConfigLoader.DefaultSymbolsPath,
                new[] { GymConfigLoader.ModeArg, "eval", GymConfigLoader.SegmentArg, segmentName });
            SegmentSpec segment = settings.EvalSegment;
            var env = TradingEnv.ForSegment(settings.Series, settings.Rules, segment, settings.Config.initialCash, 0, 0);
            JsonObject market = Market(env);
            Debug.Log($"[Gym] baselines on {segment}: {Json(market)}");

            foreach (double fee in feeRates)
            {
                var cost = new CostModel(fee, 0, 0);

                if (policies.Contains(BuyAndHoldPolicy))
                {
                    EpisodeMetrics hold = Baselines.RunBuyAndHold(env, cost);
                    WriteSingleRun(outDir, settings, cost, market, Baselines.BuyAndHoldName, hold);
                    Debug.Log($"[Gym] fee {fee}: buy_and_hold {hold.TotalReturn:P2}");
                }

                if (policies.Contains(CashPolicy))
                {
                    EpisodeMetrics cash = Baselines.RunCash(env, cost);
                    WriteSingleRun(outDir, settings, cost, market, Baselines.CashName, cash);
                    Debug.Log($"[Gym] fee {fee}: cash {cash.TotalReturn:P2}");
                }

                if (policies.Contains(RandomPolicy)) RunRandomPolicy(outDir, settings, env, market, cost, seeds);
            }

            string logPath = Path.Combine(outDir, EvaluationLog.FileName);
            Debug.Log($"[Gym] baselines written to {logPath}");
            return logPath;
        }

        /// <summary>-gymFeeRates：用逗号分隔的数字，按 invariant culture 解析。</summary>
        static double[] ParseFeeRates(string text) =>
            text.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => double.Parse(x.Trim(), NumberStyles.Float, Inv)).ToArray();

        /// <summary>seed 0 … n−1 上的随机 policy：评估流水里一行记中位数，明细文件里记每个 seed。</summary>
        static void RunRandomPolicy(string outDir, GymSettings settings, TradingEnv env, JsonObject market, CostModel cost, int seeds)
        {
            var runs = new List<(int seed, EpisodeMetrics metrics)>();
            for (int seed = 0; seed < seeds; seed++) runs.Add((seed, Baselines.RunRandom(env, cost, seed)));
            EpisodeMetrics median = MedianOf(runs.Select(r => r.metrics).ToList());
            double p5 = Metrics.Percentile(runs.Select(r => r.metrics.TotalReturn), 5);
            double p95 = Metrics.Percentile(runs.Select(r => r.metrics.TotalReturn), 95);
            string notes = $"seeds mixed; medians over seeds 0-{seeds - 1}; " +
                           $"total_return p5={EvaluationLog.Number(p5)} p95={EvaluationLog.Number(p95)}";
            WriteRandomRuns(outDir, settings, cost, market, median, runs, notes);
            Debug.Log($"[Gym] fee {cost.FeeRate}: random median {median.TotalReturn:P2} (p5 {p5:P2}, p95 {p95:P2})");
        }

        /// <summary>只运行一次的 policy（买入持有、一直拿现金）：写评估流水的一行，和一份带指标的明细文件。</summary>
        static void WriteSingleRun(string outDir, GymSettings settings, CostModel cost, JsonObject market,
            string policy, EpisodeMetrics metrics)
        {
            DateTime now = DateTime.UtcNow;
            EvaluationRecord record = BaselineRecord(now, settings, cost, policy, metrics, 1, "");
            JsonObject details = DetailsHead(now, record, settings, cost, market);
            details.Add("metrics", EvaluationLog.MetricsJson(metrics));
            Save(outDir, now, record, details);
        }

        /// <summary>随机 policy：评估流水那一行放中位数，明细文件放汇总和每个 seed 的指标。</summary>
        static void WriteRandomRuns(string outDir, GymSettings settings, CostModel cost, JsonObject market,
            EpisodeMetrics median, List<(int seed, EpisodeMetrics metrics)> perSeed, string notes)
        {
            DateTime now = DateTime.UtcNow;
            EvaluationRecord record = BaselineRecord(now, settings, cost, Baselines.RandomName, median, perSeed.Count, notes);
            // 整数计数的中位数可能带 .5；保留精确值。
            record.Trades = Metrics.Median(perSeed.Select(r => (double)r.metrics.Trades));
            record.Rejected = Metrics.Median(perSeed.Select(r => (double)r.metrics.Rejected));
            JsonObject details = DetailsHead(now, record, settings, cost, market);
            details.Add("summary", RandomSummary(record, perSeed));
            details.Add("per_seed", PerSeedMetrics(perSeed));
            Save(outDir, now, record, details);
        }

        static EvaluationRecord BaselineRecord(DateTime now, GymSettings settings, CostModel cost, string policy,
            EpisodeMetrics metrics, int seeds, string notes)
        {
            var record = new EvaluationRecord
            {
                TimestampUtc = now,
                Kind = EvaluationRecord.BaselineKind,
                Policy = policy,
                Symbol = settings.Rules.Symbol,
                Seeds = seeds,
                Notes = notes,
            };
            record.SetSegment(settings.EvalSegment);
            record.SetCost(cost);
            record.SetMetrics(metrics);
            return record;
        }

        /// <summary>每份对照组明细文件开头都有的那些键。</summary>
        static JsonObject DetailsHead(DateTime now, EvaluationRecord record, GymSettings settings, CostModel cost, JsonObject market) =>
            new JsonObject
            {
                { "timestamp_utc", now },
                { "kind", record.Kind },
                { "policy", record.Policy },
                { "generated_by", "Gym.Editor.EvalTools.RunBaselines" },
                { "symbol", settings.Rules.Symbol },
                { "data_file", RecordPaths.PathForRecords(settings.Config.dataFile) },
                { "segment", SegmentJson(settings.EvalSegment, settings.Series) },
                { "market", market },
                { "cost", new JsonObject { { "fee_rate", cost.FeeRate }, { "fixed_fee", cost.FixedFee }, { "slippage", cost.Slippage } } },
                { "initial_cash", settings.Config.initialCash },
                { "seeds", record.Seeds },
            };

        static JsonObject RandomSummary(EvaluationRecord record, List<(int seed, EpisodeMetrics metrics)> perSeed) =>
            new JsonObject
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
            };

        static List<JsonObject> PerSeedMetrics(List<(int seed, EpisodeMetrics metrics)> perSeed) =>
            perSeed.Select(r =>
            {
                JsonObject m = EvaluationLog.MetricsJson(r.metrics);
                var row = new JsonObject { { "seed", r.seed } };
                foreach (KeyValuePair<string, object> pair in m) row.Add(pair.Key, pair.Value);
                return row;
            }).ToList();

        static void Save(string outDir, DateTime now, EvaluationRecord record, JsonObject details)
        {
            EvaluationLog.Append(outDir, record);
            EvaluationLog.WriteRunDetails(outDir, now, record.Policy, record.Segment, details);
        }

        /// <summary>-gymPolicies：buyhold、cash、random 里用逗号分隔的一部分；没给时三个都要。</summary>
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

        /// <summary>每个指标的中位数；计数用取整后的中位数（精确的 .5 写在评估流水那一行里）。</summary>
        static EpisodeMetrics MedianOf(IReadOnlyList<EpisodeMetrics> runs)
        {
            double Median(Func<EpisodeMetrics, double> metric) => Metrics.Median(runs.Select(metric));
            return new EpisodeMetrics(
                Median(r => r.TotalReturn), Median(r => r.MaxDrawdown), Median(r => r.SharpeAnnualized),
                (int)Math.Round(Median(r => r.Trades)), (int)Math.Round(Median(r => r.Rejected)),
                Median(r => r.Turnover), Median(r => r.FeesPaid), Median(r => r.FeesPct), Median(r => r.Exposure),
                runs[0].Steps, Median(r => r.InitialEquity), Median(r => r.FinalEquity));
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

        /// <summary>分段的价格变化：从评估起点的 close 到最后一个 close。</summary>
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

        static string Json(JsonObject value) => JsonWriter.Serialize(value).Replace("\n", " ").Replace("  ", "");

        /// <summary>
        /// 仓库根目录：装着 Unity 工程的那个文件夹（Application.dataPath 是 &lt;project&gt;/Assets）。
        /// editor 在 Unity 工程里面运行，所以只写 "evaluations" 的话，会落到工程里，而不是仓库的 evaluations/ 文件夹。
        /// </summary>
        static string RepositoryRoot() => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
    }
}
