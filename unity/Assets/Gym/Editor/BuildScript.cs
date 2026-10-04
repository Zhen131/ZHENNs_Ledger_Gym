using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Gym.Runtime.Agents;
using Gym.Runtime.Configuration;
using Gym.Runtime.Evaluation;
using Unity.InferenceEngine;
using Unity.MLAgents.Policies;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Gym.Editor
{
    /// <summary>
    /// 打训练用的 player（只含 Training scene，Mono，不是 development 版）：
    ///
    ///   Unity -batchmode -nographics -projectPath "$PWD/unity" -executeMethod Gym.Editor.BuildScript.BuildMacTraining -quit -logFile "$PWD/unity/Logs/build-mac.log"
    ///   Unity -batchmode -nographics -projectPath "$PWD/unity" -executeMethod Gym.Editor.BuildScript.BuildWindowsTraining -quit -logFile "$PWD/unity/Logs/build-win.log"
    ///
    /// 打包失败时，batch mode 下的 editor 以退出码 1 退出。
    ///
    /// 打评估用的 player，把训练好的模型打进包里：
    ///
    ///   Unity -batchmode -nographics -projectPath "$PWD/unity" -executeMethod Gym.Editor.BuildScript.BuildMacEval
    ///         -gymModel "$PWD/results/&lt;run-id&gt;/TradingAgent.onnx" -quit -logFile "$PWD/unity/Logs/build-eval.log"
    ///
    /// BuildWindowsEval 对 Windows x64 做同样的事（Builds/win-eval/GymEval.exe）。
    /// </summary>
    public static class BuildScript
    {
        public const string TrainingScenePath = GymSceneBuilder.TrainingScenePath;
        public const string MacOutput = "Builds/mac/Gym.app";
        public const string WindowsOutput = "Builds/win/Gym.exe";
        public const string MacEvalOutput = "Builds/mac/GymEval.app";
        // 单独一个文件夹：Windows 的 player 会和放在它旁边的其他东西共用 UnityPlayer.dll 和
        // MonoBleedingEdge\，而 Gym.exe 训练时这些文件是锁住的。
        public const string WindowsEvalOutput = "Builds/win-eval/GymEval.exe";
        public const string ImportedModelsFolder = "Assets/Gym/Models/Imported";
        public const string ModelArg = "-gymModel";
        public const string RunIdArg = "-gymRunId";
        public const int WindowWidth = 640;
        public const int WindowHeight = 360;

        [MenuItem("Gym/Build/Mac Training Player")]
        public static void BuildMacTraining() => Build(BuildTarget.StandaloneOSX, MacOutput);

        [MenuItem("Gym/Build/Windows Training Player")]
        public static void BuildWindowsTraining() => Build(BuildTarget.StandaloneWindows64, WindowsOutput);

        /// <summary>
        /// 把 -gymModel 给的 ONNX 复制成 Assets/Gym/Models/Imported/&lt;run-id&gt;.onnx，
        /// 在同一个文件夹里复制一份 Eval scene，把模型挂到 Agent 上（Inference Only、确定性、CPU/Burst），
        /// 用这份副本打包出 Builds/mac/GymEval.app，再把 run id 和模型的 SHA-256 写进包里的
        /// StreamingAssets/Gym/build-info.json。run id 取 -gymRunId；没给时取 ONNX 上一级文件夹的名字（results/&lt;run-id&gt;/）。
        /// </summary>
        [MenuItem("Gym/Build/Mac Evaluation Player (needs -gymModel)")]
        public static void BuildMacEval() => BuildEval(BuildTarget.StandaloneOSX, MacEvalOutput);

        /// <summary>和 <see cref="BuildMacEval"/> 一样，只是针对 Windows x64：Builds/win-eval/GymEval.exe。</summary>
        [MenuItem("Gym/Build/Windows Evaluation Player (needs -gymModel)")]
        public static void BuildWindowsEval() => BuildEval(BuildTarget.StandaloneWindows64, WindowsEvalOutput);

        static void BuildEval(BuildTarget target, string output)
        {
            try
            {
                RequireModule(target);
                (string modelPath, string runId) = ReadModelArguments(Environment.GetCommandLineArgs());
                string sha = Sha256(modelPath);
                ModelAsset model = ImportModel(modelPath, runId);
                string scenePath = MakeEvalScene(runId, model);
                BuildSummary summary = BuildPlayer(target, output, scenePath);
                string infoPath = WriteBuildInfo(target, output, runId, sha, modelPath);
                if (target == BuildTarget.StandaloneOSX) ResignMacApp(output);
                Debug.Log($"[Gym] eval build: run id {runId}, model sha256 {sha}, {summary.totalSize / 1048576.0:F1} MB -> {output}; wrote {infoPath}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Gym] eval build failed: {e.Message}\n{e}");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
        }

        /// <summary>-gymModel 文件的完整路径和 run id（-gymRunId，或者模型所在文件夹的名字）；任何一个用不了就抛异常。</summary>
        static (string modelPath, string runId) ReadModelArguments(string[] args)
        {
            string modelPath = CommandLineArgs.ValueOf(args, ModelArg)
                ?? throw new ArgumentException($"{ModelArg} <path to .onnx> is required");
            modelPath = Path.GetFullPath(modelPath);
            if (!File.Exists(modelPath)) throw new FileNotFoundException($"model not found: {modelPath}");
            string runId = CommandLineArgs.ValueOf(args, RunIdArg) ?? Path.GetFileName(Path.GetDirectoryName(modelPath));
            if (string.IsNullOrEmpty(runId) || runId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new ArgumentException($"cannot use '{runId}' as a run id; pass {RunIdArg}");
            return (modelPath, runId);
        }

        /// <summary>把 ONNX 复制成 Imported/&lt;run-id&gt;.onnx 并导入。打评估包和观战选模型都走这里。</summary>
        public static ModelAsset ImportModel(string modelPath, string runId)
        {
            AssetFolders.Ensure(ImportedModelsFolder);
            string modelAssetPath = $"{ImportedModelsFolder}/{runId}.onnx";
            File.Copy(modelPath, modelAssetPath, true);
            AssetDatabase.ImportAsset(modelAssetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<ModelAsset>(modelAssetPath)
                ?? throw new InvalidOperationException($"{modelAssetPath} did not import as a ModelAsset");
        }

        /// <summary>Eval scene 的一份副本 Imported/Eval-&lt;run-id&gt;.unity，里面的 Agent 运行这个模型（Inference Only、确定性、CPU/Burst）。</summary>
        static string MakeEvalScene(string runId, ModelAsset model)
        {
            string scenePath = $"{ImportedModelsFolder}/Eval-{runId}.unity";
            AssetDatabase.DeleteAsset(scenePath);
            if (!AssetDatabase.CopyAsset(GymSceneBuilder.EvalScenePath, scenePath))
                throw new InvalidOperationException($"could not copy {GymSceneBuilder.EvalScenePath} to {scenePath}");
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            TradingAgent[] agents = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<TradingAgent>(true)).ToArray();
            if (agents.Length != 1) throw new InvalidOperationException($"{scenePath} has {agents.Length} agents, expected 1");
            var behavior = agents[0].GetComponent<BehaviorParameters>();
            behavior.Model = model;
            UseEvaluationInference(behavior);
            PrefabUtility.RecordPrefabInstancePropertyModifications(behavior);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException($"could not save {scenePath}");
            return scenePath;
        }

        /// <summary>评估包的推理设置：Inference Only、确定性、CPU/Burst。Watch scene 也用这一份，两边一样。</summary>
        public static void UseEvaluationInference(BehaviorParameters behavior)
        {
            behavior.BehaviorType = BehaviorType.InferenceOnly;
            behavior.DeterministicInference = true;
            behavior.InferenceDevice = InferenceDevice.Burst;
        }

        /// <summary>把 run id 和模型的 SHA-256 写进包里的 StreamingAssets/Gym/build-info.json；返回这个文件的路径。</summary>
        static string WriteBuildInfo(BuildTarget target, string output, string runId, string sha, string modelPath)
        {
            var info = new EvalBuildInfo
            {
                run_id = runId,
                model_sha256 = sha,
                model_file = modelPath,
                built_at_utc = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
                unity_version = Application.unityVersion,
            };
            string infoPath = Path.Combine(StreamingGymFolder(target, output), EvalRunner.BuildInfoFile);
            File.WriteAllText(infoPath, JsonUtility.ToJson(info, true));
            return infoPath;
        }

        /// <summary>
        /// build-info.json 是在 Unity 签名之后才写进 .app 的，这会破坏签名：在本机还能运行，
        /// 但复制到另一台 Mac 上就会被拒绝。所以再用 ad hoc 方式签一次。
        /// </summary>
        static void ResignMacApp(string app)
        {
            if (Application.platform != RuntimePlatform.OSXEditor)
            {
                Debug.LogWarning($"[Gym] {app} was not re-signed (codesign needs macOS); run: codesign --force --deep -s - {app}");
                return;
            }
            foreach (string[] arguments in new[]
                     {
                         new[] { "--force", "--deep", "--sign", "-", app },
                         new[] { "--verify", "--deep", "--strict", app },
                     })
            {
                var start = new System.Diagnostics.ProcessStartInfo("/usr/bin/codesign",
                    string.Join(" ", arguments.Select(a => "\"" + a.Replace("\"", "\\\"") + "\"")))
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using (var process = System.Diagnostics.Process.Start(start))
                {
                    string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    if (process.ExitCode != 0)
                        throw new InvalidOperationException($"codesign {string.Join(" ", arguments)} failed ({process.ExitCode}): {output}");
                }
            }
            Debug.Log($"[Gym] re-signed {app} (ad hoc) and verified the signature");
        }

        /// <summary>打出来的 player 把 StreamingAssets/Gym 放在哪里。</summary>
        static string StreamingGymFolder(BuildTarget target, string output) =>
            target == BuildTarget.StandaloneOSX
                ? Path.Combine(output, "Contents", "Resources", "Data", "StreamingAssets", "Gym")
                : Path.Combine(Path.GetDirectoryName(output) ?? "", Path.GetFileNameWithoutExtension(output) + "_Data", "StreamingAssets", "Gym");

        static string Sha256(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        /// <summary>每个训练 player 都依赖的 Player 设置。会存进 ProjectSettings.asset。</summary>
        public static void ApplyPlayerSettings()
        {
            PlayerSettings.runInBackground = true;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = WindowWidth;
            PlayerSettings.defaultScreenHeight = WindowHeight;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
        }

        static void Build(BuildTarget target, string output)
        {
            try
            {
                BuildPlayer(target, output, TrainingScenePath);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Gym] build failed for {target}: {e.Message}");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
        }

        /// <summary>为一个目标平台打包一个 scene；缺模块或打包失败时抛异常。</summary>
        static BuildSummary BuildPlayer(BuildTarget target, string output, string scenePath)
        {
            RequireModule(target);

            ApplyPlayerSettings();
#if UNITY_EDITOR_OSX
            if (target == BuildTarget.StandaloneOSX)
                UnityEditor.OSXStandalone.UserBuildSettings.architecture = OSArchitecture.ARM64;
#endif
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
            var options = new BuildPlayerOptions
            {
                scenes = new[] { scenePath },
                target = target,
                targetGroup = BuildTargetGroup.Standalone,
                locationPathName = output,
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            Debug.Log($"[Gym] build {summary.result}: {target} -> {output} ({scenePath}), " +
                      $"{summary.totalSize / 1048576.0:F1} MB, {summary.totalTime.TotalSeconds:F0} s, " +
                      $"{summary.totalErrors} errors, {summary.totalWarnings} warnings");
            if (summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException($"Build {summary.result} with {summary.totalErrors} errors; see the log above.");
            return summary;
        }

        static void RequireModule(BuildTarget target)
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, target))
                throw new InvalidOperationException(
                    $"Build support for {target} is not installed in Unity {Application.unityVersion}. " +
                    $"Add the module \"{ModuleName(target)}\" in Unity Hub (Installs → {Application.unityVersion} → Add modules) and run again.");
        }

        static string ModuleName(BuildTarget target) =>
            target == BuildTarget.StandaloneWindows64 ? "Windows Build Support (Mono)" :
            target == BuildTarget.StandaloneOSX ? "Mac Build Support (Mono)" : target.ToString();
    }
}
