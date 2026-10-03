using System.Globalization;
using System.IO;
using System.Text;
using Gym.Core;
using Gym.Runtime;
using UnityEditor;
using UnityEngine;

namespace Gym.EditorTools
{
    /// <summary>
    /// Prints the expected HUD values for the first three key presses of the Play
    /// scene (select 25 % + B, H, select 50 % + S), computed with TradingEnv from the
    /// default config. Writes Logs/play-checklist.md as well.
    ///
    ///   Unity -batchmode -nographics -projectPath . -executeMethod Gym.EditorTools.PlayChecklist.Print -quit
    /// </summary>
    public static class PlayChecklist
    {
        [MenuItem("Gym/Print Play Checklist")]
        public static void Print()
        {
            GymSettings s = GymConfigLoader.Load(GymConfigLoader.DefaultConfigPath, GymConfigLoader.DefaultSymbolsPath);
            GymConfig c = s.Config;
            var env = TradingEnv.ForSegment(s.Series, s.Rules, s.Train, c.initialCash, c.episodeLength, c.randomInitialPositionShare);
            var cost = new CostModel(CostModel.DefaultFeeRate, 0, 0);
            env.Reset(cost, s.PlayStartIndex);

            CultureInfo inv = CultureInfo.InvariantCulture;
            var md = new StringBuilder();
            md.AppendLine($"Start: {s.Series.OpenTimeUtc(env.T).ToString("yyyy-MM-dd HH:mm", inv)} UTC, close {env.CurrentClose.ToString("F2", inv)}, cash {env.Account.Cash.ToString("F4", inv)}, fee {cost.FeeRate}, fixed {cost.FixedFee}, slippage {cost.Slippage}");
            md.AppendLine();
            md.AppendLine("| Step | Keys | Candle now (UTC) | Fill price | Cash | Coin units | Coin BTC | Fee this step | Equity | Fees total | Trades | Rejected |");
            md.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");

            (int branch, double fraction, string keys)[] presses =
            {
                (ActionCodec.Buy, 0.25, "2, B"),
                (ActionCodec.Hold, 0.25, "H"),
                (ActionCodec.Sell, 0.50, "3, S"),
            };
            for (int i = 0; i < presses.Length; i++)
            {
                (int branch, double fraction, string keys) = presses[i];
                double feesBefore = env.Account.FeesPaid;
                StepResult r = env.Step(branch, ActionCodec.FromFraction(fraction));
                string price = r.Traded ? env.Trades[env.Trades.Count - 1].Price.ToString("F4", inv) : "-";
                md.AppendLine($"| {i + 1} | {keys} | {s.Series.OpenTimeUtc(env.T).ToString("yyyy-MM-dd HH:mm", inv)} | {price} | " +
                    $"{env.Account.Cash.ToString("F4", inv)} | {env.Account.CoinUnits} | {env.Account.Quantity.ToString("F5", inv)} | " +
                    $"{(env.Account.FeesPaid - feesBefore).ToString("F4", inv)} | {env.CurrentEquity.ToString("F4", inv)} | " +
                    $"{env.Account.FeesPaid.ToString("F4", inv)} | {env.Account.Trades} | {env.Account.Rejected} |");
            }

            string report = md.ToString();
            Directory.CreateDirectory("Logs");
            File.WriteAllText(Path.Combine("Logs", "play-checklist.md"), report);
            Debug.Log("[Gym] Play checklist\n" + report);
        }
    }
}
