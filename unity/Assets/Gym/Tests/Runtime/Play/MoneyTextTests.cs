using Gym.Runtime.Play;
using NUnit.Framework;

namespace Gym.Tests.Runtime.Play
{
    public class MoneyTextTests
    {
        [Test]
        public void Cash_IsWrittenWithADollarSignThousandsSeparatorsAndCents()
        {
            Assert.AreEqual("$1,900.00", MoneyText.Amount(1900));
            Assert.AreEqual("$10,000.00", MoneyText.Amount(10_000));
            Assert.AreEqual("$7,500.38", MoneyText.Amount(7500.3841));
            Assert.AreEqual("$0.00", MoneyText.Amount(0));
        }

        [Test]
        public void ProfitAndLoss_CarryASignAndZeroCountsAsGain()
        {
            Assert.AreEqual("+$12.34", MoneyText.Signed(12.34));
            Assert.AreEqual("-$5.60", MoneyText.Signed(-5.6));
            Assert.AreEqual("-$1,234.57", MoneyText.Signed(-1234.567));
            Assert.AreEqual("+$0.00", MoneyText.Signed(0));
            Assert.IsTrue(MoneyText.IsGainOrZero(0));
            Assert.IsTrue(MoneyText.IsGainOrZero(12.34));
            Assert.IsFalse(MoneyText.IsGainOrZero(-5.6));
        }

        [Test]
        public void LessThanHalfACentBelowZero_ShowsAsGreenZeroNotMinusZero()
        {
            Assert.AreEqual("+$0.00", MoneyText.Signed(-0.004));
            Assert.IsTrue(MoneyText.IsGainOrZero(-0.004));
            Assert.AreEqual("-$0.01", MoneyText.Signed(-0.005));
        }

        [Test]
        public void SpentAndReceived_UseTheKeyboardMinusAndPlus()
        {
            Assert.AreEqual("-$2,499.62", MoneyText.Spent(2499.6159));
            Assert.AreEqual("-$2,499.62", MoneyText.Spent(-2499.6159));
            Assert.AreEqual("+$1,250.21", MoneyText.Received(1250.2068));
            foreach (string text in new[] { MoneyText.Spent(1), MoneyText.Signed(-1) })
                foreach (char c in text) Assert.Less(c, 128, $"'{c}' in {text} is plain ASCII");
        }
    }
}
