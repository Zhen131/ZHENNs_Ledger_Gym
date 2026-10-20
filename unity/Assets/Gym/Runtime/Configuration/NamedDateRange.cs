using System;

namespace Gym.Runtime.Configuration
{
    /// <summary>gym-config.json 里 extraSegments 的一项：一个带名字的评估段，首尾两天都包括。</summary>
    [Serializable]
    public class NamedDateRange
    {
        public string name;
        public string start;
        public string end;
    }
}
