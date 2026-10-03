namespace Gym.Core.Env
{
    /// <summary>Why an episode stopped. Both are time limits, not a real terminal state.</summary>
    public enum EndReason
    {
        None = 0,
        /// <summary>Training episode reached its fixed length.</summary>
        EpisodeLength = 1,
        /// <summary>The segment ran out of candles.</summary>
        SegmentEnd = 2,
    }
}
