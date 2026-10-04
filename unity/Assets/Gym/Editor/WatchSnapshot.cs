using System;
using System.IO;
using Gym.Runtime.Configuration;
using Gym.Runtime.Play;
using Gym.Runtime.Watch;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Gym.Editor
{
    /// <summary>
    /// 把 Watch scene 渲染成 1600×900 的 PNG，放在 Logs/：
    ///
    ///   watch-snapshot-no-model.png   没选模型时的提示（在 editor 里直接画，中文）
    ///   watch-snapshot.png            模型在交易：走菜单那一段（导入、记住、打开 scene、进入播放），Agent 真的在跑推理；
    ///                                 走过 300 步以后停在下一笔成交上，小人和飘字的动作拨到一半（英文）
    ///   watch-snapshot-end.png        同一次播放一直走到段尾：画面定格在最后一步，上方提示按 R 重来（中文）
    ///
    /// 要训练好的模型（-gymModel，和打评估包同一个参数）、要图形设备、要进入播放，所以不加 -nographics 和 -quit，
    /// 截完自己退出（成功 0，失败 1）。记住的模型写在 Temp/ 下的临时记录里，不改工程自己记住的那个：
    ///
    ///   Unity -batchmode -projectPath "$PWD/unity" -executeMethod Gym.Editor.WatchSnapshot.Render
    ///         -gymModel "$PWD/results/&lt;run-id&gt;/TradingAgent.onnx" -logFile "$PWD/unity/Logs/watch-snapshot.log"
    ///
    /// 进入播放会重新加载脚本，所以要做的事记在 SessionState 里（只活在这一次 editor 进程里），重新加载后接着做。
    /// </summary>
    [InitializeOnLoad]
    public static class WatchSnapshot
    {
        const string RecordKey = "Gym.WatchSnapshot.Record";
        const int StepsBeforeTheShot = 300;
        /// <summary>找下一笔成交最多再走几步；找不到也照样截图。</summary>
        const int StepsToFindATrade = 2000;
        /// <summary>进入播放后最多等几秒让观战开始（Agent 激活）。</summary>
        const double SecondsToStart = 60;

        static double startedAt;

        static WatchSnapshot()
        {
            string record = SessionState.GetString(RecordKey, "");
            if (record.Length == 0) return;
            WatchModelMemory.Location = record;
            EditorApplication.update += WhilePlaying;
        }

        public static void Render()
        {
            try
            {
                RenderNoModel();
                string model = CommandLineArgs.ValueOf(Environment.GetCommandLineArgs(), BuildScript.ModelArg)
                    ?? throw new ArgumentException($"{BuildScript.ModelArg} <path to .onnx> is required for the playing shots");
                string record = Path.GetFullPath(Path.Combine("Temp", "gym-watch-snapshot", WatchModelMemory.FileName));
                if (File.Exists(record)) File.Delete(record);
                WatchModelMemory.Location = record;
                SessionState.SetString(RecordKey, record);
                WatchSetup.Watch(model);
            }
            catch (Exception e)
            {
                Finish(1, $"watch snapshot failed: {e.Message}\n{e}");
            }
        }

        /// <summary>没选模型时的画面：在 editor 里直接请提示出来，和播放时 WatchController 请的是同一段。</summary>
        static void RenderNoModel()
        {
            EditorSceneManager.OpenScene(GymSceneBuilder.WatchScenePath, OpenSceneMode.Single);
            var language = Object.FindFirstObjectByType<PlayLanguageSwitch>();
            language.Set(language.DefaultLanguage);
            Object.FindFirstObjectByType<WatchNotice>().Show(WatchNoticeKind.NoModel);
            string path = PlaySnapshot.WritePng(Camera.main, "watch-snapshot-no-model.png");
            Debug.Log($"[Gym] wrote {path} (no model, {language.Current}), font {PlayFont.ChosenName}, graphics {SystemInfo.graphicsDeviceType}");
            // 从磁盘重新打开，丢掉画图时建的物体，scene 也不算改过。
            EditorSceneManager.OpenScene(GymSceneBuilder.WatchScenePath, OpenSceneMode.Single);
        }

        /// <summary>进入播放后每帧调一次：等观战开始，截两张，退出。</summary>
        static void WhilePlaying()
        {
            if (!EditorApplication.isPlaying) return;
            if (startedAt == 0) startedAt = EditorApplication.timeSinceStartup;
            try
            {
                var controller = Object.FindFirstObjectByType<WatchController>();
                if (controller == null || !controller.IsWatching)
                {
                    if (EditorApplication.timeSinceStartup - startedAt > SecondsToStart)
                        Finish(1, "the Watch scene did not start watching the model; see the warnings above");
                    return;
                }
                EditorApplication.update -= WhilePlaying;
                ShootWhileTrading(controller);
                ShootTheEnd(controller);
                Finish(0, "watch snapshots done");
            }
            catch (Exception e)
            {
                Finish(1, $"watch snapshot failed: {e.Message}\n{e}");
            }
        }

        /// <summary>暂停，单步走过前几百步，停在下一笔成交上，把小人和飘字拨到一半，切到英文截图。</summary>
        static void ShootWhileTrading(WatchController controller)
        {
            controller.ManualClock = true;
            controller.Avatar.ManualClock = true;
            controller.Wallet.ManualClock = true;
            WatchPlayback playback = controller.Playback;
            if (!playback.Paused) playback.TogglePause();
            while (playback.StepsTaken < StepsBeforeTheShot && !playback.Finished) playback.StepOnce();
            for (int i = 0; i < StepsToFindATrade && !controller.Agent.LastResult.Traded && !playback.Finished; i++) playback.StepOnce();
            controller.Avatar.Advance(controller.Avatar.MotionSeconds / 2);
            controller.Wallet.Advance(controller.Wallet.FloatSeconds / 2);
            controller.Language.Set(PlayLanguage.English);
            Save(controller, "watch-snapshot.png", "trading");
        }

        /// <summary>一直单步到段尾，画面定格，切回中文截图。</summary>
        static void ShootTheEnd(WatchController controller)
        {
            WatchPlayback playback = controller.Playback;
            int limit = controller.Agent.Env.Last - controller.Agent.Env.First + 100;
            for (int i = 0; i < limit && !playback.Finished; i++) playback.StepOnce();
            if (!playback.Finished) throw new InvalidOperationException($"the segment did not end within {limit} steps");
            controller.Avatar.StandStill();
            controller.Wallet.ClearFloat();
            controller.Language.Set(PlayLanguage.Chinese);
            Save(controller, "watch-snapshot-end.png", "end of the segment");
        }

        static void Save(WatchController controller, string fileName, string what)
        {
            string path = PlaySnapshot.WritePng(Camera.main, fileName);
            HudSnapshot shown = controller.Hud.Shown;
            Debug.Log($"[Gym] wrote {path} ({what}, {controller.Language.Current}): model {controller.ModelAsset}, " +
                      $"{controller.Playback.StepsTaken} steps, readout step {shown.Step} at {shown.TimeUtc:yyyy-MM-dd HH:mm} UTC, " +
                      $"last action {shown.LastAction}, avatar {controller.Avatar.Motion}, frozen {controller.Playback.Frozen}, " +
                      $"graphics {SystemInfo.graphicsDeviceType}");
        }

        static void Finish(int exitCode, string message)
        {
            EditorApplication.update -= WhilePlaying;
            SessionState.EraseString(RecordKey);
            WatchModelMemory.Location = null;
            if (exitCode == 0) Debug.Log("[Gym] " + message);
            else Debug.LogError("[Gym] " + message);
            if (Application.isBatchMode) EditorApplication.Exit(exitCode);
        }
    }
}
