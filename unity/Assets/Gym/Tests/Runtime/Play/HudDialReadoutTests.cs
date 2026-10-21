using System;
using System.Collections.Generic;
using Gym.Core.Accounting;
using Gym.Core.Env;
using Gym.Core.Market;
using Gym.Runtime.Play;
using NUnit.Framework;

namespace Gym.Tests.Runtime.Play
{
    /// <summary>
    /// 观战旋钮模式下「上一步」那一行（<see cref="HudReadout.LastActionRow"/>）：数字对（目标仓位等于当前档、换档次数等于
    /// 环境的计数），两种语言的字对；下单模式和键盘试玩照原样。不依赖环境变量，不会被忽略。
    /// </summary>
    public class HudDialReadoutTests
    {
        static readonly double[] Levels = { 0.3, 0.5, 0.7 };
        static readonly PlayLanguage[] Languages = { PlayLanguage.Chinese, PlayLanguage.English };

        /// <summary>200 根小时 candle（和 HudReadoutTests 的一样），从第 40 根开始；旋钮模式时 24 根一次决策、带宽 0.05。</summary>
        static TradingEnv SmallEnv(bool dial)
        {
            var candles = new List<Candle>();
            for (int i = 0; i < 200; i++)
            {
                double open = 40_000 + 500 * Math.Sin(i * 0.3);
                double close = 40_000 + 500 * Math.Sin((i + 1) * 0.3);
                candles.Add(new Candle(1704067200000 + i * CandleSeries.HourMs, open, Math.Max(open, close) + 20, Math.Min(open, close) - 20, close, 1));
            }
            var env = new TradingEnv(CandleSeries.FromCandles(candles), SymbolRules.BtcUsdt, 0, 199,
                dial: dial ? new DialSettings(Levels, 24, 0.05) : null);
            env.Reset(new CostModel(0.001, 0, 0.0005), 40);
            return env;
        }

        static HudSnapshot Capture(TradingEnv env, bool hasStepped) => HudView.Capture(env, hasStepped, TradeAction.Hold, 0, false);

        [Test]
        public void DialRow_ShowsTheCurrentLevelAndTheEnvironmentsDialChanges()
        {
            TradingEnv env = SmallEnv(true);
            HudSnapshot start = Capture(env, false);
            Assert.IsTrue(start.DialMode);
            Assert.AreEqual(-1, start.DialTarget, "no decision yet");
            Assert.AreEqual(("目标仓位", "还没有｜换档 0 次"), HudReadout.LastActionRow(start, PlayLanguage.Chinese, true));
            Assert.AreEqual(("Target position", "none yet | changes: 0"), HudReadout.LastActionRow(start, PlayLanguage.English, true));

            var expected = new (int level, string chinese, string english)[]
            {
                (0, "30%｜换档 0 次", "30% | changes: 0"),
                (2, "70%｜换档 1 次", "70% | changes: 1"),
                (2, "70%｜换档 1 次", "70% | changes: 1"),
            };
            foreach ((int level, string chinese, string english) in expected)
            {
                env.StepDial(level);
                HudSnapshot s = Capture(env, true);
                Assert.AreEqual(Levels[env.CurrentLevel], s.DialTarget, "the target is the current level");
                Assert.AreEqual(env.DialChanges, s.DialChanges, "the changes are the environment's count");
                Assert.AreEqual(("目标仓位", chinese), HudReadout.LastActionRow(s, PlayLanguage.Chinese, true), $"after level {level}");
                Assert.AreEqual(("Target position", english), HudReadout.LastActionRow(s, PlayLanguage.English, true), $"after level {level}");
            }
            Assert.AreEqual(1, env.DialChanges, "premise: one change in three decisions");
        }

        [Test]
        public void OrderMode_AndTheKeyboardPlayPanel_KeepTheLastActionRowAsItWas()
        {
            TradingEnv orders = SmallEnv(false);
            StepResult r = orders.Step(TradeAction.Buy, ActionCodec.FromFraction(0.25));
            HudSnapshot s = HudView.Capture(orders, true, TradeAction.Buy, 0.25, r.Traded);
            Assert.IsFalse(s.DialMode);
            Assert.AreEqual(0, s.DialChanges);

            TradingEnv dial = SmallEnv(true);
            dial.StepDial(2);
            HudSnapshot d = Capture(dial, true);
            foreach (PlayLanguage language in Languages)
            {
                var original = (HudReadout.Label(PlayTextKey.LastAction, language), HudReadout.LastAction(s, language));
                Assert.AreEqual(original, HudReadout.LastActionRow(s, language, true), $"order mode in the Watch scene, {language}");
                Assert.AreEqual(original, HudReadout.LastActionRow(s, language, false), $"order mode in the Play scene, {language}");
                Assert.AreEqual((HudReadout.Label(PlayTextKey.LastAction, language), HudReadout.LastAction(d, language)),
                    HudReadout.LastActionRow(d, language, false), $"without the Watch scene's switch the row stays as it was, {language}");
            }
            Assert.AreEqual(("上一步", "买入 25% 成交"), HudReadout.LastActionRow(s, PlayLanguage.Chinese, true));
        }
    }
}
