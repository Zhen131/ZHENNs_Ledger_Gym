using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Gym.Core.Env;
using Gym.Core.Evaluation;
using Gym.Core.Market;
using Gym.Runtime.Agents;
using Gym.Runtime.Configuration;
using Unity.MLAgents;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Gym.Runtime.Evaluation
{
    /// <summary>
    /// 在 Eval scene 里驱动一个评估 episode：关掉自动 step，每帧成批调用 Academy.EnvironmentStep，
    /// 直到 Agent 的 episode 结束；把结果追加到 &lt;-gymOut&gt;/log.csv，另写一份 JSON 明细文件；
    /// 然后退出（成功为 0，失败为 1）。只有带着 -gymMode eval、-gymSegment &lt;段名&gt; 和
    /// -gymOut &lt;folder&gt; 启动时才运行；段名是 gym-config.json 里的一个评估段，由 GymConfigLoader 检查。
    /// 开工前还核对打包时记下的模型输入维数和当前观测集的大小、模型的动作形状和当前动作模式，对不上就退出码 1、什么都不写。
    /// </summary>
    public class EvalRunner : MonoBehaviour
    {
        public const string BuildInfoFile = "build-info.json";
        public const string OutArg = "-gymOut";

        /// <summary>超出分段的 candle 数之后，还允许多走几个环境 step；再多就算这次运行卡住了。</summary>
        public const int StepMargin = 100;

        [SerializeField] TradingAgent agent;
        [SerializeField] int stepsPerFrame = 500;
        [SerializeField] bool quitWhenDone = true;

        int exitCode = 1;
        int steps;
        int stepLimit;
        Stopwatch watch;
        EvalBuildInfo buildInfo;
        bool buildInfoRead;

        public TradingAgent Agent
        {
            get => agent;
            set => agent = value;
        }

        public bool QuitWhenDone
        {
            get => quitWhenDone;
            set => quitWhenDone = value;
        }

        public bool Finished { get; private set; }
        public EpisodeMetrics Result { get; private set; }
        public string LogPath { get; private set; }
        public string DetailsPath { get; private set; }

        void Awake()
        {
            Academy.Instance.AutomaticSteppingEnabled = false;
            if (agent != null) agent.EpisodeFinished += OnEpisodeFinished;
        }

        void OnDestroy()
        {
            if (agent != null) agent.EpisodeFinished -= OnEpisodeFinished;
        }

        /// <summary>
        /// 命令行确实要求一次评估时返回 null，否则返回缺了什么。
        /// 没有 -gymMode eval，player 会悄悄地评估测试段；没有 -gymSegment，它也会退回到测试段
        /// （测试段留给最终数字，而评估流水撤不回）；没有 -gymOut，它会写到启动它的位置旁边。
        /// 这里只看命令行、拿不到配置，所以只查三样给没给；段名是不是配置里的评估段，由 GymConfigLoader
        /// 在 Agent 初始化读配置时检查（不是时列出能用的名字，player 以退出码 1 退出，什么都不写）。
        /// </summary>
        public static string CheckArguments(string[] args)
        {
            var missing = new List<string>();
            if (!string.Equals(CommandLineArgs.ValueOf(args, GymConfigLoader.ModeArg), "eval", StringComparison.OrdinalIgnoreCase))
                missing.Add($"{GymConfigLoader.ModeArg} eval");
            if (string.IsNullOrWhiteSpace(CommandLineArgs.ValueOf(args, GymConfigLoader.SegmentArg)))
                missing.Add($"{GymConfigLoader.SegmentArg} <segment>");
            if (string.IsNullOrWhiteSpace(CommandLineArgs.ValueOf(args, OutArg)))
                missing.Add($"{OutArg} <folder>");
            if (missing.Count == 0) return null;
            string player = args.Length > 0 ? RecordPaths.FileNameOnly(args[0]) : "GymEval";
            return $"the evaluation player needs {GymConfigLoader.ModeArg} eval, {GymConfigLoader.SegmentArg} <segment> " +
                   $"(a segment name from gym-config.json) and {OutArg} <folder>; missing {JoinWithAnd(missing)}. " +
                   "Nothing was written. Example: " +
                   $"{player} -batchmode -nographics -gymMode eval -gymSegment validation -gymFeeRate 0.001 -gymOut evaluations/smoke";
        }

        /// <summary>
        /// 评估包里的模型和当前观测集对不对得上：对不上返回报错（写明两个数，Nothing was written），对得上返回 null。
        /// 没有 build-info.json（editor 里、没挂模型的 Eval scene）或它没记维数（13 批之前打的包）时不查，返回 null，
        /// 原因写进 <paramref name="notChecked"/>。
        /// </summary>
        public static string CheckModelInput(EvalBuildInfo info, ObservationSet set, out string notChecked)
        {
            notChecked = null;
            if (info == null)
            {
                notChecked = $"no {BuildInfoFile}, so the model's input size is not checked against the observation set {set.Name()}";
                return null;
            }
            if (info.vector_observation_size <= 0)
            {
                notChecked = $"{BuildInfoFile} does not record the model's input size (built before observation sets existed), " +
                             $"so it is not checked against the observation set {set.Name()}";
                return null;
            }
            string problem = ObservationSets.ModelInputProblem(info.vector_observation_size, set);
            return problem == null ? null : $"{problem} (from {BuildInfoFile}, model run {info.run_id}). Nothing was written.";
        }

        /// <summary>
        /// 评估包里的模型和当前动作模式对不对得上：对不上返回报错（写明两边的动作形状，Nothing was written），对得上返回 null。
        /// 期望的形状来自 <see cref="ActionShape.For"/>（即 ActionCodec）。没有 build-info.json，或它没记动作形状
        /// （<see cref="EvalBuildInfo.action_shape_recorded"/> 为 false：24 批之前打的包）时不查，返回 null，原因写进
        /// <paramref name="notChecked"/>。旧包只按 action_shape_recorded 判，不看 continuous_actions 是不是 0（0 是旋钮模式的正常值）。
        /// </summary>
        public static string CheckModelActions(EvalBuildInfo info, ActionMode mode, int levelCount, out string notChecked)
        {
            notChecked = null;
            if (info == null)
            {
                notChecked = $"no {BuildInfoFile}, so the model's action shape is not checked against the action mode {mode.Name()}";
                return null;
            }
            if (!info.action_shape_recorded)
            {
                notChecked = $"{BuildInfoFile} does not record the model's action shape (built before action shapes were recorded), " +
                             $"so it is not checked against the action mode {mode.Name()}";
                return null;
            }
            var model = new ActionShape(info.continuous_actions, info.discrete_branches);
            string problem = ActionShape.ModelActionProblem(model, mode, levelCount);
            return problem == null ? null : $"{problem} (from {BuildInfoFile}, model run {info.run_id}). Nothing was written.";
        }

        /// <summary>评估流水 agent 行的 notes 列：推理设置、走了几步、观测集。</summary>
        public static string AgentNotes(bool deterministicInference, string inferenceDevice, int steps, ObservationSet set) =>
            $"deterministic={deterministicInference}; device={inferenceDevice}; steps={steps}; obs={set.Name()}";

        /// <summary>拼成 "a"、"a and b"、"a, b and c" 这样。</summary>
        static string JoinWithAnd(List<string> items) =>
            items.Count < 2 ? string.Join("", items)
                : string.Join(", ", items.GetRange(0, items.Count - 1)) + " and " + items[items.Count - 1];

        IEnumerator Start()
        {
            string problem = CheckArguments(Environment.GetCommandLineArgs());
            if (problem != null)
            {
                Debug.LogError("[Gym] " + problem);
                Quit(1);
                yield break;
            }
            if (agent == null)
            {
                Debug.LogError("[Gym] EvalRunner has no agent");
                Quit(1);
                yield break;
            }
            yield return null;
            if (agent.Env == null)
            {
                Debug.LogError("[Gym] the evaluation agent did not initialise; see the errors above");
                Quit(1);
                yield break;
            }
            buildInfo = ReadBuildInfo();
            buildInfoRead = true;
            string mismatch = CheckModelInput(buildInfo, agent.Env.ObservationSet, out string notChecked);
            if (notChecked != null) Debug.LogWarning("[Gym] " + notChecked);
            TradingEnv checkedEnv = agent.Env;
            string actionMismatch = CheckModelActions(buildInfo, checkedEnv.ActionMode, checkedEnv.Dial?.Levels.Count ?? 0,
                out string actionsNotChecked);
            if (actionsNotChecked != null) Debug.LogWarning("[Gym] " + actionsNotChecked);
            if (mismatch != null || actionMismatch != null)
            {
                if (mismatch != null) Debug.LogError("[Gym] " + mismatch);
                if (actionMismatch != null) Debug.LogError("[Gym] " + actionMismatch);
                Quit(1);
                yield break;
            }
            // 走一遍大约每根 candle 一个 step；远多于这个数，说明 episode 永远不会结束。
            stepLimit = agent.Env.Last - agent.Env.First + 1 + StepMargin;
            watch = Stopwatch.StartNew();
            steps = 0;
            while (!Finished)
            {
                if (!StepBatch())
                {
                    Quit(1);
                    yield break;
                }
                yield return null;
            }
            Debug.Log($"[Gym] evaluation finished: {steps} environment steps in {watch.Elapsed.TotalSeconds:F1} s, " +
                      $"return {Result.TotalReturn:R}, log {LogPath}");
            Quit(exitCode);
        }

        /// <summary>一帧要走的那批 step。出现异常或超过 step 上限时返回 false，并把原因写进日志。</summary>
        bool StepBatch()
        {
            try
            {
                for (int i = 0; i < stepsPerFrame && !Finished; i++)
                {
                    if (steps >= stepLimit)
                    {
                        Debug.LogError($"[Gym] evaluation did not finish within {stepLimit} environment steps " +
                                       $"(segment length + {StepMargin}); stopping");
                        return false;
                    }
                    Academy.Instance.EnvironmentStep();
                    steps++;
                }
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Gym] evaluation failed after {steps} environment steps: {e}");
                return false;
            }
        }

        void Quit(int code)
        {
            if (!quitWhenDone) return;
            Application.Quit(code);
        }

        void OnEpisodeFinished(TradingAgent source, EpisodeMetrics metrics)
        {
            if (Finished) return;
            Finished = true;
            Result = metrics;
            try
            {
                Write(source, metrics);
                exitCode = 0;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Gym] could not write the evaluation: {e}");
                exitCode = 1;
            }
        }

        void Write(TradingAgent source, EpisodeMetrics metrics)
        {
            string outDir = Path.GetFullPath(CommandLineArgs.ValueOf(Environment.GetCommandLineArgs(), OutArg)); // 已在 Start 里检查过
            EvalBuildInfo info = buildInfoRead ? buildInfo : ReadBuildInfo(); // 开工时已经读过一次
            GymSettings s = source.Settings;
            TradingEnv env = source.Env;
            SegmentSpec segment = s.EvalSegment; // Eval scene 的 Agent 总是运行这个分段
            DateTime now = DateTime.UtcNow;
            var behavior = source.GetComponent<Unity.MLAgents.Policies.BehaviorParameters>();

            var record = new EvaluationRecord
            {
                TimestampUtc = now,
                Kind = EvaluationRecord.AgentKind,
                Policy = "agent",
                ModelRunId = info?.run_id ?? "unknown",
                ModelSha256 = info?.model_sha256 ?? "",
                Symbol = env.Rules.Symbol, // 环境的币；评估只有当前币一个环境，读环境是防将来混用
                Seeds = 1,
                Notes = AgentNotes(behavior.DeterministicInference, behavior.InferenceDevice.ToString(), metrics.Steps, env.ObservationSet),
            };
            record.SetSegment(segment);
            record.SetCost(env.Cost);
            record.SetMetrics(metrics);

            JsonObject details = Details(record, info, s, env, metrics, behavior.BehaviorType.ToString(),
                behavior.DeterministicInference, behavior.InferenceDevice.ToString());
            LogPath = EvaluationLog.Append(outDir, record);
            DetailsPath = EvaluationLog.WriteRunDetails(outDir, now, record.Policy, segment.Name, details);
        }

        /// <summary>
        /// 一次 Agent 评估的明细 JSON。symbol 和 data_file 是当前币（-gymSymbol）的，数据文件按配置里的写法记录，模型只记文件名（靠 SHA-256 识别），
        /// 这样提交进仓库的文件里不带任何机器的文件夹或用户名。observation_set 和 history_length 是环境实际用的那一档。
        /// </summary>
        public static JsonObject Details(EvaluationRecord record, EvalBuildInfo info, GymSettings settings, TradingEnv env,
            EpisodeMetrics metrics, string behaviorType, bool deterministicInference, string inferenceDevice)
        {
            SegmentSpec segment = settings.EvalSegment;
            return new JsonObject
            {
                { "timestamp_utc", record.TimestampUtc },
                { "kind", record.Kind },
                { "policy", record.Policy },
                { "generated_by", "Gym.Runtime.Evaluation.EvalRunner" },
                { "model_run_id", record.ModelRunId },
                { "model_sha256", record.ModelSha256 },
                { "model_file", RecordPaths.FileNameOnly(info?.model_file) },
                { "build_built_at_utc", info?.built_at_utc ?? "" },
                { "unity_version", Application.unityVersion },
                { "behavior_type", behaviorType },
                { "deterministic_inference", deterministicInference },
                { "inference_device", inferenceDevice },
                { "symbol", env.Rules.Symbol }, // 环境的币（评估时就是当前币）
                { "data_file", RecordPaths.PathForRecords(settings.DataFile) }, // 当前币的数据文件
                { "observation_set", env.ObservationSet.Name() },
                { "history_length", env.HistoryLength },
                { "segment", new JsonObject
                    {
                        { "name", segment.Name },
                        { "start_date", segment.StartDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) },
                        { "end_date", segment.EndDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) },
                        { "first_candle_utc", env.Series.OpenTimeUtc(env.StartIndex) },
                        { "last_candle_utc", env.Series.OpenTimeUtc(env.CurrentIndex) },
                        { "steps", env.StepCount },
                    }
                },
                { "cost", new JsonObject { { "fee_rate", env.Cost.FeeRate }, { "fixed_fee", env.Cost.FixedFee }, { "slippage", env.Cost.Slippage } } },
                { "initial_cash", settings.Config.initialCash },
                { "metrics", EvaluationLog.MetricsJson(metrics) },
            };
        }

        public static EvalBuildInfo ReadBuildInfo()
        {
            string path = Path.Combine(GymConfigLoader.DefaultDirectory, BuildInfoFile);
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[Gym] {path} not found; model run id is recorded as 'unknown'");
                return null;
            }
            return JsonUtility.FromJson<EvalBuildInfo>(File.ReadAllText(path));
        }
    }
}
