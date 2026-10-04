using Gym.Runtime.Play;
using NUnit.Framework;

namespace Gym.Tests.Runtime.Play
{
    public class FloatingAmountTests
    {
        [Test]
        public void MoneySpent_RisesFadesAndEndsOnTime()
        {
            var f = new FloatingAmount("-$10.00", FloatDirection.RiseAndFade, 0f, 1.5f, 1.2f);
            Assert.AreEqual(0f, f.Y);
            Assert.AreEqual(1f, f.Alpha);
            float lastY = f.Y, lastAlpha = f.Alpha;
            for (int i = 0; i < 11; i++)
            {
                f.Advance(0.1f);
                Assert.Greater(f.Y, lastY, $"step {i}: moves up");
                Assert.Less(f.Alpha, lastAlpha, $"step {i}: fades");
                Assert.IsFalse(f.Finished, $"step {i}");
                lastY = f.Y;
                lastAlpha = f.Alpha;
            }
            f.Advance(0.1f);
            Assert.IsTrue(f.Finished);
            Assert.AreEqual(1.5f, f.Y, 1e-5);
            Assert.AreEqual(0f, f.Alpha, 1e-5);
        }

        [Test]
        public void MoneyReceived_FallsStaysOpaqueAndEndsAtThePocket()
        {
            var f = new FloatingAmount("+$10.00", FloatDirection.FallIntoPocket, 1.5f, 0f, 1.2f);
            float lastY = f.Y;
            for (int i = 0; i < 11; i++)
            {
                f.Advance(0.1f);
                Assert.Less(f.Y, lastY, $"step {i}: moves down");
                Assert.AreEqual(1f, f.Alpha, $"step {i}: stays opaque");
                lastY = f.Y;
            }
            f.Advance(0.5f);
            Assert.IsTrue(f.Finished);
            Assert.AreEqual(0f, f.Y, 1e-5, "it ends at the pocket");
        }

        [Test]
        public void HalfWay_IsBetweenStartAndEnd()
        {
            var rise = new FloatingAmount("-$1.00", FloatDirection.RiseAndFade, 0f, 1f, 1f);
            var fall = new FloatingAmount("+$1.00", FloatDirection.FallIntoPocket, 1f, 0f, 1f);
            rise.Advance(0.5f);
            fall.Advance(0.5f);
            Assert.AreEqual(0.5f, rise.Progress);
            Assert.AreEqual(0.75f, rise.Y, 1e-6, "rising slows down");
            Assert.AreEqual(0.5f, rise.Alpha, 1e-6);
            Assert.AreEqual(0.75f, fall.Y, 1e-6, "falling speeds up");
        }

        [Test]
        public void ZeroOrNegativeDuration_IsRefused()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new FloatingAmount("x", FloatDirection.RiseAndFade, 0, 1, 0));
        }
    }
}
