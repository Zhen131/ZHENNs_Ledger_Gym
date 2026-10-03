using System;
using Gym.Core.Accounting;
using Gym.Core.Market;
using NUnit.Framework;

namespace Gym.Tests.Core.Accounting
{
    public class AccountTests
    {
        const double Tol = 1e-9;
        static readonly SymbolRules Btc = SymbolRules.BtcUsdt;
        static readonly SymbolRules Ada = new SymbolRules("ADAUSDT", 5.0, 0.1m);

        // ---- 手算核对过的记账

        [Test]
        public void BuyAQuarterThenSellHalf_BooksTheHandCheckedValues()
        {
            // 在 50,000 买入 10,000 USDT 的 25 %，fee 0.1 %，没有 slippage，没有固定 fee：
            //   B = 10000 × 0.25 = 2500, p = 50000
            //   units = floor(2500 ÷ (50000 × 1.001) ÷ 0.00001) = floor(4995.004995…) = 4995  → 0.04995 BTC
            //   N = 0.04995 × 50000 = 2497.5
            //   F = 2497.5 × 0.001 = 2.4975
            //   Cash = 10000 − 2497.5 − 2.4975 = 7500.0025
            //   AvgCost = (2497.5 + 2.4975) ÷ 0.04995 = 50050
            var a = new Account(Btc, new CostModel(0.001), 10_000);
            Assert.IsTrue(a.TryBuy(0.25, 50_000, out Fill buy));
            Assert.AreEqual(4995, buy.Units);
            Assert.AreEqual(4995, a.CoinUnits);
            Assert.AreEqual(0.04995, a.Quantity, Tol);
            Assert.AreEqual(2497.5, buy.Notional, Tol);
            Assert.AreEqual(2.4975, buy.Fee, Tol);
            Assert.AreEqual(7500.0025, a.Cash, Tol);
            Assert.AreEqual(50_050, a.AvgCost, 1e-7);
            Assert.AreEqual(1, a.Trades);
            Assert.AreEqual(0, a.Rejected);
            Assert.AreEqual(2.4975, a.FeesPaid, Tol);
            Assert.AreEqual(2497.5, a.TurnoverNotional, Tol);
            // 按平均成本算，未实现收益率正好是 −FeeRate。
            Assert.AreEqual(-0.001, a.UnrealizedReturn(50_050), 1e-12);

            // 在 52,000 卖出 50 %：
            //   units = floor(4995 × 0.5) = 2497  → 0.02497 BTC
            //   N = 0.02497 × 52000 = 1298.44,  F = 1.29844
            //   Cash = 7500.0025 + 1298.44 − 1.29844 = 8797.14406
            //   CoinUnits = 2498, AvgCost 不变 = 50050
            //   Equity(52000) = 8797.14406 + 0.02498 × 52000 = 10096.10406
            Assert.IsTrue(a.TrySell(0.5, 52_000, out Fill sell));
            Assert.AreEqual(2497, sell.Units);
            Assert.AreEqual(1298.44, sell.Notional, Tol);
            Assert.AreEqual(1.29844, sell.Fee, Tol);
            Assert.AreEqual(8797.14406, a.Cash, Tol);
            Assert.AreEqual(2498, a.CoinUnits);
            Assert.AreEqual(50_050, a.AvgCost, 1e-7);
            Assert.AreEqual(2, a.Trades);
            Assert.AreEqual(3.79594, a.FeesPaid, Tol);
            Assert.AreEqual(3795.94, a.TurnoverNotional, Tol);
            Assert.AreEqual(10_096.10406, a.Equity(52_000), Tol);
            Assert.AreEqual(1298.96 / 10_096.10406, a.PositionRatio(52_000), 1e-12);
            // (0.02498 × 52000 × 0.999 − 0.02498 × 50050) ÷ (0.02498 × 50050)
            Assert.AreEqual((1298.96 * 0.999 - 0.02498 * 50_050) / (0.02498 * 50_050), a.UnrealizedReturn(52_000), 1e-12);
        }

        [Test]
        public void FiveBasisPointsOfSlippage_MoveBothFillsAgainstTheTrader()
        {
            // fee 0.1 %，slippage 0.0005，按基准价格 50,000 买入 10,000 的 25 %：
            //   p = 50000 × 1.0005 = 50025
            //   units = floor(2500 ÷ (50025 × 1.001) ÷ 0.00001) = floor(4992.508…) = 4992  → 0.04992 BTC
            //   N = 0.04992 × 50025 = 2497.248,  F = 2.497248
            //   Cash = 10000 − 2497.248 − 2.497248 = 7500.254752
            //   AvgCost = 2499.745248 ÷ 0.04992 = 50075.025
            var a = new Account(Btc, new CostModel(0.001, 0, 0.0005), 10_000);
            Assert.IsTrue(a.TryBuy(0.25, 50_000, out Fill buy));
            Assert.AreEqual(4992, buy.Units);
            Assert.AreEqual(50_025, buy.Price, Tol);
            Assert.AreEqual(2497.248, buy.Notional, Tol);
            Assert.AreEqual(2.497248, buy.Fee, Tol);
            Assert.AreEqual(7500.254752, a.Cash, Tol);
            Assert.AreEqual(50_075.025, a.AvgCost, 1e-7);

            // 按基准价格 50,000 全部卖出：p = 50000 × 0.9995 = 49975
            //   N = 0.04992 × 49975 = 2494.752,  F = 2.494752
            //   Cash = 7500.254752 + 2494.752 − 2.494752 = 9992.512
            Assert.IsTrue(a.TrySell(1, 50_000, out Fill sell));
            Assert.AreEqual(4992, sell.Units);
            Assert.AreEqual(49_975, sell.Price, Tol);
            Assert.AreEqual(2494.752, sell.Notional, Tol);
            Assert.AreEqual(9992.512, a.Cash, Tol);
            Assert.AreEqual(0, a.CoinUnits);
            Assert.AreEqual(0, a.AvgCost);
        }

