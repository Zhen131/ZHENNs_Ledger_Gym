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
    /// Drives one evaluation episode in the Eval scene (04B §4.2): turns off automatic
    /// stepping, calls Academy.EnvironmentStep in batches each frame until the agent's
    /// episode ends, appends the result to &lt;-gymOut&gt;/log.csv with a JSON detail file,
    /// and quits (0 on success, 1 on failure). It runs only when started with
    /// -gymMode eval, -gymSegment validation|test and -gymOut &lt;folder&gt;.
    /// </summary>
    public class EvalRunner : MonoBehaviour
    {
        public const string BuildInfoFile = "build-info.json";
        public const string OutArg = "-gymOut";

        /// <summary>Environment steps allowed beyond the segment's candle count before the run counts as stuck.</summary>
        public const int StepMargin = 100;

        [SerializeField] TradingAgent agent;
        [SerializeField] int stepsPerFrame = 500;
        [SerializeField] bool quitWhenDone = true;

        int exitCode = 1;
        int steps;
        int stepLimit;
        Stopwatch watch;

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
        /// Null when the command line asks for an evaluation run, else what is missing (05D S-1, 08D N-1).
        /// Without -gymMode eval the player would quietly evaluate the test segment, without
        /// -gymSegment it would fall back to the test segment too (kept for the final numbers,
        /// and the log cannot be undone), and without -gymOut it would write next to wherever
        /// it was started.
        /// </summary>
        public static string CheckArguments(string[] args)
        {
            var missing = new List<string>();
            if (!string.Equals(CommandLineArgs.ValueOf(args, GymConfigLoader.ModeArg), "eval", StringComparison.OrdinalIgnoreCase))
                missing.Add($"{GymConfigLoader.ModeArg} eval");
            string segment = CommandLineArgs.ValueOf(args, GymConfigLoader.SegmentArg);
            if (!string.Equals(segment, SegmentNames.Validation, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(segment, SegmentNames.Test, StringComparison.OrdinalIgnoreCase))
                missing.Add($"{GymConfigLoader.SegmentArg} validation|test");
            if (string.IsNullOrWhiteSpace(CommandLineArgs.ValueOf(args, OutArg)))
                missing.Add($"{OutArg} <folder>");
            if (missing.Count == 0) return null;
            string player = args.Length > 0 ? RecordPaths.FileNameOnly(args[0]) : "GymEval";
            return $"the evaluation player needs {GymConfigLoader.ModeArg} eval, {GymConfigLoader.SegmentArg} validation|test " +
                   $"and {OutArg} <folder>; missing {JoinWithAnd(missing)}. Nothing was written. Example: " +
                   $"{player} -batchmode -nographics -gymMode eval -gymSegment validation -gymFeeRate 0.001 -gymOut evaluations/smoke";
        }

        /// <summary>"a", "a and b", "a, b and c".</summary>
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
            // One pass needs about one step per candle; far more means the episode never ends (05D M-1).
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

        /// <summary>One frame's worth of steps. False, with the reason logged, on an exception or past the step limit.</summary>
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
            string outDir = Path.GetFullPath(CommandLineArgs.ValueOf(Environment.GetCommandLineArgs(), OutArg)); // checked in Start
            EvalBuildInfo info = ReadBuildInfo();
            GymSettings s = source.Settings;
            TradingEnv env = source.Env;
            SegmentSpec segment = s.EvalSegment; // the Eval scene's agent always runs this segment
            DateTime now = DateTime.UtcNow;
            var behavior = source.GetComponent<Unity.MLAgents.Policies.BehaviorParameters>();

            var record = new EvaluationRecord
            {
                TimestampUtc = now,
                Kind = EvaluationRecord.AgentKind,
                Policy = "agent",
                ModelRunId = info?.run_id ?? "unknown",
                ModelSha256 = info?.model_sha256 ?? "",
                Symbol = s.Rules.Symbol,
                Seeds = 1,
                Notes = $"deterministic={behavior.DeterministicInference}; device={behavior.InferenceDevice}; steps={metrics.Steps}",
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
        /// The detail JSON of one agent evaluation. The data file is written as the config
        /// names it and the model only by its file name (the SHA-256 identifies it), so the
        /// committed file carries no machine's folders or user name (05D M-2).
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
                { "generated_by", "Gym.Runtime.EvalRunner" },
                { "model_run_id", record.ModelRunId },
                { "model_sha256", record.ModelSha256 },
                { "model_file", RecordPaths.FileNameOnly(info?.model_file) },
                { "build_built_at_utc", info?.built_at_utc ?? "" },
                { "unity_version", Application.unityVersion },
                { "behavior_type", behaviorType },
                { "deterministic_inference", deterministicInference },
                { "inference_device", inferenceDevice },
                { "symbol", settings.Rules.Symbol },
                { "data_file", RecordPaths.PathForRecords(settings.Config.dataFile) },
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
            string path = Path.Combine(Application.streamingAssetsPath, "Gym", BuildInfoFile);
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[Gym] {path} not found; model run id is recorded as 'unknown'");
                return null;
            }
            return JsonUtility.FromJson<EvalBuildInfo>(File.ReadAllText(path));
        }
    }
}
