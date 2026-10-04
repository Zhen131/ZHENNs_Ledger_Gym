using Gym.Runtime.Play;
using NUnit.Framework;

namespace Gym.Tests.Runtime.Play
{
    public class AvatarHintTests
    {
        [Test]
        public void BuySellAndRejected_HaveTextInBothLanguagesWhileHoldAndStandHaveNone()
        {
            Assert.AreEqual("买入 25%", AvatarHint.Text(AvatarMotion.Buy, 0.25, PlayLanguage.Chinese));
            Assert.AreEqual("BUY 25%", AvatarHint.Text(AvatarMotion.Buy, 0.25, PlayLanguage.English));
            Assert.AreEqual("卖出 50%", AvatarHint.Text(AvatarMotion.Sell, 0.5, PlayLanguage.Chinese));
            Assert.AreEqual("SELL 50%", AvatarHint.Text(AvatarMotion.Sell, 0.5, PlayLanguage.English));
            Assert.AreEqual("被拒", AvatarHint.Text(AvatarMotion.Rejected, 0.5, PlayLanguage.Chinese));
            Assert.AreEqual("REJECTED", AvatarHint.Text(AvatarMotion.Rejected, 0.5, PlayLanguage.English));
            Assert.IsNull(AvatarHint.Text(AvatarMotion.Hold, 0.25, PlayLanguage.Chinese));
            Assert.IsNull(AvatarHint.Text(AvatarMotion.Stand, 0.25, PlayLanguage.English));
        }

        [Test]
        public void AFractionThatIsNotAWholePercent_IsWrittenLikeTheReadoutPanel()
        {
            Assert.AreEqual("买入 37.5%", AvatarHint.Text(AvatarMotion.Buy, 0.375, PlayLanguage.Chinese));
            Assert.AreEqual("SELL 12.3%", AvatarHint.Text(AvatarMotion.Sell, 0.12345, PlayLanguage.English));
            Assert.AreEqual(HudReadout.Percent(0.12345), "12.3%", "same format as the Last action row");
        }
    }
}
