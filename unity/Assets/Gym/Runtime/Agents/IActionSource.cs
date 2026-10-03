using Unity.MLAgents.Actuators;

namespace Gym.Runtime.Agents
{
    /// <summary>Something that can fill the agent's actions when it runs on heuristics (the keyboard).</summary>
    public interface IActionSource
    {
        void FillActions(in ActionBuffers actionsOut);
    }
}
