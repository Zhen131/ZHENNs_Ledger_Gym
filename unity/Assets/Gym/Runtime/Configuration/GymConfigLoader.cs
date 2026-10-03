using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Gym.Core.Accounting;
using Gym.Core.Env;
using Gym.Core.Market;
using UnityEngine;

namespace Gym.Runtime.Configuration
{
    /// <summary>
    /// 从 StreamingAssets/Gym（或者 -gymConfig 给的文件）读 gym-config.json 和 symbols.json，
    /// 载入数据，并检查分段的划分。
    /// </summary>
    public static class GymConfigLoader
    {
        public const string ConfigArg = "-gymConfig";
        public const string ModeArg = "-gymMode";
        public const string SegmentArg = "-gymSegment";
        public const string FeeRateArg = "-gymFeeRate";
        public const string FixedFeeArg = "-gymFixedFee";
        public const string SlippageArg = "-gymSlippage";

        /// <summary>
        /// 能接受的最大 initialCash。从大约 1e12 起，double 能分辨的最小金额差达到 0.0001 USDT 甚至更大，
        /// 远大于全仓买入的 1e-9 容差，所以给买入数量取整时可能超出现金，买入就会抛异常。1e9 留足了余地。
        /// </summary>
        public const double MaxInitialCash = 1e9;

        static readonly object Gate = new object();
        static GymSettings runtimeSettings;
        static string runtimeKey;

        public static string DefaultDirectory => Path.Combine(Application.streamingAssetsPath, "Gym");
        public static string DefaultConfigPath => Path.Combine(DefaultDirectory, "gym-config.json");
        public static string DefaultSymbolsPath => Path.Combine(DefaultDirectory, "symbols.json");

        /// <summary>
        /// 本进程的设置：按命令行载入一次，所有 Agent 共用。出错时：在 player 里把错误写进日志，
        /// 程序以退出码 1 退出；在 editor 里抛出异常。
        /// </summary>
        public static GymSettings LoadForRuntime()
        {
            string[] args = Environment.GetCommandLineArgs();
            string configPath = CommandLineArgs.ValueOf(args, ConfigArg) ?? DefaultConfigPath;
            string key = string.Join("\u0001", args);
            lock (Gate)
            {
                if (runtimeSettings != null && runtimeKey == key) return runtimeSettings;
                try
                {
                    GymSettings settings = Load(configPath, DefaultSymbolsPath, args);
                    foreach (string warning in settings.Warnings) Debug.LogWarning("[Gym] " + warning);
                    Debug.Log($"[Gym] config {settings.ConfigPath}, data {settings.DataPath} ({settings.Series.Count} candles), mode {settings.Mode}");
                    runtimeSettings = settings;
                    runtimeKey = key;
                    return settings;
                }
                catch (GymConfigException e)
                {
                    Debug.LogError("[Gym] " + e.Message);
                    if (!Application.isEditor) Application.Quit(1);
                    throw;
                }
            }
        }

        /// <summary>载入并检查。有问题时抛出 <see cref="GymConfigException"/>，列出找到的每一个问题。</summary>
        public static GymSettings Load(string configPath, string symbolsPath, string[] args = null)
        {
            var errors = new List<string>();
            var warnings = new List<string>();
            var settings = new GymSettings
            {
                ConfigPath = Path.GetFullPath(configPath),
                SymbolsPath = Path.GetFullPath(symbolsPath),
            };

            GymConfig config = ReadJson<GymConfig>(settings.ConfigPath, "config", errors);
            SymbolTable table = ReadJson<SymbolTable>(settings.SymbolsPath, "symbols", errors);
            if (config == null || table == null) throw new GymConfigException(errors);
            settings.Config = config;

            args = args ?? Array.Empty<string>();
            settings.Mode = ReadMode(args, errors, out EvaluationSegment evalSegment);
            settings.FeeRateArg = ReadCostArg(args, FeeRateArg, errors, v => new CostModel(v, 0, 0));
            settings.FixedFeeArg = ReadCostArg(args, FixedFeeArg, errors, v => new CostModel(0, v, 0));
            settings.SlippageArg = ReadCostArg(args, SlippageArg, errors, v => new CostModel(0, 0, v));
            settings.Rules = FindSymbol(config.symbol, table, settings.SymbolsPath, errors);
            CheckNumbers(config, errors);
            bool datesOk = ReadDates(config, settings, errors);
            LoadData(config, settings, errors);

            if (settings.Series != null && datesOk)
            {
                ValidateSplit(config, settings, errors, warnings);
                settings.EvalSegment = evalSegment == EvaluationSegment.Validation ? settings.Validation : settings.Test;
                CheckPlayStart(config, settings, errors);
            }

            if (errors.Count > 0) throw new GymConfigException(errors);
            settings.Warnings = warnings;
            return settings;
        }

