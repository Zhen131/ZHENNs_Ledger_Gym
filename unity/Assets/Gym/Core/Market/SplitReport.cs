using System.Collections.Generic;

namespace Gym.Core.Market
{
    public sealed class SplitReport
    {
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Warnings = new List<string>();
        public bool IsValid => Errors.Count == 0;
    }
}
