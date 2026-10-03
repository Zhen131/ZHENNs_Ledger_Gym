using System;
using System.Collections.Generic;
using Gym.Core.Accounting;
using Gym.Core.Env;

namespace Gym.Core.Evaluation
{
    /// <summary>
    /// 对照组的几个 policy：在评估模式下走同一个 <see cref="TradingEnv"/>，fill 和 fee 都和 Agent 一样。
    /// </summary>
    public static class Baselines
    {
        public const string BuyAndHoldName = "buy_and_hold";
        public const string CashName = "cash";
        public const string RandomName = "random";

        /// <summary>第一个 step 以比例 1 买入，然后一直不动到结束。</summary>
        public static EpisodeMetrics RunBuyAndHold(TradingEnv env, CostModel cost)
        {
            env.ResetForEvaluation(0, cost);
            env.Step(TradeAction.Buy, ActionCodec.FromFraction(1));
            while (!env.Done) env.Step(TradeAction.Hold, 0f);
            return Metrics.From(env);
        }

        /// <summary>整个分段都拿着现金。</summary>
        public static EpisodeMetrics RunCash(TradingEnv env, CostModel cost)
        {
            env.ResetForEvaluation(0, cost);
            while (!env.Done) env.Step(TradeAction.Hold, 0f);
            return Metrics.From(env);
        }

        /// <summary>
        /// 每个 step：在 action mask 此刻允许的选择里均匀地挑一个，再在 [0, 1] 里均匀地取一个比例，
        /// 随机数来自 <c>System.Random(SeedMixer.Mix(seed))</c>。每个 step 正好取两个数：先取选择，再取比例。
        /// </summary>
        public static EpisodeMetrics RunRandom(TradingEnv env, CostModel cost, int seed)
        {
            // 先打散：System.Random 用 seed 0、1、2 …，会给出同一个序列平移后的副本。
            var random = new Random(SeedMixer.Mix(seed));
            var choices = new List<TradeAction>(ActionCodec.BranchSize);
            env.ResetForEvaluation(seed, cost);
            while (!env.Done)
            {
                choices.Clear();
                choices.Add(TradeAction.Hold);
                if (env.BuyEnabled) choices.Add(TradeAction.Buy);
                if (env.SellEnabled) choices.Add(TradeAction.Sell);
                TradeAction branch = choices[random.Next(choices.Count)];
                double fraction = random.NextDouble();
                env.Step(branch, ActionCodec.FromFraction(fraction));
            }
            return Metrics.From(env);
        }
    }
}
