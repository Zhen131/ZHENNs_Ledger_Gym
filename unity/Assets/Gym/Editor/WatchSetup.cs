using System;
using System.IO;
using Gym.Runtime.Watch;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Gym.Editor
{
    /// <summary>
    /// Unity 菜单 Gym &gt; Watch a Model...：选一个训练好的 .onnx，导入工程（和打评估包同一份导入），记住它，
    /// 打开 Watch scene 并进入播放，模型就开始交易。下次直接打开 Watch scene 点播放，看的还是它。
    /// 「选文件」和后面那一段分开：文件对话框在命令行下弹不出来，后一段（<see cref="Watch"/>、<see cref="Remember"/>）
    /// 能被测试和截图工具直接调用。
    /// </summary>
    public static class WatchSetup
    {
        public const string MenuPath = "Gym/Watch a Model...";

        /// <summary>导入的模型叫 Watch-&lt;它所在文件夹的名字&gt;.onnx，和打评估包导入的 &lt;run-id&gt;.onnx 分开。</summary>
        public const string ImportedPrefix = "Watch-";

        [MenuItem(MenuPath, true)]
        static bool CanChooseModel() => !EditorApplication.isPlayingOrWillChangePlaymode;

        [MenuItem(MenuPath)]
        public static void ChooseModel()
        {
            WatchModelRecord last = WatchModelMemory.Read();
            string startFolder = last != null && File.Exists(last.source_file) ? Path.GetDirectoryName(last.source_file) : "";
            string modelPath = EditorUtility.OpenFilePanel("Choose a trained model to watch", startFolder, "onnx");
            if (string.IsNullOrEmpty(modelPath)) return;
            Watch(modelPath);
        }

        /// <summary>导入并记住 <paramref name="modelPath"/>，打开 Watch scene，进入播放。</summary>
        public static void Watch(string modelPath)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning($"[Gym] stop Play mode first, then use {MenuPath} again");
                return;
            }
            Remember(modelPath);
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(GymSceneBuilder.WatchScenePath, OpenSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }

        /// <summary>
        /// 把 <paramref name="modelPath"/> 导入成 Models/Imported/Watch-&lt;文件夹名&gt;.onnx，写进
        /// <see cref="WatchModelMemory"/> 的记录；返回导入后的 asset 路径。文件不在或不是能用的模型时抛异常。
        /// </summary>
        public static string Remember(string modelPath)
        {
            modelPath = Path.GetFullPath(modelPath);
            if (!File.Exists(modelPath)) throw new FileNotFoundException($"model not found: {modelPath}");
            string name = ImportedName(modelPath);
            BuildScript.ImportModel(modelPath, name);
            string asset = $"{BuildScript.ImportedModelsFolder}/{name}.onnx";
            WatchModelMemory.Write(new WatchModelRecord { model_asset = asset, source_file = modelPath });
            Debug.Log($"[Gym] imported {modelPath} as {asset} for the Watch scene; remembered in {WatchModelMemory.Location}");
            return asset;
        }

        /// <summary>Watch- 加上模型所在文件夹的名字（训练的输出是 results/&lt;run-id&gt;/TradingAgent.onnx）。</summary>
        public static string ImportedName(string modelPath)
        {
            string folder = Path.GetFileName(Path.GetDirectoryName(Path.GetFullPath(modelPath)));
            bool usable = !string.IsNullOrEmpty(folder) && folder.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
            return ImportedPrefix + (usable ? folder : "model");
        }
    }
}
