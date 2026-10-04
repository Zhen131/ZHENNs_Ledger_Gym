using UnityEngine;

namespace Gym.Runtime.Play
{
    /// <summary>
    /// 小人在动作某一刻的样子：离站立位置偏了多少、放大了多少、歪了几度。纯函数算出来，不碰画面，
    /// 所以测试能直接看动作走到一半是什么样。
    /// </summary>
    public readonly struct AvatarPose
    {
        /// <summary>被拒时一共晃几个半来回：取半整数次，正好在动作一半时晃到最远，好认也好测。</summary>
        const float ShakeHalfSwings = 5f;

        public AvatarPose(Vector2 offset, float scale, float tiltDegrees)
        {
            Offset = offset;
            Scale = scale;
            TiltDegrees = tiltDegrees;
        }

        /// <summary>离站立位置的偏移，世界单位；y 向上。</summary>
        public readonly Vector2 Offset;
        public readonly float Scale;
        /// <summary>往右歪的角度。</summary>
        public readonly float TiltDegrees;

        public static AvatarPose Rest => new AvatarPose(Vector2.zero, 1f, 0f);

        /// <summary>
        /// <paramref name="motion"/> 做到 <paramref name="progress"/>（0 刚开始，1 做完）时的样子。
        /// 跳、变大、歪都按半个正弦走：开始和结束时为 0，一半时最大；被拒的左右晃也在两头收成 0。
        /// </summary>
        public static AvatarPose At(AvatarMotion motion, float progress, AvatarMotionStyle style)
        {
            float p = Mathf.Clamp01(progress);
            float bump = Mathf.Sin(Mathf.PI * p);
            var hop = new Vector2(0f, style.HopHeight * bump);
            switch (motion)
            {
                case AvatarMotion.Hold:
                    return new AvatarPose(hop, 1f, 0f);
                case AvatarMotion.Buy:
                    return new AvatarPose(hop, 1f + style.BuyGrowth * bump, 0f);
                case AvatarMotion.Sell:
                    return new AvatarPose(hop, 1f, style.SellLeanDegrees * bump);
                case AvatarMotion.Rejected:
                    float sideways = style.ShakeDistance * Mathf.Sin(ShakeHalfSwings * Mathf.PI * p) * bump;
                    return new AvatarPose(new Vector2(sideways, 0f), 1f, 0f);
                default:
                    return Rest;
            }
        }
    }
}
