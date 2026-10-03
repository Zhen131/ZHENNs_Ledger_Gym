namespace Gym.Runtime.Agents
{
    public enum AgentStartMode
    {
        /// <summary>Random start in the training segment, episode length from the config.</summary>
        Training,
        /// <summary>Start in cash at the config's playStart and run to the end of the training segment.</summary>
        PlayFromConfig,
        /// <summary>One full pass over the evaluation segment (-gymSegment, test by default), in cash, no randomness.</summary>
        Evaluation,
    }
}
