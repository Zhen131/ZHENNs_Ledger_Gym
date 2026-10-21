namespace Gym.Core.Env
{
    /// <summary>
    /// 模型手里的动作是什么。数值就是训练器环境参数 action_mode 的值。
    /// </summary>
    public enum ActionMode
    {
        /// <summary>下单模式（第一、二轮，默认）：每小时一次，买、卖或不动，带一个比例。</summary>
        Orders = 0,
        /// <summary>旋钮模式（第三轮）：每 <see cref="DialSettings.DecisionHours"/> 根小时 K 线开一次口，只选一个目标仓位档位，买卖由调仓规则代劳。</summary>
        Dial = 1,
    }

    public static class ActionModes
    {
        public const string OrdersName = "orders";
        public const string DialName = "dial";
        /// <summary>报错时列出的名字。</summary>
        public const string NamesText = "orders or dial";
        /// <summary>报错时列出的环境参数值。</summary>
        public const string ParameterValuesText = "0 (orders) or 1 (dial)";

        public static string Name(this ActionMode mode)
        {
            switch (mode)
            {
                case ActionMode.Orders: return OrdersName;
                case ActionMode.Dial: return DialName;
                default: throw new System.ArgumentOutOfRangeException(nameof(mode), (int)mode, "Not an action mode.");
            }
        }

        /// <summary>按名字（不分大小写）找模式；不是 orders、dial 时返回 false。</summary>
        public static bool TryParse(string text, out ActionMode mode)
        {
            mode = ActionMode.Orders;
            if (text == null) return false;
            switch (text.Trim().ToLowerInvariant())
            {
                case OrdersName: mode = ActionMode.Orders; return true;
                case DialName: mode = ActionMode.Dial; return true;
                default: return false;
            }
        }

        /// <summary>环境参数 action_mode 的值：只认 0、1（按 float 传来，必须正好是整数）。</summary>
        public static bool TryFromParameter(double value, out ActionMode mode)
        {
            mode = ActionMode.Orders;
            if (value == 0) { mode = ActionMode.Orders; return true; }
            if (value == 1) { mode = ActionMode.Dial; return true; }
            return false;
        }
    }
}
