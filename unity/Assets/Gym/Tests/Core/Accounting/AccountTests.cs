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

        // ---- T-3 hand-checked bookkeeping

        [Test]
        public void T03_BuyQuarterThenSellHalf()
        {
            // Buy 25 % of 10,000 USDT at 50,000, fee 0.1 %, no slippage, no fixed fee:
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
            // Unrealised return at the average cost is exactly −FeeRate.
            Assert.AreEqual(-0.001, a.UnrealizedReturn(50_050), 1e-12);

            // Sell 50 % at 52,000:
            //   units = floor(4995 × 0.5) = 2497  → 0.02497 BTC
            //   N = 0.02497 × 52000 = 1298.44,  F = 1.29844
            //   Cash = 7500.0025 + 1298.44 − 1.29844 = 8797.14406
            //   CoinUnits = 2498, AvgCost unchanged = 50050
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
        public void T03_SlippageFiveBasisPoints()
        {
            // Fee 0.1 %, slippage 0.0005, buy 25 % of 10,000 at base price 50,000:
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

            // Sell everything at base 50,000: p = 50000 × 0.9995 = 49975
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
        public void T03_FixedFeeOneUsdt()
        {
            // Fee 0.1 % + 1 USDT per order, buy 25 % of 10,000 at 50,000:
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
            // Unrealised at 50,000: (0.04993 × 50000 × 0.999 − 1 − 2499.9965) ÷ 2499.9965
            Assert.AreEqual((2496.5 * 0.999 - 1 - 2499.9965) / 2499.9965, a.UnrealizedReturn(50_000), 1e-12);

            // Sell everything at 50,000: N = 2496.5, F = 3.4965, Cash = 7500.0035 + 2493.0035 = 9993.007
            Assert.IsTrue(a.TrySell(1, 50_000, out _));
            Assert.AreEqual(9993.007, a.Cash, Tol);
            Assert.AreEqual(6.993, a.FeesPaid, Tol);
        }

        // ---- T-4 minimum order and step size

        [Test]
        public void T04_FourUsdtBudgetIsRejectedAndChangesNothingElse()
        {
            // B = 16 × 0.25 = 4 → units = floor(4 ÷ 50050 ÷ 0.00001) = 7 → N = 3.5 < 5 → rejected.
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
        public void T04_SellBelowMinimumIsRejected()
        {
            // 0.0001 BTC at 40,000 = 4 USDT < 5.
            var a = new Account(Btc, new CostModel(), 0, coinUnits: 10, avgCost: 40_000);
            Assert.IsFalse(a.Sell(1, 40_000));
            Assert.AreEqual(1, a.Rejected);
            Assert.AreEqual(10, a.CoinUnits);
            Assert.AreEqual(0, a.Cash);
            Assert.AreEqual(40_000, a.AvgCost);
            // Selling a fraction that rounds to zero units is rejected too.
            var b = new Account(Btc, new CostModel(), 0, coinUnits: 1000, avgCost: 40_000);
            Assert.IsFalse(b.Sell(0.0005, 40_000));
            Assert.AreEqual(1000, b.CoinUnits);
        }

        [Test]
        public void T04_SellRejectedWhenFixedFeeEatsTheProceeds()
        {
            // N = 0.0002 × 50000 = 10 ≥ 5, but F = 0.01 + 10 = 10.01 → N − F ≤ 0 → rejected.
            var a = new Account(Btc, new CostModel(0.001, 10), 0, coinUnits: 20, avgCost: 50_000);
            Assert.IsFalse(a.Sell(1, 50_000));
            Assert.AreEqual(20, a.CoinUnits);
        }

        [Test]
        public void T04_QuantityRoundsDownToTheStep()
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
        public void T04_AdaStepPointOneUsesExactDecimalDivision()
        {
            // ADA, step 0.1, fee 0.1 %, all of 30.03 USDT at 0.3:
            //   units = floor(30.03 ÷ (0.3 × 1.001) ÷ 0.1) = floor(30.03 ÷ 0.3003 ÷ 0.1) = floor(1000) = 1000
            //   N = 100 × 0.3 = 30, F = 0.03, Cash = 30.03 − 30 − 0.03 = 0 (within 1e-9, clamped at 0)
            var a = new Account(Ada, new CostModel(0.001), 30.03);
            Assert.IsTrue(a.TryBuy(1, 0.3, out Fill fill));
            Assert.AreEqual(1000, fill.Units);
            Assert.AreEqual(100.0, a.Quantity);
            Assert.AreEqual(30.0, fill.Notional, Tol);
            Assert.AreEqual(0.03, fill.Fee, Tol);
            Assert.AreEqual(0, a.Cash, Tol);
            Assert.GreaterOrEqual(a.Cash, 0);

            // Sell 0.25 of 1000 units = 250 units = 25 ADA at 0.4 → N = 10.
            Assert.IsTrue(a.TrySell(0.25, 0.4, out Fill sell));
            Assert.AreEqual(250, sell.Units);
            Assert.AreEqual(10.0, sell.Notional, Tol);
            Assert.AreEqual(750, a.CoinUnits);
        }

        [Test]
        public void T04_DecimalFloorWhereDoubleWouldLoseAUnit()
        {
            // ADA at 0.1, fee 0.1 %, all of 5.62562 USDT:
            //   exact: 5.62562 ÷ (0.1 × 1.001) ÷ 0.1 = 5.62562 ÷ 0.1001 ÷ 0.1 = 562
            //   double: 5.62562 / (0.1 * 1.001) / 0.1 = 561.999…  → floor would give 561
            Assert.AreEqual(561, Math.Floor(5.62562 / (0.1 * 1.001) / 0.1), "premise: double loses a unit here");
            var a = new Account(Ada, new CostModel(0.001), 5.62562);
            Assert.IsTrue(a.TryBuy(1, 0.1, out Fill fill));
            Assert.AreEqual(562, fill.Units);
            Assert.AreEqual(5.62, fill.Notional, Tol);
            Assert.AreEqual(0, a.Cash, Tol);
        }

        [Test]
        public void T04_ZeroFractionIsRejected()
        {
            var a = new Account(Btc, new CostModel(), 10_000);
            Assert.IsFalse(a.Buy(0, 50_000));
            Assert.AreEqual(1, a.Rejected);
            Assert.Throws<ArgumentOutOfRangeException>(() => a.Buy(1.5, 50_000));
            Assert.Throws<ArgumentOutOfRangeException>(() => a.Buy(0.5, 0));
        }

        // ---- 01D review

        [Test]
        public void D01_1_FullBuyWithMillionsInCashDoesNotOverspend()
        {
            // (decimal)12345678.999999998 keeps 15 significant digits and rounds up to 12345679,
            // so without a fee the floor gave 1,234,567,900 units of 0.1 ADA at 0.1 = 12345679 USDT
            // and left cash at −1.9e-9: the buy threw. Now it gives one unit back.
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
        public void D01_2_HoldingCoinNeedsAPositiveAverageCost()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new Account(Btc, new CostModel(), 0, coinUnits: 10, avgCost: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Account(Btc, new CostModel(), 0, coinUnits: 10, avgCost: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Account(Btc, new CostModel(), 0, coinUnits: 10, avgCost: double.NaN));
            Assert.DoesNotThrow(() => new Account(Btc, new CostModel(), 10_000, coinUnits: 0, avgCost: 0));
        }
    }
}
