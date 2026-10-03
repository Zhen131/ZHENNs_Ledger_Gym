using System;

namespace Gym.Runtime
{
    [Serializable]
    public class SymbolEntry
    {
        public string symbol;
        public double minNotional;
        /// <summary>A string so that the step keeps its exact decimal value.</summary>
        public string stepSize;
    }
}
