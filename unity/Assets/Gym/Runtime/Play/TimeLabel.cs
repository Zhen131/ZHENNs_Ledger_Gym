namespace Gym.Runtime.Play
{
    /// <summary>时间轴上的一个标签：标在可见的第几根 candle 下面（0 是最旧的那根），写什么。</summary>
    public readonly struct TimeLabel
    {
        public readonly int Offset;
        public readonly string Text;

        public TimeLabel(int offset, string text)
        {
            Offset = offset;
            Text = text;
        }
    }
}
