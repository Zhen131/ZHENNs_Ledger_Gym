namespace Gym.Runtime.Play
{
    /// <summary>头像框小人这一步做什么动作；按这一步的结果选（<see cref="AvatarAnimation.MotionFor"/>）。</summary>
    public enum AvatarMotion
    {
        /// <summary>站着不动：开局、重来，或者上一个动作做完了。</summary>
        Stand,
        /// <summary>不动：只跳一下。</summary>
        Hold,
        /// <summary>买入成交：跳，跳到最高时变大一点。</summary>
        Buy,
        /// <summary>卖出成交：跳，跳到最高时往右歪一下。</summary>
        Sell,
        /// <summary>被拒：不跳，左右晃。和别的动作有意不同，一眼分得出来。</summary>
        Rejected,
    }
}
