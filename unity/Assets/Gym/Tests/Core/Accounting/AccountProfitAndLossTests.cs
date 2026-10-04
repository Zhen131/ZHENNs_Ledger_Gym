using System;
using Gym.Core.Accounting;
using Gym.Core.Env;
using Gym.Core.Market;
using NUnit.Framework;

namespace Gym.Tests.Core.Accounting
{
    public class AccountProfitAndLossTests
    {
        /// <summary>
        /// 「已实现 + 未实现 = equity − 开局 equity」在数学上精确成立，差只来自 double 的舍入
        /// （金额在 1e4～1e5 USDT，每次运算误差约 1e-11）和买入时把 1e-9 以内的负现金截成 0。
        /// 几千步下来也到不了 1e-7；1e-6 USDT 留了十倍余量，又远小于画面显示的一分钱。
        /// </summary>
        const double IdentityTolerance = 1e-6;
        const int RandomSteps = 3000;
        static readonly SymbolRules Btc = SymbolRules.BtcUsdt;

        static readonly CostModel[] CostSettings =
        {
            new CostModel(0, 0, 0),
            new CostModel(0.001, 0, 0),
            new CostModel(0.003, 1, 0.0005),
            new CostModel(0.001, 0.5, 0.0002),
        };

        [Test]
        public void NewAccount_HasNoProfitOrLoss()
        {
            var cash = new Account(Btc, new CostModel(), 10_000);
            Assert.AreEqual(0, cash.RealizedPnl);
            Assert.AreEqual(0, cash.UnrealizedPnl(50_000));
            // 开局带 coin 时成本记成开局价，所以按开局价算没有盈亏。
            var withCoin = new Account(Btc, new CostModel(), 5_000, coinUnits: 10_000, avgCost: 50_000);
            Assert.AreEqual(0, withCoin.RealizedPnl);
            Assert.AreEqual(0, withCoin.UnrealizedPnl(50_000));
            Assert.AreEqual(0.1 * 1_000, withCoin.UnrealizedPnl(51_000), 1e-9);
        }

        [Test]
        public void BuyAQuarterThenSellHalf_BooksTheHandCheckedPnl()
        {
            // 和 AccountTests 里手算的同一组数：在 50,000 买入 25 %，AvgCost = 50050；在 52,000 卖出 50 %：
            //   卖出 2497 个单位 = 0.02497 BTC，N = 1298.44，F = 1.29844
            //   已实现 = 1298.44 − 1.29844 − 0.02497 × 50050 = 1298.44 − 1.29844 − 1249.7485 = 47.39306
            //   剩 0.02498 BTC，按 52,000 的未实现 = 0.02498 × (52000 − 50050) = 48.711
            //   已实现 + 未实现 = 96.10406 = Equity(52000) − 10000（AccountTests 里是 10096.10406）
            var a = new Account(Btc, new CostModel(0.001), 10_000);
            Assert.IsTrue(a.Buy(0.25, 50_000));
            Assert.AreEqual(0, a.RealizedPnl, "a buy realizes nothing");
            Assert.AreEqual(0.04995 * (50_000 - 50_050), a.UnrealizedPnl(50_000), 1e-9);

            Assert.IsTrue(a.Sell(0.5, 52_000));
            Assert.AreEqual(47.39306, a.RealizedPnl, 1e-9);
            Assert.AreEqual(48.711, a.UnrealizedPnl(52_000), 1e-9);
            Assert.AreEqual(a.Equity(52_000) - 10_000, a.RealizedPnl + a.UnrealizedPnl(52_000), 1e-9);
        }

        [Test]
        public void SellingEverything_LeavesNoUnrealizedPnlAndRealizesTheWholeChange()
        {
            foreach (CostModel cost in CostSettings)
            {
                var a = new Account(Btc, cost, 10_000);
                Assert.IsTrue(a.Buy(0.6, 40_000), cost.ToString());
                Assert.IsTrue(a.Sell(1, 43_000), cost.ToString());
                Assert.AreEqual(0, a.CoinUnits, cost.ToString());
                Assert.AreEqual(0, a.UnrealizedPnl(43_000), cost.ToString());
                Assert.AreEqual(a.Cash - 10_000, a.RealizedPnl, IdentityTolerance, cost.ToString());
            }
        }

        [Test]
        public void RejectedOrders_LeaveThePnlUnchanged()
        {
            var a = new Account(Btc, new CostModel(), 16);
            Assert.IsFalse(a.Buy(0.25, 50_000));
            Assert.IsFalse(a.Sell(1, 50_000));
            Assert.AreEqual(0, a.RealizedPnl);
            Assert.AreEqual(0, a.UnrealizedPnl(50_000));
        }

        [Test]
        public void UnrealizedPnl_DoesNotDeductTheSellFeeThatUnrealizedReturnDoes()
        {
            var a = new Account(Btc, new CostModel(0.001), 10_000);
            Assert.IsTrue(a.Buy(0.25, 50_000));
            double costBasis = a.Quantity * a.AvgCost;
            Assert.AreEqual(a.Quantity * (51_000 - a.AvgCost), a.UnrealizedPnl(51_000), 1e-9);
            Assert.AreEqual((a.Quantity * 51_000 * 0.999 - costBasis) / costBasis, a.UnrealizedReturn(51_000), 1e-12);
            Assert.AreNotEqual(a.UnrealizedPnl(51_000) / costBasis, a.UnrealizedReturn(51_000));
        }

