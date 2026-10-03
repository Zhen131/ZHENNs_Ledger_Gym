namespace Gym.Core.Env
{
    public readonly struct StepResult
    {
        public readonly double Reward;
        public readonly bool RewardClipped;
        public readonly bool Done;
        public readonly EndReason Reason;
        public readonly bool Traded;
        public readonly bool Rejected;

        public StepResult(double reward, bool rewardClipped, bool done, EndReason reason, bool traded, bool rejected)
        {
            Reward = reward;
            RewardClipped = rewardClipped;
            Done = done;
            Reason = reason;
            Traded = traded;
            Rejected = rejected;
        }
    }
}
