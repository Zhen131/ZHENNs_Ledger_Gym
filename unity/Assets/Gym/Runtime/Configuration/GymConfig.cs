using System;

namespace Gym.Runtime.Configuration
{
    /// <summary>gym-config.json 的结构。</summary>
    [Serializable]
    public class GymConfig
    {
        public string symbol;
        public string dataFile;
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
