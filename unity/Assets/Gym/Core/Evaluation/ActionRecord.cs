using System;
using System.Collections.Generic;
using Gym.Core.Env;

namespace Gym.Core.Evaluation
{
    /// <summary>
    /// 评估记录里和动作模式有关的那几样（24 批）：流水 notes 列末尾的一段，和明细 JSON 里的七个键。
    /// Agent 的评估（EvalRunner）和两个对手（EvalTools）用同一份写法。
    /// </summary>
    public static class ActionRecord
    {
        /// <summary>
        /// notes 里动作模式那一段：下单模式 <c>mode=orders</c>；旋钮模式 <c>mode=dial; band=&lt;带宽&gt;; hours=&lt;决策根数&gt;; changes=&lt;换档次数&gt;</c>，
        /// 顺序固定如此。数字按 invariant culture 写（带宽用全精度 "R"），不含逗号，notes 仍是一个 CSV 格子。
        /// </summary>
        public static string Notes(ActionMode mode, DialSettings dial, int dialChanges)
        {
            if (mode == ActionMode.Orders) return "mode=" + ActionModes.OrdersName;
            if (mode != ActionMode.Dial) throw new ArgumentOutOfRangeException(nameof(mode), (int)mode, "Not an action mode.");
            if (dial == null) throw new ArgumentNullException(nameof(dial), "The dial mode needs its dial settings.");
            if (dialChanges < 0) throw new ArgumentOutOfRangeException(nameof(dialChanges), dialChanges, "Must be >= 0.");
            return $"mode={ActionModes.DialName}; band={EvaluationLog.Number(dial.Band)}; " +
                   $"hours={dial.DecisionHours.ToString(System.Globalization.CultureInfo.InvariantCulture)}; " +
                   $"changes={dialChanges.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        }

        /// <summary>按跑过的环境写 notes 的那一段（模式、带宽、决策根数、换档次数都读环境）。</summary>
        public static string Notes(TradingEnv env)
        {
            if (env == null) throw new ArgumentNullException(nameof(env));
            return Notes(env.ActionMode, env.Dial, env.DialChanges);
        }

        /// <summary>
        /// 往明细 JSON 里加七个键：action_mode（orders／dial）、decision_hours、rebalance_band、dial_levels、dial_changes、decisions、
        /// last_decision_hours，都读跑过的环境。下单模式下 decision_hours 是 1（每根一次）、rebalance_band 是 null、dial_levels 是空数组、
        /// dial_changes 是 0、decisions 等于步数。
        /// </summary>
        public static void AddDetails(JsonObject details, TradingEnv env)
        {
            if (details == null) throw new ArgumentNullException(nameof(details));
            if (env == null) throw new ArgumentNullException(nameof(env));
            DialSettings dial = env.Dial;
            details.Add("action_mode", env.ActionMode.Name());
            details.Add("decision_hours", dial == null ? 1 : dial.DecisionHours);
            details.Add("rebalance_band", dial == null ? null : (object)dial.Band);
            details.Add("dial_levels", dial == null ? new List<double>() : new List<double>(dial.Levels));
            details.Add("dial_changes", env.DialChanges);
            details.Add("decisions", env.Decisions);
            details.Add("last_decision_hours", env.LastDecisionHours);
        }
    }
}
