namespace Gym.Runtime.Agents
{
    public enum AgentStartMode
    {
        /// <summary>在训练段里随机起步，episode 长度取自配置。</summary>
        Training,
        /// <summary>从配置里的 playStart 开始，全是现金，一直运行到训练段结束。</summary>
        PlayFromConfig,
        /// <summary>在评估用的分段（-gymSegment，默认 test）上完整走一遍，全是现金，没有随机。</summary>
        Evaluation,
    }
}
