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
    /// 载入配置里每只币的数据，并对每只币检查分段的划分。-gymSymbol 选当前币，不给时是主币。
    /// </summary>
    public static class GymConfigLoader
    {
        public const string ConfigArg = "-gymConfig";
        public const string ModeArg = "-gymMode";
        public const string SegmentArg = "-gymSegment";
        public const string FeeRateArg = "-gymFeeRate";
        public const string FixedFeeArg = "-gymFixedFee";
        public const string SlippageArg = "-gymSlippage";
        public const string SymbolArg = "-gymSymbol";

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
                    foreach (string note in settings.Notes) Debug.Log("[Gym] " + note);
                    foreach (string warning in settings.Warnings) Debug.LogWarning("[Gym] " + warning);
                    Debug.Log($"[Gym] config {settings.ConfigPath}, symbol {settings.Rules.Symbol}, data {settings.DataPath} ({settings.Series.Count} candles), mode {settings.Mode}");
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
            settings.Mode = ReadMode(args, errors, out string evalSegmentName);
            settings.FeeRateArg = ReadCostArg(args, FeeRateArg, errors, v => new CostModel(v, 0, 0));
            settings.FixedFeeArg = ReadCostArg(args, FixedFeeArg, errors, v => new CostModel(0, v, 0));
            settings.SlippageArg = ReadCostArg(args, SlippageArg, errors, v => new CostModel(0, 0, v));
            CheckNumbers(config, errors);
            var extraNames = new List<string>();
            bool datesOk = ReadDates(config, settings, errors, extraNames);
            List<SymbolSettings> symbols = LoadSymbols(config, table, settings, errors);
            settings.Symbols = symbols;
            settings.Current = ChooseSymbol(args, config, symbols, errors);
            UseCurrentSymbol(settings);
            CheckEvalSegmentName(evalSegmentName, extraNames, errors);

            var notes = new List<string>();
            if (settings.Series != null && datesOk)
            {
                ValidateSplits(config, settings, errors, warnings, notes);
                settings.EvalSegment = settings.TryGetEvaluationSegment(evalSegmentName ?? SegmentNames.Test, out SegmentSpec chosen)
                    ? chosen
                    : settings.Test; // 名字不对时上面已经记了错误，这里的值不会被用到
                CheckPlayStart(config, settings, errors);
            }

            if (errors.Count > 0) throw new GymConfigException(errors);
            settings.Warnings = warnings;
            settings.Notes = notes;
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

        /// <summary>
        /// 读三个分段、额外的评估段和 playStart。任何一个日期缺失或格式不对时返回 false；但每一个都照样检查。
        /// 名字合格的额外段（不管日期对不对）记进 <paramref name="extraNames"/>。
        /// </summary>
        static bool ReadDates(GymConfig config, GymSettings settings, List<string> errors, List<string> extraNames) =>
            TryRange(SegmentNames.Train, config.train, errors, out settings.Train)
            & TryRange(SegmentNames.Validation, config.validation, errors, out settings.Validation)
            & TryRange(SegmentNames.Test, config.test, errors, out settings.Test)
            & ReadExtraSegments(config, settings, errors, extraNames)
            & TryDate("playStart", config.playStart, errors, out settings.PlayStart);

        /// <summary>
        /// 读 extraSegments。名字只许小写字母、数字和连字符，不许重名，不许叫 train、validation、test；
        /// 名字不合格的那一项记一条错误后跳过。名字合格的记进 <paramref name="names"/>，日期也合格的才进
        /// <see cref="GymSettings.ExtraSegments"/>。有日期不对的项时返回 false。
        /// </summary>
        static bool ReadExtraSegments(GymConfig config, GymSettings settings, List<string> errors, List<string> names)
        {
            var segments = new List<SegmentSpec>();
            bool datesOk = true;
            NamedDateRange[] entries = config.extraSegments ?? Array.Empty<NamedDateRange>();
            for (int i = 0; i < entries.Length; i++)
            {
                NamedDateRange entry = entries[i];
                string where = $"extraSegments[{i}]";
                if (entry == null || string.IsNullOrEmpty(entry.name))
                {
                    errors.Add($"{where} needs a name");
                    continue;
                }
                string name = entry.name;
                if (!IsSegmentName(name))
                {
                    errors.Add($"{where}: name '{name}' may only use lower-case letters, digits and hyphens");
                    continue;
                }
                if (name == SegmentNames.Train || name == SegmentNames.Validation || name == SegmentNames.Test)
                {
                    errors.Add($"{where}: name '{name}' is taken by the {name} segment");
                    continue;
                }
                if (names.Contains(name))
                {
                    errors.Add($"{where}: name '{name}' is used twice");
                    continue;
                }
                names.Add(name);
                if (TryRange(name, new DateRange { start = entry.start, end = entry.end }, errors, out SegmentSpec segment))
                    segments.Add(segment);
                else
                    datesOk = false;
            }
            settings.ExtraSegments = segments;
            return datesOk;
        }

        /// <summary>只有小写字母、数字和连字符，并且不是空的。</summary>
        static bool IsSegmentName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            foreach (char c in name)
                if (!(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '-')
                    return false;
            return true;
        }

        /// <summary>
        /// 评估模式下 -gymSegment 给的名字必须是配置里的一个评估段：validation、额外段或 test。
        /// 不是时记一条错误，列出能用的名字。<paramref name="name"/> 为 null（没给，或不是评估模式）时不查。
        /// </summary>
        static void CheckEvalSegmentName(string name, List<string> extraNames, List<string> errors)
        {
            if (name == null) return;
            var names = new List<string> { SegmentNames.Validation };
            names.AddRange(extraNames);
            names.Add(SegmentNames.Test);
            if (!names.Contains(name))
                errors.Add($"{SegmentArg} {name} is not an evaluation segment; use {JoinWithOr(names)}");
        }

        /// <summary>拼成 "a"、"a or b"、"a, b or c" 这样。</summary>
        static string JoinWithOr(List<string> items) =>
            items.Count < 2 ? string.Join("", items)
                : string.Join(", ", items.GetRange(0, items.Count - 1)) + " or " + items[items.Count - 1];

        /// <summary>
        /// 配置里的每只币：去 symbols.json 找下单规则，读数据。没有币数组（或是空的）时只有主币（symbol、dataFile）。
        /// 有币数组时：每项要有 symbol 和 dataFile，不许重名（不分大小写），主币必须列在里面，而且它的数据文件和
        /// 顶层的 dataFile 是同一个文件；主币的 DataFile 记顶层 dataFile 的写法。
        /// </summary>
        static List<SymbolSettings> LoadSymbols(GymConfig config, SymbolTable table, GymSettings settings, List<string> errors)
        {
            var symbols = new List<SymbolSettings>();
            string configDir = Path.GetDirectoryName(settings.ConfigPath);
            string main = config.symbol;
            if (string.IsNullOrEmpty(main)) errors.Add("symbol is missing");
            string mainPath = null;
            if (string.IsNullOrEmpty(config.dataFile)) errors.Add("dataFile is missing");
            else mainPath = ResolveDataPath(config.dataFile, configDir);

            SymbolDataEntry[] entries = config.symbols;
            if (entries == null || entries.Length == 0)
            {
                if (!string.IsNullOrEmpty(main))
                    symbols.Add(LoadSymbol(main, config.dataFile, mainPath, false, table, settings.SymbolsPath, errors));
                return symbols;
            }

            bool mainListed = false;
            for (int i = 0; i < entries.Length; i++)
            {
                SymbolDataEntry entry = entries[i];
                string where = $"symbols[{i}]";
                if (entry == null || string.IsNullOrEmpty(entry.symbol))
                {
                    errors.Add($"{where} needs a symbol");
                    continue;
                }
                string name = entry.symbol;
                if (string.IsNullOrEmpty(entry.dataFile))
                {
                    errors.Add($"{where}: {name} needs a dataFile");
                    continue;
                }
                if (symbols.Exists(s => string.Equals(s.Symbol, name, StringComparison.OrdinalIgnoreCase)))
                {
                    errors.Add($"{where}: {name} is listed twice");
                    continue;
                }
                string dataFile = entry.dataFile;
                string path = ResolveDataPath(dataFile, configDir);
                if (name == main)
                {
                    mainListed = true;
                    if (mainPath != null && path != mainPath)
                    {
                        errors.Add($"{where}: {name} is the main symbol, so its dataFile must be the same file as dataFile " +
                                   $"({config.dataFile}), not {dataFile}");
                        continue;
                    }
                    if (mainPath != null) dataFile = config.dataFile;
                }
                symbols.Add(LoadSymbol(name, dataFile, path, entry.evalOnly, table, settings.SymbolsPath, errors));
            }
            if (!string.IsNullOrEmpty(main) && !mainListed)
            {
                errors.Add($"symbol {main} is not listed in the symbols of the config; the main symbol must be one of them");
                FindSymbol(main, table, settings.SymbolsPath, errors); // 顺便说清 symbols.json 里有没有它
            }
            return symbols;
        }

        /// <summary>一只币：规则和数据；用不了时记下原因（规则或数据为 null）。</summary>
        static SymbolSettings LoadSymbol(string symbol, string dataFile, string path, bool evalOnly, SymbolTable table,
            string symbolsPath, List<string> errors)
        {
            var loaded = new SymbolSettings { Symbol = symbol, DataFile = dataFile, DataPath = path, EvalOnly = evalOnly };
            loaded.Rules = FindSymbol(symbol, table, symbolsPath, errors);
            if (path == null) return loaded;
            if (!File.Exists(path))
            {
                errors.Add($"data file not found: {path}");
                return loaded;
            }
            try
            {
                loaded.Series = GymDataCache.Get(path);
            }
            catch (FormatException e)
            {
                errors.Add($"data file {path} is malformed: {e.Message}");
            }
            return loaded;
        }

        /// <summary>
        /// 当前币：-gymSymbol 给的名字（不分大小写），没给时是主币。名字不是配置里的币时记一条错误，列出能用的名字。
        /// </summary>
        static SymbolSettings ChooseSymbol(string[] args, GymConfig config, List<SymbolSettings> symbols, List<string> errors)
        {
            string wanted = CommandLineArgs.ValueOf(args, SymbolArg);
            if (wanted == null) return symbols.Find(s => s.Symbol == config.symbol);
            SymbolSettings chosen = symbols.Find(s => string.Equals(s.Symbol, wanted, StringComparison.OrdinalIgnoreCase));
            if (chosen == null)
                errors.Add($"{SymbolArg} {wanted} is not one of the symbols in the config; " +
                           $"use {JoinWithOr(symbols.ConvertAll(s => s.Symbol))}");
            return chosen;
        }

        /// <summary>DataPath、DataFile、Rules、Series 指向当前币。</summary>
        static void UseCurrentSymbol(GymSettings settings)
        {
            SymbolSettings current = settings.Current;
            if (current == null) return;
            settings.DataPath = current.DataPath;
            settings.DataFile = current.DataFile;
            settings.Rules = current.Rules;
            settings.Series = current.Series;
        }

        /// <summary>
        /// 每只币各查一次分段：先按它的数据起点裁训练段（裁剪记进 <paramref name="notes"/>，是普通日志，不算警告），
        /// 再过 <see cref="SplitValidator"/>：验证段们和测试段必须完整落在这只币的数据里，裁过的训练段也要够长。
        /// 当前币先查，它的问题照原样报；别的币的问题前面加「币名: 」，和当前币一字不差的不再重复报。
        /// 最后 settings.Train 换成当前币的训练段。
        /// </summary>
        static void ValidateSplits(GymConfig config, GymSettings settings, List<string> errors, List<string> warnings,
            List<string> notes)
        {
            SegmentSpec configured = settings.Train;
            int episodeLength = Math.Max(config.episodeLength, 0);
            var currentErrors = new List<string>();
            var currentWarnings = new List<string>();
            var ordered = new List<SymbolSettings> { settings.Current };
            foreach (SymbolSettings symbol in settings.Symbols)
                if (symbol != settings.Current) ordered.Add(symbol);

            foreach (SymbolSettings symbol in ordered)
            {
                symbol.Train = configured;
                if (symbol.Series == null) continue; // 读不了数据，上面已经记了错误
                ClipTraining(symbol, configured, notes);
                bool isCurrent = symbol == settings.Current;
                string prefix = isCurrent ? "" : symbol.Symbol + ": ";
                SplitReport report = SplitValidator.Validate(symbol.Train, settings.Validation, settings.Test,
                    settings.ExtraSegments, symbol.Series, episodeLength);
                AddOnce(report.Errors, isCurrent, prefix, currentErrors, errors);
                AddOnce(report.Warnings, isCurrent, prefix, currentWarnings, warnings);
            }
            settings.Train = settings.Current.Train;
        }

        /// <summary>当前币的条目原样加；别的币的条目和当前币一字不差时跳过，否则加上币名前缀。</summary>
        static void AddOnce(List<string> found, bool isCurrent, string prefix, List<string> current, List<string> into)
        {
            foreach (string item in found)
            {
                if (isCurrent) current.Add(item);
                else if (current.Contains(item)) continue;
                into.Add(prefix + item);
            }
        }

        /// <summary>
        /// 配置里的训练段早于这只币的第一根 candle、但终点在它之后时，起点裁到第一根 candle 那天，终点不变。
        /// 终点也早于第一根时不裁，让分段检查报「out of range」。
        /// </summary>
        static void ClipTraining(SymbolSettings symbol, SegmentSpec configured, List<string> notes)
        {
            symbol.Train = configured;
            symbol.TrainClipped = false;
            DateTime firstCandle = symbol.Series.OpenTimeUtc(0);
            DateTime firstDay = firstCandle.Date;
            if (configured.StartDate >= firstDay || configured.EndDate < firstDay) return;
            symbol.Train = new SegmentSpec(configured.Name, firstDay, configured.EndDate);
            symbol.TrainClipped = true;
            notes.Add($"{symbol.Symbol}: data starts {firstCandle:yyyy-MM-dd HH:mm} UTC, so its training segment " +
                      $"{configured} is clipped to {symbol.Train}");
        }

        /// <summary>设置 PlayStartIndex（按当前币的序列），并检查 playStart 落在当前币的训练段里。</summary>
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
        /// -gymMode train（默认）只接受 train 这一个分段。-gymMode eval 接受任何一个评估段的名字
        /// （validation、test 和配置里的额外段），不给时是 test；名字对不对等读完配置再查
        /// （<see cref="CheckEvalSegmentName"/>）。<paramref name="evalSegmentName"/> 只在评估模式下有值。
        /// </summary>
        static GymMode ReadMode(string[] args, List<string> errors, out string evalSegmentName)
        {
            string mode = CommandLineArgs.ValueOf(args, ModeArg)?.ToLowerInvariant();
            string segment = CommandLineArgs.ValueOf(args, SegmentArg)?.ToLowerInvariant();
            evalSegmentName = null;
            if (mode == null || mode == "train")
            {
                if (segment != null && segment != SegmentNames.Train)
                    errors.Add($"{SegmentArg} {segment} needs {ModeArg} eval; training always uses the train segment");
                return GymMode.Train;
            }
            if (mode == "eval")
            {
                evalSegmentName = segment;
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
