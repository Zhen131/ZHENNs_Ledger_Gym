using System;

namespace Gym.Runtime.Configuration
{
    /// <summary>The shape of gym-config.json.</summary>
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
        public string playStart;
    }
}
