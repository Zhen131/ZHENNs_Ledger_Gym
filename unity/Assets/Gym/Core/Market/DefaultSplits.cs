namespace Gym.Core
{
    /// <summary>The three segments of PRD D-14.</summary>
    public static class DefaultSplits
    {
        public static readonly SegmentSpec Train = SegmentSpec.Parse("train", "2017-08-17", "2024-08-31");
        public static readonly SegmentSpec Validation = SegmentSpec.Parse("validation", "2024-09-01", "2025-08-31");
        public static readonly SegmentSpec Test = SegmentSpec.Parse("test", "2025-09-01", "2026-08-31");
    }
}
