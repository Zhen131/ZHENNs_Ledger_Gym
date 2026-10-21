using Gym.Core.Env;
using NUnit.Framework;

namespace Gym.Tests.Core.Env
{
    /// <summary>
    /// 调仓规则（从 Baselines.RuleOrder 搬来，带宽成了参数）。带宽 0.10 那组照抄 BaselinesTests 里的原用例，
    /// 再加带宽 0.05 一组（旋钮模式和两个对手用的带宽）。
    /// </summary>
    public class RebalancerTests
    {
        static void AssertOrder(TradeAction action, double fraction, (TradeAction action, double fraction) actual, string what)
        {
            Assert.AreEqual(action, actual.action, what);
            Assert.AreEqual(fraction, actual.fraction, 1e-12, what);
        }

        [Test]
        public void BandTenPoints_MovesTowardTheTargetOnlyOutsideTheBand()
        {
            const double band = 0.10;
            // 现金 6000、币值 4000：p = 0.4
            AssertOrder(TradeAction.Buy, 1, Rebalancer.Order(1, 6000, 4000, band), "target 1: min(1, 0.6 × 10000 ÷ 6000)");
            AssertOrder(TradeAction.Buy, 1.0 / 3, Rebalancer.Order(0.6, 6000, 4000, band), "target 0.6: 0.2 × 10000 ÷ 6000");
            AssertOrder(TradeAction.Hold, 0, Rebalancer.Order(0.45, 6000, 4000, band), "target 0.45: within the band");
            AssertOrder(TradeAction.Hold, 0, Rebalancer.Order(0.35, 6000, 4000, band), "target 0.35: within the band");
            AssertOrder(TradeAction.Sell, 0.375, Rebalancer.Order(0.25, 6000, 4000, band), "target 0.25: 0.15 ÷ 0.4");
            AssertOrder(TradeAction.Sell, 1, Rebalancer.Order(0, 6000, 4000, band), "target 0: sell everything");
            AssertOrder(TradeAction.Hold, 0, Rebalancer.Order(0, 10000, 0, band), "in cash, target 0");
        }

        [Test]
        public void BandFivePoints_MovesTowardTheTargetOnlyOutsideTheBand()
        {
            const double band = 0.05;
            // 现金 6000、币值 4000：p = 0.4
            AssertOrder(TradeAction.Buy, 1, Rebalancer.Order(1, 6000, 4000, band), "target 1: min(1, 0.6 × 10000 ÷ 6000)");
            AssertOrder(TradeAction.Buy, 1.0 / 3, Rebalancer.Order(0.6, 6000, 4000, band), "target 0.6: 0.2 × 10000 ÷ 6000");
            AssertOrder(TradeAction.Buy, 0.07 * 10000 / 6000, Rebalancer.Order(0.47, 6000, 4000, band),
                "target 0.47: outside 5 points (inside 10), 0.07 × 10000 ÷ 6000");
            AssertOrder(TradeAction.Hold, 0, Rebalancer.Order(0.44, 6000, 4000, band), "target 0.44: within the band");
            AssertOrder(TradeAction.Hold, 0, Rebalancer.Order(0.36, 6000, 4000, band), "target 0.36: within the band");
            AssertOrder(TradeAction.Sell, 0.25, Rebalancer.Order(0.3, 6000, 4000, band), "target 0.3: outside 5 points (inside 10), 0.1 ÷ 0.4");
            AssertOrder(TradeAction.Sell, 1, Rebalancer.Order(0, 6000, 4000, band), "target 0: sell everything");
            AssertOrder(TradeAction.Buy, 0.5, Rebalancer.Order(0.5, 10000, 0, band), "in cash, target 0.5: buy half the cash");
            AssertOrder(TradeAction.Hold, 0, Rebalancer.Order(0.3, 7000, 3000, band), "on the target");
        }

        [Test]
        public void WithoutCashOrEquity_TheEdgeCasesStayAsBefore()
        {
            // 现金不为正时买入比例给 1；总资产不为正时不动
            AssertOrder(TradeAction.Buy, 1, Rebalancer.Order(1.2, 0, 10000, 0.05), "no cash: fraction 1");
            AssertOrder(TradeAction.Hold, 0, Rebalancer.Order(0.5, 0, 0, 0.05), "no equity");
            AssertOrder(TradeAction.Hold, 0, Rebalancer.Order(0.5, -1, 0, 0.10), "negative equity");
        }

        [Test]
        public void BaselinesRuleOrder_IsTheRuleWithTheTenPointBand()
        {
            for (int i = 0; i <= 20; i++)
            {
                double target = i / 20.0;
                (TradeAction action, double fraction) a = Gym.Core.Evaluation.Baselines.RuleOrder(target, 6000, 4000);
                (TradeAction action, double fraction) b = Rebalancer.Order(target, 6000, 4000, 0.10);
                Assert.AreEqual(b.action, a.action, $"target {target}");
                Assert.IsTrue(TestData.SameBits(b.fraction, a.fraction), $"target {target}");
            }
            Assert.AreEqual(0.10, Gym.Core.Evaluation.Baselines.RebalanceBand);
        }
    }
}
