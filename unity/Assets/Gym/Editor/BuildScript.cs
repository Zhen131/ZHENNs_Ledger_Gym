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
    /// Training player builds (Training scene only, Mono, non-development):
    ///
    ///   Unity -batchmode -nographics -projectPath "$PWD/unity" -executeMethod Gym.Editor.BuildScript.BuildMacTraining -quit -logFile "$PWD/unity/Logs/build-mac.log"
    ///   Unity -batchmode -nographics -projectPath "$PWD/unity" -executeMethod Gym.Editor.BuildScript.BuildWindowsTraining -quit -logFile "$PWD/unity/Logs/build-win.log"
    ///
    /// A failed build exits the batch-mode editor with code 1.
    ///
    /// Evaluation player with a trained model baked in (04B §4.2):
    ///
    ///   Unity -batchmode -nographics -projectPath "$PWD/unity" -executeMethod Gym.Editor.BuildScript.BuildMacEval
    ///         -gymModel "$PWD/results/&lt;run-id&gt;/TradingAgent.onnx" -quit -logFile "$PWD/unity/Logs/build-eval.log"
    ///
    /// BuildWindowsEval does the same for Windows x64 (Builds/win-eval/GymEval.exe).
    /// </summary>
    public static class BuildScript
    {
        public const string TrainingScenePath = "Assets/Gym/Scenes/Training.unity";
        public const string MacOutput = "Builds/mac/Gym.app";
        public const string WindowsOutput = "Builds/win/Gym.exe";
        public const string MacEvalOutput = "Builds/mac/GymEval.app";
        // Its own folder: a Windows player shares UnityPlayer.dll and MonoBleedingEdge\ with
        // whatever else sits next to it, and those files are locked while Gym.exe trains (05D S-3).
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
        /// Copies the ONNX given by -gymModel into Assets/Gym/Models/Imported/&lt;run-id&gt;.onnx,
        /// makes a copy of the Eval scene there with the model on the agent (Inference Only,
        /// deterministic, CPU/Burst), builds Builds/mac/GymEval.app from that copy and writes
        /// the run id and the model's SHA-256 into the build's StreamingAssets/Gym/build-info.json.
        /// The run id is -gymRunId, or the name of the folder above the ONNX (results/&lt;run-id&gt;/).
        /// </summary>
        [MenuItem("Gym/Build/Mac Evaluation Player (needs -gymModel)")]
        public static void BuildMacEval() => BuildEval(BuildTarget.StandaloneOSX, MacEvalOutput);

        /// <summary>Same as <see cref="BuildMacEval"/> for Windows x64: Builds/win-eval/GymEval.exe.</summary>
        [MenuItem("Gym/Build/Windows Evaluation Player (needs -gymModel)")]
        public static void BuildWindowsEval() => BuildEval(BuildTarget.StandaloneWindows64, WindowsEvalOutput);

        static void BuildEval(BuildTarget target, string output)
        {
            try
            {
                RequireModule(target);
                string[] args = Environment.GetCommandLineArgs();
                string modelPath = GymConfigLoader.GetArg(args, ModelArg)
                    ?? throw new ArgumentException($"{ModelArg} <path to .onnx> is required");
                modelPath = Path.GetFullPath(modelPath);
                if (!File.Exists(modelPath)) throw new FileNotFoundException($"model not found: {modelPath}");
                string runId = GymConfigLoader.GetArg(args, RunIdArg) ?? Path.GetFileName(Path.GetDirectoryName(modelPath));
                if (string.IsNullOrEmpty(runId) || runId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    throw new ArgumentException($"cannot use '{runId}' as a run id; pass {RunIdArg}");
                string sha = Sha256(modelPath);

                EnsureFolder(ImportedModelsFolder);
                string modelAssetPath = $"{ImportedModelsFolder}/{runId}.onnx";
                File.Copy(modelPath, modelAssetPath, true);
                AssetDatabase.ImportAsset(modelAssetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                var model = AssetDatabase.LoadAssetAtPath<ModelAsset>(modelAssetPath)
                    ?? throw new InvalidOperationException($"{modelAssetPath} did not import as a ModelAsset");

                string scenePath = $"{ImportedModelsFolder}/Eval-{runId}.unity";
                AssetDatabase.DeleteAsset(scenePath);
                if (!AssetDatabase.CopyAsset(GymSceneBuilder.EvalScenePath, scenePath))
                    throw new InvalidOperationException($"could not copy {GymSceneBuilder.EvalScenePath} to {scenePath}");
                Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                TradingAgent[] agents = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<TradingAgent>(true)).ToArray();
                if (agents.Length != 1) throw new InvalidOperationException($"{scenePath} has {agents.Length} agents, expected 1");
                var behavior = agents[0].GetComponent<BehaviorParameters>();
                behavior.Model = model;
                behavior.BehaviorType = BehaviorType.InferenceOnly;
                behavior.DeterministicInference = true;
                behavior.InferenceDevice = InferenceDevice.Burst;
                PrefabUtility.RecordPrefabInstancePropertyModifications(behavior);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException($"could not save {scenePath}");

                BuildSummary summary = BuildPlayer(target, output, scenePath);

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

        /// <summary>
        /// build-info.json is written into the .app after Unity signed it, which breaks the seal:
        /// it still runs here, but a copy on another Mac is refused. Sign it again, ad hoc (05D S-13).
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

        /// <summary>Where a player build keeps StreamingAssets/Gym.</summary>
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

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        /// <summary>Player settings every training build relies on. Saved into ProjectSettings.asset.</summary>
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

        /// <summary>Build one scene for a target; throws when the module is missing or the build fails.</summary>
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
