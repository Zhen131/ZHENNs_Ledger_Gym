using System;

namespace Gym.Runtime.Configuration
{
    /// <summary>The shape of symbols.json.</summary>
    [Serializable]
    public class SymbolTable
    {
        public SymbolEntry[] symbols;
    }
}
