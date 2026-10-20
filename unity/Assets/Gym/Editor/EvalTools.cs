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
    ///         -gymSegment validation -gymFeeRates 0,0.001,0.003 -gymRandomSeeds 100 -gymOut "$PWD/evaluations"
    ///         [-gymSymbol ETHUSDT] [-gymObs long] [-gymPolicies buyhold,cash,random,momentum,sma,voltarget] -quit
    ///
    /// -gymSymbol 选 gym-config.json 的 symbols 里的一只币（不给时是主币），记录里的 symbol、data_file 跟着它。
    /// -gymObs 选观测集（对照组不看观测，只有起点跟着它的历史长度走：评估从 max(段首, 历史长度) 开始）。
    /// -gymSegment 必须给（gym-config.json 里的一个评估段）：没给时抛异常、什么都不写，免得误考测试段
    /// ——评估流水只追加，写进去就撤不回。
    /// 对每一个费率，让选中的 policy（默认六个都跑：买入持有、一直拿现金、seed 0 … n−1 上的随机 policy
    /// （seed 经 SeedMixer 打散），以及三条规则 momentum_90d、sma_cross、vol_target）在评估模式下走
    /// TradingEnv；每个 policy 往 log.csv 追加一行，并在 runs/ 下写一份明细 JSON。
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
        public const string MomentumPolicy = "momentum";
        public const string SmaCrossPolicy = "sma";
        public const string VolTargetPolicy = "voltarget";
        public static readonly string[] PolicyNames =
            { BuyAndHoldPolicy, CashPolicy, RandomPolicy, MomentumPolicy, SmaCrossPolicy, VolTargetPolicy };

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
            if (string.IsNullOrWhiteSpace(CommandLineArgs.ValueOf(args, GymConfigLoader.SegmentArg)))
                throw new ArgumentException($"{GymConfigLoader.SegmentArg} is required: give a segment name from gym-config.json " +
                                            "(for example validation). Nothing was written.");
            double[] feeRates = ParseFeeRates(CommandLineArgs.ValueOf(args, FeeRatesArg) ?? "0,0.001,0.003");
            int seeds = int.Parse(CommandLineArgs.ValueOf(args, RandomSeedsArg) ?? "100", Inv);
            string outDir = Path.GetFullPath(CommandLineArgs.ValueOf(args, OutArg) ?? Path.Combine(RepositoryRoot(), "evaluations"));
            string configPath = CommandLineArgs.ValueOf(args, GymConfigLoader.ConfigArg) ?? GymConfigLoader.DefaultConfigPath;
            if (seeds < 1) throw new ArgumentOutOfRangeException(RandomSeedsArg, seeds, "Need at least one seed.");
            HashSet<string> policies = ParsePolicies(CommandLineArgs.ValueOf(args, PoliciesArg));

            GymSettings settings = GymConfigLoader.Load(configPath, GymConfigLoader.DefaultSymbolsPath, LoaderArgs(args));
            foreach (string note in settings.Notes) Debug.Log("[Gym] " + note);
            SegmentSpec segment = settings.EvalSegment;
            var env = TradingEnv.ForSegment(settings.Series, settings.Rules, segment, settings.Config.initialCash, 0, 0,
                settings.ObservationSet);
            JsonObject market = Market(env);
            Debug.Log($"[Gym] baselines for {env.Rules.Symbol} ({settings.DataFile}) on {segment}: {Json(market)}");

            foreach (double fee in feeRates)
            {
                var cost = new CostModel(fee, 0, 0);

                if (policies.Contains(BuyAndHoldPolicy))
                {
                    EpisodeMetrics hold = Baselines.RunBuyAndHold(env, cost);
                    WriteSingleRun(outDir, settings, env, cost, market, Baselines.BuyAndHoldName, hold);
                    Debug.Log($"[Gym] fee {fee}: buy_and_hold {hold.TotalReturn:P2}");
                }

                if (policies.Contains(CashPolicy))
                {
                    EpisodeMetrics cash = Baselines.RunCash(env, cost);
                    WriteSingleRun(outDir, settings, env, cost, market, Baselines.CashName, cash);
                    Debug.Log($"[Gym] fee {fee}: cash {cash.TotalReturn:P2}");
                }

                if (policies.Contains(RandomPolicy)) RunRandomPolicy(outDir, settings, env, market, cost, seeds);

                if (policies.Contains(MomentumPolicy))
                    RunRulePolicy(outDir, settings, env, market, cost, Baselines.MomentumName, Baselines.RunMomentum(env, cost));
                if (policies.Contains(SmaCrossPolicy))
                    RunRulePolicy(outDir, settings, env, market, cost, Baselines.SmaCrossName, Baselines.RunSmaCross(env, cost));
                if (policies.Contains(VolTargetPolicy))
                    RunRulePolicy(outDir, settings, env, market, cost, Baselines.VolTargetName, Baselines.RunVolTarget(env, cost));
            }

            string logPath = Path.Combine(outDir, EvaluationLog.FileName);
            Debug.Log($"[Gym] baselines written to {logPath}");
            return logPath;
        }

        /// <summary>
        /// 交给 <see cref="GymConfigLoader.Load"/> 的参数：命令行里每一个 -gym* 参数连同它后面的值原样转交
        /// （以后加的 -gymSymbol 之类也就跟着到了），只有 -gymMode 强制成 eval。别的参数（Unity 自己的）不转交；
        /// Load 不认识的 -gym* 参数（-gymFeeRates、-gymOut 等）它照旧不管。对每一个 -gym* 名字，
        /// CommandLineArgs.ValueOf 在转交后的参数里读到的值和在原命令行里读到的一样。
        /// </summary>
        public static string[] LoaderArgs(string[] args)
        {
            var keep = new bool[args.Length];
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (!args[i].StartsWith("-gym", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(args[i], GymConfigLoader.ModeArg, StringComparison.OrdinalIgnoreCase)) continue;
                keep[i] = keep[i + 1] = true;
            }
            var forwarded = new List<string> { GymConfigLoader.ModeArg, "eval" };
            for (int i = 0; i < args.Length; i++)
                if (keep[i]) forwarded.Add(args[i]);
            return forwarded.ToArray();
        }

        /// <summary>-gymFeeRates：用逗号分隔的数字，按 invariant culture 解析。</summary>
        static double[] ParseFeeRates(string text) =>
            text.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => double.Parse(x.Trim(), NumberStyles.Float, Inv)).ToArray();

        /// <summary>一条规则：和买入持有一样只跑一次，写一行和一份明细文件。</summary>
        static void RunRulePolicy(string outDir, GymSettings settings, TradingEnv env, JsonObject market, CostModel cost,
            string policy, EpisodeMetrics metrics)
        {
            WriteSingleRun(outDir, settings, env, cost, market, policy, metrics);
            Debug.Log($"[Gym] fee {cost.FeeRate}: {policy} {metrics.TotalReturn:P2} ({metrics.Trades} trades)");
        }

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
            WriteRandomRuns(outDir, settings, env, cost, market, median, runs, notes);
            Debug.Log($"[Gym] fee {cost.FeeRate}: random median {median.TotalReturn:P2} (p5 {p5:P2}, p95 {p95:P2})");
        }

        /// <summary>只运行一次的 policy（买入持有、一直拿现金、三条规则）：写评估流水的一行，和一份带指标的明细文件。</summary>
        static void WriteSingleRun(string outDir, GymSettings settings, TradingEnv env, CostModel cost, JsonObject market,
            string policy, EpisodeMetrics metrics)
        {
            DateTime now = DateTime.UtcNow;
            EvaluationRecord record = BaselineRecord(now, settings, env, cost, policy, metrics, 1, "");
            JsonObject details = DetailsHead(now, record, settings, env, cost, market);
            details.Add("metrics", EvaluationLog.MetricsJson(metrics));
            Save(outDir, now, record, details);
        }

        /// <summary>随机 policy：评估流水那一行放中位数，明细文件放汇总和每个 seed 的指标。</summary>
        static void WriteRandomRuns(string outDir, GymSettings settings, TradingEnv env, CostModel cost, JsonObject market,
            EpisodeMetrics median, List<(int seed, EpisodeMetrics metrics)> perSeed, string notes)
        {
            DateTime now = DateTime.UtcNow;
            EvaluationRecord record = BaselineRecord(now, settings, env, cost, Baselines.RandomName, median, perSeed.Count, notes);
            // 整数计数的中位数可能带 .5；保留精确值。
            record.Trades = Metrics.Median(perSeed.Select(r => (double)r.metrics.Trades));
            record.Rejected = Metrics.Median(perSeed.Select(r => (double)r.metrics.Rejected));
            JsonObject details = DetailsHead(now, record, settings, env, cost, market);
            details.Add("summary", RandomSummary(record, perSeed));
            details.Add("per_seed", PerSeedMetrics(perSeed));
            Save(outDir, now, record, details);
        }

        /// <summary>对照组的一行。symbol 读环境的币（基准工具只建当前币一个环境；读环境是防将来混用）。</summary>
        static EvaluationRecord BaselineRecord(DateTime now, GymSettings settings, TradingEnv env, CostModel cost, string policy,
            EpisodeMetrics metrics, int seeds, string notes)
        {
            var record = new EvaluationRecord
            {
                TimestampUtc = now,
                Kind = EvaluationRecord.BaselineKind,
                Policy = policy,
                Symbol = env.Rules.Symbol,
                Seeds = seeds,
                Notes = notes,
            };
            record.SetSegment(settings.EvalSegment);
            record.SetCost(cost);
            record.SetMetrics(metrics);
            return record;
        }

        /// <summary>每份对照组明细文件开头都有的那些键。</summary>
        static JsonObject DetailsHead(DateTime now, EvaluationRecord record, GymSettings settings, TradingEnv env, CostModel cost,
            JsonObject market) =>
            new JsonObject
            {
                { "timestamp_utc", now },
                { "kind", record.Kind },
                { "policy", record.Policy },
                { "generated_by", "Gym.Editor.EvalTools.RunBaselines" },
                { "symbol", env.Rules.Symbol },
                { "data_file", RecordPaths.PathForRecords(settings.DataFile) }, // 当前币的数据文件
                { "segment", SegmentJson(settings.EvalSegment, settings.Series, settings.ObservationSet.HistoryLength()) },
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

        /// <summary>-gymPolicies：buyhold、cash、random、momentum、sma、voltarget 里用逗号分隔的一部分；没给时六个都要。</summary>
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

        /// <summary>分段的明细：评估从 max(段首, <paramref name="historyLength"/>) 开始（base 的历史长度是 32）。</summary>
        public static JsonObject SegmentJson(SegmentSpec segment, CandleSeries series, int historyLength = ObservationBuilder.Lookback)
        {
            int first = segment.FirstIndex(series);
            int last = segment.LastIndex(series);
            int start = Math.Max(first, historyLength);
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

        /// <summary>分段的价格变化：从评估起点（max(段首, 观测集的历史长度)）的 close 到最后一个 close。</summary>
        public static JsonObject Market(TradingEnv env)
        {
            int start = env.FirstDecisionIndex;
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
