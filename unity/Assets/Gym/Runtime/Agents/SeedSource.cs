namespace Gym.Runtime.Agents
{
    /// <summary>Agent 的 master seed 从哪里来。日志里显示的词由 <see cref="MasterSeedChooser.LogName"/> 给出。</summary>
    public enum SeedSource
    {
        /// <summary>mlagents-learn 发来的 seed（--seed）。</summary>
        Trainer,
        /// <summary>时钟：没接 trainer，或者读不到 trainer 的 seed 时用它。</summary>
        Clock,
    }
}
