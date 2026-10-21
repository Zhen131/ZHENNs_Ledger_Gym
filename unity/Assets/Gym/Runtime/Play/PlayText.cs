using System;
using System.Collections.Generic;
using System.Globalization;

namespace Gym.Runtime.Play
{
    /// <summary>
    /// Play scene 画面上所有文字的唯一一张表：键 → 中文、英文。日志和报错不在这里，它们只用英文。
    /// 数字的写法不随语言变（小数点、千分位都按英文习惯），所以格式串里的数字由调用方先排好。
    /// </summary>
    public static class PlayText
    {
        static readonly Dictionary<PlayTextKey, (string chinese, string english)> Table =
            new Dictionary<PlayTextKey, (string, string)>
            {
                [PlayTextKey.LanguageButton] = ("语言：中文（L）", "Language: English (L)"),
                [PlayTextKey.TimeUtc] = ("时间（UTC）", "Time (UTC)"),
                [PlayTextKey.Step] = ("步数", "step"),
                [PlayTextKey.Close] = ("收盘价", "Close"),
                [PlayTextKey.Cash] = ("现金", "Cash"),
                [PlayTextKey.Coin] = ("持币", "Coin"),
                // {2} 是基础资产名，由当前币推出来（BTCUSDT → BTC），见 BaseAsset
                [PlayTextKey.CoinAmount] = ("{0} {2}（{1} 个单位）", "{0} {2} ({1} units)"),
                [PlayTextKey.Position] = ("仓位", "Position"),
                [PlayTextKey.Equity] = ("总资产", "Equity"),
                [PlayTextKey.Return] = ("累计收益", "Return"),
                [PlayTextKey.FeesPaid] = ("手续费累计", "Fees paid"),
                [PlayTextKey.Trades] = ("成交", "Trades"),
                [PlayTextKey.Rejected] = ("被拒", "Rejected"),
                [PlayTextKey.LastAction] = ("上一步", "Last action"),
                [PlayTextKey.ActionNone] = ("还没有", "none yet"),
                [PlayTextKey.ActionHold] = ("不动", "HOLD"),
                [PlayTextKey.ActionBuyFilled] = ("买入 {0} 成交", "BUY {0}: filled"),
                [PlayTextKey.ActionBuyRejected] = ("买入 {0} 被拒", "BUY {0}: REJECTED"),
                [PlayTextKey.ActionSellFilled] = ("卖出 {0} 成交", "SELL {0}: filled"),
                [PlayTextKey.ActionSellRejected] = ("卖出 {0} 被拒", "SELL {0}: REJECTED"),
                [PlayTextKey.Fraction] = ("下单比例", "Fraction"),
                [PlayTextKey.AutoPlay] = ("自动播放中", "AUTO"),
                [PlayTextKey.Costs] = ("费率 / 固定费 / 滑点", "Fee rate / fixed / slip"),
                [PlayTextKey.BasisPoints] = ("基点", "bp"),
                [PlayTextKey.Keys] = ("按键", "Keys"),
                [PlayTextKey.KeysTrade] = ("1-4 选比例  B 买入  S 卖出  H/空格 不动", "1-4 fraction   B buy   S sell   H/Space hold"),
                [PlayTextKey.KeysOther] = ("P 自动播放  R 重来  L 切换语言", "P auto-play   R restart   L language"),
                [PlayTextKey.UnrealizedPnl] = ("未实现盈亏", "Unrealized P&L"),
                [PlayTextKey.RealizedPnl] = ("已实现盈亏", "Realized P&L"),
                [PlayTextKey.AvatarBuy] = ("买入 {0}", "BUY {0}"),
                [PlayTextKey.AvatarSell] = ("卖出 {0}", "SELL {0}"),
                [PlayTextKey.AvatarRejected] = ("被拒", "REJECTED"),
                [PlayTextKey.WatchStatus] = ("观战（{0}）", "Watching ({0})"),
                [PlayTextKey.WatchSegmentValidation] = ("验证段", "validation"),
                [PlayTextKey.WatchSegmentTest] = ("测试段", "test"),
                [PlayTextKey.WatchSpeed] = ("每秒 {0} 步", "{0} steps/s"),
                [PlayTextKey.WatchPaused] = ("暂停中", "PAUSED"),
                [PlayTextKey.WatchFinished] = ("走完了", "finished"),
                [PlayTextKey.WatchKeysFirst] = ("P 暂停/继续  N 单步  [ 慢  ] 快", "P pause   N one step   [ slower   ] faster"),
                [PlayTextKey.WatchKeysSecond] = ("R 重来  L 切换语言", "R restart   L language"),
                [PlayTextKey.WatchNoModel] = ("还没有选模型。\n在 Unity 菜单里点 Gym > Watch a Model...，\n选一个训练好的 .onnx 文件，模型就开始交易。",
                    "No model chosen yet.\nIn the Unity menu, click Gym > Watch a Model...\nand pick a trained .onnx file to watch it trade."),
                [PlayTextKey.WatchEnd] = ("这一段走完了。按 R 从头再看。", "End of the segment. Press R to watch it again."),
                // 观战旋钮模式下「上一步」那一行换成的名字和值：{0} 是目标仓位（例如 50%），{1} 是本局换档次数
                [PlayTextKey.DialTarget] = ("目标仓位", "Target position"),
                [PlayTextKey.DialTargetValue] = ("{0}｜换档 {1} 次", "{0} | changes: {1}"),
            };

        public static IEnumerable<PlayTextKey> Keys => Table.Keys;

        public static string Get(PlayTextKey key, PlayLanguage language)
        {
            if (!Table.TryGetValue(key, out (string chinese, string english) entry))
                throw new ArgumentOutOfRangeException(nameof(key), key, "No Play scene text for this key.");
            return language == PlayLanguage.English ? entry.english : entry.chinese;
        }

        public static string Format(PlayTextKey key, PlayLanguage language, params object[] values) =>
            string.Format(CultureInfo.InvariantCulture, Get(key, language), values);

        /// <summary>
        /// 币对名里的基础资产：去掉结尾的 USDT（BTCUSDT → BTC、DOGEUSDT → DOGE）。不以 USDT 结尾的名字原样返回，
        /// 没有名字时返回空串。
        /// </summary>
        public static string BaseAsset(string symbol)
        {
            const string Quote = "USDT";
            if (string.IsNullOrEmpty(symbol)) return "";
            return symbol.Length > Quote.Length && symbol.EndsWith(Quote, StringComparison.Ordinal)
                ? symbol.Substring(0, symbol.Length - Quote.Length)
                : symbol;
        }
    }
}
