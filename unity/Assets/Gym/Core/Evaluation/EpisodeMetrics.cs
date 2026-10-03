namespace Gym.Core
{
    /// <summary>The evaluation numbers of one episode (04B §1).</summary>
    public readonly struct EpisodeMetrics
    {
        public readonly double TotalReturn;
        public readonly double MaxDrawdown;
        public readonly double SharpeAnnualized;
        public readonly int Trades;
        public readonly int Rejected;
        public readonly double Turnover;
        public readonly double FeesPaid;
        public readonly double FeesPct;
        public readonly double Exposure;
        public readonly int Steps;
        public readonly double InitialEquity;
        public readonly double FinalEquity;

        public EpisodeMetrics(double totalReturn, double maxDrawdown, double sharpeAnnualized, int trades, int rejected,
            double turnover, double feesPaid, double feesPct, double exposure, int steps, double initialEquity, double finalEquity)
        {
            TotalReturn = totalReturn;
            MaxDrawdown = maxDrawdown;
            SharpeAnnualized = sharpeAnnualized;
            Trades = trades;
            Rejected = rejected;
            Turnover = turnover;
            FeesPaid = feesPaid;
            FeesPct = feesPct;
            Exposure = exposure;
            Steps = steps;
            InitialEquity = initialEquity;
            FinalEquity = finalEquity;
        }
    }
}
