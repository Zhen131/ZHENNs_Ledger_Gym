using System;
using Gym.Core;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;

namespace Gym.Runtime
{
    /// <summary>Something that can fill the agent's actions when it runs on heuristics (the keyboard).</summary>
    public interface IActionSource
    {
        void FillActions(in ActionBuffers actionsOut);
    }

    public enum AgentStartMode
    {
        /// <summary>Random start in the training segment, episode length from the config.</summary>
        Training,
        /// <summary>Start in cash at the config's playStart and run to the end of the training segment.</summary>
        PlayFromConfig,
    }

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
        public int EpisodeSeed { get; private set; }
        public int FinishedEpisodes { get; private set; }
        public EpisodeStats LastEpisodeStats { get; private set; }

        public bool HasStepped { get; private set; }
        public int LastBranch { get; private set; }
        public float LastContinuous { get; private set; }
        public StepResult LastResult { get; private set; }

        public event Action<TradingAgent> EpisodeStarted;
        public event Action<TradingAgent> Stepped;

        public override void Initialize()
        {
            Settings = GymConfigLoader.LoadForRuntime();
            GymConfig c = Settings.Config;
            Env = TradingEnv.ForSegment(Settings.Series, Settings.Rules, Settings.Train,
                c.initialCash, c.episodeLength, c.randomInitialPositionShare);
            MasterSeed = unchecked((int)DateTime.UtcNow.Ticks + 7919 * (agentIndex + 1));
            seedSource = new System.Random(MasterSeed);
            Debug.Log($"[Gym] {name}: index {agentIndex}, mode {startMode}, master seed {MasterSeed}");
            if (startMode == AgentStartMode.PlayFromConfig) ResetEnv(); // views can draw before the first step
        }

        public override void OnEpisodeBegin() => ResetEnv();

        public override void CollectObservations(VectorSensor sensor)
        {
            Env.WriteObservation(observation);
            sensor.AddObservation(observation);
        }

        public override void WriteDiscreteActionMask(IDiscreteActionMask actionMask)
        {
            actionMask.SetActionEnabled(0, ActionCodec.Buy, Env.BuyEnabled);
            actionMask.SetActionEnabled(0, ActionCodec.Sell, Env.SellEnabled);
        }

        public override void OnActionReceived(ActionBuffers actions)
        {
            int branch = actions.DiscreteActions[0];
            float continuous = actions.ContinuousActions[0];
            StepResult result = Env.Step(branch, continuous);

            HasStepped = true;
            LastBranch = branch;
            LastContinuous = continuous;
            LastResult = result;
            AddReward((float)result.Reward);
            Stepped?.Invoke(this);

            if (result.Done)
            {
                RecordEpisodeStats();
                FinishedEpisodes++;
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
            discrete[0] = ActionCodec.Hold;
            continuous[0] = 0f;
        }

        void ResetEnv()
        {
            CostModel cost = ReadCost();
            if (startMode == AgentStartMode.PlayFromConfig)
            {
                EpisodeSeed = 0;
                Env.Reset(cost, Settings.PlayStartIndex);
            }
            else
            {
                EpisodeSeed = seedSource.Next();
                Env.Reset(EpisodeSeed, false, cost);
            }
            HasStepped = false;
            LastBranch = ActionCodec.Hold;
            LastContinuous = 0f;
            LastResult = default;
            EpisodeStarted?.Invoke(this);
        }

        CostModel ReadCost()
        {
            EnvironmentParameters parameters = Academy.Instance.EnvironmentParameters;
            return new CostModel(
                ReadParameter(parameters, FeeRateKey, defaultFeeRate),
                ReadParameter(parameters, FixedFeeKey, defaultFixedFee),
                ReadParameter(parameters, SlippageKey, defaultSlippage));
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
