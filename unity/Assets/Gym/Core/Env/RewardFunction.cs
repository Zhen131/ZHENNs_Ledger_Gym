using System;

namespace Gym.Core.Env
{
    /// <summary>
    /// 原得分 r = clamp(100 × ln(E₁ ÷ E₀), −1, 1)，E₁ ≤ 0 时 r = −1。fee 已经从 equity 里扣掉了，所以没有单独的 fee 惩罚。
    /// 风险系数 λ（≥ 0、有限）只罚亏损：罚款 = λ × min(r, 0)²，得分 = r − 罚款。λ = 0 时得分逐位等于 r。
    /// clipped 只看 r 有没有被截，和罚款无关。
    /// </summary>
    public static class RewardFunction
    {
        public const double Scale = 100;
        public const double Limit = 1;

        /// <summary>不加罚款的入口，等价于 λ = 0。</summary>
        public static double Compute(double equityBefore, double equityAfter, out bool clipped) =>
            Compute(equityBefore, equityAfter, 0, out clipped, out _);

        /// <summary>得分 = r − λ × min(r, 0)²。</summary>
        public static double Compute(double equityBefore, double equityAfter, double riskAversion, out bool clipped) =>
            Compute(equityBefore, equityAfter, riskAversion, out clipped, out _);

        /// <summary>得分 = r − λ × min(r, 0)²；<paramref name="penalty"/> 是这一步的罚款（≥ 0，赚的步为 0）。</summary>
        public static double Compute(double equityBefore, double equityAfter, double riskAversion, out bool clipped,
            out double penalty)
        {
            CheckRiskAversion(riskAversion);
            double r = Raw(equityBefore, equityAfter, out clipped);
            penalty = Penalty(r, riskAversion);
            return r - penalty;
        }

        /// <summary>罚款 λ × min(r, 0)²：r ≥ 0 时为 0。λ 不合法时抛 <see cref="ArgumentOutOfRangeException"/>。</summary>
        public static double Penalty(double r, double riskAversion)
        {
            CheckRiskAversion(riskAversion);
            double loss = Math.Min(r, 0);
            return riskAversion * (loss * loss);
        }

        /// <summary>λ 是不是一个能用的风险系数：有限、≥ 0（NaN 不算）。</summary>
        public static bool IsValidRiskAversion(double riskAversion) =>
            riskAversion >= 0 && !double.IsInfinity(riskAversion);

        /// <summary>λ 为负、NaN 或无穷大时抛 <see cref="ArgumentOutOfRangeException"/>。</summary>
        public static void CheckRiskAversion(double riskAversion)
        {
            if (!IsValidRiskAversion(riskAversion))
                throw new ArgumentOutOfRangeException(nameof(riskAversion), riskAversion, "Must be a finite value >= 0.");
        }

        /// <summary>原得分 r（改动前的 Compute，一字不差）。</summary>
        static double Raw(double equityBefore, double equityAfter, out bool clipped)
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
