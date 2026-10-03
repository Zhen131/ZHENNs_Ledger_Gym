using System.Linq;
using Gym.Core.Env;
using Gym.Runtime.Agents;
using NUnit.Framework;

namespace Gym.Tests.Runtime.Agents
{
    /// <summary>Agent 的 master seed 从哪里来。</summary>
    public class MasterSeedChooserTests
    {
        [Test]
        public void WithATrainer_TheMasterSeedFollowsTheTrainerSeed()
        {
            var a = MasterSeedChooser.Choose(true, 7, clockTicks: 111, agentIndex: 3);
            var b = MasterSeedChooser.Choose(true, 7, clockTicks: 999_999, agentIndex: 3);
            Assert.AreEqual("trainer", MasterSeedChooser.LogName(a.source));
            Assert.AreEqual(a.seed, b.seed, "the clock does not matter when a trainer is attached");
            Assert.AreEqual(SeedMixer.Mix(7, 3), a.seed);

            var perAgent = Enumerable.Range(0, 16).Select(i => MasterSeedChooser.Choose(true, 7, 0, i).seed).ToList();
            Assert.AreEqual(16, perAgent.Distinct().Count(), "16 agents, 16 different master seeds");

            // mlagents 给第 k 个环境的 seed 是 --seed + k：这两个环境不同。
            Assert.AreNotEqual(MasterSeedChooser.Choose(true, 7000, 0, 0).seed,
                MasterSeedChooser.Choose(true, 7001, 0, 0).seed);
        }

        [Test]
        public void PinnedMlAgents_StillHasTheTrainerSeedField()
        {
            // ML-Agents 4.0.3 里 Academy.InferenceSeed 没有 getter；MasterSeedChooser 读的是它写入的那个私有字段。
            // 哪次升级 ML-Agents 把这个字段改了名，这里就必须失败。
            Assert.IsTrue(MasterSeedChooser.CanReadTrainerSeed, "Academy.m_InferenceSeed (int) not found");
            Assert.IsFalse(MasterSeedChooser.TryReadTrainerSeed(null, out _));
        }

        [Test]
        public void WithoutATrainer_TheSeedComesFromTheMixedClock()
        {
            var a = MasterSeedChooser.Choose(false, 7, clockTicks: 111, agentIndex: 3);
            var b = MasterSeedChooser.Choose(false, 7, clockTicks: 112, agentIndex: 3);
            Assert.AreEqual("clock", MasterSeedChooser.LogName(a.source));
            Assert.AreNotEqual(a.seed, b.seed);
            Assert.AreEqual(SeedMixer.Mix(unchecked(111 + 7919 * 4)), a.seed);
            Assert.GreaterOrEqual(a.seed, 0);
        }
    }
}