        [Test]
        public void FixedFeeOfOneUsdt_IsChargedOnEveryFill()
        {
            // fee 0.1 % 加每笔订单 1 USDT，在 50,000 买入 10,000 的 25 %：
            //   B − FixedFee = 2500 − 1 = 2499
            //   units = floor(2499 ÷ 50050 ÷ 0.00001) = floor(4993.0069…) = 4993  → 0.04993 BTC
            //   N = 0.04993 × 50000 = 2496.5,  F = 2.4965 + 1 = 3.4965
            //   Cash = 10000 − 2496.5 − 3.4965 = 7500.0035
            //   AvgCost = 2499.9965 ÷ 0.04993
            var a = new Account(Btc, new CostModel(0.001, 1, 0), 10_000);
            Assert.IsTrue(a.TryBuy(0.25, 50_000, out Fill buy));
            Assert.AreEqual(4993, buy.Units);
            Assert.AreEqual(2496.5, buy.Notional, Tol);
            Assert.AreEqual(3.4965, buy.Fee, Tol);
            Assert.AreEqual(7500.0035, a.Cash, Tol);
            Assert.AreEqual(2499.9965 / 0.04993, a.AvgCost, 1e-7);
            // 在 50,000 时的未实现收益率：(0.04993 × 50000 × 0.999 − 1 − 2499.9965) ÷ 2499.9965
            Assert.AreEqual((2496.5 * 0.999 - 1 - 2499.9965) / 2499.9965, a.UnrealizedReturn(50_000), 1e-12);

            // 在 50,000 全部卖出：N = 2496.5, F = 3.4965, Cash = 7500.0035 + 2493.0035 = 9993.007
            Assert.IsTrue(a.TrySell(1, 50_000, out _));
            Assert.AreEqual(9993.007, a.Cash, Tol);
            Assert.AreEqual(6.993, a.FeesPaid, Tol);
        }

        // ---- 最小订单和数量步长

        [Test]
        public void FourUsdtBudget_IsRejectedAndChangesNothingElse()
        {
            // B = 16 × 0.25 = 4 → units = floor(4 ÷ 50050 ÷ 0.00001) = 7 → N = 3.5 < 5 → 被拒绝。
            var a = new Account(Btc, new CostModel(), 16);
            Assert.IsFalse(a.Buy(0.25, 50_000));
            Assert.AreEqual(1, a.Rejected);
            Assert.AreEqual(16, a.Cash);
            Assert.AreEqual(0, a.CoinUnits);
            Assert.AreEqual(0, a.AvgCost);
            Assert.AreEqual(0, a.Trades);
            Assert.AreEqual(0, a.FeesPaid);
            Assert.AreEqual(0, a.TurnoverNotional);
        }

        [Test]
        public void SellBelowTheMinimumOrder_IsRejected()
        {
            // 0.0001 BTC 在 40,000 时值 4 USDT < 5。
            var a = new Account(Btc, new CostModel(), 0, coinUnits: 10, avgCost: 40_000);
            Assert.IsFalse(a.Sell(1, 40_000));
            Assert.AreEqual(1, a.Rejected);
            Assert.AreEqual(10, a.CoinUnits);
            Assert.AreEqual(0, a.Cash);
            Assert.AreEqual(40_000, a.AvgCost);
            // 卖出的比例取整后是零个单位时，也会被拒绝。
            var b = new Account(Btc, new CostModel(), 0, coinUnits: 1000, avgCost: 40_000);
            Assert.IsFalse(b.Sell(0.0005, 40_000));
            Assert.AreEqual(1000, b.CoinUnits);
        }

        [Test]
        public void FixedFeeEatingTheProceeds_RejectsTheSell()
        {
            // N = 0.0002 × 50000 = 10 ≥ 5，但 F = 0.01 + 10 = 10.01 → N − F ≤ 0 → 被拒绝。
            var a = new Account(Btc, new CostModel(0.001, 10), 0, coinUnits: 20, avgCost: 50_000);
            Assert.IsFalse(a.Sell(1, 50_000));
            Assert.AreEqual(20, a.CoinUnits);
        }

