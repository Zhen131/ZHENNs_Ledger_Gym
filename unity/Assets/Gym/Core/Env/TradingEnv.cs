using System;
using System.Collections.Generic;

namespace Gym.Core
{
    /// <summary>
    /// One trading episode over candles [First, Last], in plain C#. The Unity
    /// agent is only a shell around this class (01B §2.7).
    ///
    /// At decision index t the agent sees candles up to the close of t; the
    /// order fills at the open of t + 1; the reward compares equity at the
    /// close of t and the close of t + 1. Randomness comes only from the seed
    /// passed to <see cref="Reset"/>.
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

        /// <summary>Environment over the candles of a date segment.</summary>
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
        /// <summary>Steps per training episode; 0 runs to the end of the segment.</summary>
        public int EpisodeLength { get; }
        public double RandomInitialPositionShare { get; }

        public Account Account { get; private set; }
        public CostModel Cost { get; private set; }
        public int Seed { get; private set; }
        public bool Evaluation { get; private set; }

        /// <summary>Index of the candle whose close the agent has just seen.</summary>
        public int T { get; private set; }
        public int StartIndex { get; private set; }
        public int StepCount { get; private set; }
        public int StepsSinceTrade { get; private set; }
        public bool StartedWithCoin { get; private set; }
        public bool Done { get; private set; }
        public EndReason EndReason { get; private set; }

        /// <summary>Equity at the close of the start candle, then after every step.</summary>
        public IReadOnlyList<double> EquityCurve => equityCurve;
        public IReadOnlyList<TradeRecord> Trades => trades;
        /// <summary>Steps after which the account still held coin.</summary>
        public int HoldingSteps { get; private set; }
        public int ClippedRewards { get; private set; }
        public double RewardSum { get; private set; }

        public double CurrentClose => Series.CloseAt(T);
        public double CurrentEquity => Account.Equity(Series.CloseAt(T));

        public bool BuyEnabled => ActionCodec.BuyEnabled(RequireAccount(), Series.CloseAt(T));
        public bool SellEnabled => ActionCodec.SellEnabled(RequireAccount(), Series.CloseAt(T));

        /// <summary>
        /// Start an episode. Training: random start in [max(First, 32), Last − EpisodeLength]
        /// and, with probability <see cref="RandomInitialPositionShare"/>, a random part of
        /// the equity already in coin (no fee). Evaluation: start at max(First, 32) in cash
        /// and run to Last.
        /// </summary>
        public void Reset(int seed, bool evaluation, CostModel cost)
        {
            Cost = cost ?? throw new ArgumentNullException(nameof(cost));
            Seed = seed;
            Evaluation = evaluation;

            int lo = Math.Max(First, ObservationBuilder.Lookback);
            int start;
            double cash = InitialCash;
            long units = 0;
            double avgCost = 0;

            if (evaluation)
            {
                start = lo;
            }
            else
            {
                int hi = EpisodeLength > 0 ? Last - EpisodeLength : Last - 1;
                if (hi < lo)
                    throw new InvalidOperationException(
                        $"Segment [{First}, {Last}] is too short for a {EpisodeLength}-step episode.");
                // Mixed first: System.Random with nearby seeds gives shifted copies of one sequence (Q03).
                var random = new Random(SeedMixer.Mix(seed));
                start = random.Next(lo, hi + 1);
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
            }

            Account = new Account(Rules, cost, cash, units, avgCost);
            StartIndex = start;
            T = start;
            StepCount = 0;
            StepsSinceTrade = 0;
            StartedWithCoin = units > 0;
            Done = false;
            EndReason = EndReason.None;
            HoldingSteps = 0;
            ClippedRewards = 0;
            RewardSum = 0;
            trades.Clear();
            equityCurve.Clear();
            equityCurve.Add(CurrentEquity);
        }

        /// <summary>
        /// Evaluation-style start at a chosen candle: all cash, no randomness, runs to
        /// <see cref="Last"/>. Used by the Play scene (02B §2.3); added alongside the
        /// original <see cref="Reset(int, bool, CostModel)"/>, which is unchanged.
        /// </summary>
        public void Reset(CostModel cost, int startIndex)
        {
            int lo = Math.Max(First, ObservationBuilder.Lookback);
            if (startIndex < lo || startIndex >= Last)
                throw new ArgumentOutOfRangeException(nameof(startIndex), startIndex, $"Must be in [{lo}, {Last - 1}].");
            Cost = cost ?? throw new ArgumentNullException(nameof(cost));
            Seed = 0;
            Evaluation = true;
            Account = new Account(Rules, cost, InitialCash);
            StartIndex = startIndex;
            T = startIndex;
            StepCount = 0;
            StepsSinceTrade = 0;
            StartedWithCoin = false;
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
            ObservationBuilder.Write(Series, T, RequireAccount(), StepsSinceTrade, dst, offset);

        /// <summary>
        /// Apply one action. A masked choice that is sent anyway (e.g. from the
        /// keyboard) is booked as a rejected order.
        /// </summary>
        public StepResult Step(int branch, float a)
        {
            Account account = RequireAccount();
            if (Done) throw new InvalidOperationException("The episode is over; call Reset.");
            if (branch < 0 || branch >= ActionCodec.BranchSize)
                throw new ArgumentOutOfRangeException(nameof(branch), branch, "Must be 0, 1 or 2.");

            double closeNow = Series.CloseAt(T);
            double equityBefore = account.Equity(closeNow);
            double fillBase = Series.OpenAt(T + 1);
            double fraction = ActionCodec.Fraction(a);
            bool traded = false;
            bool rejected = false;
            Fill fill = default;

            if (branch != ActionCodec.Hold)
            {
                if (!ActionCodec.IsEnabled(branch, account, closeNow))
                {
                    account.RecordRejection();
                    rejected = true;
                }
                else
                {
                    traded = branch == ActionCodec.Buy
                        ? account.TryBuy(fraction, fillBase, out fill)
                        : account.TrySell(fraction, fillBase, out fill);
                    rejected = !traded;
                }
            }

            if (traded)
            {
                trades.Add(new TradeRecord(StepCount, T + 1, branch, fraction, fill));
                StepsSinceTrade = 0;
            }
            else
            {
                StepsSinceTrade++;
            }

            T++;
            StepCount++;
            if (account.CoinUnits > 0) HoldingSteps++;

            double equityAfter = account.Equity(Series.CloseAt(T));
            double reward = RewardFunction.Compute(equityBefore, equityAfter, out bool clipped);
            if (clipped) ClippedRewards++;
            RewardSum += reward;
            equityCurve.Add(equityAfter);

            EndReason reason = EndReason.None;
            if (!Evaluation && EpisodeLength > 0 && StepCount >= EpisodeLength) reason = EndReason.EpisodeLength;
            else if (T >= Last) reason = EndReason.SegmentEnd;
            Done = reason != EndReason.None;
            EndReason = reason;
            return new StepResult(reward, clipped, Done, reason, traded, rejected);
        }

        Account RequireAccount() =>
            Account ?? throw new InvalidOperationException("Call Reset before using the environment.");
    }
}
