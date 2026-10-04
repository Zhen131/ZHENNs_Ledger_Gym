using System.Globalization;
using System.IO;
using System.Text;
using Gym.Core.Accounting;
using Gym.Core.Env;
using Gym.Core.Market;
using Gym.Runtime.Configuration;
using Gym.Runtime.Play;
using UnityEditor;
using UnityEngine;

namespace Gym.Editor
{
    /// <summary>
    /// 打印 Play scene 前三次按键（选 25 % + B、H、选 50 % + S）之后 HUD 应该显示的值，
    /// 用 TradingEnv 按默认配置算出来。同时也写一份 Logs/play-checklist.md。
    /// 左下角口袋应该显示的现金变化、飘字和盈亏另写一份 Logs/play-checklist-pnl.md（前一份的内容不变）。
    ///
    ///   Unity -batchmode -nographics -projectPath "$PWD/unity" -executeMethod Gym.Editor.PlayChecklist.Print -quit
    /// </summary>
    public static class PlayChecklist
    {
        static readonly (TradeAction branch, double fraction, string keys)[] Presses =
        {
            (TradeAction.Buy, 0.25, "2, B"),
            (TradeAction.Hold, 0.25, "H"),
            (TradeAction.Sell, 0.50, "3, S"),
        };

        [MenuItem("Gym/Print Play Checklist")]
        public static void Print()
        {
            GymSettings s = GymConfigLoader.Load(GymConfigLoader.DefaultConfigPath, GymConfigLoader.DefaultSymbolsPath);
            var cost = new CostModel(CostModel.DefaultFeeRate, 0, 0);
            TradingEnv env = StartAtPlayStart(s, cost);

            CultureInfo inv = CultureInfo.InvariantCulture;
            var md = new StringBuilder();
            md.AppendLine($"Start: {s.Series.OpenTimeUtc(env.CurrentIndex).ToString("yyyy-MM-dd HH:mm", inv)} UTC, close {env.CurrentClose.ToString("F2", inv)}, cash {env.Account.Cash.ToString("F4", inv)}, fee {cost.FeeRate}, fixed {cost.FixedFee}, slippage {cost.Slippage}");
            md.AppendLine();
            md.AppendLine("| Step | Keys | Candle now (UTC) | Fill price | Cash | Coin units | Coin BTC | Fee this step | Equity | Fees total | Trades | Rejected |");
            md.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");

            for (int i = 0; i < Presses.Length; i++)
            {
                (TradeAction branch, double fraction, string keys) = Presses[i];
                double feesBefore = env.Account.FeesPaid;
                StepResult r = env.Step(branch, ActionCodec.FromFraction(fraction));
                string price = r.Traded ? env.Trades[env.Trades.Count - 1].Price.ToString("F4", inv) : "-";
                md.AppendLine($"| {i + 1} | {keys} | {s.Series.OpenTimeUtc(env.CurrentIndex).ToString("yyyy-MM-dd HH:mm", inv)} | {price} | " +
                    $"{env.Account.Cash.ToString("F4", inv)} | {env.Account.CoinUnits} | {env.Account.Quantity.ToString("F5", inv)} | " +
                    $"{(env.Account.FeesPaid - feesBefore).ToString("F4", inv)} | {env.CurrentEquity.ToString("F4", inv)} | " +
                    $"{env.Account.FeesPaid.ToString("F4", inv)} | {env.Account.Trades} | {env.Account.Rejected} |");
            }

            string report = md.ToString();
            Directory.CreateDirectory("Logs");
            File.WriteAllText(Path.Combine("Logs", "play-checklist.md"), report);
            Debug.Log("[Gym] Play checklist\n" + report);

            WriteProfitAndLoss(s, cost);
        }

        static TradingEnv StartAtPlayStart(GymSettings s, CostModel cost)
        {
            GymConfig c = s.Config;
            var env = TradingEnv.ForSegment(s.Series, s.Rules, s.Train, c.initialCash, c.episodeLength, c.randomInitialPositionShare);
            env.Reset(cost, s.PlayStartIndex);
            return env;
        }

        /// <summary>
        /// 同样三次按键之后，口袋应该显示什么：现金变化和飘字、口袋上的现金、按当前这根 close 算的未实现盈亏、
        /// 已实现盈亏，以及「已实现 + 未实现」和「equity − 开局 equity」两列（应该相等）。
        /// </summary>
        static void WriteProfitAndLoss(GymSettings s, CostModel cost)
        {
            TradingEnv env = StartAtPlayStart(s, cost);
            double startEquity = env.CurrentEquity;
            CultureInfo inv = CultureInfo.InvariantCulture;
            var md = new StringBuilder();
            md.AppendLine($"Pocket after the same presses. Start equity {startEquity.ToString("F4", inv)}; profit and loss at the close of the candle now.");
            md.AppendLine();
            md.AppendLine("| Step | Keys | Cash change | Floating amount | Pocket cash | Avg cost | Unrealized P&L | Realized P&L | Realized + unrealized | Equity - start equity |");
            md.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");
            for (int i = 0; i < Presses.Length; i++)
            {
                (TradeAction branch, double fraction, string keys) = Presses[i];
                double cashBefore = env.Account.Cash;
                StepResult r = env.Step(branch, ActionCodec.FromFraction(fraction));
                Account a = env.Account;
                double change = a.Cash - cashBefore;
                string floating = !r.Traded ? "-" : branch == TradeAction.Buy ? MoneyText.Spent(change) : MoneyText.Received(change);
                double unrealized = a.UnrealizedPnl(env.CurrentClose);
                md.AppendLine($"| {i + 1} | {keys} | {change.ToString("F4", inv)} | {floating} | {MoneyText.Amount(a.Cash)} | " +
                    $"{a.AvgCost.ToString("F4", inv)} | {unrealized.ToString("F4", inv)} ({MoneyText.Signed(unrealized)}) | " +
                    $"{a.RealizedPnl.ToString("F4", inv)} ({MoneyText.Signed(a.RealizedPnl)}) | {(a.RealizedPnl + unrealized).ToString("F4", inv)} | " +
                    $"{(env.CurrentEquity - startEquity).ToString("F4", inv)} |");
            }

            string report = md.ToString();
            File.WriteAllText(Path.Combine("Logs", "play-checklist-pnl.md"), report);
            Debug.Log("[Gym] Play checklist, pocket\n" + report);
        }
    }
}
