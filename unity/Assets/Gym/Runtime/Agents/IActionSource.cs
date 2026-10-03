using Unity.MLAgents.Actuators;

namespace Gym.Runtime.Agents
{
    /// <summary>Agent 走 heuristic（键盘）时，能替它填 action 的东西。</summary>
    public interface IActionSource
    {
        void FillActions(in ActionBuffers actionsOut);
    }
}
