using System;
using Gym.Core.Accounting;
using Gym.Core.Market;

namespace Gym.Core.Evaluation
{
    /// <summary>One row of log.csv (04B §3).</summary>
    public sealed class EvaluationRecord
    {
        public const string AgentKind = "agent";
        public const string BaselineKind = "baseline";

        public DateTime TimestampUtc;
        public string Kind = BaselineKind;
        public string Policy = "";
        public string ModelRunId = "";
        public string ModelSha256 = "";
        public string Symbol = "";
        public string Segment = "";
        public DateTime SegmentStart;
        public DateTime SegmentEnd;
        public double FeeRate;
        public double FixedFee;
        public double Slippage;
        public int Seeds = 1;
        public double TotalReturn;
        public double MaxDrawdown;
        public double Sharpe;
        public double Trades;
        public double Rejected;
        public double Turnover;
        public double FeesPaid;
        public double FeesPct;
        public double Exposure;
        public string Notes = "";

        public void SetMetrics(EpisodeMetrics m)
        {
            TotalReturn = m.TotalReturn;
            MaxDrawdown = m.MaxDrawdown;
            Sharpe = m.SharpeAnnualized;
            Trades = m.Trades;
            Rejected = m.Rejected;
            Turnover = m.Turnover;
            FeesPaid = m.FeesPaid;
            FeesPct = m.FeesPct;
            Exposure = m.Exposure;
        }

        public void SetCost(CostModel cost)
        {
            FeeRate = cost.FeeRate;
            FixedFee = cost.FixedFee;
            Slippage = cost.Slippage;
        }

        public void SetSegment(SegmentSpec segment)
        {
            Segment = segment.Name;
            SegmentStart = segment.StartDate;
            SegmentEnd = segment.EndDate;
        }
    }
}
