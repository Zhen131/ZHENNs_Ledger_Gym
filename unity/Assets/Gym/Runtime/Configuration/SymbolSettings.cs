using Gym.Core.Market;

namespace Gym.Runtime.Configuration
{
    /// <summary>
    /// 一只币载入并检查过的东西：下单规则、数据，以及按它的数据起点裁过的训练段。
    /// 验证段、测试段和额外的评估段所有币共用，在 <see cref="GymSettings"/> 里。
    /// </summary>
    public sealed class SymbolSettings
    {
        public string Symbol;
        /// <summary>配置里的写法（相对路径原样），写进评估记录用。</summary>
        public string DataFile;
        /// <summary>解析后的完整路径。</summary>
        public string DataPath;
        /// <summary>只考不训的币（配置里的 evalOnly）。</summary>
        public bool EvalOnly;
        public SymbolRules Rules;
        public CandleSeries Series;
        /// <summary>
        /// 这只币的训练段：配置里的训练段早于它的第一根 candle 时，起点裁到第一根 candle 那天，
        /// 终点不变；否则就是配置里的训练段。
        /// </summary>
        public SegmentSpec Train;
        /// <summary>训练段的起点被裁过时为 true。</summary>
        public bool TrainClipped;

        public override string ToString() => Symbol;
    }
}
