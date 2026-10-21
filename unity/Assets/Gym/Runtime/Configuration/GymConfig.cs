using System;
using Gym.Core.Env;

namespace Gym.Runtime.Configuration
{
    /// <summary>gym-config.json 的结构。</summary>
    [Serializable]
    public class GymConfig
    {
        /// <summary>主币：不给 -gymSymbol 时用它。</summary>
        public string symbol;
        /// <summary>主币的小时线文件。</summary>
        public string dataFile;
        /// <summary>
        /// 这份配置认识的所有币（主币也要列在里面，数据文件和上面的 dataFile 是同一个），每只币有自己的数据文件，
        /// evalOnly 的只考不训。-gymSymbol 从这里面选当前币。空或没有时等于只有主币。
        /// JsonUtility 不支持字典，所以用数组。
        /// </summary>
        public SymbolDataEntry[] symbols;
        /// <summary>
        /// 训练用的币（混训的清单）：symbols 里的币名（不分大小写），不许有 evalOnly 的，不许重复。训练时每局从里面选一只，
        /// 选哪只由这一局的 seed 定。空或没有时只用当前币（-gymSymbol 选的那只，没给时是主币）。
        /// 命令行 -gymTrainSymbols 盖过它。
        /// </summary>
        public string[] trainSymbols;
        public double initialCash;
        public int episodeLength;
        public double randomInitialPositionShare;
        public DateRange train;
        public DateRange validation;
        public DateRange test;
        /// <summary>
        /// validation、test 之外的评估段（例如 validation-bear、validation-recovery），每个有自己的名字和日期。
        /// 评估模式下 -gymSegment 可以写它们的名字。JsonUtility 不支持字典，所以用数组。
        /// </summary>
        public NamedDateRange[] extraSegments;
        public string playStart;
        /// <summary>
        /// 观测集：base（第一轮的 35 个数）、long（再加 6 个长周期的数）或 long-ta（再加 MACD、RSI）。
        /// 空或没有时是 base。命令行 -gymObs 盖过它，训练器的环境参数 obs_set 又盖过 -gymObs。
        /// </summary>
        public string observationSet;
        /// <summary>
        /// 动作模式：orders（下单模式，第一、二轮）或 dial（旋钮模式，第三轮）。空或没有时是 orders。
        /// 命令行 -gymActionMode 盖过它，训练器的环境参数 action_mode 又盖过 -gymActionMode。
        /// </summary>
        public string actionMode = ActionModes.OrdersName;
        /// <summary>旋钮模式每次决策走几根小时 K 线（正整数）。命令行 -gymDecisionHours、环境参数 decision_hours 盖过它。</summary>
        public int decisionHours = 24;
        /// <summary>旋钮模式的调仓带宽，(0, 0.5) 内。命令行 -gymBand、环境参数 rebalance_band 盖过它。</summary>
        public double rebalanceBand = 0.05;
        /// <summary>旋钮模式的档位（币占总资产的比例），严格递增、都在 (0, 1) 内、至少两个。</summary>
        public double[] dialLevels = { 0.3, 0.5, 0.7 };
        /// <summary>旋钮模式每局的小时数，[720, 2160] 内；下单模式仍用 episodeLength。</summary>
        public int dialEpisodeLength = 2160;
    }
}
