using System;

namespace Gym.Core.Env
{
    /// <summary>
    /// 观测集：Agent 每一步看到哪些数。三档的大小和历史长度见 <see cref="ObservationSets"/>，
    /// 每个数怎么算见 <see cref="ObservationBuilder"/>。数值就是训练器环境参数 obs_set 的取值。
    /// </summary>
    public enum ObservationSet
    {
        /// <summary>第一轮的 35 个数：32 根价格、仓位、浮盈、距上次交易。</summary>
        Base = 0,
        /// <summary>base 再加 6 个长周期的数（35～40 号）。</summary>
        Long = 1,
        /// <summary>long 再加 MACD、RSI 两个连续值（41、42 号），只当对照组。</summary>
        LongTa = 2,
    }

    /// <summary>三档观测集的名字、大小和历史长度，以及「模型的输入维数和观测集对不对得上」的检查。</summary>
    public static class ObservationSets
    {
        public const string BaseName = "base";
        public const string LongName = "long";
        public const string LongTaName = "long-ta";

        /// <summary>按 0、1、2 的顺序。</summary>
        public static readonly ObservationSet[] All = { ObservationSet.Base, ObservationSet.Long, ObservationSet.LongTa };

        /// <summary>报错时列出能用的名字。</summary>
        public const string NamesText = BaseName + ", " + LongName + " or " + LongTaName;

        /// <summary>报错时列出环境参数 obs_set 能用的值。</summary>
        public const string ParameterValuesText = "0 (" + BaseName + "), 1 (" + LongName + ") or 2 (" + LongTaName + ")";

        /// <summary>命令行和配置里的名字：base、long、long-ta。</summary>
        public static string Name(this ObservationSet set)
        {
            switch (set)
            {
                case ObservationSet.Base: return BaseName;
                case ObservationSet.Long: return LongName;
                case ObservationSet.LongTa: return LongTaName;
                default: throw new ArgumentOutOfRangeException(nameof(set), set, "Not an observation set.");
            }
        }

        /// <summary>这一档每一步写几个数：base 35、long 41、long-ta 43。</summary>
        public static int Size(this ObservationSet set)
        {
            switch (set)
            {
                case ObservationSet.Base: return ObservationBuilder.Size;
                case ObservationSet.Long: return ObservationBuilder.LongSize;
                case ObservationSet.LongTa: return ObservationBuilder.LongTaSize;
                default: throw new ArgumentOutOfRangeException(nameof(set), set, "Not an observation set.");
            }
        }

        /// <summary>
        /// 算齐这一档所有的数最少要几根更早的 K 线，也就是最早能决策的下标：base 32、long 和 long-ta 2160。
        /// 和价格窗口（三档都是 32）不是一回事。
        /// </summary>
        public static int HistoryLength(this ObservationSet set)
        {
            switch (set)
            {
                case ObservationSet.Base: return ObservationBuilder.Lookback;
                case ObservationSet.Long:
                case ObservationSet.LongTa: return ObservationBuilder.LongHistory;
                default: throw new ArgumentOutOfRangeException(nameof(set), set, "Not an observation set.");
            }
        }

        /// <summary>按名字找一档，不分大小写；不是 base、long、long-ta 时返回 false。</summary>
        public static bool TryParse(string name, out ObservationSet set)
        {
            set = ObservationSet.Base;
            if (string.IsNullOrEmpty(name)) return false;
            foreach (ObservationSet candidate in All)
            {
                if (!string.Equals(candidate.Name(), name, StringComparison.OrdinalIgnoreCase)) continue;
                set = candidate;
                return true;
            }
            return false;
        }

        /// <summary>环境参数 obs_set 的值：恰好是 0、1、2 时返回 true，别的值（1.5、3、−1、无穷大、NaN）返回 false。</summary>
        public static bool TryFromParameter(double value, out ObservationSet set)
        {
            set = ObservationSet.Base;
            foreach (ObservationSet candidate in All)
            {
                if (value != (int)candidate) continue;
                set = candidate;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 模型的向量观测输入维数和观测集的大小对不对得上：对得上返回 null，对不上返回一句写明两个数的英文报错。
        /// 不碰模型本身，评估包和观战都用它。
        /// </summary>
        public static string ModelInputProblem(int modelInputSize, ObservationSet set)
        {
            int size = set.Size();
            if (modelInputSize == size) return null;
            return $"the model takes {modelInputSize} observations but the observation set {set.Name()} has {size}; " +
                   $"use the observation set the model was trained with, or a model trained on {set.Name()}";
        }
    }
}
