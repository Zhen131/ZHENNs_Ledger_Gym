using System;

namespace Gym.Runtime.Configuration
{
    [Serializable]
    public class SymbolEntry
    {
        public string symbol;
        public double minNotional;
        /// <summary>用字符串，这样步长能保留精确的十进制值。</summary>
        public string stepSize;
    }
}
