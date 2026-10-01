using System;
using System.Collections.Generic;

namespace Gym.Core
{
    /// <summary>
    /// Reference policies run through the same <see cref="TradingEnv"/> in evaluation
    /// mode, with the same fills and fees as the agent (04B §2).
    /// </summary>
    public static class Baselines
    {
        public const string BuyAndHoldName = "buy_and_hold";
        public const string CashName = "cash";
        public const string RandomName = "random";

        /// <summary>Buy with fraction 1 on the first step, then hold to the end.</summary>
        public static EpisodeMetrics RunBuyAndHold(TradingEnv env, CostModel cost)
        {
            env.Reset(0, true, cost);
            env.Step(ActionCodec.Buy, ActionCodec.FromFraction(1));
            while (!env.Done) env.Step(ActionCodec.Hold, 0f);
            return Metrics.From(env);
        }

        /// <summary>Hold cash the whole segment.</summary>
        public static EpisodeMetrics RunCash(TradingEnv env, CostModel cost)
        {
            env.Reset(0, true, cost);
            while (!env.Done) env.Step(ActionCodec.Hold, 0f);
            return Metrics.From(env);
        }

        /// <summary>
        /// Every step: pick uniformly among the choices the mask allows right now, and a
        /// fraction uniformly in [0, 1], from <c>System.Random(seed)</c>.
        /// </summary>
        public static EpisodeMetrics RunRandom(TradingEnv env, CostModel cost, int seed)
        {
            var random = new Random(seed);
            var choices = new List<int>(ActionCodec.BranchSize);
            env.Reset(seed, true, cost);
            while (!env.Done)
            {
                choices.Clear();
                choices.Add(ActionCodec.Hold);
                if (env.BuyEnabled) choices.Add(ActionCodec.Buy);
                if (env.SellEnabled) choices.Add(ActionCodec.Sell);
                int branch = choices[random.Next(choices.Count)];
                double fraction = random.NextDouble();
                env.Step(branch, ActionCodec.FromFraction(fraction));
            }
            return Metrics.From(env);
        }
    }
}
