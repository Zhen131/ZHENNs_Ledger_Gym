using System;

namespace Gym.Core
{
    /// <summary>
    /// r = clamp(100 × ln(E₁ ÷ E₀), −1, 1). Fees are already out of the
    /// equity, so there is no separate fee penalty.
    /// </summary>
    public static class RewardFunction
    {
        public const double Scale = 100;
        public const double Limit = 1;

        public static double Compute(double equityBefore, double equityAfter, out bool clipped)
        {
            if (!(equityBefore > 0) || double.IsInfinity(equityBefore))
                throw new ArgumentOutOfRangeException(nameof(equityBefore), equityBefore, "Must be a finite value > 0.");
            if (!(equityAfter > 0))
            {
                clipped = true;
                return -Limit;
            }

            double raw = Scale * Math.Log(equityAfter / equityBefore);
            clipped = raw > Limit || raw < -Limit;
            return Math.Max(-Limit, Math.Min(Limit, raw));
        }
    }
}
