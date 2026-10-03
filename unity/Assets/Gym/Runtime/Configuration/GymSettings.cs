using System;
using System.Collections.Generic;
using Gym.Core.Market;

namespace Gym.Runtime.Configuration
{
    /// <summary>A loaded and validated configuration, with its data.</summary>
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
        /// <summary>The segment -gymSegment picks for evaluation (validation or test; test by default).</summary>
        public SegmentSpec EvalSegment;
        /// <summary>Costs from -gymFeeRate / -gymFixedFee / -gymSlippage; null when not given.</summary>
        public double? FeeRateArg;
        public double? FixedFeeArg;
        public double? SlippageArg;
        public IReadOnlyList<string> Warnings;
    }
}
