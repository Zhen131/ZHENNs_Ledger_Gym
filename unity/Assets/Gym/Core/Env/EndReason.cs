namespace Gym.Core.Env
{
    /// <summary>episode 为什么停下。两种都是时间上限，不是真正的终止状态。</summary>
    public enum EndReason
    {
        None = 0,
        /// <summary>训练 episode 走到了固定长度。</summary>
        EpisodeLength = 1,
        /// <summary>分段里的 candle 用完了。</summary>
        SegmentEnd = 2,
    }
}
