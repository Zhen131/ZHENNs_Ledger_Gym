using System;
using System.Collections.Generic;
using Gym.Core.Market;

namespace Gym.Runtime.Configuration
{
    /// <summary>载入并检查过的配置，连同它的数据。</summary>
    public sealed class GymSettings
    {
        public GymConfig Config;
        public string ConfigPath;
        public string SymbolsPath;
        public string DataPath;
        public SymbolRules Rules;
        public CandleSeries Series;
        public SegmentSpec Train;
        public SegmentSpec Validation;
        public SegmentSpec Test;
        /// <summary>配置里 extraSegments 列出的评估段，按配置里的顺序。</summary>
        public IReadOnlyList<SegmentSpec> ExtraSegments = Array.Empty<SegmentSpec>();
        public DateTime PlayStart;
        public int PlayStartIndex;
        public GymMode Mode;
        /// <summary>-gymSegment 为评估选的分段（任何一个评估段的名字；默认 test）。</summary>
        public SegmentSpec EvalSegment;
        /// <summary>来自 -gymFeeRate / -gymFixedFee / -gymSlippage 的成本；没给时为 null。</summary>
        public double? FeeRateArg;
        public double? FixedFeeArg;
        public double? SlippageArg;
        public IReadOnlyList<string> Warnings;

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
