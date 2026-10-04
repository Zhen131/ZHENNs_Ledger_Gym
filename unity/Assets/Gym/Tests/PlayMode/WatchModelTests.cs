#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Gym.Core.Env;
using Gym.Core.Evaluation;
using Gym.Editor;
using Gym.Runtime.Agents;
using Gym.Runtime.Configuration;
using Gym.Runtime.Play;
using Gym.Runtime.Watch;
using NUnit.Framework;
using Unity.MLAgents;
using Unity.MLAgents.Policies;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Gym.Tests.PlayMode
{
    /// <summary>
    /// 要训练好的模型才能做的观战测试。模型从环境变量 GYM_WATCH_MODEL 拿（.onnx 的完整路径），没给就报告为忽略；
    /// 要和评估比时，再用 GYM_WATCH_EVAL_LOG 给那次评估写的 log.csv。导入走菜单的同一段（WatchSetup.Remember），
    /// 记录写在临时位置。跑的时候本机不能开着 mlagents-learn（editor 里 ML-Agents 会去连它），测试会查。
    /// </summary>
    public class WatchModelTests
    {
        const string WatchScenePath = "Assets/Gym/Scenes/Watch.unity";
        const string ModelVariable = "GYM_WATCH_MODEL";
        const string EvalLogVariable = "GYM_WATCH_EVAL_LOG";
        /// <summary>评估包每帧走 500 步；这里照样每 500 步让一帧。</summary>
        const int StepsPerFrame = 500;

        string folder;

        [SetUp]
        public void SetUp()
        {
            folder = Path.Combine(Path.GetTempPath(), "gym-watch-model-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            WatchModelMemory.Location = Path.Combine(folder, WatchModelMemory.FileName);
        }

        [TearDown]
        public void TearDown()
        {
            WatchModelMemory.Location = null;
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            if (Academy.IsInitialized) Academy.Instance.AutomaticSteppingEnabled = true;
        }

        static string ModelOrIgnore()
        {
            string path = Environment.GetEnvironmentVariable(ModelVariable);
            if (string.IsNullOrEmpty(path)) Assert.Ignore($"needs a trained model: set {ModelVariable} to the full path of an .onnx file");
            path = Path.GetFullPath(path);
            Assert.IsTrue(File.Exists(path), $"{ModelVariable} points to {path}, which does not exist");
            return path;
        }

        static WatchController Controller => Object.FindFirstObjectByType<WatchController>();

        /// <summary>
        /// 按路径加载 Watch scene，等它挂上模型、开始观战；播放的时钟一开始就归测试管。
        /// 时钟要在 sceneLoaded 里接手：Unity 在新 scene 的 Awake、OnEnable 之后、第一次 Start 和 Update 之前调它。
        /// 等加载完再接手就晚了：控制器开始观战的那一帧要读模型、初始化 Agent，冷启动时这一帧有 0.4 秒，
        /// 它自己的 Update 按真实时间算，会在测试接手之前先走一步。
        /// </summary>
        static IEnumerator LoadWatchScene()
        {
            UnityAction<Scene, LoadSceneMode> takeTheClock = (scene, mode) =>
            {
                foreach (GameObject root in scene.GetRootGameObjects())
                foreach (WatchController c in root.GetComponentsInChildren<WatchController>(true))
                    c.ManualClock = true;
            };
            SceneManager.sceneLoaded += takeTheClock;
            try
            {
                yield return EditorSceneManager.LoadSceneAsyncInPlayMode(WatchScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            }
            finally
            {
                SceneManager.sceneLoaded -= takeTheClock;
            }
            yield return null;
            yield return null;
            WatchController controller = Controller;
            Assert.IsNotNull(controller);
            Assert.IsTrue(controller.ManualClock, "the test holds the clock from before the controller's first Update");
            Assert.IsTrue(controller.IsWatching, "the remembered model was attached and the agent switched on");
            Assert.AreEqual(0, controller.Playback.StepsTaken, "no step before the test took over");
            Assert.AreEqual(0, controller.Agent.Env.StepCount, "the agent is still on the segment's first candle");
            Assert.IsFalse(Academy.Instance.IsCommunicatorOn, "no mlagents-learn may be running: the editor would connect to it");
        }

        /// <summary>暂停后一步一步走到段尾，每 500 步让一帧（和评估包一样不限速）。</summary>
        static IEnumerator StepToTheEnd(WatchPlayback playback)
        {
            if (!playback.Paused) playback.TogglePause();
            while (!playback.Finished)
            {
                for (int i = 0; i < StepsPerFrame && !playback.Finished; i++) playback.StepOnce();
                yield return null;
            }
        }

        static string Bits(double value) => BitConverter.DoubleToInt64Bits(value).ToString("X16");

        /// <summary>一次从段首走到段尾的观战：每一步的动作和账户（按二进制位），以及走完那一刻按评估流水写成的一行。</summary>
        sealed class WatchRun
        {
            public readonly List<string> Steps = new List<string>();
            public string Row;
            /// <summary>走完那一刻的总资产，按评估明细 JSON 的写法（round-trip）。</summary>
            public string FinalEquity;
            public double Seconds;

            public string StepsSha256()
            {
                using (SHA256 sha = SHA256.Create())
                    return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", Steps)))).Replace("-", "").ToLowerInvariant();
            }
        }

        static string Sha256Of(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        /// <summary>和 EvalRunner 写评估流水的那一行同样的写法（时间和 run id 除外，比的时候不看这两列）。</summary>
        static string RowLikeTheEvaluation(TradingAgent agent, EpisodeMetrics metrics, string modelSha)
        {
            GymSettings s = agent.Settings;
            var behavior = agent.GetComponent<BehaviorParameters>();
            var record = new EvaluationRecord
            {
                TimestampUtc = DateTime.UtcNow,
                Kind = EvaluationRecord.AgentKind,
                Policy = "agent",
                ModelRunId = "watch",
                ModelSha256 = modelSha,
                Symbol = s.Rules.Symbol,
                Seeds = 1,
                Notes = $"deterministic={behavior.DeterministicInference}; device={behavior.InferenceDevice}; steps={metrics.Steps}",
            };
            record.SetSegment(s.Validation);
            record.SetCost(agent.Env.Cost);
            record.SetMetrics(metrics);
            return EvaluationLog.FormatRow(record);
        }

        static IEnumerator WatchOnce(WatchRun run, string modelSha)
        {
            yield return LoadWatchScene();
            WatchController controller = Controller;
            TradingAgent agent = controller.Agent;
            Assert.AreEqual(EvaluationSegment.Validation, controller.Segment);
            agent.Stepped += a => run.Steps.Add(
                $"{a.Env.StepCount} {(int)a.LastAction} {BitConverter.SingleToInt32Bits(a.LastContinuous):X8} " +
                $"{Bits(a.Env.Account.Cash)} {a.Env.Account.CoinUnits} {Bits(a.Env.CurrentEquity)}");
            agent.EpisodeFinished += (a, metrics) =>
            {
                run.Row = RowLikeTheEvaluation(a, metrics, modelSha);
                run.FinalEquity = EvaluationLog.Number(metrics.FinalEquity);
            };
            var clock = System.Diagnostics.Stopwatch.StartNew();
            yield return StepToTheEnd(controller.Playback);
            run.Seconds = clock.Elapsed.TotalSeconds;
            Assert.IsNotNull(run.Row, "the episode finished");
        }

        /// <summary>评估流水里最后一行 agent、validation 的那一行。</summary>
        static string[] LastValidationAgentRow(string logPath)
        {
            string[] header = null;
            string[] found = null;
            foreach (string line in File.ReadAllLines(logPath).Where(l => l.Length > 0))
            {
                string[] cells = line.Split(',');
                if (header == null)
                {
                    header = cells;
                    continue;
                }
                if (cells[Array.IndexOf(header, "kind")] == "agent" && cells[Array.IndexOf(header, "segment")] == "validation") found = cells;
            }
            Assert.IsNotNull(found, $"{logPath} has an agent row on the validation segment");
            return found;
        }

        /// <summary>评估流水旁边 runs/ 里那次评估的明细 JSON 写的 final_equity（最后一个 agent、validation 的文件）。</summary>
        static string EvaluationFinalEquity(string logPath)
        {
            string runs = Path.Combine(Path.GetDirectoryName(logPath) ?? ".", EvaluationLog.RunsFolder);
            string details = Directory.GetFiles(runs, "*-agent-validation*.json").OrderBy(f => f, StringComparer.Ordinal).LastOrDefault();
            Assert.IsNotNull(details, $"{runs} has the evaluation's details file");
            Match match = Regex.Match(File.ReadAllText(details), "\"final_equity\": ([^,\\s}]+)");
            Assert.IsTrue(match.Success, $"{details} has a final_equity");
            return match.Groups[1].Value;
        }

        static string BurstState()
        {
            Type burst = Type.GetType("Unity.Burst.BurstCompiler, Unity.Burst");
            object enabled = burst?.GetProperty("IsEnabled")?.GetValue(null);
            return enabled == null ? "unknown" : enabled.ToString();
        }

        // ---- 观战和评估是不是同一条路

        [UnityTest]
        public IEnumerator WatchingTheModelTwice_TakesTheSameStepsBitForBit_AndIsComparedWithItsEvaluation()
        {
            string model = ModelOrIgnore();
            using var logs = new LogGuard();
            WatchSetup.Remember(model);
            string modelSha = Sha256Of(model);
            var first = new WatchRun();
            var second = new WatchRun();
            yield return WatchOnce(first, modelSha);
            yield return WatchOnce(second, modelSha);

            var report = new StringBuilder();
            report.AppendLine($"[Gym] watch vs evaluation: model {model} (sha256 {modelSha}); Burst compilation enabled in the editor: {BurstState()}; " +
                              $"batch mode {Application.isBatchMode}; trainer connected {Academy.Instance.IsCommunicatorOn}");
            report.AppendLine($"run 1: {first.Steps.Count} steps in {first.Seconds:F1} s, steps sha256 {first.StepsSha256()}");
            report.AppendLine($"run 2: {second.Steps.Count} steps in {second.Seconds:F1} s, steps sha256 {second.StepsSha256()}");
            report.AppendLine($"run 1 final equity {first.FinalEquity}, run 2 final equity {second.FinalEquity}");
            report.AppendLine("run 1 row: " + first.Row);
            report.AppendLine("run 2 row: " + second.Row);

            int firstDifference = Enumerable.Range(0, Math.Min(first.Steps.Count, second.Steps.Count)).FirstOrDefault(i => first.Steps[i] != second.Steps[i]);
            bool sameSteps = first.Steps.SequenceEqual(second.Steps);
            Assert.IsTrue(sameSteps, $"two watches of one model differ, first at step {firstDifference + 1}: '{first.Steps.ElementAtOrDefault(firstDifference)}' vs '{second.Steps.ElementAtOrDefault(firstDifference)}'");
            string[] a = first.Row.Split(','), b = second.Row.Split(',');
            Assert.AreEqual(a.Skip(1), b.Skip(1), "the two rows differ beyond the time");
            Assert.AreEqual(first.FinalEquity, second.FinalEquity);

            string evalLog = Environment.GetEnvironmentVariable(EvalLogVariable);
            if (!string.IsNullOrEmpty(evalLog))
            {
                string[] header = EvaluationLog.Columns;
                string[] eval = LastValidationAgentRow(evalLog);
                report.AppendLine("evaluation row: " + string.Join(",", eval));
                var differing = new List<string>();
                for (int i = 0; i < header.Length; i++)
                {
                    if (header[i] == "timestamp_utc" || header[i] == "model_run_id") continue;
                    if (a[i] != eval[i]) differing.Add($"{header[i]}: watch {a[i]} vs evaluation {eval[i]}");
                }
                string evalEquity = EvaluationFinalEquity(evalLog);
                report.AppendLine($"final equity: watch {first.FinalEquity} vs evaluation {evalEquity}");
                if (first.FinalEquity != evalEquity) differing.Add($"final_equity: watch {first.FinalEquity} vs evaluation {evalEquity}");
                report.AppendLine(differing.Count == 0
                    ? "RESULT: the watch row equals the evaluation row in every column but timestamp_utc and model_run_id, and the final equity is the same"
                    : "RESULT: differs in " + differing.Count + " places:\n" + string.Join("\n", differing));
                Debug.Log(report.ToString());
                // 本批实测两边逐位相同；以后谁改了推理设置或观战的走法，这里会拦下来。
                CollectionAssert.IsEmpty(differing, "watching is no longer the same path as the evaluation");
            }
            else
            {
                report.AppendLine($"no comparison with the evaluation: set {EvalLogVariable} to the log.csv that evaluation wrote");
                Debug.Log(report.ToString());
            }
            logs.AssertNoErrors();
        }

        // ---- 真的 Agent 加模型：按速度走、走到头定格、R 重来

        [UnityTest]
        public IEnumerator WatchingAModel_PlaysAtItsSpeedFreezesAtTheEndAndStartsOverOnRestart()
        {
            string model = ModelOrIgnore();
            using var logs = new LogGuard();
            WatchSetup.Remember(model);
            yield return LoadWatchScene();
            WatchController controller = Controller;
            TradingAgent agent = controller.Agent;
            WatchPlayback playback = controller.Playback;
            var hud = Object.FindFirstObjectByType<HudView>();
            var wallet = Object.FindFirstObjectByType<WalletView>();
            var notice = Object.FindFirstObjectByType<WatchNotice>();
            var language = Object.FindFirstObjectByType<PlayLanguageSwitch>();

            Assert.IsTrue(agent.gameObject.activeSelf);
            Assert.IsNull(notice.Shown, "no hint while watching");
            Assert.AreEqual(5f, playback.StepsPerSecond);
            Assert.AreEqual("每秒 5 步", controller.StatusValue(language.Current));
            Assert.AreEqual(0, agent.Env.StepCount, "starts at the segment start");
            Assert.AreEqual(agent.Settings.Validation.FirstIndex(agent.Settings.Series), agent.Env.StartIndex);

            for (int i = 0; i < 30; i++) playback.Advance(1.0 / 30);
            Assert.AreEqual(5, agent.Env.StepCount, "one second at 5 steps a second");
            playback.Faster();
            for (int i = 0; i < 30; i++) playback.Advance(1.0 / 30);
            Assert.AreEqual(15, agent.Env.StepCount, "one more second at 10 steps a second");
            playback.TogglePause();
            for (int i = 0; i < 30; i++) playback.Advance(1.0 / 30);
            Assert.AreEqual(15, agent.Env.StepCount, "paused");
            Assert.IsTrue(playback.StepOnce());
            Assert.AreEqual(16, agent.Env.StepCount, "one single step");
            Assert.AreEqual(16, hud.Shown.Step, "the readout follows");

            int last = agent.Env.Last;
            yield return StepToTheEnd(playback);
            Assert.IsTrue(playback.Finished);
            Assert.IsTrue(playback.Frozen);
            Assert.AreEqual(0, agent.Env.StepCount, "premise: the environment already started over by itself");
            Assert.AreEqual(last - agent.Env.StartIndex, hud.Shown.Step, "the readout stays on the last step");
            Assert.AreEqual(agent.Settings.Series.OpenTimeUtc(last), hud.Shown.TimeUtc);
            Assert.AreEqual(WatchNoticeKind.EndOfSegment, notice.Shown);
            Assert.AreEqual(PlayText.Get(PlayTextKey.WatchEnd, language.Current), notice.ShownText.text);
            Assert.AreEqual(PlayText.Get(PlayTextKey.WatchFinished, language.Current), controller.StatusValue(language.Current));
            Assert.AreNotEqual(agent.Settings.Config.initialCash, wallet.ShownCash, "the pocket keeps the last cash");
            for (int i = 0; i < 30; i++) playback.Advance(1.0 / 30);
            Assert.AreEqual(0, agent.Env.StepCount, "nothing moves after the end");

            playback.Restart();
            Assert.IsFalse(playback.Finished);
            Assert.IsFalse(playback.Frozen);
            Assert.IsNull(notice.Shown);
            Assert.AreEqual(0, hud.Shown.Step, "back at the segment start");
            Assert.AreEqual(agent.Settings.Config.initialCash, hud.Shown.Cash);
            Assert.AreEqual(agent.Settings.Config.initialCash, wallet.ShownCash);
            Assert.IsTrue(playback.StepOnce(), "still paused, so a single step works");
            Assert.AreEqual(1, agent.Env.StepCount);
            logs.AssertNoErrors();
        }
    }
}
#endif
