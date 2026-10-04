using System;
using Gym.Core.Env;

namespace Gym.Runtime.Play
{
    /// <summary>
    /// 小人正在做的动作和做到哪了。时钟由调用方拨（<see cref="Advance"/>），所以测试和截图工具能停在动作的
    /// 任何一刻。新动作来了立刻换上、从头开始，不排队；做完回到站着不动。
    /// </summary>
    public sealed class AvatarAnimation
    {
        public AvatarAnimation(AvatarMotionStyle style)
        {
            if (!(style.Seconds > 0))
                throw new ArgumentOutOfRangeException(nameof(style), style.Seconds, "A motion must take more than 0 seconds.");
            Style = style;
        }

        public AvatarMotionStyle Style { get; }
        public AvatarMotion Motion { get; private set; } = AvatarMotion.Stand;
        public float Age { get; private set; }

        /// <summary>0 刚开始，1 做完；站着不动时为 0。</summary>
        public float Progress => Motion == AvatarMotion.Stand ? 0f : Math.Min(Age / Style.Seconds, 1f);

        public AvatarPose Pose => AvatarPose.At(Motion, Progress, Style);

        /// <summary>开始一个新动作。正在做的那个立刻放弃，进度从 0 开始。</summary>
        public void Start(AvatarMotion motion)
        {
            Motion = motion;
            Age = 0f;
        }

        /// <summary>回到站着不动。</summary>
        public void Stop() => Start(AvatarMotion.Stand);

        public void Advance(float seconds)
        {
            if (Motion == AvatarMotion.Stand || !(seconds > 0)) return;
            Age += seconds;
            if (Age >= Style.Seconds) Stop();
        }

        /// <summary>这一步该做哪个动作：被拒的晃，成交的买入、卖出各有各的跳法，其余（不动）只跳。</summary>
        public static AvatarMotion MotionFor(TradeAction action, StepResult result)
        {
            if (result.Rejected) return AvatarMotion.Rejected;
            if (!result.Traded) return AvatarMotion.Hold;
            return action == TradeAction.Sell ? AvatarMotion.Sell : AvatarMotion.Buy;
        }
    }
}
