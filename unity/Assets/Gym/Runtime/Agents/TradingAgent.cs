using System;
using Gym.Core.Accounting;
using Gym.Core.Env;
using Gym.Core.Evaluation;
using Gym.Core.Market;
using Gym.Runtime.Configuration;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;

namespace Gym.Runtime.Agents
{
    /// <summary>
    /// 包在 <see cref="TradingEnv"/> 外面的 ML-Agents 外壳。记账、observation 和 reward 的逻辑都不在这里。
    /// </summary>
    public class TradingAgent : Agent
    {
        public const string BehaviorNameValue = "TradingAgent";
        public const string FeeRateKey = "fee_rate";
        public const string FixedFeeKey = "fixed_fee";
        public const string SlippageKey = "slippage";

        [SerializeField] AgentStartMode startMode = AgentStartMode.Training;
        [Tooltip("Instance number; mixed with the clock to seed this agent's episode seeds.")]
        [SerializeField] int agentIndex;
        [SerializeField] double defaultFeeRate = CostModel.DefaultFeeRate;
        [SerializeField] double defaultFixedFee;
        [SerializeField] double defaultSlippage;

        readonly float[] observation = new float[ObservationBuilder.Size];
        System.Random seedSource;
        bool firstEpisodeLogged;

        public AgentStartMode StartMode
        {
            get => startMode;
            set => startMode = value;
        }

        public int AgentIndex
        {
            get => agentIndex;
            set => agentIndex = value;
        }

        public double DefaultFeeRate => defaultFeeRate;
        public double DefaultFixedFee => defaultFixedFee;
        public double DefaultSlippage => defaultSlippage;

        /// <summary>由 Play scene 的 PlayController 设置；为 null 时，heuristic 一律不动。</summary>
        public IActionSource ActionSource { get; set; }

        public GymSettings Settings { get; private set; }
        public TradingEnv Env { get; private set; }
        public int MasterSeed { get; private set; }
        /// <summary>seed 来自 mlagents-learn（--seed）时为 Trainer，否则为 Clock。</summary>
        public SeedSource MasterSeedSource { get; private set; }
        public int EpisodeSeed { get; private set; }
        public int FinishedEpisodes { get; private set; }
        public EpisodeStats LastEpisodeStats { get; private set; }

        public bool HasStepped { get; private set; }
        public TradeAction LastAction { get; private set; }
        public float LastContinuous { get; private set; }
        public StepResult LastResult { get; private set; }

        public EpisodeMetrics LastEpisodeMetrics { get; private set; }

        /// <summary>这个 Agent 实际怎样开始 episode：给了 -gymMode eval 时就是 Evaluation。</summary>
        public AgentStartMode EffectiveMode =>
            Settings != null && Settings.Mode == GymMode.Eval ? AgentStartMode.Evaluation : startMode;

        public event Action<TradingAgent> EpisodeStarted;
        public event Action<TradingAgent> Stepped;
        /// <summary>一个 episode 结束时触发，在下一个 episode 重置之前。</summary>
        public event Action<TradingAgent, EpisodeMetrics> EpisodeFinished;

        public override void Initialize()
        {
            Settings = GymConfigLoader.LoadForRuntime();
            GymConfig c = Settings.Config;
            AgentStartMode mode = EffectiveMode;
            SegmentSpec segment = mode == AgentStartMode.Evaluation ? Settings.EvalSegment : Settings.Train;
            Env = TradingEnv.ForSegment(Settings.Series, Settings.Rules, segment,
                c.initialCash, c.episodeLength, c.randomInitialPositionShare);
            Academy academy = Academy.Instance; // 先连上 trainer，seed 由 trainer 发来
            int trainerSeed = 0;
            bool fromTrainer = academy.IsCommunicatorOn && MasterSeedChooser.TryReadTrainerSeed(academy, out trainerSeed);
            if (academy.IsCommunicatorOn && !fromTrainer)
                Debug.LogWarning("[Gym] trainer attached but its seed could not be read; seeding from the clock");
            (MasterSeed, MasterSeedSource) = MasterSeedChooser.Choose(fromTrainer, trainerSeed, DateTime.UtcNow.Ticks, agentIndex);
            seedSource = new System.Random(MasterSeed);
            Debug.Log($"[Gym] {name}: index {agentIndex}, mode {mode}, segment {segment}, master seed {MasterSeed} ({MasterSeedChooser.LogName(MasterSeedSource)})");
            if (mode != AgentStartMode.Training) ResetEnv(); // 这样 view 和 runner 在第一个 step 之前就能读到起点
        }

        public override void OnEpisodeBegin() => ResetEnv();

        public override void CollectObservations(VectorSensor sensor)
        {
            Env.WriteObservation(observation);
            sensor.AddObservation(observation);
        }

