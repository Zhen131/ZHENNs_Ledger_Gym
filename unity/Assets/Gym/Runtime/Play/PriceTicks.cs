using System.Collections.Generic;

namespace Gym.Runtime.Play
{
    /// <summary><see cref="PriceScale.Compute"/> 的结果：刻度间隔，和落在可见范围里的每条线的价格（从低到高）。</summary>
    public readonly struct PriceTicks
    {
        public readonly double Step;
        public readonly IReadOnlyList<double> Values;

        public PriceTicks(double step, IReadOnlyList<double> values)
        {
            Step = step;
            Values = values;
        }
    }
}
