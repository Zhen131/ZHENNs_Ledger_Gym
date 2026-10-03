using System.IO;
using Gym.Core.Accounting;
using Gym.Core.Env;
using Gym.Runtime.Configuration;
using Gym.Runtime.Play;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Gym.Editor
{
    /// <summary>
    /// 把 Play scene 的图表渲染到 Logs/play-snapshot.png，画的是 40 次不动、25 % 买入、12 次不动、
    /// 50 % 卖出、再 6 次不动之后的样子。IMGUI 的 HUD 只在真正的 Game 视图里才画，所以图里没有它。
    /// 需要图形设备（运行时不要加 -nographics）：
    ///
    ///   Unity -batchmode -projectPath "$PWD/unity" -executeMethod Gym.Editor.PlaySnapshot.Render -quit
    /// </summary>
    public static class PlaySnapshot
    {
        [MenuItem("Gym/Render Play Snapshot")]
        public static void Render()
        {
            GymSettings s = GymConfigLoader.Load(GymConfigLoader.DefaultConfigPath, GymConfigLoader.DefaultSymbolsPath);
            GymConfig c = s.Config;
            var env = TradingEnv.ForSegment(s.Series, s.Rules, s.Train, c.initialCash, c.episodeLength, c.randomInitialPositionShare);
            env.Reset(new CostModel(), s.PlayStartIndex);
            for (int i = 0; i < 40; i++) env.Step(TradeAction.Hold, 0f);
            env.Step(TradeAction.Buy, ActionCodec.FromFraction(0.25));
            for (int i = 0; i < 12; i++) env.Step(TradeAction.Hold, 0f);
            env.Step(TradeAction.Sell, ActionCodec.FromFraction(0.5));
            for (int i = 0; i < 6; i++) env.Step(TradeAction.Hold, 0f);

            EditorSceneManager.OpenScene(GymSceneBuilder.PlayScenePath, OpenSceneMode.Single);
            var chart = Object.FindFirstObjectByType<CandleChartView>();
            chart.Draw(env);
            byte[] png = RenderToPng(Camera.main, 1600, 900);

            Directory.CreateDirectory("Logs");
            string path = Path.Combine("Logs", "play-snapshot.png");
            File.WriteAllBytes(path, png);
            Debug.Log($"[Gym] wrote {path}: {chart.DrawnCandles} candles, {chart.DrawnMarkers} markers, " +
                      $"last candle {s.Series.OpenTimeUtc(env.CurrentIndex):yyyy-MM-dd HH:mm} UTC, graphics {SystemInfo.graphicsDeviceType}");
        }

        /// <summary>把相机的一帧渲染到指定大小的纹理里，返回 PNG 字节。</summary>
        static byte[] RenderToPng(Camera camera, int width, int height)
        {
            var target = new RenderTexture(width, height, 24);
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();
            camera.targetTexture = null;
            RenderTexture.active = null;
            byte[] png = image.EncodeToPNG();
            Object.DestroyImmediate(image);
            Object.DestroyImmediate(target);
            return png;
        }
    }
}
