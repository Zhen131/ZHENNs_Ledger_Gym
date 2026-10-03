using System.IO;
using Gym.Core.Accounting;
using Gym.Core.Env;
using Gym.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Gym.EditorTools
{
    /// <summary>
    /// Renders the Play scene's chart after the checklist's three key presses to
    /// Logs/play-snapshot.png. The IMGUI HUD is drawn only in a real Game view, so it
    /// is not in the picture. Needs a graphics device (run without -nographics):
    ///
    ///   Unity -batchmode -projectPath "$PWD/unity" -executeMethod Gym.EditorTools.PlaySnapshot.Render -quit
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
            for (int i = 0; i < 40; i++) env.Step(ActionCodec.Hold, 0f);
            env.Step(ActionCodec.Buy, ActionCodec.FromFraction(0.25));
            for (int i = 0; i < 12; i++) env.Step(ActionCodec.Hold, 0f);
            env.Step(ActionCodec.Sell, ActionCodec.FromFraction(0.5));
            for (int i = 0; i < 6; i++) env.Step(ActionCodec.Hold, 0f);

            EditorSceneManager.OpenScene(GymSceneBuilder.PlayScenePath, OpenSceneMode.Single);
            var chart = Object.FindFirstObjectByType<CandleChartView>();
            chart.Draw(env);
            Camera camera = Camera.main;

            var target = new RenderTexture(1600, 900, 24);
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            var image = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
            image.Apply();
            camera.targetTexture = null;
            RenderTexture.active = null;

            Directory.CreateDirectory("Logs");
            string path = Path.Combine("Logs", "play-snapshot.png");
            File.WriteAllBytes(path, image.EncodeToPNG());
            Debug.Log($"[Gym] wrote {path}: {chart.DrawnCandles} candles, {chart.DrawnMarkers} markers, " +
                      $"last candle {s.Series.OpenTimeUtc(env.T):yyyy-MM-dd HH:mm} UTC, graphics {SystemInfo.graphicsDeviceType}");
            Object.DestroyImmediate(image);
            Object.DestroyImmediate(target);
        }
    }
}
