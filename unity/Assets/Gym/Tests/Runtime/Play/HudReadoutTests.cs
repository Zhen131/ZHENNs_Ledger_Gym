using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Gym.Core.Accounting;
using Gym.Core.Env;
using Gym.Core.Market;
using Gym.Runtime.Play;
using NUnit.Framework;

namespace Gym.Tests.Runtime.Play
{
    public class HudReadoutTests
    {
        static readonly Regex Chinese = new Regex(@"[㐀-鿿]");

        /// <summary>改成两种语言之前，面板上每一行左边的英文名字（"Trades … Rejected" 那一行现在拆成两行）。</summary>
        static readonly string[] OldEnglishLabels =
        {
            "Time (UTC)", "step", "Close", "Cash", "Coin", "Position", "Equity", "Return", "Fees paid",
            "Trades", "Rejected", "Last action", "Fraction", "Fee rate / fixed / slip",
        };

        /// <summary>200 根小时 candle，价格在 40,000 上下波动，够一个从第 40 根开始的短 episode。</summary>
        static TradingEnv SmallEnv()
        {
            var candles = new List<Candle>();
            for (int i = 0; i < 200; i++)
            {
                double open = 40_000 + 500 * Math.Sin(i * 0.3);
                double close = 40_000 + 500 * Math.Sin((i + 1) * 0.3);
                candles.Add(new Candle(1704067200000 + i * CandleSeries.HourMs, open, Math.Max(open, close) + 20, Math.Min(open, close) - 20, close, 1));
            }
            var env = new TradingEnv(CandleSeries.FromCandles(candles), SymbolRules.BtcUsdt, 0, 199);
            env.Reset(new CostModel(0.001, 0, 0.0005), 40);
            return env;
        }

        static HudSnapshot After(TradingEnv env, TradeAction side, double fraction)
        {
            StepResult r = env.Step(side, ActionCodec.FromFraction(fraction));
            return HudView.Capture(env, true, side, fraction, r.Traded);
        }

        [Test]
        public void EveryRowOfTheEnglishOnlyPanel_IsStillThereInBothLanguages()
        {
            CollectionAssert.AreEqual(OldEnglishLabels, HudReadout.Rows.Select(r => HudReadout.Label(r, PlayLanguage.English)).ToArray());
            foreach (PlayTextKey row in HudReadout.Rows)
                StringAssert.IsMatch(Chinese.ToString(), HudReadout.Label(row, PlayLanguage.Chinese), row.ToString());

            HudSnapshot s = After(SmallEnv(), TradeAction.Buy, 0.25);
            foreach (PlayLanguage language in new[] { PlayLanguage.Chinese, PlayLanguage.English })
            foreach (PlayTextKey row in HudReadout.Rows)
                Assert.IsFalse(string.IsNullOrWhiteSpace(HudReadout.Value(row, s, language, 0.25, true)), $"{row} {language}");
        }

        [Test]
        public void KeyHints_NameEveryKeyIncludingLInBothLanguages()
        {
            foreach (PlayLanguage language in new[] { PlayLanguage.Chinese, PlayLanguage.English })
            {
                string keys = PlayText.Get(PlayTextKey.KeysTrade, language) + " " + PlayText.Get(PlayTextKey.KeysOther, language);
                foreach (string key in new[] { "1-4", "B ", "S ", "H/", "P ", "R ", "L " })
                    StringAssert.Contains(key, keys, $"{language}: {key}");
            }
        }

        [Test]
        public void NumbersAndDates_AreWrittenTheSameWayInBothLanguages()
        {
            HudSnapshot s = After(SmallEnv(), TradeAction.Buy, 0.25);
            var languageFree = new[] { PlayTextKey.TimeUtc, PlayTextKey.Step, PlayTextKey.Close, PlayTextKey.Cash, PlayTextKey.Position,
                PlayTextKey.Equity, PlayTextKey.Return, PlayTextKey.FeesPaid, PlayTextKey.Trades, PlayTextKey.Rejected };
            foreach (PlayTextKey row in languageFree)
                Assert.AreEqual(HudReadout.Value(row, s, PlayLanguage.English, 0.25, false),
                    HudReadout.Value(row, s, PlayLanguage.Chinese, 0.25, false), row.ToString());
            StringAssert.Contains(",", HudReadout.Value(PlayTextKey.Cash, s, PlayLanguage.Chinese, 0.25, false), "thousands separator");
            string units = s.CoinUnits.ToString();
            StringAssert.Contains(units + " units", HudReadout.Value(PlayTextKey.Coin, s, PlayLanguage.English, 0.25, false));
            StringAssert.Contains(units + " 个单位", HudReadout.Value(PlayTextKey.Coin, s, PlayLanguage.Chinese, 0.25, false));
        }