        static void CheckNumbers(GymConfig config, List<string> errors)
        {
            if (!(config.initialCash > 0)) errors.Add($"initialCash must be > 0 (got {config.initialCash})");
            else if (config.initialCash > MaxInitialCash)
                errors.Add($"initialCash must be <= {MaxInitialCash:0} (got {config.initialCash}): with more cash a double " +
                           "is not precise enough to round the quantity of an all-in buy correctly");
            if (config.episodeLength < 0) errors.Add($"episodeLength must be >= 0 (got {config.episodeLength})");
            if (!(config.randomInitialPositionShare >= 0 && config.randomInitialPositionShare <= 1))
                errors.Add($"randomInitialPositionShare must be in [0, 1] (got {config.randomInitialPositionShare})");
        }

        /// <summary>读三个分段和 playStart。任何一个缺失或格式不对时返回 false；但每一个都照样检查。</summary>
        static bool ReadDates(GymConfig config, GymSettings settings, List<string> errors) =>
            TryRange(SegmentNames.Train, config.train, errors, out settings.Train)
            & TryRange(SegmentNames.Validation, config.validation, errors, out settings.Validation)
            & TryRange(SegmentNames.Test, config.test, errors, out settings.Test)
            & TryDate("playStart", config.playStart, errors, out settings.PlayStart);

        /// <summary>设置 DataPath 和 Series；或者加上 candle 文件用不了的原因。</summary>
        static void LoadData(GymConfig config, GymSettings settings, List<string> errors)
        {
            if (string.IsNullOrEmpty(config.dataFile))
            {
                errors.Add("dataFile is missing");
                return;
            }
            settings.DataPath = ResolveDataPath(config.dataFile, Path.GetDirectoryName(settings.ConfigPath));
            if (!File.Exists(settings.DataPath))
            {
                errors.Add($"data file not found: {settings.DataPath}");
                return;
            }
            try
            {
                settings.Series = GymDataCache.Get(settings.DataPath);
            }
            catch (FormatException e)
            {
                errors.Add($"data file {settings.DataPath} is malformed: {e.Message}");
            }
        }

        static void ValidateSplit(GymConfig config, GymSettings settings, List<string> errors, List<string> warnings)
        {
            SplitReport report = SplitValidator.Validate(settings.Train, settings.Validation, settings.Test,
                settings.Series, Math.Max(config.episodeLength, 0));
            errors.AddRange(report.Errors);
            warnings.AddRange(report.Warnings);
        }

        /// <summary>设置 PlayStartIndex，并检查 playStart 落在训练段里。</summary>
        static void CheckPlayStart(GymConfig config, GymSettings settings, List<string> errors)
        {
            int trainLast = settings.Train.LastIndex(settings.Series);
            settings.PlayStartIndex = settings.Series.FirstIndexOnOrAfter(settings.PlayStart);
            int playMin = Math.Max(settings.Train.FirstIndex(settings.Series), ObservationBuilder.Lookback);
            if (settings.PlayStart < settings.Train.StartDate || settings.PlayStart > settings.Train.EndDate ||
                settings.PlayStartIndex < playMin || settings.PlayStartIndex >= trainLast)
                errors.Add($"playStart {config.playStart} must fall inside the training segment {settings.Train}");
        }

        /// <summary>
        /// -gymMode train（默认）只接受 train 这一个分段。-gymMode eval 接受
        /// -gymSegment validation 或 test（默认 test）。
        /// </summary>
        static GymMode ReadMode(string[] args, List<string> errors, out EvaluationSegment evalSegment)
        {
            string mode = CommandLineArgs.ValueOf(args, ModeArg)?.ToLowerInvariant();
            string segment = CommandLineArgs.ValueOf(args, SegmentArg)?.ToLowerInvariant();
            evalSegment = EvaluationSegment.Test;
            if (mode == null || mode == "train")
            {
                if (segment != null && segment != SegmentNames.Train)
                    errors.Add($"{SegmentArg} {segment} needs {ModeArg} eval; training always uses the train segment");
                return GymMode.Train;
            }
            if (mode == "eval")
            {
                if (segment == SegmentNames.Validation) evalSegment = EvaluationSegment.Validation;
                else if (segment != null && segment != SegmentNames.Test)
                    errors.Add($"{SegmentArg} {segment} is not an evaluation segment; use validation or test");
                return GymMode.Eval;
            }
            errors.Add($"{ModeArg} {mode} is not a mode; use train or eval");
            return GymMode.Train;
        }

