using System;

namespace Gym.Runtime.Configuration
{
    /// <summary>gym-config.json 的结构。</summary>
    [Serializable]
    public class GymConfig
    {
        /// <summary>主币：不给 -gymSymbol 时用它。</summary>
        public string symbol;
        /// <summary>主币的小时线文件。</summary>
        public string dataFile;
        /// <summary>
        /// 这份配置认识的所有币（主币也要列在里面，数据文件和上面的 dataFile 是同一个），每只币有自己的数据文件，
        /// evalOnly 的只考不训。-gymSymbol 从这里面选当前币。空或没有时等于只有主币。
        /// JsonUtility 不支持字典，所以用数组。
        /// </summary>
        public SymbolDataEntry[] symbols;
        /// <summary>
        /// 训练用的币（混训的清单）：symbols 里的币名（不分大小写），不许有 evalOnly 的，不许重复。训练时每局从里面选一只，
        /// 选哪只由这一局的 seed 定。空或没有时只用当前币（-gymSymbol 选的那只，没给时是主币）。
        /// 命令行 -gymTrainSymbols 盖过它。
        /// </summary>
        public string[] trainSymbols;
        public double initialCash;
        public int episodeLength;
        public double randomInitialPositionShare;
        public DateRange train;
        public DateRange validation;
        public DateRange test;
        /// <summary>
        /// validation、test 之外的评估段（例如 validation-bear、validation-recovery），每个有自己的名字和日期。
        /// 评估模式下 -gymSegment 可以写它们的名字。JsonUtility 不支持字典，所以用数组。
        /// </summary>
        public NamedDateRange[] extraSegments;
        public string playStart;
        /// <summary>
        /// 观测集：base（第一轮的 35 个数）、long（再加 6 个长周期的数）或 long-ta（再加 MACD、RSI）。
        /// 空或没有时是 base。命令行 -gymObs 盖过它，训练器的环境参数 obs_set 又盖过 -gymObs。
        /// </summary>
        public string observationSet;
    }
}
