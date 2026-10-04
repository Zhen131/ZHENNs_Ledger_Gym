using System;

namespace Gym.Runtime.Play
{
    /// <summary>
    /// 一条飘字的动画：只记位置、透明度和走了多久，不碰画面，所以测试能直接拨时钟看它走到哪了。
    /// 往上飘的先快后慢，往下掉的先慢后快（像掉下去）。
    /// </summary>
    public sealed class FloatingAmount
    {
        public FloatingAmount(string text, FloatDirection direction, float startY, float endY, float duration)
        {
            if (!(duration > 0)) throw new ArgumentOutOfRangeException(nameof(duration), duration, "Must be > 0.");
            Text = text;
            Direction = direction;
            StartY = startY;
            EndY = endY;
            Duration = duration;
        }

        public string Text { get; }
        public FloatDirection Direction { get; }
        public float StartY { get; }
        public float EndY { get; }
        public float Duration { get; }
        public float Age { get; private set; }

        /// <summary>0 刚出现，1 走完。</summary>
        public float Progress => Math.Min(Age / Duration, 1f);
        public bool Finished => Age >= Duration;

        public float Y
        {
            get
            {
                float p = Progress;
                float eased = Direction == FloatDirection.RiseAndFade ? 1 - (1 - p) * (1 - p) : p * p;
                return StartY + (EndY - StartY) * eased;
            }
        }

        /// <summary>往上飘的越来越透明；往下掉的一直不透明，到口袋才消失。</summary>
        public float Alpha => Direction == FloatDirection.RiseAndFade ? 1 - Progress : 1f;

        public void Advance(float seconds)
        {
            if (seconds > 0) Age += seconds;
        }
    }
}