        static double? ReadNumberArg(string[] args, string name, List<string> errors)
        {
            string text = CommandLineArgs.ValueOf(args, name);
            if (text == null) return null;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) &&
                !double.IsNaN(value) && !double.IsInfinity(value))
                return value;
            errors.Add($"{name} '{text}' is not a number");
            return null;
        }

        /// <summary>
        /// 一个成本参数，用 CostModel 强制的同一套范围来检查：CostModel 会拒绝的值，在这里就算配置错误，
        /// 而不是等到 Agent 第一次重置时才抛异常。
        /// </summary>
        static double? ReadCostArg(string[] args, string name, List<string> errors, Func<double, CostModel> check)
        {
            double? value = ReadNumberArg(args, name, errors);
            if (value == null) return null;
            try
            {
                check(value.Value);
                return value;
            }
            catch (ArgumentOutOfRangeException e)
            {
                string reason = e.Message.Split('\n')[0].Trim();
                errors.Add($"{name} {CommandLineArgs.ValueOf(args, name)} is out of range: {reason}");
                return null;
            }
        }

        static T ReadJson<T>(string path, string what, List<string> errors) where T : class
        {
            if (!File.Exists(path))
            {
                errors.Add($"{what} file not found: {path}");
                return null;
            }
            try
            {
                T value = JsonUtility.FromJson<T>(File.ReadAllText(path));
                if (value == null) errors.Add($"{what} file is empty: {path}");
                return value;
            }
            catch (ArgumentException e)
            {
                errors.Add($"{what} file is not valid JSON: {path} ({e.Message})");
                return null;
            }
        }

        static SymbolRules FindSymbol(string symbol, SymbolTable table, string symbolsPath, List<string> errors)
        {
            if (string.IsNullOrEmpty(symbol))
            {
                errors.Add("symbol is missing");
                return null;
            }
            foreach (SymbolEntry entry in table.symbols ?? Array.Empty<SymbolEntry>())
            {
                if (entry == null || entry.symbol != symbol) continue;
                if (!decimal.TryParse(entry.stepSize, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal step) || step <= 0)
                {
                    errors.Add($"symbol {symbol}: stepSize '{entry.stepSize}' is not a positive number");
                    return null;
                }
                if (!(entry.minNotional >= 0))
                {
                    errors.Add($"symbol {symbol}: minNotional must be >= 0");
                    return null;
                }
                return new SymbolRules(entry.symbol, entry.minNotional, step);
            }
            errors.Add($"symbol {symbol} is not listed in {symbolsPath}");
            return null;
        }

        static bool TryRange(string name, DateRange range, List<string> errors, out SegmentSpec segment)
        {
            segment = default;
            if (range == null || string.IsNullOrEmpty(range.start) || string.IsNullOrEmpty(range.end))
            {
                errors.Add($"{name} needs start and end dates (yyyy-MM-dd)");
                return false;
            }
            try
            {
                segment = SegmentSpec.Parse(name, range.start, range.end);
                return true;
            }
            catch (Exception e) when (e is FormatException || e is ArgumentException)
            {
                errors.Add($"{name}: {e.Message}");
                return false;
            }
        }

        static bool TryDate(string name, string text, List<string> errors, out DateTime date)
        {
            if (DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out date))
                return true;
            errors.Add($"{name} '{text}' is not a yyyy-MM-dd date");
            return false;
        }

        /// <summary>绝对路径照原样用；相对路径先找配置文件旁边，找不到再找 StreamingAssets/Gym。</summary>
        static string ResolveDataPath(string dataFile, string configDir)
        {
            if (Path.IsPathRooted(dataFile)) return Path.GetFullPath(dataFile);
            string nextToConfig = Path.GetFullPath(Path.Combine(configDir, dataFile));
            if (File.Exists(nextToConfig)) return nextToConfig;
            return Path.GetFullPath(Path.Combine(DefaultDirectory, dataFile));
        }
    }
}
