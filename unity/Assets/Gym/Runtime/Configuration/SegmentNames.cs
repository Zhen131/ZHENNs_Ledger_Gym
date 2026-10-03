namespace Gym.Runtime.Configuration
{
    /// <summary>
    /// The names of the three segments. They are the words -gymSegment takes on the command line
    /// and the segment names written into log.csv, the detail JSON and its file name, so they stay
    /// lower case.
    /// </summary>
    public static class SegmentNames
    {
        public const string Train = "train";
        public const string Validation = "validation";
        public const string Test = "test";
    }
}
