using System.Reflection;
using Gym.Core.Env;
using Unity.MLAgents;

namespace Gym.Runtime.Agents
{
    /// <summary>
    /// The agent's master seed (Q07). With a trainer attached it derives from the seed
    /// mlagents-learn sends (--seed, plus the environment's worker index), mixed with the
    /// agent index, so a run can be repeated; without one it comes from the clock as before.
    /// Both pass through <see cref="SeedMixer"/> (Q03).
    /// </summary>
    public static class MasterSeedChooser
    {
        // Academy.InferenceSeed is set-only in ML-Agents 4.0.3, so the seed the trainer sent
        // (stored in m_InferenceSeed during the handshake) is read by reflection (Q08).
        static readonly FieldInfo InferenceSeedField =
            typeof(Academy).GetField("m_InferenceSeed", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>The seed mlagents-learn sent to this player, if it can be read.</summary>
        public static bool TryReadTrainerSeed(Academy academy, out int seed)
        {
            seed = 0;
            if (academy == null || InferenceSeedField == null || InferenceSeedField.FieldType != typeof(int)) return false;
            seed = (int)InferenceSeedField.GetValue(academy);
            return true;
        }

        public static bool CanReadTrainerSeed => InferenceSeedField != null && InferenceSeedField.FieldType == typeof(int);

        public static (int seed, SeedSource source) Choose(bool trainerConnected, int trainerSeed, long clockTicks, int agentIndex)
        {
            if (trainerConnected) return (SeedMixer.Mix(trainerSeed, agentIndex), SeedSource.Trainer);
            return (SeedMixer.Mix(unchecked((int)clockTicks + 7919 * (agentIndex + 1))), SeedSource.Clock);
        }

        /// <summary>
        /// The word the agent's log line shows in brackets: "trainer" or "clock", lower case
        /// (not the enum's name), because the training scripts search the player log for "(clock)".
        /// </summary>
        public static string LogName(SeedSource source) => source == SeedSource.Trainer ? "trainer" : "clock";
    }
}