        public override void WriteDiscreteActionMask(IDiscreteActionMask actionMask)
        {
            actionMask.SetActionEnabled(0, (int)TradeAction.Buy, Env.BuyEnabled);
            actionMask.SetActionEnabled(0, (int)TradeAction.Sell, Env.SellEnabled);
        }

        public override void OnActionReceived(ActionBuffers actions)
        {
            var action = (TradeAction)actions.DiscreteActions[0];
            float continuous = actions.ContinuousActions[0];
            StepResult result = Env.Step(action, continuous);

            HasStepped = true;
            LastAction = action;
            LastContinuous = continuous;
            LastResult = result;
            AddReward((float)result.Reward);
            Stepped?.Invoke(this);

            if (result.Done)
            {
                RecordEpisodeStats();
                LastEpisodeMetrics = Metrics.From(Env);
                FinishedEpisodes++;
                EpisodeFinished?.Invoke(this, LastEpisodeMetrics);
                EpisodeInterrupted(); // 是时间上限，不是终止状态
            }
        }

        public override void Heuristic(in ActionBuffers actionsOut)
        {
            if (ActionSource != null)
            {
                ActionSource.FillActions(actionsOut);
                return;
            }
            ActionSegment<int> discrete = actionsOut.DiscreteActions;
            ActionSegment<float> continuous = actionsOut.ContinuousActions;
            discrete[0] = (int)TradeAction.Hold;
            continuous[0] = 0f;
        }

        void ResetEnv()
        {
            CostModel cost = ReadCost();
            AgentStartMode mode = EffectiveMode;
            if (mode == AgentStartMode.PlayFromConfig)
            {
                EpisodeSeed = 0;
                Env.Reset(cost, Settings.PlayStartIndex);
            }
            else if (mode == AgentStartMode.Evaluation)
            {
                EpisodeSeed = 0;
                Env.ResetForEvaluation(0, cost);
            }
            else
            {
                EpisodeSeed = seedSource.Next();
                if (!firstEpisodeLogged)
                {
                    firstEpisodeLogged = true;
                    Debug.Log($"[Gym] {name}: first episode seed {EpisodeSeed}");
                }
                Env.ResetForTraining(EpisodeSeed, cost);
            }
            HasStepped = false;
            LastAction = TradeAction.Hold;
            LastContinuous = 0f;
            LastResult = default;
            EpisodeStarted?.Invoke(this);
        }

        /// <summary>
        /// trainer 设了环境参数就用环境参数；否则用命令行里的 -gymFeeRate / -gymFixedFee /
        /// -gymSlippage；再没有就用 Inspector 里的默认值。
        /// </summary>
        CostModel ReadCost()
        {
            EnvironmentParameters parameters = Academy.Instance.EnvironmentParameters;
            return new CostModel(
                ReadParameter(parameters, FeeRateKey, Settings.FeeRateArg ?? defaultFeeRate),
                ReadParameter(parameters, FixedFeeKey, Settings.FixedFeeArg ?? defaultFixedFee),
                ReadParameter(parameters, SlippageKey, Settings.SlippageArg ?? defaultSlippage));
        }

        /// <summary>
        /// 环境参数传过来是 float。先转成 decimal，0.001f 就变成 0.001，而不是 0.0010000000474974513。
        /// </summary>
        static double ReadParameter(EnvironmentParameters parameters, string key, double fallback)
        {
            float value = parameters.GetWithDefault(key, float.NaN);
            if (float.IsNaN(value)) return fallback;
            if (float.IsInfinity(value)) return value;
            return (double)(decimal)value;
        }

        void RecordEpisodeStats()
        {
            EpisodeStats stats = EpisodeStats.From(Env);
            LastEpisodeStats = stats;
            StatsRecorder recorder = Academy.Instance.StatsRecorder;
            recorder.Add("Trading/Return", (float)stats.Return, StatAggregationMethod.Average);
            recorder.Add("Trading/Trades", stats.Trades, StatAggregationMethod.Average);
            recorder.Add("Trading/Rejected", stats.Rejected, StatAggregationMethod.Average);
            recorder.Add("Trading/FeesPaidPct", (float)stats.FeesPaidPct, StatAggregationMethod.Average);
            recorder.Add("Trading/Exposure", (float)stats.Exposure, StatAggregationMethod.Average);
            recorder.Add("Trading/Turnover", (float)stats.Turnover, StatAggregationMethod.Average);
            recorder.Add("Trading/RewardClips", stats.RewardClips, StatAggregationMethod.Average);
            recorder.Add("Trading/FeeRate", (float)stats.FeeRate, StatAggregationMethod.Average);
            recorder.Add("Trading/FixedFee", (float)stats.FixedFee, StatAggregationMethod.Average);
        }
    }
}
