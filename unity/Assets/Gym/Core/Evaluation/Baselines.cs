using System;
using System.Collections.Generic;
using Gym.Core.Accounting;
using Gym.Core.Env;

namespace Gym.Core.Evaluation
{
    /// <summary>
    /// Reference policies run through the same <see cref="TradingEnv"/> in evaluation
    /// mode, with the same fills and fees as the agent.
    /// </summary>
    public static class Baselines
    {
        public const string BuyAndHoldName = "buy_and_hold";
        public const string CashName = "cash";
        public const string RandomName = "random";

        /// <summary>Buy with fraction 1 on the first step, then hold to the end.</summary>
        public static EpisodeMetrics RunBuyAndHold(TradingEnv env, CostModel cost)
        {
            env.ResetForEvaluation(0, cost);
            env.Step(TradeAction.Buy, ActionCodec.FromFraction(1));
            while (!env.Done) env.Step(TradeAction.Hold, 0f);
            return Metrics.From(env);
        }

        /// <summary>Hold cash the whole segment.</summary>
        public static EpisodeMetrics RunCash(TradingEnv env, CostModel cost)
        {
            env.ResetForEvaluation(0, cost);
            while (!env.Done) env.Step(TradeAction.Hold, 0f);
            return Metrics.From(env);
        }

        /// <summary>
        /// Every step: pick uniformly among the choices the mask allows right now, and a
        /// fraction uniformly in [0, 1], from <c>System.Random(SeedMixer.Mix(seed))</c>.
        /// Each step draws exactly two numbers: the choice, then the fraction.
        /// </summary>
        public static EpisodeMetrics RunRandom(TradingEnv env, CostModel cost, int seed)
        {
            // Mixed first: System.Random with seeds 0, 1, 2 … gives shifted copies of one sequence.
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
