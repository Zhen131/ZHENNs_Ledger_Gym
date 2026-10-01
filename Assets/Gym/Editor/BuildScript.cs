using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Gym.Editor
{
    /// <summary>
    /// Training player builds (Training scene only, Mono, non-development):
    ///
    ///   Unity -batchmode -nographics -projectPath . -executeMethod Gym.Editor.BuildScript.BuildMacTraining -quit -logFile Logs/build-mac.log
    ///   Unity -batchmode -nographics -projectPath . -executeMethod Gym.Editor.BuildScript.BuildWindowsTraining -quit -logFile Logs/build-win.log
    ///
    /// A failed build exits the batch-mode editor with code 1.
    /// </summary>
    public static class BuildScript
    {
        public const string TrainingScenePath = "Assets/Gym/Scenes/Training.unity";
        public const string MacOutput = "Builds/mac/Gym.app";
        public const string WindowsOutput = "Builds/win/Gym.exe";
        public const int WindowWidth = 640;
        public const int WindowHeight = 360;

        [MenuItem("Gym/Build/Mac Training Player")]
        public static void BuildMacTraining() => Build(BuildTarget.StandaloneOSX, MacOutput);

        [MenuItem("Gym/Build/Windows Training Player")]
        public static void BuildWindowsTraining() => Build(BuildTarget.StandaloneWindows64, WindowsOutput);

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
                if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, target))
                    throw new InvalidOperationException(
                        $"Build support for {target} is not installed in Unity {Application.unityVersion}. " +
                        $"Add the module \"{ModuleName(target)}\" in Unity Hub (Installs → {Application.unityVersion} → Add modules) and run again.");

                ApplyPlayerSettings();
#if UNITY_EDITOR_OSX
                if (target == BuildTarget.StandaloneOSX)
                    UnityEditor.OSXStandalone.UserBuildSettings.architecture = OSArchitecture.ARM64;
#endif
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
                var options = new BuildPlayerOptions
                {
                    scenes = new[] { TrainingScenePath },
                    target = target,
                    targetGroup = BuildTargetGroup.Standalone,
                    locationPathName = output,
                    options = BuildOptions.None,
                };
                BuildReport report = BuildPipeline.BuildPlayer(options);
                BuildSummary summary = report.summary;
                Debug.Log($"[Gym] build {summary.result}: {target} -> {output}, " +
                          $"{summary.totalSize / 1048576.0:F1} MB, {summary.totalTime.TotalSeconds:F0} s, " +
                          $"{summary.totalErrors} errors, {summary.totalWarnings} warnings");
                if (summary.result != BuildResult.Succeeded)
                    throw new InvalidOperationException($"Build {summary.result} with {summary.totalErrors} errors; see the log above.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Gym] build failed for {target}: {e.Message}");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
        }

        static string ModuleName(BuildTarget target) =>
            target == BuildTarget.StandaloneWindows64 ? "Windows Build Support (Mono)" :
            target == BuildTarget.StandaloneOSX ? "Mac Build Support (Mono)" : target.ToString();
    }
}
