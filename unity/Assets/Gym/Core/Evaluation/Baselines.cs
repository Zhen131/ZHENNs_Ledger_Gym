using System;
using System.Collections.Generic;
using Gym.Core.Accounting;
using Gym.Core.Env;
using Gym.Core.Market;

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
        public const string MomentumName = "momentum_90d";
        public const string SmaCrossName = "sma_cross";
        public const string VolTargetName = "vol_target";
        /// <summary>第三轮的对手：旋钮永远拧在 50% 那档。</summary>
        public const string ConstantMix50Name = "constant_mix_50";
        /// <summary>第三轮的对手：过去 90 天涨了拧到 70%、否则拧到 30%，每天一次。</summary>
        public const string DialMomentumName = "dial_momentum_90d";

        /// <summary>momentum_90d 比较的距离：90 天 = 2160 根小时线。</summary>
        public const int MomentumLookback = 2160;
        /// <summary>sma_cross 的快线：过去 168 小时（1 周）的收盘均值。</summary>
        public const int FastWindow = 168;
        /// <summary>sma_cross 的慢线：过去 720 小时（30 天）的收盘均值。</summary>
        public const int SlowWindow = 720;
        /// <summary>vol_target 算波动率用的小时收益个数（30 天）。</summary>
        public const int VolatilityWindow = 720;
        /// <summary>vol_target 的目标：每小时收益的标准差 0.45 %。</summary>
        public const double TargetHourlyVolatility = 0.0045;
        /// <summary>波动率小于这个数时按这个数算，免得除以 0。</summary>
        public const double MinVolatility = 1e-6;
        /// <summary>三条老规则的带宽：仓位离目标超过这么多（按总资产的比例）才调仓。调仓规则本身在 <see cref="Rebalancer"/>。</summary>
        public const double RebalanceBand = 0.10;

        // ---- 两个对手的固定参数（22A 第二节第 4 条，固定）。它们是对手自己的，不读配置、不读环境参数；
        //      模型的档位、带宽、决策根数在配置里，那边怎么改都不影响这里。

        /// <summary>两个对手的调仓带宽：5 个点（22A 第二节第 4 条，固定）。</summary>
        public const double OpponentBand = 0.05;
        /// <summary>两个对手每次决策走几根小时 K 线：24，即每天一次（22A 第二节第 4 条，固定）。</summary>
        public const int OpponentDecisionHours = 24;
        /// <summary>两个对手的低档：币占总资产 30%（22A 第二节第 4 条，固定）。</summary>
        public const double OpponentLowLevel = 0.3;
        /// <summary>两个对手的中档：50%（22A 第二节第 4 条，固定）。</summary>
        public const double OpponentMiddleLevel = 0.5;
        /// <summary>两个对手的高档：70%（22A 第二节第 4 条，固定）。</summary>
        public const double OpponentHighLevel = 0.7;
        /// <summary>dial_momentum_90d 比较的距离：90 天 = 2160 根小时线（22A 第二节第 4 条，固定）。</summary>
        public const int DialMomentumLookback = 2160;

        /// <summary>两个对手的档位：0.3、0.5、0.7（下标 0、1、2）。</summary>
        public static IReadOnlyList<double> OpponentLevels { get; } =
            Array.AsReadOnly(new[] { OpponentLowLevel, OpponentMiddleLevel, OpponentHighLevel });

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

        // ---- 三条简单规则。每小时决策一次，目标仓位只看决策下标 t 及之前的收盘价（不前视）。

        /// <summary>过去 90 天涨了就满仓：close[t] &gt; close[t − 2160] 时目标仓位为 1，否则为 0。</summary>
        public static EpisodeMetrics RunMomentum(TradingEnv env, CostModel cost) => RunRule(env, cost, MomentumTarget);

        /// <summary>1 周均线在 1 月均线之上就满仓，否则空仓。</summary>
        public static EpisodeMetrics RunSmaCross(TradingEnv env, CostModel cost) => RunRule(env, cost, SmaCrossTarget);

        /// <summary>按波动率调仓：目标仓位 = min(1, 0.0045 ÷ 过去 720 根小时收益的标准差)。</summary>
        public static EpisodeMetrics RunVolTarget(TradingEnv env, CostModel cost) => RunRule(env, cost, VolTargetTarget);

        /// <summary>momentum_90d 在决策下标 t 的目标仓位；t − 2160 不在数据里时为 0。</summary>
        public static double MomentumTarget(CandleSeries series, int t)
        {
            if (t - MomentumLookback < 0) return 0;
            return series.CloseAt(t) > series.CloseAt(t - MomentumLookback) ? 1 : 0;
        }

        /// <summary>
        /// sma_cross 在决策下标 t 的目标仓位：close[t − 167 … t] 的均值大于 close[t − 719 … t] 的均值时为 1，
        /// 否则为 0；不够 720 根收盘价时为 0。
        /// </summary>
        public static double SmaCrossTarget(CandleSeries series, int t)
        {
            if (t - (SlowWindow - 1) < 0) return 0;
            return MeanClose(series, t, FastWindow) > MeanClose(series, t, SlowWindow) ? 1 : 0;
        }

        /// <summary>
        /// vol_target 在决策下标 t 的目标仓位：min(1, 0.0045 ÷ σ)，σ 是 <see cref="HourlyVolatility"/>，
        /// 小于 1e-6 时按 1e-6 算；不够 720 个小时收益（要 close[t − 720 … t]）时为 0。
        /// </summary>
        public static double VolTargetTarget(CandleSeries series, int t)
        {
            if (t - VolatilityWindow < 0) return 0;
            double sigma = Math.Max(MinVolatility, HourlyVolatility(series, t, VolatilityWindow));
            return Math.Min(1, TargetHourlyVolatility / sigma);
        }

        /// <summary>
        /// 小时收益 ln(close[i] ÷ close[i − 1])（i = t − n + 1 … t，共 n 个）的总体标准差（除以 n）。
        /// 要求 t − n ≥ 0。
        /// </summary>
        public static double HourlyVolatility(CandleSeries series, int t, int n)
        {
            if (n < 1 || t - n < 0) throw new ArgumentOutOfRangeException(nameof(t), t, $"Need t - n >= 0 and n >= 1 (n = {n}).");
            double sum = 0;
            for (int i = t - n + 1; i <= t; i++) sum += Math.Log(series.CloseAt(i) / series.CloseAt(i - 1));
            double mean = sum / n;
            double squares = 0;
            for (int i = t - n + 1; i <= t; i++)
            {
                double d = Math.Log(series.CloseAt(i) / series.CloseAt(i - 1)) - mean;
                squares += d * d;
            }
            return Math.Sqrt(squares / n);
        }

        /// <summary>
        /// 三条规则共用的下单规则：转调 <see cref="Rebalancer.Order"/>，带宽是 <see cref="RebalanceBand"/>（0.10）。
        /// p = 币值 ÷ 总资产。目标 − p &gt; 0.10 时买，比例（占现金）= min(1, (目标 − p) × 总资产 ÷ 现金)；
        /// p − 目标 &gt; 0.10 时卖，比例（占持币）= min(1, (p − 目标) ÷ p)；其余不动（比例 0）。
        /// </summary>
        public static (TradeAction action, double fraction) RuleOrder(double target, double cash, double coinValue) =>
            Rebalancer.Order(target, cash, coinValue, RebalanceBand);

        /// <summary>
        /// 按一条规则从分段开头走到结尾：每个 step 用目标仓位和 <see cref="RuleOrder"/> 定下单，
        /// 被 action mask 挡住的买入或卖出改成不动；比例经 <see cref="ActionCodec.FromFraction"/> 交给 env。
        /// </summary>
        public static EpisodeMetrics RunRule(TradingEnv env, CostModel cost, Func<CandleSeries, int, double> target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            env.ResetForEvaluation(0, cost);
            while (!env.Done)
            {
                int t = env.CurrentIndex;
                double close = env.Series.CloseAt(t);
                Account account = env.Account;
                (TradeAction action, double fraction) =
                    RuleOrder(target(env.Series, t), account.Cash, account.Quantity * close);
                if ((action == TradeAction.Buy && !env.BuyEnabled) || (action == TradeAction.Sell && !env.SellEnabled))
                    action = TradeAction.Hold;
                env.Step(action, action == TradeAction.Hold ? 0f : ActionCodec.FromFraction(fraction));
            }
            return Metrics.From(env);
        }

        // ---- 两个对手。都在旋钮模式的机制上跑（每 24 根决策一次、调仓规则带宽 5 个点），费率由调用方给。

        /// <summary>
        /// constant_mix_50：每次决策都选 50% 那一档。在 <paramref name="env"/> 的币、K 线、分段、初始资金和观测集上，
        /// 用对手自己的固定参数新建一个旋钮模式的环境来跑；<paramref name="env"/> 本身不动，它的动作模式、档位和带宽都不看。
        /// </summary>
        public static EpisodeMetrics RunConstantMix50(TradingEnv env, CostModel cost) => RunConstantMix50(env, cost, out _);

        /// <summary>同上，另交出跑过的那个旋钮环境（成交、曲线、换档次数都在里面）。</summary>
        public static EpisodeMetrics RunConstantMix50(TradingEnv env, CostModel cost, out TradingEnv dialEnv) =>
            RunOpponent(env, cost, ConstantMix50Level, out dialEnv);

        /// <summary>
        /// dial_momentum_90d：决策下标 t，close[t] &gt; close[t − 2160] 时选 70% 那档，否则选 30% 那档；t − 2160 不在数据里时选 30%。
        /// 环境的建法同 <see cref="RunConstantMix50(TradingEnv, CostModel)"/>。
        /// </summary>
        public static EpisodeMetrics RunDialMomentum(TradingEnv env, CostModel cost) => RunDialMomentum(env, cost, out _);

        /// <summary>同上，另交出跑过的那个旋钮环境。</summary>
        public static EpisodeMetrics RunDialMomentum(TradingEnv env, CostModel cost, out TradingEnv dialEnv) =>
            RunOpponent(env, cost, DialMomentumLevel, out dialEnv);

        /// <summary>constant_mix_50 在任何下标选的档：50% 那一档（下标 1）。</summary>
        public static int ConstantMix50Level(CandleSeries series, int t) => 1;

        /// <summary>dial_momentum_90d 在决策下标 t 选的档：涨了选 70%（下标 2），否则或没有 2160 根历史时选 30%（下标 0）。只读 t 及之前。</summary>
        public static int DialMomentumLevel(CandleSeries series, int t)
        {
            if (t - DialMomentumLookback < 0) return 0;
            return series.CloseAt(t) > series.CloseAt(t - DialMomentumLookback) ? 2 : 0;
        }

        /// <summary>
        /// 两个对手共用：用对手的固定参数在 <paramref name="env"/> 的数据和分段上新建旋钮环境，评估方式开局（全现金、从段首），
        /// 每次决策按 <paramref name="chooseLevel"/>（只给它决策下标 t）选档，走到分段结束。
        /// </summary>
        static EpisodeMetrics RunOpponent(TradingEnv env, CostModel cost, Func<CandleSeries, int, int> chooseLevel, out TradingEnv dialEnv)
        {
            if (env == null) throw new ArgumentNullException(nameof(env));
            var dial = new DialSettings(OpponentLevels, OpponentDecisionHours, OpponentBand);
            dialEnv = new TradingEnv(env.Series, env.Rules, env.First, env.Last, env.InitialCash, 0, 0, env.ObservationSet, dial);
            dialEnv.ResetForEvaluation(0, cost);
            while (!dialEnv.Done) dialEnv.StepDial(chooseLevel(dialEnv.Series, dialEnv.CurrentIndex));
            return Metrics.From(dialEnv);
        }

        /// <summary>close[t − n + 1 … t] 的均值。</summary>
        static double MeanClose(CandleSeries series, int t, int n)
        {
            double sum = 0;
            for (int i = t - n + 1; i <= t; i++) sum += series.CloseAt(i);
            return sum / n;
        }
    }
}
