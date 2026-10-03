using System;
using System.Collections.Generic;
using Gym.Core.Accounting;
using Gym.Core.Market;

namespace Gym.Core.Env
{
    /// <summary>
    /// 在 candle [First, Last] 上走的一个交易 episode，纯 C#。Unity 里的 Agent 只是包在这个类外面的一层壳。
    ///
    /// 在决策下标 t，Agent 看到的 candle 截止到第 t 根的 close；订单在第 t + 1 根的 open 成交；
    /// reward 比较第 t 根 close 时和第 t + 1 根 close 时的 equity。随机性只来自传给
    /// <see cref="ResetForTraining"/> 的 seed。
    /// </summary>
    public sealed class TradingEnv
    {
        public const double DefaultInitialCash = 10_000;
        public const int TrainingEpisodeLength = 720;
        public const double DefaultRandomInitialPositionShare = 0.5;

        readonly List<double> equityCurve = new List<double>();
        readonly List<TradeRecord> trades = new List<TradeRecord>();

        public TradingEnv(CandleSeries series, SymbolRules rules, int first, int last,
            double initialCash = DefaultInitialCash, int episodeLength = TrainingEpisodeLength,
            double randomInitialPositionShare = DefaultRandomInitialPositionShare)
        {
            Series = series ?? throw new ArgumentNullException(nameof(series));
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
            if (first < 0 || last >= series.Count || first >= last)
                throw new ArgumentOutOfRangeException(nameof(first), $"Need 0 <= first < last < {series.Count}, got [{first}, {last}].");
            if (Math.Max(first, ObservationBuilder.Lookback) >= last)
                throw new ArgumentOutOfRangeException(nameof(last), $"Segment [{first}, {last}] has no step after index {ObservationBuilder.Lookback}.");
            if (!(initialCash > 0) || double.IsInfinity(initialCash))
                throw new ArgumentOutOfRangeException(nameof(initialCash), initialCash, "Must be a finite value > 0.");
            if (episodeLength < 0) throw new ArgumentOutOfRangeException(nameof(episodeLength), episodeLength, "Must be >= 0.");
            if (!(randomInitialPositionShare >= 0 && randomInitialPositionShare <= 1))
                throw new ArgumentOutOfRangeException(nameof(randomInitialPositionShare), randomInitialPositionShare, "Must be in [0, 1].");

            First = first;
            Last = last;
            InitialCash = initialCash;
            EpisodeLength = episodeLength;
            RandomInitialPositionShare = randomInitialPositionShare;
        }

        /// <summary>建在一个日期分段的 candle 上的环境。</summary>
        public static TradingEnv ForSegment(CandleSeries series, SymbolRules rules, SegmentSpec segment,
            double initialCash = DefaultInitialCash, int episodeLength = TrainingEpisodeLength,
            double randomInitialPositionShare = DefaultRandomInitialPositionShare)
        {
            int first = segment.FirstIndex(series);
            int last = segment.LastIndex(series);
            if (first < 0 || last < 0 || !segment.IsInside(series))
                throw new ArgumentException($"Segment {segment} is outside the data.", nameof(segment));
            return new TradingEnv(series, rules, first, last, initialCash, episodeLength, randomInitialPositionShare);
        }

        public CandleSeries Series { get; }
        public SymbolRules Rules { get; }
        public int First { get; }
        public int Last { get; }
        public double InitialCash { get; }
        /// <summary>每个训练 episode 的 step 数；为 0 时一直运行到分段结束。</summary>
        public int EpisodeLength { get; }
        public double RandomInitialPositionShare { get; }

        public Account Account { get; private set; }
        public CostModel Cost { get; private set; }
        public int Seed { get; private set; }
        public bool Evaluation { get; private set; }

        /// <summary>Agent 刚刚看到 close 的那根 candle 的下标。</summary>
        public int CurrentIndex { get; private set; }
        public int StartIndex { get; private set; }
        public int StepCount { get; private set; }
        public int StepsSinceTrade { get; private set; }
        public bool StartedWithCoin { get; private set; }
        public bool Done { get; private set; }
        public EndReason EndReason { get; private set; }

