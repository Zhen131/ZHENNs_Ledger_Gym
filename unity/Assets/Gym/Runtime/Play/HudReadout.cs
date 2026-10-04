using System.Globalization;
using Gym.Core.Env;

namespace Gym.Runtime.Play
{
    /// <summary>
    /// 读数面板每一行写什么：左边的名字和右边的值，按语言生成。纯函数，不碰画面，所以能直接测。
    /// 数字的写法两种语言一样（小数点、千分位按英文习惯）。
    /// </summary>
    public static class HudReadout
    {
        /// <summary>面板从上到下的行，每行的名字就是这个键的文字。</summary>
        public static readonly PlayTextKey[] Rows =
        {
            PlayTextKey.TimeUtc, PlayTextKey.Step, PlayTextKey.Close, PlayTextKey.Cash, PlayTextKey.Coin,
            PlayTextKey.Position, PlayTextKey.Equity, PlayTextKey.Return, PlayTextKey.FeesPaid, PlayTextKey.Trades,
            PlayTextKey.Rejected, PlayTextKey.LastAction, PlayTextKey.Fraction, PlayTextKey.Costs,
        };

        static readonly CultureInfo Numbers = CultureInfo.InvariantCulture;

        public static string Label(PlayTextKey row, PlayLanguage language) => PlayText.Get(row, language);

        /// <param name="selectedFraction">试玩时选中的下单比例；没有 PlayController 时为 null。</param>
        public static string Value(PlayTextKey row, HudSnapshot s, PlayLanguage language, double? selectedFraction, bool autoPlay)
        {
            switch (row)
            {
                case PlayTextKey.TimeUtc: return s.TimeUtc.ToString("yyyy-MM-dd HH:mm", Numbers);
                case PlayTextKey.Step: return s.Step.ToString(Numbers);
                case PlayTextKey.Close: return s.Close.ToString("N2", Numbers);
                case PlayTextKey.Cash: return s.Cash.ToString("N4", Numbers) + " USDT";
                case PlayTextKey.Coin: return PlayText.Format(PlayTextKey.CoinAmount, language, s.Quantity.ToString("F5", Numbers), s.CoinUnits);
                case PlayTextKey.Position: return (s.PositionRatio * 100).ToString("F2", Numbers) + " %";
                case PlayTextKey.Equity: return s.Equity.ToString("N4", Numbers) + " USDT";
                case PlayTextKey.Return: return (s.Return * 100).ToString("F4", Numbers) + " %";
                case PlayTextKey.FeesPaid: return s.FeesPaid.ToString("N4", Numbers) + " USDT";
                case PlayTextKey.Trades: return s.Trades.ToString(Numbers);
                case PlayTextKey.Rejected: return s.Rejected.ToString(Numbers);
                case PlayTextKey.LastAction: return LastAction(s, language);
                case PlayTextKey.Fraction: return Fraction(selectedFraction, autoPlay, language);
                case PlayTextKey.Costs: return Costs(s, language);
                default: return "";
            }
        }

        /// <summary>上一步做了什么，按语言说。英文和 <see cref="HudSnapshot.LastAction"/> 一样，只有还没走时不同。</summary>
        public static string LastAction(HudSnapshot s, PlayLanguage language)
        {
            if (!s.HasStepped) return PlayText.Get(PlayTextKey.ActionNone, language);
            string pct = Percent(s.LastFraction);
            switch (s.LastSide)
            {
                case TradeAction.Buy:
                    return PlayText.Format(s.LastFilled ? PlayTextKey.ActionBuyFilled : PlayTextKey.ActionBuyRejected, language, pct);
                case TradeAction.Sell:
                    return PlayText.Format(s.LastFilled ? PlayTextKey.ActionSellFilled : PlayTextKey.ActionSellRejected, language, pct);
                default:
                    return PlayText.Get(PlayTextKey.ActionHold, language);
            }
        }

        /// <summary>和 <see cref="HudSnapshot.LastAction"/> 里的写法相同，例如 "25%"、"12.5%"。</summary>
        public static string Percent(double fraction) => (fraction * 100).ToString("0.#", Numbers) + "%";

        static string Fraction(double? selectedFraction, bool autoPlay, PlayLanguage language)
        {
            if (selectedFraction == null) return "-";
            string selected = (selectedFraction.Value * 100).ToString("0", Numbers) + " %";
            return autoPlay ? selected + "  " + PlayText.Get(PlayTextKey.AutoPlay, language) : selected;
        }

        static string Costs(HudSnapshot s, PlayLanguage language) =>
            $"{(s.FeeRate * 100).ToString("0.###", Numbers)} % / {s.FixedFee.ToString("0.##", Numbers)} / " +
            $"{(s.Slippage * 10000).ToString("0.#", Numbers)} {PlayText.Get(PlayTextKey.BasisPoints, language)}";
    }
}
