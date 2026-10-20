namespace Gym.Runtime.Configuration
{
    /// <summary>Train：在训练段上跑随机起点、720 个 step 的 episode。Eval：在配置里的某个评估段上完整走一遍。</summary>
    public enum GymMode
    {
        Train,
        Eval,
    }
}