        /// <summary>起始 candle close 时的 equity，然后是每个 step 之后的 equity。</summary>
        public IReadOnlyList<double> EquityCurve => equityCurve;
        public IReadOnlyList<TradeRecord> Trades => trades;
        /// <summary>走完之后账户仍然持有 coin 的 step 个数。</summary>
        public int HoldingSteps { get; private set; }
        public int ClippedRewards { get; private set; }
        public double RewardSum { get; private set; }

        public double CurrentClose => Series.CloseAt(CurrentIndex);
        public double CurrentEquity => Account.Equity(Series.CloseAt(CurrentIndex));

        public bool BuyEnabled => ActionCodec.BuyEnabled(RequireAccount(), Series.CloseAt(CurrentIndex));
        public bool SellEnabled => ActionCodec.SellEnabled(RequireAccount(), Series.CloseAt(CurrentIndex));

        /// <summary>
        /// 开始一个训练 episode：起点在 [max(First, 32), Last − EpisodeLength] 里随机取
        /// （EpisodeLength 为 0 时上限是 Last − 1）；并且以 <see cref="RandomInitialPositionShare"/> 的概率，
        /// 让 equity 里随机的一部分一开始就是 coin（不收 fee）。
        /// </summary>
        public void ResetForTraining(int seed, CostModel cost)
        {
            Cost = cost ?? throw new ArgumentNullException(nameof(cost));
            Seed = seed;
            Evaluation = false;

            int lo = Math.Max(First, ObservationBuilder.Lookback);
            double cash = InitialCash;
            long units = 0;
            double avgCost = 0;

            int hi = EpisodeLength > 0 ? Last - EpisodeLength : Last - 1;
            if (hi < lo)
                throw new InvalidOperationException(
                    $"Segment [{First}, {Last}] is too short for a {EpisodeLength}-step episode.");
            // 先打散：System.Random 用相近的 seed，会给出同一个序列平移后的副本。
            var random = new Random(SeedMixer.Mix(seed));
            int start = random.Next(lo, hi + 1);
            bool holdCoin = random.NextDouble() < RandomInitialPositionShare;
            double share = random.NextDouble();
            if (holdCoin)
            {
                double price = Series.CloseAt(start);
                units = (long)decimal.Floor((decimal)(share * InitialCash) / (decimal)price / Rules.StepSize);
                if (units > 0)
                {
                    cash = InitialCash - (double)(units * Rules.StepSize * (decimal)price);
                    if (cash < 0) cash = 0;
                    avgCost = price;
                }
            }

            StartEpisode(start, cash, units, avgCost);
        }

        /// <summary>
        /// 开始一个评估 episode：从 max(First, 32) 开始，全是现金，一直运行到 Last。
        /// 没有任何随机；seed 只记在 <see cref="Seed"/> 里。
        /// </summary>
        public void ResetForEvaluation(int seed, CostModel cost)
        {
            Cost = cost ?? throw new ArgumentNullException(nameof(cost));
            Seed = seed;
            Evaluation = true;
            StartEpisode(Math.Max(First, ObservationBuilder.Lookback), InitialCash, 0, 0);
        }

        /// <summary>
        /// 按评估的方式从选定的 candle 开始：全是现金，没有随机，一直运行到 <see cref="Last"/>。
        /// Play scene 用它。
        /// </summary>
        public void Reset(CostModel cost, int startIndex)
        {
            int lo = Math.Max(First, ObservationBuilder.Lookback);
            if (startIndex < lo || startIndex >= Last)
                throw new ArgumentOutOfRangeException(nameof(startIndex), startIndex, $"Must be in [{lo}, {Last - 1}].");
            Cost = cost ?? throw new ArgumentNullException(nameof(cost));
            Seed = 0;
            Evaluation = true;
            StartEpisode(startIndex, InitialCash, 0, 0);
        }

