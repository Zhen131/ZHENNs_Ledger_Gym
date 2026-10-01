using System;
using Gym.Editor;
using NUnit.Framework;

namespace Gym.Tests.Editor
{
    public class EvalToolsTests
    {
        [Test]
        public void Q03_PoliciesArgumentDefaultsToAllThreeAndRejectsUnknownNames()
        {
            CollectionAssert.AreEquivalent(new[] { "buyhold", "cash", "random" }, EvalTools.ParsePolicies(null));
            CollectionAssert.AreEquivalent(new[] { "random" }, EvalTools.ParsePolicies("random"));
            CollectionAssert.AreEquivalent(new[] { "cash", "random" }, EvalTools.ParsePolicies(" Random , cash "));
            Assert.Throws<ArgumentException>(() => EvalTools.ParsePolicies("random,momentum"));
            Assert.Throws<ArgumentException>(() => EvalTools.ParsePolicies(","));
        }
    }
}
