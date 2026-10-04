namespace Gym.Runtime.Play
{
    /// <summary>口袋上方的飘字往哪走。</summary>
    public enum FloatDirection
    {
        /// <summary>钱花出去了（买入）：从口袋往上飘，越来越透明。</summary>
        RiseAndFade,
        /// <summary>钱收进来了（卖出）：从上面往下掉，掉到口袋处消失。</summary>
        FallIntoPocket,
    }
}