        /// <summary>用 <see cref="Cost"/> 开户，并把每个按 episode 计数的计数器清零。</summary>
        void StartEpisode(int startIndex, double cash, long coinUnits, double avgCost)
        {
            Account = new Account(Rules, Cost, cash, coinUnits, avgCost);
            StartIndex = startIndex;
            CurrentIndex = startIndex;
            StepCount = 0;
            StepsSinceTrade = 0;
            StartedWithCoin = coinUnits > 0;
            Done = false;
            EndReason = EndReason.None;
            HoldingSteps = 0;
            ClippedRewards = 0;
            RewardSum = 0;
            trades.Clear();
            equityCurve.Clear();
            equityCurve.Add(CurrentEquity);
        }

        public void WriteObservation(float[] dst, int offset = 0) =>
            ObservationBuilder.Write(Series, CurrentIndex, RequireAccount(), StepsSinceTrade, dst, offset);

        /// <summary>
        /// 执行一个 action。被 action mask 挡掉、却仍然发过来的选择（例如来自键盘），记为一笔被拒绝的订单。
        /// </summary>
        public StepResult Step(TradeAction action, float continuousAction)
        {
            Account account = RequireAccount();
            if (Done) throw new InvalidOperationException("The episode is over; call Reset.");
            if ((int)action < 0 || (int)action >= ActionCodec.BranchSize)
                throw new ArgumentOutOfRangeException(nameof(action), (int)action, "Must be 0, 1 or 2.");

            double closeNow = Series.CloseAt(CurrentIndex);
            double equityBefore = account.Equity(closeNow);
            double fillBase = Series.OpenAt(CurrentIndex + 1);
            double fraction = ActionCodec.Fraction(continuousAction);
            bool traded = PlaceOrder(account, action, fraction, closeNow, fillBase, out bool rejected, out Fill fill);

            if (traded)
            {
                trades.Add(new TradeRecord(StepCount, CurrentIndex + 1, action, fraction, fill));
                StepsSinceTrade = 0;
            }
            else
            {
                StepsSinceTrade++;
            }

            CurrentIndex++;
            StepCount++;
            if (account.CoinUnits > 0) HoldingSteps++;

            double equityAfter = account.Equity(Series.CloseAt(CurrentIndex));
            double reward = RewardFunction.Compute(equityBefore, equityAfter, out bool clipped);
            if (clipped) ClippedRewards++;
            RewardSum += reward;
            equityCurve.Add(equityAfter);

            EndReason reason = EndReasonAfterStep();
            Done = reason != EndReason.None;
            EndReason = reason;
            return new StepResult(reward, clipped, Done, reason, traded, rejected);
        }

        /// <summary>
        /// 把买入或卖出发给账户，以 <paramref name="fillBase"/> 为基准成交。按 <paramref name="closeNow"/>
        /// 被 action mask 禁止的选择不会到达账户，记为被拒绝。返回有没有订单成交。
        /// </summary>
        bool PlaceOrder(Account account, TradeAction action, double fraction, double closeNow, double fillBase,
            out bool rejected, out Fill fill)
        {
            fill = default;
            rejected = false;
            if (action == TradeAction.Hold) return false;
            if (!ActionCodec.IsEnabled(action, account, closeNow))
            {
                account.RecordRejection();
                rejected = true;
                return false;
            }
            bool traded = action == TradeAction.Buy
                ? account.TryBuy(fraction, fillBase, out fill)
                : account.TrySell(fraction, fillBase, out fill);
            rejected = !traded;
            return traded;
        }

        /// <summary>刚走完的这个 step 之后 episode 为什么结束；不结束时为 <see cref="EndReason.None"/>。</summary>
        EndReason EndReasonAfterStep()
        {
            if (!Evaluation && EpisodeLength > 0 && StepCount >= EpisodeLength) return EndReason.EpisodeLength;
            if (CurrentIndex >= Last) return EndReason.SegmentEnd;
            return EndReason.None;
        }

        Account RequireAccount() =>
            Account ?? throw new InvalidOperationException("Call Reset before using the environment.");
    }
}
