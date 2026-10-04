namespace Gym.Runtime.Play
{
    /// <summary>小人动作的几个数：做一个动作要几秒、跳多高、买入变大多少、卖出歪几度、被拒晃多远。</summary>
    public readonly struct AvatarMotionStyle
    {
        public AvatarMotionStyle(float seconds, float hopHeight, float buyGrowth, float sellLeanDegrees, float shakeDistance)
        {
            Seconds = seconds;
            HopHeight = hopHeight;
            BuyGrowth = buyGrowth;
            SellLeanDegrees = sellLeanDegrees;
            ShakeDistance = shakeDistance;
        }

        public readonly float Seconds;
        /// <summary>跳到最高时离站立的高度，世界单位。</summary>
        public readonly float HopHeight;
        /// <summary>买入跳到最高时放大的比例（0.15 = 大 15 %）。</summary>
        public readonly float BuyGrowth;
        /// <summary>卖出跳到最高时往右歪的角度。</summary>
        public readonly float SellLeanDegrees;
        /// <summary>被拒时左右晃的最大距离，世界单位。</summary>
        public readonly float ShakeDistance;
    }
}
