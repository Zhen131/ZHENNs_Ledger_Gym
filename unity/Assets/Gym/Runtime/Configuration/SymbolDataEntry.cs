using System;

namespace Gym.Runtime.Configuration
{
    /// <summary>
    /// gym-config.json 里 symbols 的一项：一只币、它的小时线文件，以及它是不是只考不训。
    /// 下单规则不在这里，按 symbol 去 symbols.json 里找。
    /// </summary>
    [Serializable]
    public class SymbolDataEntry
    {
        public string symbol;
        /// <summary>和顶层的 dataFile 一样：相对路径先找配置文件旁边，再找 StreamingAssets/Gym。</summary>
        public string dataFile;
        /// <summary>true：这只币只用来评估（没见过的卷子），混训的清单不许有它（15 批检查）。</summary>
        public bool evalOnly;
    }
}
