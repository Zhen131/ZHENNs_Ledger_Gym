namespace Gym.Core.Env
{
    /// <summary>旋钮模式的一步（一次决策，走 <see cref="Hours"/> 根小时 K 线）。</summary>
    public readonly struct DialStepResult
    {
        /// <summary>这一步选的档位和它的目标仓位。</summary>
        public readonly int Level;
        public readonly double Target;
        /// <summary>这一步的目标和上一步不同（第一步不算）。</summary>
        public readonly bool Changed;
        /// <summary>这一步走了几根（通常是决策根数，段尾或局尾不足时是剩下的根数）。</summary>
        public readonly int Hours;
        /// <summary>这几小时得分之和。</summary>
        public readonly double Reward;
        /// <summary>这几小时里得分被截的次数。</summary>
        public readonly int ClippedHours;
        /// <summary>任一小时成交。</summary>
        public readonly bool Traded;
        /// <summary>任一小时有被拒绝的单子。</summary>
        public readonly bool Rejected;
        public readonly bool Done;
        public readonly EndReason Reason;

        public DialStepResult(int level, double target, bool changed, int hours, double reward, int clippedHours,
            bool traded, bool rejected, bool done, EndReason reason)
        {
            Level = level;
            Target = target;
            Changed = changed;
            Hours = hours;
            Reward = reward;
            ClippedHours = clippedHours;
            Traded = traded;
            Rejected = rejected;
            Done = done;
            Reason = reason;
        }

        /// <summary>按下单模式那一步的样子汇总（给只认 <see cref="StepResult"/> 的地方用）：被截 = 任一小时被截。</summary>
        public StepResult AsStepResult() => new StepResult(Reward, ClippedHours > 0, Done, Reason, Traded, Rejected);
    }
}
