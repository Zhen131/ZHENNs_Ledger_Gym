using System;
using System.IO;
using Gym.Core.Accounting;
using Gym.Core.Env;
using Gym.Runtime.Configuration;
using Gym.Runtime.Play;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Gym.Editor
{
    /// <summary>
    /// 把 Play scene 渲染成四张 1600×900 的 PNG，放在 Logs/。按键顺序是 40 次不动、25 % 买入、12 次不动、
    /// 50 % 卖出、再 6 次不动：
    ///
    ///   play-snapshot-buy.png    25 % 买入那一步之后，红色的飘字飘到一半
    ///   play-snapshot-sell.png   50 % 卖出那一步之后，绿色的飘字掉到一半
    ///   play-snapshot.png        最后那一步之后，默认语言（中文）
    ///   play-snapshot-en.png     同一个画面切到英文
    ///
    /// 画面上的东西都由那台相机画（字是 TextMesh），所以图里什么都有。需要图形设备（运行时不要加 -nographics）：
    ///
    ///   Unity -batchmode -projectPath "$PWD/unity" -executeMethod Gym.Editor.PlaySnapshot.Render -quit
    /// </summary>
    public static class PlaySnapshot
    {
        const int ImageWidth = 1600;
        const int ImageHeight = 900;

        [MenuItem("Gym/Render Play Snapshot")]
        public static void Render()
        {
            GymSettings s = GymConfigLoader.Load(GymConfigLoader.DefaultConfigPath, GymConfigLoader.DefaultSymbolsPath);
            GymConfig c = s.Config;
            var env = TradingEnv.ForSegment(s.Series, s.Rules, s.Train, c.initialCash, c.episodeLength, c.randomInitialPositionShare);
            env.Reset(new CostModel(), s.PlayStartIndex);

            EditorSceneManager.OpenScene(GymSceneBuilder.PlayScenePath, OpenSceneMode.Single);
            var views = new SceneViews();
            views.Language.Set(views.Language.DefaultLanguage);

            HoldFor(env, 40);
            views.Step(env, TradeAction.Buy, 0.25);
            Save(views, env, "play-snapshot-buy.png", "buy amount half way up");
            HoldFor(env, 12);
            views.Step(env, TradeAction.Sell, 0.5);
            Save(views, env, "play-snapshot-sell.png", "sell amount half way down");
            HoldFor(env, 5);
            views.Step(env, TradeAction.Hold, 0.25);
            Save(views, env, "play-snapshot.png", $"{views.Language.Current}");
            views.Language.Toggle();
            views.Redraw();
            Save(views, env, "play-snapshot-en.png", $"{views.Language.Current}");

            // 从磁盘重新打开，丢掉画图时在 scene 里建的那些物体。
            EditorSceneManager.OpenScene(GymSceneBuilder.PlayScenePath, OpenSceneMode.Single);
        }

        static void HoldFor(TradingEnv env, int steps)
        {
            for (int i = 0; i < steps; i++) env.Step(TradeAction.Hold, 0f);
        }

        static void Save(SceneViews views, TradingEnv env, string fileName, string what)
        {
            byte[] png = RenderToPng(Camera.main, ImageWidth, ImageHeight);
            Directory.CreateDirectory("Logs");
            string path = Path.Combine("Logs", fileName);
            File.WriteAllBytes(path, png);
            CandleChartView chart = views.Chart;
            Debug.Log($"[Gym] wrote {path} ({what}): {chart.DrawnCandles} candles, {chart.DrawnMarkers} markers, " +
                      $"{chart.DrawnPriceTicks.Values.Count} price lines, {chart.DrawnTimeLabels.Count} date labels, " +
                      $"last candle {env.Series.OpenTimeUtc(env.CurrentIndex):yyyy-MM-dd HH:mm} UTC, font {PlayFont.ChosenName}, " +
                      $"graphics {SystemInfo.graphicsDeviceType}");
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

        /// <summary>
        /// Play scene 上那几个画面组件。editor 里它们的 Awake、Start 不会跑，也没有 Agent 推着走，
        /// 所以这里照 Agent 的做法，每一步之后把环境的状态直接交给它们。
        /// </summary>
        sealed class SceneViews
        {
            public readonly CandleChartView Chart = Object.FindFirstObjectByType<CandleChartView>();
            public readonly HudView Hud = Object.FindFirstObjectByType<HudView>();
            public readonly WalletView Wallet = Object.FindFirstObjectByType<WalletView>();
            public readonly PlayLanguageSwitch Language = Object.FindFirstObjectByType<PlayLanguageSwitch>();

            /// <summary>走一步并画出来；成交了就冒飘字，并把动画拨到一半。</summary>
            public void Step(TradingEnv env, TradeAction side, double fraction)
            {
                double cashBefore = env.Account.Cash;
                float continuous = ActionCodec.FromFraction(fraction);
                StepResult r = env.Step(side, continuous);
                Chart.Draw(env);
                Hud.Show(HudView.Capture(env, true, side, ActionCodec.Fraction(continuous), r.Traded));
                Account a = env.Account;
                Wallet.Show(a.Cash, a.UnrealizedPnl(env.CurrentClose), a.RealizedPnl);
                Wallet.ClearFloat();
                if (!r.Traded) return;
                Wallet.StartFloat(side, Math.Abs(a.Cash - cashBefore));
                Wallet.Advance(Wallet.FloatSeconds / 2);
            }

            /// <summary>换了语言以后重画所有的字（editor 里组件没有订阅语言切换）。</summary>
            public void Redraw()
            {
                Language.Redraw();
                Hud.Draw();
                Wallet.DrawLabels();
            }
        }
    }
}