        [Test]
        public void BoughtQuantity_RoundsDownToTheStep()
        {
            var a = new Account(Btc, new CostModel(), 1000);
            const double price = 30_000.123;
            Assert.IsTrue(a.TryBuy(1, price, out Fill fill));
            // units = floor(1000 ÷ (30000.123 × 1.001) ÷ 0.00001) = floor(3329.99…) = 3329
            Assert.AreEqual(3329, fill.Units);
            Assert.AreEqual(0.03329, a.Quantity);
            Assert.LessOrEqual(3329 * 0.00001 * price * 1.001, 1000);
            Assert.Greater(3330 * 0.00001 * price * 1.001, 1000);
            Assert.GreaterOrEqual(a.Cash, 0);
        }

        [Test]
        public void StepOfPointOne_UsesExactDecimalDivision()
        {
            // ADA，步长 0.1，fee 0.1 %，在 0.3 用掉全部 30.03 USDT：
            //   units = floor(30.03 ÷ (0.3 × 1.001) ÷ 0.1) = floor(30.03 ÷ 0.3003 ÷ 0.1) = floor(1000) = 1000
            //   N = 100 × 0.3 = 30, F = 0.03, Cash = 30.03 − 30 − 0.03 = 0（在 1e-9 以内，截到 0）
            var a = new Account(Ada, new CostModel(0.001), 30.03);
            Assert.IsTrue(a.TryBuy(1, 0.3, out Fill fill));
            Assert.AreEqual(1000, fill.Units);
            Assert.AreEqual(100.0, a.Quantity);
            Assert.AreEqual(30.0, fill.Notional, Tol);
            Assert.AreEqual(0.03, fill.Fee, Tol);
            Assert.AreEqual(0, a.Cash, Tol);
            Assert.GreaterOrEqual(a.Cash, 0);

            // 在 0.4 卖出 1000 个单位的 0.25 = 250 个单位 = 25 ADA → N = 10。
            Assert.IsTrue(a.TrySell(0.25, 0.4, out Fill sell));
            Assert.AreEqual(250, sell.Units);
            Assert.AreEqual(10.0, sell.Notional, Tol);
            Assert.AreEqual(750, a.CoinUnits);
        }

        [Test]
        public void WhereDoubleWouldLoseAUnit_DecimalFloorKeepsIt()
        {
            // ADA 价格 0.1，fee 0.1 %，用掉全部 5.62562 USDT：
            //   精确值：5.62562 ÷ (0.1 × 1.001) ÷ 0.1 = 5.62562 ÷ 0.1001 ÷ 0.1 = 562
            //   double：5.62562 / (0.1 * 1.001) / 0.1 = 561.999…  → 向下取整会得到 561
            Assert.AreEqual(561, Math.Floor(5.62562 / (0.1 * 1.001) / 0.1), "premise: double loses a unit here");
            var a = new Account(Ada, new CostModel(0.001), 5.62562);
            Assert.IsTrue(a.TryBuy(1, 0.1, out Fill fill));
            Assert.AreEqual(562, fill.Units);
            Assert.AreEqual(5.62, fill.Notional, Tol);
            Assert.AreEqual(0, a.Cash, Tol);
        }

        [Test]
        public void ZeroFraction_IsRejected()
        {
            var a = new Account(Btc, new CostModel(), 10_000);
            Assert.IsFalse(a.Buy(0, 50_000));
            Assert.AreEqual(1, a.Rejected);
            Assert.Throws<ArgumentOutOfRangeException>(() => a.Buy(1.5, 50_000));
            Assert.Throws<ArgumentOutOfRangeException>(() => a.Buy(0.5, 0));
        }

        [Test]
        public void FullBuyWithMillionsInCash_DoesNotOverspend()
        {
            // (decimal)12345678.999999998 只保留 15 位有效数字，会进位成 12345679。所以不收 fee 时，
            // 向下取整会得到 1,234,567,900 个 0.1 ADA 的单位，按 0.1 算 = 12345679 USDT，现金会剩 −1.9e-9，
            // 买入就会抛异常。Account 会退回一个单位，避免这种情况。
            foreach (var cost in new[] { new CostModel(0), new CostModel(0, 1, 0), new CostModel(0.001) })
            {
                var a = new Account(Ada, cost, 12_345_678.999999998);
                Assert.DoesNotThrow(() => a.TryBuy(1, 0.1, out _), cost.ToString());
                Assert.AreEqual(1, a.Trades, cost.ToString());
                Assert.GreaterOrEqual(a.Cash, 0, cost.ToString());
                Assert.Less(a.Cash, 0.1 * 0.1 * (1 + cost.FeeRate), $"{cost}: at most one unit's price is left over");
            }
        }

        [Test]
        public void HoldingCoinWithoutAPositiveAverageCost_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new Account(Btc, new CostModel(), 0, coinUnits: 10, avgCost: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Account(Btc, new CostModel(), 0, coinUnits: 10, avgCost: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Account(Btc, new CostModel(), 0, coinUnits: 10, avgCost: double.NaN));
            Assert.DoesNotThrow(() => new Account(Btc, new CostModel(), 10_000, coinUnits: 0, avgCost: 0));
        }
    }
}
