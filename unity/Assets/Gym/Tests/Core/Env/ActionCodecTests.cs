using System;
using Gym.Core.Accounting;
using Gym.Core.Env;
using Gym.Core.Market;
using NUnit.Framework;

namespace Gym.Tests.Core.Env
{
    public class ActionCodecTests
    {
        static readonly SymbolRules Btc = SymbolRules.BtcUsdt;

        [Test]
        public void T05_FractionMapsMinusOneToZeroAndOneToOne()
        {
            Assert.AreEqual(0, ActionCodec.Fraction(-1f));
            Assert.AreEqual(0.5, ActionCodec.Fraction(0f));
            Assert.AreEqual(1, ActionCodec.Fraction(1f));
            Assert.AreEqual(0, ActionCodec.Fraction(-3f));
            Assert.AreEqual(1, ActionCodec.Fraction(7f));
            Assert.AreEqual(0, ActionCodec.Fraction(float.NaN));
            Assert.AreEqual(0.25, ActionCodec.Fraction(ActionCodec.FromFraction(0.25)), 1e-7);
        }

        // ---- T-5 action mask

        [Test]
        public void T05_SellIsMaskedWhenFlat()
        {
            var a = new Account(Btc, new CostModel(), 10_000);
            Assert.IsFalse(ActionCodec.SellEnabled(a, 50_000));
            Assert.IsTrue(ActionCodec.BuyEnabled(a, 50_000));
            Assert.IsTrue(ActionCodec.HoldEnabled);
            Assert.IsTrue(ActionCodec.IsEnabled(TradeAction.Hold, a, 50_000));
        }

        [Test]
        public void T05_BuyIsMaskedWithoutEnoughCash()
        {
            // Threshold = 5 × 1.001 × 1 + 0 = 5.005.
            Assert.IsFalse(ActionCodec.BuyEnabled(new Account(Btc, new CostModel(), 5.0), 50_000));
            Assert.IsTrue(ActionCodec.BuyEnabled(new Account(Btc, new CostModel(), 5.01), 50_000));
            // Hold stays available with no cash and no coin.
            var empty = new Account(Btc, new CostModel(), 0);
            Assert.IsTrue(ActionCodec.IsEnabled(TradeAction.Hold, empty, 50_000));
            Assert.IsFalse(ActionCodec.IsEnabled(TradeAction.Buy, empty, 50_000));
            Assert.IsFalse(ActionCodec.IsEnabled(TradeAction.Sell, empty, 50_000));
        }

        [Test]
        public void T05_FixedFeeExactlyOnTheBoundary()
        {
            // No proportional fee, no slippage, fixed fee 1: threshold = 5 × 1 × 1 + 1 = 6 exactly.
            var cost = new CostModel(0, 1, 0);
            Assert.IsTrue(ActionCodec.BuyEnabled(new Account(Btc, cost, 6.0), 50_000));
            Assert.IsFalse(ActionCodec.BuyEnabled(new Account(Btc, cost, 5.999999), 50_000));

            // With fee and slippage: threshold = 5 × 1.001 × 1.0005 + 1.
            var full = new CostModel(0.001, 1, 0.0005);
            double threshold = 5 * (1 + 0.001) * (1 + 0.0005) + 1;
            Assert.IsTrue(ActionCodec.BuyEnabled(new Account(Btc, full, threshold), 50_000));
            Assert.IsFalse(ActionCodec.BuyEnabled(new Account(Btc, full, threshold - 1e-9), 50_000));
        }

        [Test]
        public void T05_SellBoundaryUsesSlippage()
        {
            // 10 units = 0.0001 BTC; at 50,000 it is worth exactly 5 USDT.
            var a = new Account(Btc, new CostModel(), 0, coinUnits: 10, avgCost: 50_000);
            Assert.IsTrue(ActionCodec.SellEnabled(a, 50_000));
            Assert.IsFalse(ActionCodec.SellEnabled(a, 49_999.99));
            var slipped = new Account(Btc, new CostModel(0.001, 0, 0.001), 0, coinUnits: 10, avgCost: 50_000);
            Assert.IsFalse(ActionCodec.SellEnabled(slipped, 50_000)); // 5 × 0.999 < 5
        }

        [Test]
        public void T05_MaskedActionSentAnywayIsBookedAsRejected()
        {
            CandleSeries s = TestData.Synthetic(100, i => 100);
            var env = new TradingEnv(s, Btc, 0, 99);
            env.ResetForEvaluation(0, new CostModel());
            Assert.IsFalse(env.SellEnabled);
            StepResult r = env.Step(TradeAction.Sell, 1f);
            Assert.IsTrue(r.Rejected);
            Assert.IsFalse(r.Traded);
            Assert.AreEqual(1, env.Account.Rejected);
            Assert.AreEqual(0, env.Account.Trades);
            Assert.AreEqual(10_000, env.Account.Cash);
            Assert.AreEqual(1, env.StepsSinceTrade);
            Assert.Throws<ArgumentOutOfRangeException>(() => env.Step((TradeAction)3, 0f));
        }
    }
}