        // ---- 恒等式：用仓库里的 BTCUSDT 数据随机买、卖、不动

        [Test]
        public void RandomTradesStartingInCash_RealizedPlusUnrealizedEqualsTheEquityChange(
            [Values(0, 1, 2, 3)] int costIndex, [Values(11, 12)] int seed)
        {
            CostModel cost = CostSettings[costIndex];
            CandleSeries series = TestData.Btc;
            int start = series.Count / 3;
            var account = new Account(Btc, cost, 10_000);
            double worst = RunRandomTrades(account, series, start, 10_000, seed, $"{cost}, seed {seed}");
            TestContext.Out.WriteLine($"{cost}, seed {seed}, start in cash: largest gap {worst:R} USDT");
        }

        [Test]
        public void RandomTradesStartingWithCoin_RealizedPlusUnrealizedEqualsTheEquityChange(
            [Values(0, 1, 2, 3)] int costIndex, [Values(21, 22)] int seed)
        {
            CostModel cost = CostSettings[costIndex];
            CandleSeries series = TestData.Btc;
            int start = series.Count / 2;
            // 和 TradingEnv.ResetForTraining 一样：按开局 candle 的 close 买进一部分 coin，成本记成这个价，不收 fee。
            double price = series.CloseAt(start);
            long units = (long)decimal.Floor(4_000m / (decimal)price / Btc.StepSize);
            double cash = 10_000 - (double)(units * Btc.StepSize * (decimal)price);
            var account = new Account(Btc, cost, cash, units, price);
            double initialEquity = account.Equity(price);
            double worst = RunRandomTrades(account, series, start, initialEquity, seed, $"{cost}, seed {seed}, with coin");
            TestContext.Out.WriteLine($"{cost}, seed {seed}, start with coin: largest gap {worst:R} USDT");
        }

        [Test]
        public void TrainingEpisodes_KeepTheIdentityWhetherTheyStartInCashOrWithCoin()
        {
            TradingEnv env = TestData.TrainEnv();
            int startedWithCoin = 0;
            for (int seed = 0; seed < 20; seed++)
            {
                env.ResetForTraining(seed, new CostModel(0.001, 0.5, 0.0002));
                if (env.StartedWithCoin) startedWithCoin++;
                var random = new Random(seed);
                double initialEquity = env.EquityCurve[0];
                AssertIdentity(env.Account, env.CurrentClose, initialEquity, $"seed {seed}, start");
                while (!env.Done)
                {
                    env.Step((TradeAction)random.Next(ActionCodec.BranchSize), (float)(random.NextDouble() * 2 - 1));
                    AssertIdentity(env.Account, env.CurrentClose, initialEquity, $"seed {seed}, step {env.StepCount}");
                }
            }
            Assert.Greater(startedWithCoin, 0, "premise: some episodes start with coin");
            Assert.Less(startedWithCoin, 20, "premise: some episodes start in cash");
        }

        /// <summary>
        /// 像环境那样走：第 i 根 close 时下单，在第 i + 1 根的 open 成交，然后按第 i + 1 根的 close 检查。
        /// 最后全部卖掉；卖得掉的话，未实现必须是 0。返回最大的差。
        /// </summary>
        static double RunRandomTrades(Account account, CandleSeries series, int start, double initialEquity, int seed, string what)
        {
            var random = new Random(seed);
            double worst = 0;
            int buys = 0, sells = 0;
            for (int i = start; i < start + RandomSteps; i++)
            {
                double fraction = random.NextDouble();
                switch (random.Next(3))
                {
                    case 0:
                        if (account.Buy(fraction, series.OpenAt(i + 1))) buys++;
                        break;
                    case 1:
                        if (account.Sell(fraction, series.OpenAt(i + 1))) sells++;
                        break;
                }
                worst = Math.Max(worst, AssertIdentity(account, series.CloseAt(i + 1), initialEquity, $"{what}, candle {i + 1}"));
            }
            Assert.Greater(buys, 100, $"{what}: premise: many buys fill");
            Assert.Greater(sells, 100, $"{what}: premise: many sells fill");

            double last = series.CloseAt(start + RandomSteps);
            if (account.CoinUnits > 0 && account.Sell(1, last))
            {
                Assert.AreEqual(0, account.CoinUnits, what);
                Assert.AreEqual(0, account.UnrealizedPnl(last), what);
            }
            return Math.Max(worst, AssertIdentity(account, last, initialEquity, $"{what}, after selling everything"));
        }

        static double AssertIdentity(Account account, double price, double initialEquity, string when)
        {
            double gap = Math.Abs(account.RealizedPnl + account.UnrealizedPnl(price) - (account.Equity(price) - initialEquity));
            Assert.LessOrEqual(gap, IdentityTolerance, when);
            return gap;
        }
    }
}
