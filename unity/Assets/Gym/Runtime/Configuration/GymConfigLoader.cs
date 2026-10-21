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
    /// 载入配置里每只币的数据，并对每只币检查分段的划分。-gymSymbol 选当前币，不给时是主币；
    /// -gymObs 选观测集，不给时是配置里的 observationSet，再没有是 base。-gymRiskAversion 给奖励的风险系数（≥ 0、有限）。
    /// -gymTrainSymbols 给训练用的币（混训），不给时是配置里的 trainSymbols，再没有只用当前币。
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
        public const string ObservationSetArg = "-gymObs";
        public const string RiskAversionArg = "-gymRiskAversion";
        /// <summary>训练用的币，逗号分隔，例如 -gymTrainSymbols BTCUSDT,ETHUSDT。盖过配置里的 trainSymbols。</summary>
        public const string TrainSymbolsArg = "-gymTrainSymbols";
        /// <summary>动作模式：orders 或 dial。盖过配置里的 actionMode。</summary>
        public const string ActionModeArg = "-gymActionMode";
        /// <summary>旋钮模式每次决策走几根（正整数）。盖过配置里的 decisionHours。</summary>
        public const string DecisionHoursArg = "-gymDecisionHours";
        /// <summary>旋钮模式的调仓带宽，(0, 0.5) 内。盖过配置里的 rebalanceBand。</summary>
        public const string BandArg = "-gymBand";
        /// <summary>旋钮模式每局小时数的合法范围 [720, 2160]（22A 第二节第 5 条）。</summary>
        public const int MinDialEpisodeLength = 720;
        public const int MaxDialEpisodeLength = 2160;
        /// <summary>配置里训练用的币那个字段的名字（写进错误信息）。</summary>
        public const string TrainSymbolsField = "trainSymbols";

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
                    Debug.Log($"[Gym] config {settings.ConfigPath}, symbol {settings.Rules.Symbol}, data {settings.DataPath} ({settings.Series.Count} candles), mode {settings.Mode}, observations {settings.ObservationSet.Name()}, actions {settings.ActionMode.Name()}" +
                              (settings.ActionMode == ActionMode.Dial
                                  ? $" (decision hours {settings.DecisionHours}, band {Text(settings.RebalanceBand)}, levels {LevelsText(settings.DialLevels)}, episodes of {settings.DialEpisodeLength} candles)"
                                  : ""));
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

        /// <summary>
        /// 运行中才发现的配置错误（例如训练器的 obs_set 换了一档、分段因此不够长）：和 <see cref="LoadForRuntime"/>
        /// 一样，在 player 里写进日志并以退出码 1 退出；在 editor 里写进日志。返回要抛出的异常。
        /// </summary>
        public static GymConfigException FailAtRuntime(IEnumerable<string> errors)
        {
            var e = new GymConfigException(new List<string>(errors));
            Debug.LogError("[Gym] " + e.Message);
            if (!Application.isEditor) Application.Quit(1);
            return e;
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
            settings.RiskAversionArg = ReadRiskAversionArg(args, errors);
            settings.ObservationSet = ReadObservationSet(args, config, errors);
            ReadDial(args, config, settings, errors);
            CheckNumbers(config, errors);
            var extraNames = new List<string>();
            bool datesOk = ReadDates(config, settings, errors, extraNames);
            List<SymbolSettings> symbols = LoadSymbols(config, table, settings, errors);
            settings.Symbols = symbols;
            settings.Current = ChooseSymbol(args, config, symbols, errors);
            UseCurrentSymbol(settings);
            settings.TrainSymbols = ReadTrainSymbols(args, config, settings, errors);
            CheckEvalSegmentName(evalSegmentName, extraNames, errors);

            var notes = new List<string>();
            if (settings.Series != null && datesOk)
            {
                ValidateSplits(config, settings, errors, warnings, notes);
                settings.EvalSegment = settings.TryGetEvaluationSegment(evalSegmentName ?? SegmentNames.Test, out SegmentSpec chosen)
                    ? chosen
                    : settings.Test; // 名字不对时上面已经记了错误，这里的值不会被用到
                settings.PlayStartIndex = settings.Series.FirstIndexOnOrAfter(settings.PlayStart);
                CheckPlayStart(config, settings, settings.ObservationSet, errors);
            }

            if (errors.Count > 0) throw new GymConfigException(errors);
            settings.Warnings = warnings;
            settings.Notes = notes;
            return settings;
        }

        /// <summary>
        /// 换成 <paramref name="set"/> 这一档时，依赖历史长度的检查（每只币的训练段够不够长、playStart 够不够靠后）
        /// 还过不过：过返回空表，不过返回每一条错误（写法和载入时一样）。不改 <paramref name="settings"/>。
        /// 训练器的环境参数 obs_set 和命令行、配置选的档不同时，Agent 用它重查一遍。
        /// </summary>
        public static List<string> ProblemsWithObservationSet(GymSettings settings, ObservationSet set)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            return ProblemsWith(settings, set, settings.ActionMode);
        }

        /// <summary>
        /// 换成 <paramref name="set"/> 这一档观测、<paramref name="mode"/> 这种动作模式时，每只币的训练段（按这种模式每局的长度
        /// 和这一档的历史长度）够不够长、playStart 够不够靠后：过返回空表，不过返回每一条错误。不改 <paramref name="settings"/>。
        /// 训练器的环境参数 obs_set 或 action_mode 和命令行、配置选的不同时，Agent 用它重查一遍。
        /// </summary>
        public static List<string> ProblemsWith(GymSettings settings, ObservationSet set, ActionMode mode)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            var errors = new List<string>();
            CheckSplits(settings, settings.EpisodeLengthFor(mode), set, errors, new List<string>());
            CheckPlayStart(settings.Config, settings, set, errors);
            return errors;
        }

        /// <summary>
        /// 动作模式（-gymActionMode，没给时是配置里的 actionMode，空或没有时是 orders）、决策根数（-gymDecisionHours 或
        /// decisionHours）、带宽（-gymBand 或 rebalanceBand）、档位（dialLevels）、旋钮模式每局长度（dialEpisodeLength）。
        /// 配置里的值不管有没有被命令行盖过都要对；不对时各记一条错误，不静默回落。下单模式下也查（训练器的 action_mode 能换成旋钮）。
        /// </summary>
        static void ReadDial(string[] args, GymConfig config, GymSettings settings, List<string> errors)
        {
            var mode = ActionMode.Orders;
            if (!string.IsNullOrEmpty(config.actionMode) && !ActionModes.TryParse(config.actionMode, out mode))
                errors.Add($"actionMode '{config.actionMode}' is not an action mode; use {ActionModes.NamesText}");
            string wanted = CommandLineArgs.ValueOf(args, ActionModeArg);
            if (wanted != null)
            {
                if (ActionModes.TryParse(wanted, out ActionMode fromArgs)) mode = fromArgs;
                else errors.Add($"{ActionModeArg} {wanted} is not an action mode; use {ActionModes.NamesText}");
            }
            settings.ActionMode = mode;

            if (config.decisionHours < 1)
                errors.Add($"decisionHours must be a whole number >= 1 (got {config.decisionHours})");
            settings.DecisionHours = config.decisionHours;
            string hoursText = CommandLineArgs.ValueOf(args, DecisionHoursArg);
            if (hoursText != null)
            {
                if (int.TryParse(hoursText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int hours) && hours >= 1)
                    settings.DecisionHours = hours;
                else
                    errors.Add($"{DecisionHoursArg} {hoursText} is out of range: must be a whole number >= 1");
            }

            if (!IsBand(config.rebalanceBand))
                errors.Add($"rebalanceBand must be inside (0, 0.5) (got {Text(config.rebalanceBand)})");
            settings.RebalanceBand = config.rebalanceBand;
            double? band = ReadNumberArg(args, BandArg, errors);
            if (band != null)
            {
                if (IsBand(band.Value)) settings.RebalanceBand = band.Value;
                else errors.Add($"{BandArg} {CommandLineArgs.ValueOf(args, BandArg)} is out of range: must be inside (0, 0.5)");
            }

            double[] levels = config.dialLevels ?? Array.Empty<double>();
            foreach (string problem in DialSettings.LevelProblems(levels))
                errors.Add($"dialLevels: {problem}");
            settings.DialLevels = Array.AsReadOnly((double[])levels.Clone());

            if (config.dialEpisodeLength < MinDialEpisodeLength || config.dialEpisodeLength > MaxDialEpisodeLength)
                errors.Add($"dialEpisodeLength must be in [{MinDialEpisodeLength}, {MaxDialEpisodeLength}] (got {config.dialEpisodeLength})");
            settings.DialEpisodeLength = config.dialEpisodeLength;
        }

        /// <summary>带宽的宽范围：(0, 0.5)，和 <see cref="DialSettings"/> 一样。</summary>
        public static bool IsBand(double value) => value > 0 && value < 0.5;

        static string Text(double value) => value.ToString("R", CultureInfo.InvariantCulture);

        /// <summary>日志里的档位，例如 "0.3, 0.5, 0.7"。</summary>
        public static string LevelsText(IReadOnlyList<double> levels)
        {
            var parts = new List<string>();
            foreach (double level in levels) parts.Add(Text(level));
            return string.Join(", ", parts);
        }

        /// <summary>
        /// -gymObs（不分大小写）；没给时是配置里的 observationSet（空或没有时是 base）。配置里的名字不管有没有被
        /// -gymObs 盖过都要对；名字不对时记一条错误，列出 base、long、long-ta。
        /// </summary>
        static ObservationSet ReadObservationSet(string[] args, GymConfig config, List<string> errors)
        {
            var chosen = ObservationSet.Base;
            if (!string.IsNullOrEmpty(config.observationSet) && !ObservationSets.TryParse(config.observationSet, out chosen))
                errors.Add($"observationSet '{config.observationSet}' is not an observation set; use {ObservationSets.NamesText}");
            string wanted = CommandLineArgs.ValueOf(args, ObservationSetArg);
            if (wanted == null) return chosen;
            if (ObservationSets.TryParse(wanted, out ObservationSet fromArgs)) return fromArgs;
            errors.Add($"{ObservationSetArg} {wanted} is not an observation set; use {ObservationSets.NamesText}");
            return chosen;
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

        /// <summary>
        /// 训练用的币：-gymTrainSymbols（逗号分隔，名字前后的空格不算，空的项跳过），没给时是配置里的 trainSymbols，
        /// 再没有（或是空的）时只有当前币。配置里的清单不管有没有被命令行盖过都要对（和 observationSet 一样）。
        /// 每个名字都要是配置里的币、不是 evalOnly、不重复（<see cref="ResolveTrainSymbols"/>）。
        /// 当前币是 evalOnly 时这里不拒绝：评估、试玩和观战可以用它，训练模式的 Agent 开工前另查（<see cref="ProblemsWithTraining"/>）。
        /// </summary>
        static IReadOnlyList<SymbolSettings> ReadTrainSymbols(string[] args, GymConfig config, GymSettings settings, List<string> errors)
        {
            List<SymbolSettings> fromConfig = null;
            if (config.trainSymbols != null && config.trainSymbols.Length > 0)
                fromConfig = ResolveTrainSymbols(settings, config.trainSymbols, TrainSymbolsField, errors);
            string text = CommandLineArgs.ValueOf(args, TrainSymbolsArg);
            if (text != null)
            {
                string[] names = text.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                var trimmed = new List<string>();
                foreach (string name in names)
                    if (name.Trim().Length > 0) trimmed.Add(name.Trim());
                if (trimmed.Count > 0) return ResolveTrainSymbols(settings, trimmed, TrainSymbolsArg, errors);
                errors.Add($"{TrainSymbolsArg} '{text}' names no symbol; give one or more, separated by commas, " +
                           $"for example {TrainSymbolsArg} BTCUSDT,ETHUSDT");
            }
            if (fromConfig != null) return fromConfig;
            return settings.Current != null ? new[] { settings.Current } : Array.Empty<SymbolSettings>();
        }

        /// <summary>
        /// 把训练用的币名换成配置里的币（不分大小写），按给的顺序。名字为空、不是配置里的币、是 evalOnly 的、或者重复时，
        /// 各记一条错误（开头是 <paramref name="source"/>，例如 -gymTrainSymbols），这一项跳过；不是配置里的币和 evalOnly
        /// 的那两种错误列出能训练的名字。载入器读 trainSymbols 和 -gymTrainSymbols 用它，Agent 的
        /// <c>TrainSymbolsOverride</c>（只给测试用）也用它。
        /// </summary>
        public static List<SymbolSettings> ResolveTrainSymbols(GymSettings settings, IEnumerable<string> names, string source,
            List<string> errors)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            var chosen = new List<SymbolSettings>();
            foreach (string raw in names ?? Array.Empty<string>())
            {
                string name = raw?.Trim();
                if (string.IsNullOrEmpty(name))
                {
                    errors.Add($"{source}: a symbol name is empty");
                    continue;
                }
                if (!settings.TryGetSymbol(name, out SymbolSettings symbol))
                {
                    errors.Add($"{source}: {name} is not one of the symbols in the config; use {TrainableNamesText(settings)}");
                    continue;
                }
                if (symbol.EvalOnly)
                {
                    errors.Add($"{source}: {symbol.Symbol} is evalOnly (only evaluated, never trained on); use {TrainableNamesText(settings)}");
                    continue;
                }
                if (chosen.Contains(symbol))
                {
                    errors.Add($"{source}: {symbol.Symbol} is listed twice");
                    continue;
                }
                chosen.Add(symbol);
            }
            return chosen;
        }

        /// <summary>
        /// 训练模式的 Agent 开工前查一遍：当前币或训练用的币里有 evalOnly 的（实际上只会是 -gymSymbol 选了只考不训的币，
        /// 清单里的 evalOnly 载入时已经拒绝了），或者清单是空的，返回每一条错误；都没有时返回空表。不改 <paramref name="settings"/>。
        /// </summary>
        public static List<string> ProblemsWithTraining(GymSettings settings, IReadOnlyList<SymbolSettings> trainSymbols)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            var errors = new List<string>();
            if (settings.Current != null && settings.Current.EvalOnly)
                errors.Add($"{settings.Current.Symbol} is evalOnly (only evaluated, never trained on), so a training agent cannot use it " +
                           $"as the current symbol; choose another {SymbolArg} ({TrainableNamesText(settings)})");
            if (trainSymbols == null || trainSymbols.Count == 0)
            {
                errors.Add("there is no symbol to train on");
                return errors;
            }
            foreach (SymbolSettings symbol in trainSymbols)
                if (symbol.EvalOnly && symbol != settings.Current)
                    errors.Add($"{symbol.Symbol} is evalOnly (only evaluated, never trained on), so a training agent cannot use it");
            return errors;
        }

        /// <summary>能训练的币（配置里不是 evalOnly 的），拼成 "a, b or c"。</summary>
        static string TrainableNamesText(GymSettings settings)
        {
            var names = new List<string>();
            foreach (SymbolSettings symbol in settings.Symbols)
                if (!symbol.EvalOnly) names.Add(symbol.Symbol);
            return JoinWithOr(names);
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
        /// 再过 <see cref="SplitValidator"/>：验证段们和测试段必须完整落在这只币的数据里，裁过的训练段从
        /// 观测集的历史长度起也要够长。当前币先查，它的问题照原样报；别的币的问题前面加「币名: 」，
        /// 和当前币一字不差的不再重复报。最后 settings.Train 换成当前币的训练段。
        /// </summary>
        static void ValidateSplits(GymConfig config, GymSettings settings, List<string> errors, List<string> warnings,
            List<string> notes)
        {
            SegmentSpec configured = settings.Train;
            foreach (SymbolSettings symbol in CurrentFirst(settings))
            {
                symbol.Train = configured;
                if (symbol.Series == null) continue; // 读不了数据，上面已经记了错误
                ClipTraining(symbol, configured, notes);
            }
            CheckSplits(settings, settings.EpisodeLengthFor(settings.ActionMode), settings.ObservationSet, errors, warnings);
            settings.Train = settings.Current.Train;
        }

        /// <summary>当前币在前，别的币按配置里的顺序。</summary>
        static List<SymbolSettings> CurrentFirst(GymSettings settings)
        {
            var ordered = new List<SymbolSettings> { settings.Current };
            foreach (SymbolSettings symbol in settings.Symbols)
                if (symbol != settings.Current) ordered.Add(symbol);
            return ordered;
        }

        /// <summary>每只币（训练段已裁过）过一遍 <see cref="SplitValidator"/>，按 <paramref name="set"/> 的历史长度查训练段够不够长。</summary>
        static void CheckSplits(GymSettings settings, int episodeLength, ObservationSet set, List<string> errors,
            List<string> warnings)
        {
            var currentErrors = new List<string>();
            var currentWarnings = new List<string>();
            foreach (SymbolSettings symbol in CurrentFirst(settings))
            {
                if (symbol.Series == null) continue;
                bool isCurrent = symbol == settings.Current;
                string prefix = isCurrent ? "" : symbol.Symbol + ": ";
                SplitReport report = SplitValidator.Validate(symbol.Train, settings.Validation, settings.Test,
                    settings.ExtraSegments, symbol.Series, episodeLength, set);
                AddOnce(report.Errors, isCurrent, prefix, currentErrors, errors);
                AddOnce(report.Warnings, isCurrent, prefix, currentWarnings, warnings);
            }
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

        /// <summary>
        /// 检查 playStart 落在当前币的训练段里，而且不早于 <paramref name="set"/> 的历史长度
        /// （PlayStartIndex 已按当前币的序列设好）。
        /// </summary>
        static void CheckPlayStart(GymConfig config, GymSettings settings, ObservationSet set, List<string> errors)
        {
            int trainLast = settings.Train.LastIndex(settings.Series);
            int playMin = Math.Max(settings.Train.FirstIndex(settings.Series), set.HistoryLength());
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

        /// <summary>
        /// -gymRiskAversion，用 <see cref="RewardFunction.CheckRiskAversion"/> 的同一套范围（有限、≥ 0）来检查：
        /// 负数在这里就算配置错误，而不是等到 Agent 第一次重置时才抛异常。没给时为 null。
        /// </summary>
        static double? ReadRiskAversionArg(string[] args, List<string> errors)
        {
            double? value = ReadNumberArg(args, RiskAversionArg, errors);
            if (value == null) return null;
            try
            {
                RewardFunction.CheckRiskAversion(value.Value);
                return value;
            }
            catch (ArgumentOutOfRangeException e)
            {
                string reason = e.Message.Split('\n')[0].Trim();
                errors.Add($"{RiskAversionArg} {CommandLineArgs.ValueOf(args, RiskAversionArg)} is out of range: {reason}");
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
