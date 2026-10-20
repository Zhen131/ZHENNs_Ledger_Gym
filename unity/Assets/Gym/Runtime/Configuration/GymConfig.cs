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
    }
}
