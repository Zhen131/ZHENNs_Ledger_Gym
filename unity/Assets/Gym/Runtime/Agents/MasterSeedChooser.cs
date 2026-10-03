using System.Reflection;
using Gym.Core.Env;
using Unity.MLAgents;

namespace Gym.Runtime.Agents
{
    /// <summary>
    /// 选出 Agent 的 master seed。接了 trainer 时，它由 mlagents-learn 发来的 seed（--seed 加上环境的
    /// worker 编号）和 Agent 的编号混合得到，所以一次训练可以重复；没接 trainer 时，它取自时钟。
    /// 两种都要经过 <see cref="SeedMixer"/>。
    /// </summary>
    public static class MasterSeedChooser
    {
        // ML-Agents 4.0.3 里 Academy.InferenceSeed 只能写、不能读，所以 trainer 发来的 seed
        // （握手时存进 m_InferenceSeed）要用反射来读。
        static readonly FieldInfo InferenceSeedField =
            typeof(Academy).GetField("m_InferenceSeed", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>mlagents-learn 发给这个 player 的 seed，前提是能读到。</summary>
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
        /// Agent 的日志行在括号里显示的词："trainer" 或 "clock"，小写（不是枚举成员的名字），
        /// 因为训练脚本会在 player 日志里搜 "(clock)"。
        /// </summary>
        public static string LogName(SeedSource source) => source == SeedSource.Trainer ? "trainer" : "clock";
    }
}