        [Test]
        public void LastActionInEnglish_ReadsLikeTheSnapshotText()
        {
            TradingEnv env = SmallEnv();
            var steps = new (TradeAction side, double fraction)[]
            {
                (TradeAction.Sell, 0.5), (TradeAction.Buy, 0.25), (TradeAction.Hold, 0.25), (TradeAction.Sell, 0.5), (TradeAction.Buy, 0.0001),
            };
            var seen = new HashSet<string>();
            foreach ((TradeAction side, double fraction) in steps)
            {
                HudSnapshot s = After(env, side, fraction);
                Assert.AreEqual(s.LastAction, HudReadout.LastAction(s, PlayLanguage.English));
                StringAssert.IsMatch(Chinese.ToString(), HudReadout.LastAction(s, PlayLanguage.Chinese));
                seen.Add(s.LastAction);
            }
            CollectionAssert.IsSupersetOf(seen, new[] { "SELL 50%: REJECTED", "BUY 25%: filled", "HOLD", "SELL 50%: filled", "BUY 0%: REJECTED" });
        }

        [Test]
        public void BeforeTheFirstStep_LastActionSaysSoInWords()
        {
            HudSnapshot s = HudView.Capture(SmallEnv(), false, TradeAction.Hold, 0, false);
            Assert.AreEqual("-", s.LastAction, "the snapshot keeps its old text");
            Assert.AreEqual("none yet", HudReadout.LastAction(s, PlayLanguage.English));
            Assert.AreEqual("还没有", HudReadout.LastAction(s, PlayLanguage.Chinese));
        }

        [Test]
        public void FractionRow_ShowsAutoPlayInTheCurrentLanguage()
        {
            HudSnapshot s = HudView.Capture(SmallEnv(), false, TradeAction.Hold, 0, false);
            Assert.AreEqual("25 %", HudReadout.Value(PlayTextKey.Fraction, s, PlayLanguage.English, 0.25, false));
            Assert.AreEqual("25 %  AUTO", HudReadout.Value(PlayTextKey.Fraction, s, PlayLanguage.English, 0.25, true));
            Assert.AreEqual("25 %  自动播放中", HudReadout.Value(PlayTextKey.Fraction, s, PlayLanguage.Chinese, 0.25, true));
            Assert.AreEqual("-", HudReadout.Value(PlayTextKey.Fraction, s, PlayLanguage.Chinese, null, false));
            Assert.AreEqual("0.1 % / 0 / 5 bp", HudReadout.Value(PlayTextKey.Costs, s, PlayLanguage.English, null, false));
            Assert.AreEqual("0.1 % / 0 / 5 基点", HudReadout.Value(PlayTextKey.Costs, s, PlayLanguage.Chinese, null, false));
        }

        [Test]
        public void Capture_CopiesTheAccountsProfitAndLossAtTheCurrentClose()
        {
            TradingEnv env = SmallEnv();
            env.Step(TradeAction.Buy, ActionCodec.FromFraction(0.5));
            for (int i = 0; i < 5; i++) env.Step(TradeAction.Hold, 0f);
            HudSnapshot s = After(env, TradeAction.Sell, 0.5);
            Assert.AreEqual(env.Account.RealizedPnl, s.RealizedPnl);
            Assert.AreEqual(env.Account.UnrealizedPnl(env.CurrentClose), s.UnrealizedPnl);
            Assert.AreNotEqual(0, s.RealizedPnl, "premise: the sell realized something");
        }
    }
}
