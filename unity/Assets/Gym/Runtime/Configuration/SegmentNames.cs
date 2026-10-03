namespace Gym.Runtime.Configuration
{
    /// <summary>
    /// 三个分段的名字。它们就是命令行上 -gymSegment 接受的词，也是写进 log.csv、明细 JSON 和
    /// 明细文件名的分段名，所以保持小写。
    /// </summary>
    public static class SegmentNames
    {
        public const string Train = "train";
        public const string Validation = "validation";
        public const string Test = "test";
    }
}
