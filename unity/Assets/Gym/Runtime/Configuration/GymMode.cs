namespace Gym.Runtime.Configuration
{
    /// <summary>Train：在训练段上跑随机起点、720 个 step 的 episode。Eval：在 validation 或 test 上完整走一遍。</summary>
    public enum GymMode
    {
        Train,
        Eval,
    }
}
