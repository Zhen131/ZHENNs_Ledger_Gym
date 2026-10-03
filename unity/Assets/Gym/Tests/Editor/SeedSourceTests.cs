using System.Linq;
using Gym.Core.Env;
using Gym.Runtime;
using NUnit.Framework;

namespace Gym.Tests.Editor
{
    /// <summary>Q07: where an agent's master seed comes from.</summary>
    public class SeedSourceTests
    {
        [Test]
        public void Q07_WithATrainerTheMasterSeedFollowsTheTrainerSeed()
        {
            var a = TradingAgent.ChooseMasterSeed(true, 7, clockTicks: 111, agentIndex: 3);
            var b = TradingAgent.ChooseMasterSeed(true, 7, clockTicks: 999_999, agentIndex: 3);
            Assert.AreEqual("trainer", a.source);
            Assert.AreEqual(a.seed, b.seed, "the clock does not matter when a trainer is attached");
            Assert.AreEqual(SeedMixer.Mix(7, 3), a.seed);

            var perAgent = Enumerable.Range(0, 16).Select(i => TradingAgent.ChooseMasterSeed(true, 7, 0, i).seed).ToList();
            Assert.AreEqual(16, perAgent.Distinct().Count(), "16 agents, 16 different master seeds");

            // mlagents gives environment k the seed --seed + k: the two environments differ.
            Assert.AreNotEqual(TradingAgent.ChooseMasterSeed(true, 7000, 0, 0).seed,
                TradingAgent.ChooseMasterSeed(true, 7001, 0, 0).seed);
        }

        [Test]
        public void Q08_TheTrainerSeedFieldExistsInThePinnedMlAgents()
        {
            // Academy.InferenceSeed has no getter in ML-Agents 4.0.3; TradingAgent reads the
            // private field it sets. An ML-Agents upgrade that renames it must fail here.
            Assert.IsTrue(TradingAgent.CanReadTrainerSeed, "Academy.m_InferenceSeed (int) not found");
            Assert.IsFalse(TradingAgent.TryReadTrainerSeed(null, out _));
        }

        [Test]
        public void Q07_WithoutATrainerTheClockIsUsedAndMixed()
        {
            var a = TradingAgent.ChooseMasterSeed(false, 7, clockTicks: 111, agentIndex: 3);
            var b = TradingAgent.ChooseMasterSeed(false, 7, clockTicks: 112, agentIndex: 3);
            Assert.AreEqual("clock", a.source);
            Assert.AreNotEqual(a.seed, b.seed);
            Assert.AreEqual(SeedMixer.Mix(unchecked(111 + 7919 * 4)), a.seed);
            Assert.GreaterOrEqual(a.seed, 0);
        }
    }
}
