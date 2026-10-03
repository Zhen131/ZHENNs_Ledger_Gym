namespace Gym.Runtime.Agents
{
    /// <summary>Where an agent's master seed came from. <see cref="MasterSeedChooser.LogName"/> gives the word the log shows.</summary>
    public enum SeedSource
    {
        /// <summary>The seed mlagents-learn sent (--seed).</summary>
        Trainer,
        /// <summary>The clock, when no trainer is attached or its seed cannot be read.</summary>
        Clock,
    }
}
