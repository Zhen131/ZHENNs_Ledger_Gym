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
    /// ML-Agents shell around <see cref="TradingEnv"/>. No bookkeeping, observation
    /// or reward logic lives here (02B §2.2).
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

        /// <summary>Set by the Play scene's PlayController; null means "hold" on heuristics.</summary>
        public IActionSource ActionSource { get; set; }

        public GymSettings Settings { get; private set; }
        public TradingEnv Env { get; private set; }
        public int MasterSeed { get; private set; }
        /// <summary>Trainer when the seed came from mlagents-learn (--seed), Clock otherwise.</summary>
        public SeedSource MasterSeedSource { get; private set; }
        public int EpisodeSeed { get; private set; }
        public int FinishedEpisodes { get; private set; }
        public EpisodeStats LastEpisodeStats { get; private set; }

        public bool HasStepped { get; private set; }
        public int LastBranch { get; private set; }
        public float LastContinuous { get; private set; }
        public StepResult LastResult { get; private set; }

        public EpisodeMetrics LastEpisodeMetrics { get; private set; }

        /// <summary>How this agent actually starts episodes: Evaluation when -gymMode eval was given.</summary>
        public AgentStartMode EffectiveMode =>
            Settings != null && Settings.Mode == GymMode.Eval ? AgentStartMode.Evaluation : startMode;

        public event Action<TradingAgent> EpisodeStarted;
        public event Action<TradingAgent> Stepped;
        /// <summary>Raised when an episode ends, before the next one is reset.</summary>
        public event Action<TradingAgent, EpisodeMetrics> EpisodeFinished;

        public override void Initialize()
        {
            Settings = GymConfigLoader.LoadForRuntime();
            GymConfig c = Settings.Config;
            AgentStartMode mode = EffectiveMode;
            SegmentSpec segment = mode == AgentStartMode.Evaluation ? Settings.EvalSegment : Settings.Train;
            Env = TradingEnv.ForSegment(Settings.Series, Settings.Rules, segment,
                c.initialCash, c.episodeLength, c.randomInitialPositionShare);
            Academy academy = Academy.Instance; // connects to the trainer first, which sends the seed
            int trainerSeed = 0;
            bool fromTrainer = academy.IsCommunicatorOn && MasterSeedChooser.TryReadTrainerSeed(academy, out trainerSeed);
            if (academy.IsCommunicatorOn && !fromTrainer)
                Debug.LogWarning("[Gym] trainer attached but its seed could not be read; seeding from the clock");
            (MasterSeed, MasterSeedSource) = MasterSeedChooser.Choose(fromTrainer, trainerSeed, DateTime.UtcNow.Ticks, agentIndex);
            seedSource = new System.Random(MasterSeed);
            Debug.Log($"[Gym] {name}: index {agentIndex}, mode {mode}, segment {segment}, master seed {MasterSeed} ({MasterSeedChooser.LogName(MasterSeedSource)})");
            if (mode != AgentStartMode.Training) ResetEnv(); // views and runners can read the start before the first step
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
            int branch = actions.DiscreteActions[0];
            float continuous = actions.ContinuousActions[0];
            StepResult result = Env.Step((TradeAction)branch, continuous);

            HasStepped = true;
            LastBranch = branch;
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
                EpisodeInterrupted(); // a time limit, not a terminal state
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
            LastBranch = (int)TradeAction.Hold;
            LastContinuous = 0f;
            LastResult = default;
            EpisodeStarted?.Invoke(this);
        }

        /// <summary>
        /// Trainer's environment parameter if set, else -gymFeeRate / -gymFixedFee /
        /// -gymSlippage from the command line, else the Inspector default.
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
        /// Environment parameters arrive as float. Going through decimal turns 0.001f
        /// into 0.001 instead of 0.0010000000474974513.
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
