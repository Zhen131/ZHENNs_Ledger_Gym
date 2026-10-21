using System;
using System.Collections.Generic;

namespace Gym.Core.Env
{
    /// <summary>
    /// 旋钮模式的三样东西：档位（目标仓位 = 币值 ÷ 总资产，严格递增、都在 (0, 1) 内、至少两个）、每次决策走几根小时 K 线、
    /// 调仓带宽（(0, 0.5) 内）。这里只校验宽范围；缺省值不在这里（模型的缺省在配置里，对手的固定参数在 Baselines 里）。
    /// </summary>
    public sealed class DialSettings
    {
        readonly double[] levels;

        public DialSettings(IReadOnlyList<double> levels, int decisionHours, double band)
        {
            List<string> problems = Problems(levels, decisionHours, band);
            if (problems.Count > 0) throw new ArgumentException(string.Join("; ", problems));
            this.levels = new double[levels.Count];
            for (int i = 0; i < levels.Count; i++) this.levels[i] = levels[i];
            DecisionHours = decisionHours;
            Band = band;
        }

        /// <summary>档位：第 k 档的目标仓位。</summary>
        public IReadOnlyList<double> Levels => levels;
        /// <summary>每次决策走几根小时 K 线（段尾不足时走完剩下的）。</summary>
        public int DecisionHours { get; }
        /// <summary>调仓带宽：实际仓位偏离目标超过它才下单。</summary>
        public double Band { get; }

        /// <summary>按宽范围查一遍，返回每一条问题（英文）；没有问题时返回空表。配置载入器也用它。</summary>
        public static List<string> Problems(IReadOnlyList<double> levels, int decisionHours, double band)
        {
            List<string> problems = LevelProblems(levels);
            if (decisionHours < 1) problems.Add($"decision hours must be a whole number >= 1 (got {decisionHours})");
            if (!(band > 0 && band < 0.5)) problems.Add($"rebalance band must be inside (0, 0.5) (got {Text(band)})");
            return problems;
        }

        /// <summary>只查档位：至少两个、都在 (0, 1) 内、严格递增。返回每一条问题（英文）。</summary>
        public static List<string> LevelProblems(IReadOnlyList<double> levels)
        {
            var problems = new List<string>();
            if (levels == null || levels.Count < 2)
                problems.Add($"dial levels need at least two values (got {(levels == null ? 0 : levels.Count)})");
            else
            {
                for (int i = 0; i < levels.Count; i++)
                {
                    double v = levels[i];
                    if (!(v > 0 && v < 1))
                        problems.Add($"dial level {i} = {Text(v)} must be inside (0, 1)");
                    if (i > 0 && !(v > levels[i - 1]))
                        problems.Add($"dial levels must be strictly increasing (level {i} = {Text(v)} after {Text(levels[i - 1])})");
                }
            }
            return problems;
        }

        static string Text(double v) => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    }
}
