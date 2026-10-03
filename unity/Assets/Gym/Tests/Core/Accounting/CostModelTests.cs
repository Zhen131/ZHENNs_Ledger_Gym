using System;
using Gym.Core.Accounting;
using NUnit.Framework;

namespace Gym.Tests.EditMode
{
    public class CostModelTests
    {
        [Test]
        public void T03_CostModelRejectsOutOfRangeValues()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new CostModel(-0.001));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CostModel(1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CostModel(double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CostModel(0.001, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CostModel(0.001, 0, 0.1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CostModel(0.001, 0, -0.0001));
            Assert.DoesNotThrow(() => new CostModel(0.999, 100, 0.0999));
            var defaults = new CostModel();
            Assert.AreEqual(0.001, defaults.FeeRate);
            Assert.AreEqual(0, defaults.FixedFee);
            Assert.AreEqual(0, defaults.Slippage);
        }
    }
}
