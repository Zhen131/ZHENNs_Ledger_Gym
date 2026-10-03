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
        public DateTime PlayStart;
        public int PlayStartIndex;
        public GymMode Mode;
        /// <summary>-gymSegment 为评估选的分段（validation 或 test；默认 test）。</summary>
        public SegmentSpec EvalSegment;
        /// <summary>来自 -gymFeeRate / -gymFixedFee / -gymSlippage 的成本；没给时为 null。</summary>
        public double? FeeRateArg;
        public double? FixedFeeArg;
        public double? SlippageArg;
        public IReadOnlyList<string> Warnings;
    }
}
