using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Gym.Core.Accounting;
using Gym.Core.Env;
using Gym.Core.Evaluation;
using Gym.Core.Market;
using Gym.Runtime.Configuration;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
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
        /// <summary>训练器的环境参数：0 = base、1 = long、2 = long-ta。没设时用命令行 -gymObs 或配置里的观测集。</summary>
        public const string ObservationSetKey = "obs_set";
        /// <summary>训练器的环境参数：奖励的风险系数 λ（≥ 0、有限）。没设时用命令行 -gymRiskAversion，再没有用 Inspector 默认值。</summary>
        public const string RiskAversionKey = "risk_aversion";

        [SerializeField] AgentStartMode startMode = AgentStartMode.Training;
        [Tooltip("Instance number; mixed with the clock to seed this agent's episode seeds.")]
        [SerializeField] int agentIndex;
        [SerializeField] double defaultFeeRate = CostModel.DefaultFeeRate;
        [SerializeField] double defaultFixedFee;
        [SerializeField] double defaultSlippage;
        [SerializeField] double defaultRiskAversion;

        /// <summary>按当前观测集的大小分配（<see cref="Initialize"/> 里）。</summary>
        float[] observation = Array.Empty<float>();
        /// <summary>训练模式下训练用的每只币一个环境（按清单的顺序）；别的模式只有当前币一个。<see cref="Env"/> 是其中正在用的那个。</summary>
        TradingEnv[] envs = Array.Empty<TradingEnv>();
        System.Random seedSource;
        bool firstEpisodeLogged;
        /// <summary>训练玩家开工那一行已经为哪份设置写过（每份设置写一次，不是每个 Agent 一次）。</summary>
        static GymSettings trainingStartLoggedFor;

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
        public double DefaultRiskAversion => defaultRiskAversion;

        /// <summary>由 Play scene 的 PlayController 设置；为 null 时，heuristic 一律不动。</summary>
        public IActionSource ActionSource { get; set; }

        /// <summary>
        /// 观战用：有值时 <see cref="Initialize"/> 走这个评估分段，不看 -gymSegment。要在 Agent 启用之前设。
        /// 训练和评估都不设它（为 null），走原来的路。
        /// </summary>
        public EvaluationSegment? SegmentOverride { get; set; }

        /// <summary>
        /// 观战用：有值时每个 episode 都用这份费用，不读 trainer 的环境参数、命令行和 Inspector 默认值。
        /// 训练和评估都不设它（为 null），走原来的路。
        /// </summary>
        public CostModel CostOverride { get; set; }

        /// <summary>
        /// 测试和观战用：有值时 <see cref="Initialize"/> 用这一档观测，不看 trainer 的环境参数 obs_set、-gymObs 和配置。
        /// 要在 Agent 启用之前设。训练和评估都不设它（为 null），走原来的路。
        /// </summary>
        public ObservationSet? ObservationSetOverride { get; set; }

        /// <summary>
        /// 测试用：有值时训练模式的 Agent 用这份清单（币名）混训，不看 -gymTrainSymbols 和配置里的 trainSymbols；
        /// 名字照样要是配置里的币、不是 evalOnly、不重复。要在 Agent 启用之前设。训练和评估都不设它（为 null），走原来的路。
        /// </summary>
        public IReadOnlyList<string> TrainSymbolsOverride { get; set; }

        /// <summary>这个 Agent 实际用的观测集（<see cref="Initialize"/> 之后才有意义）。</summary>
        public ObservationSet ObservationSet => Env != null ? Env.ObservationSet : ObservationSet.Base;

        public GymSettings Settings { get; private set; }
        /// <summary>这一局用的环境。训练模式下有几只训练用的币，每局开始时就在 <see cref="Envs"/> 里按本局的 seed 选一个。</summary>
        public TradingEnv Env { get; private set; }
        /// <summary>
        /// 这个 Agent 的所有环境：训练模式下训练用的每只币一个（各用这只币裁过的训练段、同一档观测），按清单的顺序；
        /// 别的模式（评估、试玩、观战）只有当前币一个。
        /// </summary>
        public IReadOnlyList<TradingEnv> Envs => envs;
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
            // 先连上 trainer，再建环境：seed 和环境参数 obs_set 都由 trainer 发来，obs_set 决定环境的历史长度。
            Academy academy = Academy.Instance;
            ObservationSet observationSet = ChooseObservationSet(academy);
            AgentStartMode mode = EffectiveMode;
            SegmentSpec segment;
            IReadOnlyList<SymbolSettings> trainSymbols = null;
            if (SegmentOverride.HasValue) segment = SegmentFor(SegmentOverride.Value);
            else if (mode == AgentStartMode.Evaluation) segment = Settings.EvalSegment;
            else if (mode == AgentStartMode.Training)
            {
                trainSymbols = ChooseTrainSymbols();
                segment = trainSymbols[0].Train;
            }
            else segment = Settings.Train; // 试玩：当前币的训练段
            if (trainSymbols != null)
            {
                // 训练：清单里每只币一个环境，各用这只币裁过的训练段；只有当前币一只时就是改动前的那一个环境
                envs = new TradingEnv[trainSymbols.Count];
                for (int i = 0; i < envs.Length; i++)
                    envs[i] = TradingEnv.ForSegment(trainSymbols[i].Series, trainSymbols[i].Rules, trainSymbols[i].Train,
                        c.initialCash, c.episodeLength, c.randomInitialPositionShare, observationSet);
            }
            else
            {
                envs = new[]
                {
                    TradingEnv.ForSegment(Settings.Series, Settings.Rules, segment,
                        c.initialCash, c.episodeLength, c.randomInitialPositionShare, observationSet),
                };
            }
            Env = envs[0];
            UseObservationSize(observationSet.Size());
            int trainerSeed = 0;
            bool fromTrainer = academy.IsCommunicatorOn && MasterSeedChooser.TryReadTrainerSeed(academy, out trainerSeed);
            if (academy.IsCommunicatorOn && !fromTrainer)
                Debug.LogWarning("[Gym] trainer attached but its seed could not be read; seeding from the clock");
            (MasterSeed, MasterSeedSource) = MasterSeedChooser.Choose(fromTrainer, trainerSeed, DateTime.UtcNow.Ticks, agentIndex);
            seedSource = new System.Random(MasterSeed);
            Debug.Log($"[Gym] {name}: index {agentIndex}, mode {mode}, segment {segment}, observations {observationSet.Name()} " +
                      $"({observationSet.Size()} numbers, history {observationSet.HistoryLength()}), " +
                      $"master seed {MasterSeed} ({MasterSeedChooser.LogName(MasterSeedSource)})");
            if (trainSymbols != null)
            {
                Debug.Log($"[Gym] {name}: training symbols {TrainSymbolsText(trainSymbols)}" +
                          (trainSymbols.Count > 1 ? "; each episode picks one from its episode seed" : ""));
                LogTrainingStart(observationSet, trainSymbols);
            }
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
            double riskAversion = ReadRiskAversion();
            AgentStartMode mode = EffectiveMode;
            if (mode == AgentStartMode.PlayFromConfig)
            {
                EpisodeSeed = 0;
                Env.Reset(cost, Settings.PlayStartIndex, riskAversion);
            }
            else if (mode == AgentStartMode.Evaluation)
            {
                EpisodeSeed = 0;
                Env.ResetForEvaluation(0, cost, riskAversion);
            }
            else
            {
                EpisodeSeed = seedSource.Next();
                if (!firstEpisodeLogged)
                {
                    firstEpisodeLogged = true;
                    Debug.Log($"[Gym] {name}: first episode seed {EpisodeSeed}");
                }
                // 选币只看本局的 seed（不再从 seedSource 抽数）；只有一只币时恒为 0，Env 不变
                Env = envs[SymbolPicker.Pick(EpisodeSeed, envs.Length)];
                Env.ResetForTraining(EpisodeSeed, cost, riskAversion);
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
            if (CostOverride != null) return CostOverride; // 观战设的费用；训练和评估时它为 null
            EnvironmentParameters parameters = Academy.Instance.EnvironmentParameters;
            return new CostModel(
                ReadParameter(parameters, FeeRateKey, Settings.FeeRateArg ?? defaultFeeRate),
                ReadParameter(parameters, FixedFeeKey, Settings.FixedFeeArg ?? defaultFixedFee),
                ReadParameter(parameters, SlippageKey, Settings.SlippageArg ?? defaultSlippage));
        }

        /// <summary>
        /// 本局的风险系数：trainer 设了环境参数 risk_aversion 就用它（和费用用同一个 <see cref="ReadParameter"/> 读）；
        /// 否则用命令行里的 -gymRiskAversion；再没有就用 Inspector 里的默认值（0）。每局开始时和费用一起读。
        /// 不是有限的非负数时报错（player 以退出码 1 退出）。
        /// </summary>
        double ReadRiskAversion()
        {
            EnvironmentParameters parameters = Academy.Instance.EnvironmentParameters;
            double value = ReadParameter(parameters, RiskAversionKey, Settings.RiskAversionArg ?? defaultRiskAversion);
            if (!RewardFunction.IsValidRiskAversion(value))
                throw GymConfigLoader.FailAtRuntime(new[]
                {
                    $"{RiskAversionKey} = {value.ToString(System.Globalization.CultureInfo.InvariantCulture)} is not a risk coefficient; " +
                    $"use a finite value >= 0 (the trainer's environment parameter, {GymConfigLoader.RiskAversionArg} or the agent's default)",
                });
            return value;
        }

        /// <summary>
        /// 这个 Agent 用哪一档观测：<see cref="ObservationSetOverride"/>，否则 trainer 的环境参数 obs_set（和费用用同一个
        /// <see cref="ReadParameter"/> 读），否则 <see cref="GymSettings.ObservationSet"/>（-gymObs 或配置）。
        /// obs_set 不是 0、1、2 时报错；选中的档和配置、命令行定的不同时，按它的历史长度把分段长度和 playStart
        /// 重查一遍，不过就报错（player 以退出码 1 退出）。
        /// </summary>
        ObservationSet ChooseObservationSet(Academy academy)
        {
            ObservationSet chosen = Settings.ObservationSet;
            if (ObservationSetOverride.HasValue)
            {
                chosen = ObservationSetOverride.Value;
            }
            else
            {
                double value = ReadParameter(academy.EnvironmentParameters, ObservationSetKey, double.NaN);
                if (!double.IsNaN(value))
                {
                    if (!ObservationSets.TryFromParameter(value, out chosen))
                        throw GymConfigLoader.FailAtRuntime(new[]
                        {
                            $"environment parameter {ObservationSetKey} = {value.ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
                            $"is not an observation set; use {ObservationSets.ParameterValuesText}",
                        });
                    if (chosen != Settings.ObservationSet)
                        Debug.Log($"[Gym] {name}: the trainer's {ObservationSetKey} chooses the observation set {chosen.Name()} " +
                                  $"(the config and command line said {Settings.ObservationSet.Name()})");
                }
            }
            if (chosen != Settings.ObservationSet)
            {
                var problems = GymConfigLoader.ProblemsWithObservationSet(Settings, chosen);
                if (problems.Count > 0)
                {
                    problems.Insert(0, $"with the observation set {chosen.Name()} (history {chosen.HistoryLength()} candles) the config does not work:");
                    throw GymConfigLoader.FailAtRuntime(problems);
                }
            }
            return chosen;
        }

        /// <summary>
        /// 传感器按 BehaviorParameters 的向量观测长度新建，这一步在 <see cref="Initialize"/> 之后（ML-Agents 的
        /// Agent.LazyInitialize）：在这里把长度改成当前档的大小，预制体里的 35 不动；观测数组也按它分配。
        /// </summary>
        void UseObservationSize(int size)
        {
            var behavior = GetComponent<BehaviorParameters>();
            if (behavior != null) behavior.BrainParameters.VectorObservationSize = size;
            observation = new float[size];
        }

        /// <summary>
        /// 训练模式的 Agent 用哪些币：<see cref="TrainSymbolsOverride"/>，否则 <see cref="GymSettings.TrainSymbols"/>
        /// （-gymTrainSymbols、配置里的 trainSymbols，或者只有当前币）。再查一遍 <see cref="GymConfigLoader.ProblemsWithTraining"/>：
        /// 当前币是 evalOnly（只考不训）时报错（player 以退出码 1 退出）。
        /// </summary>
        IReadOnlyList<SymbolSettings> ChooseTrainSymbols()
        {
            IReadOnlyList<SymbolSettings> chosen = Settings.TrainSymbols;
            var errors = new List<string>();
            if (TrainSymbolsOverride != null)
                chosen = GymConfigLoader.ResolveTrainSymbols(Settings, TrainSymbolsOverride, nameof(TrainSymbolsOverride), errors);
            if (errors.Count == 0) errors.AddRange(GymConfigLoader.ProblemsWithTraining(Settings, chosen));
            if (errors.Count > 0) throw GymConfigLoader.FailAtRuntime(errors);
            return chosen;
        }

        /// <summary>
        /// 训练玩家开工时写一行：观测集、当前币、训练币清单（各带裁过的训练段）、当前币的分段日期、实际读的 gym-config.json
        /// 的 SHA-256。同一份设置只写一次（16 个 Agent 共用一份设置）。观测集是这个 Agent 实际用的档（trainer 的 obs_set 选的档
        /// 不写回设置，所以 <see cref="GymConfigLoader.LoadForRuntime"/> 那一行写的是配置和命令行的档）。
        /// </summary>
        void LogTrainingStart(ObservationSet set, IReadOnlyList<SymbolSettings> trainSymbols)
        {
            if (ReferenceEquals(trainingStartLoggedFor, Settings)) return;
            trainingStartLoggedFor = Settings;
            Debug.Log($"[Gym] training player: observations {set.Name()} ({set.Size()} numbers), current symbol {Settings.Rules.Symbol}, " +
                      $"training symbols {TrainSymbolsText(trainSymbols)}, segment {Settings.Train}, " +
                      $"gym-config.json sha256 {FileSha256(Settings.ConfigPath)} ({Settings.ConfigPath})");
        }

        /// <summary>文件的 SHA-256（小写十六进制）；读不了时写明读不了，不让训练因为一行日志停下。</summary>
        static string FileSha256(string path)
        {
            try
            {
                using (SHA256 sha = SHA256.Create())
                using (FileStream stream = File.OpenRead(path))
                    return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return $"unreadable ({e.Message})";
            }
        }

        /// <summary>日志里的清单，例如 "BTCUSDT (train 2017-08-17..2021-12-31), ADAUSDT (train 2018-04-17..2021-12-31)"。</summary>
        static string TrainSymbolsText(IReadOnlyList<SymbolSettings> symbols)
        {
            var parts = new List<string>();
            foreach (SymbolSettings symbol in symbols) parts.Add($"{symbol.Symbol} ({symbol.Train})");
            return string.Join(", ", parts);
        }

        /// <summary><see cref="SegmentOverride"/> 选的那个评估分段。</summary>
        SegmentSpec SegmentFor(EvaluationSegment which) =>
            which == EvaluationSegment.Validation ? Settings.Validation : Settings.Test;

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
            recorder.Add("Trading/RiskAversion", (float)stats.RiskAversion, StatAggregationMethod.Average);
            recorder.Add("Trading/RiskPenalty", (float)stats.RiskPenalty, StatAggregationMethod.Average);
            // 每只币各记一条：本局是这只记 1、不是记 0，按平均聚合就是各币的占比
            foreach ((string key, float value) in EpisodeStats.SymbolShares(SymbolNames(), Env.Rules.Symbol))
                recorder.Add(key, value, StatAggregationMethod.Average);
        }

        /// <summary><see cref="Envs"/> 的币名，按顺序。</summary>
        List<string> SymbolNames()
        {
            var names = new List<string>(envs.Length);
            foreach (TradingEnv env in envs) names.Add(env.Rules.Symbol);
            return names;
        }
    }
}
