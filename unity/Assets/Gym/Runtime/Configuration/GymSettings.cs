using System;
using System.Collections.Generic;
using Gym.Core.Env;
using Gym.Core.Market;

namespace Gym.Runtime.Configuration
{
    /// <summary>
    /// 载入并检查过的配置，连同它的数据。DataPath、DataFile、Rules、Series、Train 和 PlayStartIndex 都是
    /// 当前币（<see cref="Current"/>）的；验证段、测试段和额外的评估段所有币共用。
    /// </summary>
    public sealed class GymSettings
    {
        public GymConfig Config;
        public string ConfigPath;
        public string SymbolsPath;
        public string DataPath;
        /// <summary>当前币的数据文件，按配置里的写法（写进评估记录的 data_file）。</summary>
        public string DataFile;
        public SymbolRules Rules;
        public CandleSeries Series;
        /// <summary>当前币的训练段（按它的数据起点裁过，见 <see cref="SymbolSettings.Train"/>）。</summary>
        public SegmentSpec Train;
        public SegmentSpec Validation;
        public SegmentSpec Test;
        /// <summary>配置里 extraSegments 列出的评估段，按配置里的顺序。</summary>
        public IReadOnlyList<SegmentSpec> ExtraSegments = Array.Empty<SegmentSpec>();
        public DateTime PlayStart;
        public int PlayStartIndex;
        public GymMode Mode;
        /// <summary>
        /// 命令行 -gymObs 选的观测集，没给时是配置里的 observationSet，再没有是 base。分段长度和 playStart 已按它的
        /// 历史长度查过。训练时训练器的环境参数 obs_set 还能换一档，由 Agent 另查一遍（见
        /// <see cref="GymConfigLoader.ProblemsWithObservationSet"/>），不改这里。
        /// </summary>
        public ObservationSet ObservationSet;
        /// <summary>-gymSegment 为评估选的分段（任何一个评估段的名字；默认 test）。</summary>
        public SegmentSpec EvalSegment;
        /// <summary>来自 -gymFeeRate / -gymFixedFee / -gymSlippage 的成本；没给时为 null。</summary>
        public double? FeeRateArg;
        public double? FixedFeeArg;
        public double? SlippageArg;
        /// <summary>来自 -gymRiskAversion 的风险系数（已查过有限、≥ 0）；没给时为 null。</summary>
        public double? RiskAversionArg;
        public IReadOnlyList<string> Warnings;
        /// <summary>
        /// 不是问题、但要写进日志的事，例如某只币的训练段按它的数据起点裁过。和 <see cref="Warnings"/> 分开，
        /// 默认配置载入时 Warnings 仍是空的。
        /// </summary>
        public IReadOnlyList<string> Notes = Array.Empty<string>();

        /// <summary>配置里的每只币，按配置里的顺序；配置没有币数组时只有主币。</summary>
        public IReadOnlyList<SymbolSettings> Symbols = Array.Empty<SymbolSettings>();
        /// <summary>当前币：-gymSymbol 选的那只，没给时是主币（配置里的 symbol）。</summary>
        public SymbolSettings Current;
        /// <summary>
        /// 训练用的币（混训的清单），按清单里的顺序：-gymTrainSymbols 给的，没给时是配置里的 trainSymbols，再没有（或是空的）
        /// 时只有当前币。清单里的名字都查过：是配置里的币、不是 evalOnly、不重复。训练模式的 Agent 开工前还要过一遍
        /// <see cref="GymConfigLoader.ProblemsWithTraining"/>（当前币是 evalOnly 时拒绝）。只有训练模式的 Agent 用它。
        /// </summary>
        public IReadOnlyList<SymbolSettings> TrainSymbols = Array.Empty<SymbolSettings>();

        /// <summary>-gymSymbol 接受的名字，按配置里的顺序。</summary>
        public IReadOnlyList<string> SymbolNames
        {
            get
            {
                var names = new List<string>();
                foreach (SymbolSettings symbol in Symbols) names.Add(symbol.Symbol);
                return names;
            }
        }

        /// <summary><see cref="TrainSymbols"/> 的名字，按清单里的顺序。</summary>
        public IReadOnlyList<string> TrainSymbolNames
        {
            get
            {
                var names = new List<string>();
                foreach (SymbolSettings symbol in TrainSymbols) names.Add(symbol.Symbol);
                return names;
            }
        }

        /// <summary>按名字取一只币的规则、数据和裁过的训练段，不分大小写。找不到时返回 false。</summary>
        public bool TryGetSymbol(string name, out SymbolSettings symbol)
        {
            symbol = null;
            if (string.IsNullOrEmpty(name)) return false;
            foreach (SymbolSettings candidate in Symbols)
            {
                if (!string.Equals(candidate.Symbol, name, StringComparison.OrdinalIgnoreCase)) continue;
                symbol = candidate;
                return true;
            }
            return false;
        }

        /// <summary>评估模式下 -gymSegment 接受的名字：validation、配置里的额外段、test，按这个顺序。</summary>
        public IReadOnlyList<string> EvaluationSegmentNames
        {
            get
            {
                var names = new List<string> { SegmentNames.Validation };
                foreach (SegmentSpec extra in ExtraSegments) names.Add(extra.Name);
                names.Add(SegmentNames.Test);
                return names;
            }
        }

        /// <summary>
        /// 按名字找一个评估段（validation、test 或配置里的额外段），不分大小写。
        /// 找不到时返回 false；train 不是评估段，也返回 false。
        /// </summary>
        public bool TryGetEvaluationSegment(string name, out SegmentSpec segment)
        {
            segment = default;
            if (string.IsNullOrEmpty(name)) return false;
            string key = name.ToLowerInvariant();
            if (key == SegmentNames.Validation)
            {
                segment = Validation;
                return true;
            }
            if (key == SegmentNames.Test)
            {
                segment = Test;
                return true;
            }
            foreach (SegmentSpec extra in ExtraSegments)
            {
                if (extra.Name != key) continue;
                segment = extra;
                return true;
            }
            return false;
        }
    }
}
