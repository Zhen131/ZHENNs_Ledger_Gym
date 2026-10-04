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
    /// 把 Play scene 渲染成 1600×900 的 PNG，放在 Logs/。按键顺序是 40 次不动、25 % 买入、12 次不动、
    /// 50 % 卖出、再 6 次不动，然后全部卖出、再卖一次（被拒）：
    ///
    ///   play-snapshot-buy.png              25 % 买入那一步之后，红色的飘字飘到一半，小人买入的动作做到一半
    ///   play-snapshot-sell.png             50 % 卖出那一步之后，绿色的飘字掉到一半，小人卖出的动作做到一半
    ///   play-snapshot.png                  最后那一步不动之后，默认语言（中文），小人站着
    ///   play-snapshot-en.png               同一个画面切到英文
    ///   play-snapshot-rejected.png         没有币还卖，被拒：小人左右晃到一半，头上灰色「被拒」
    ///   play-snapshot-avatar.png           小人的特写：站在最新那根 candle 上，框里是占位图
    ///   play-snapshot-avatar-picture.png   同一个特写，框里换成一张临时生成的 3:2 测试图（放在 Temp/，不碰贴图文件夹）
    ///
    /// 画面上的东西都由那台相机画（字是 TextMesh），所以图里什么都有。需要图形设备（运行时不要加 -nographics）：
    ///
    ///   Unity -batchmode -projectPath "$PWD/unity" -executeMethod Gym.Editor.PlaySnapshot.Render -quit
    /// </summary>
    public static class PlaySnapshot
    {
        const int ImageWidth = 1600;
        const int ImageHeight = 900;
        /// <summary>特写时相机至少的半高（世界单位）：画面高 3 个单位，小人占三分之一左右。</summary>
        const float CloseUpHalfHeight = 1.5f;
        /// <summary>特写时上下各多留的一点，世界单位。</summary>
        const float CloseUpMargin = 0.4f;
        const int TestPictureWidth = 240;
        const int TestPictureHeight = 160;

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
            Save(views, env, "play-snapshot-buy.png", "buy half way");
            HoldFor(env, 12);
            views.Step(env, TradeAction.Sell, 0.5);
            Save(views, env, "play-snapshot-sell.png", "sell half way");
            HoldFor(env, 5);
            views.Step(env, TradeAction.Hold, 0.25);
            Save(views, env, "play-snapshot.png", $"{views.Language.Current}");
            views.Language.Toggle();
            views.Redraw();
            Save(views, env, "play-snapshot-en.png", $"{views.Language.Current}");

            views.Language.Toggle();
            views.Redraw();
            views.Step(env, TradeAction.Sell, 1.0);
            views.Step(env, TradeAction.Sell, 0.5);
            Save(views, env, "play-snapshot-rejected.png", "rejected sell, shake half way");

            views.Avatar.StandStill();
            views.Wallet.ClearFloat();
            SaveCloseUp(views, env, "play-snapshot-avatar.png", "avatar close-up, placeholder");
            string folder = WriteTestPicture();
            try
            {
                views.Avatar.UsePictureFrom(folder);
                SaveCloseUp(views, env, "play-snapshot-avatar-picture.png", $"avatar close-up, {TestPictureWidth}x{TestPictureHeight} test picture");
            }
            finally
            {
                Directory.Delete(folder, true);
            }

            // 从磁盘重新打开，丢掉画图时在 scene 里建的那些物体。
            EditorSceneManager.OpenScene(GymSceneBuilder.PlayScenePath, OpenSceneMode.Single);
        }

        static void HoldFor(TradingEnv env, int steps)
        {
            for (int i = 0; i < steps; i++) env.Step(TradeAction.Hold, 0f);
        }

        static void Save(SceneViews views, TradingEnv env, string fileName, string what)
        {
            string path = WritePng(Camera.main, fileName);
            CandleChartView chart = views.Chart;
            Debug.Log($"[Gym] wrote {path} ({what}): {chart.DrawnCandles} candles, {chart.DrawnMarkers} markers, " +
                      $"{chart.DrawnPriceTicks.Values.Count} price lines, {chart.DrawnTimeLabels.Count} date labels, " +
                      $"last candle {env.Series.OpenTimeUtc(env.CurrentIndex):yyyy-MM-dd HH:mm} UTC, avatar {views.Avatar.Motion}, " +
                      $"font {PlayFont.ChosenName}, graphics {SystemInfo.graphicsDeviceType}");
        }

        /// <summary>相机临时拉近到小人身上照一张（从框顶到小脚指着的那根 candle 都在画面里），照完放回原处。</summary>
        static void SaveCloseUp(SceneViews views, TradingEnv env, string fileName, string what)
        {
            Camera camera = Camera.main;
            Vector3 position = camera.transform.position;
            float size = camera.orthographicSize;
            Rect frame = views.Avatar.RestFrame;
            float bottom = views.Avatar.StandPoint.y;
            try
            {
                camera.transform.position = new Vector3(frame.center.x, (frame.yMax + bottom) / 2, position.z);
                camera.orthographicSize = Mathf.Max(CloseUpHalfHeight, (frame.yMax - bottom) / 2 + CloseUpMargin);
                string path = WritePng(camera, fileName);
                AvatarPicture picture = views.Avatar.Picture;
                Debug.Log($"[Gym] wrote {path} ({what}): picture {picture.Width}x{picture.Height}, placeholder {picture.IsPlaceholder}, " +
                          $"drawn {views.Avatar.PictureSize.x:F3} x {views.Avatar.PictureSize.y:F3} world units, " +
                          $"last candle {env.Series.OpenTimeUtc(env.CurrentIndex):yyyy-MM-dd HH:mm} UTC");
            }
            finally
            {
                camera.transform.position = position;
                camera.orthographicSize = size;
            }
        }

        /// <summary>把相机的一帧写成 Logs/ 下的 PNG，返回路径。Watch scene 的截图工具也用它。</summary>
        internal static string WritePng(Camera camera, string fileName)
        {
            byte[] png = RenderToPng(camera, ImageWidth, ImageHeight);
            Directory.CreateDirectory("Logs");
            string path = Path.Combine("Logs", fileName);
            File.WriteAllBytes(path, png);
            return path;
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
        /// 在 Temp/ 下的一个临时文件夹里写一张 3:2 的测试头像（橙到紫的渐变，中间一张白色圆脸），返回文件夹路径。
        /// 一看就和占位图不一样，也看得出长方形的图没有被压扁。
        /// </summary>
        static string WriteTestPicture()
        {
            string folder = Path.GetFullPath(Path.Combine("Temp", "gym-avatar-snapshot"));
            Directory.CreateDirectory(folder);
            var texture = new Texture2D(TestPictureWidth, TestPictureHeight, TextureFormat.RGBA32, false);
            var orange = new Color(1.00f, 0.55f, 0.15f);
            var purple = new Color(0.45f, 0.20f, 0.75f);
            var dark = new Color(0.15f, 0.15f, 0.20f);
            var face = new Vector2(TestPictureWidth / 2f, TestPictureHeight / 2f);
            float radius = TestPictureHeight * 0.32f;
            for (int y = 0; y < TestPictureHeight; y++)
            for (int x = 0; x < TestPictureWidth; x++)
            {
                Vector2 p = new Vector2(x, y) - face;
                bool eye = new Vector2(Math.Abs(p.x) - radius * 0.38f, p.y - radius * 0.25f).magnitude <= radius * 0.12f;
                bool mouth = Math.Abs(p.magnitude - radius * 0.55f) <= 2.5f && p.y < -radius * 0.15f;
                Color color = eye || mouth ? dark
                    : p.magnitude <= radius ? Color.white
                    : Color.Lerp(orange, purple, (float)x / TestPictureWidth);
                texture.SetPixel(x, y, color);
            }
            texture.Apply();
            File.WriteAllBytes(Path.Combine(folder, AvatarPictureLoader.FileNames[0]), texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            return folder;
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
            public readonly AvatarView Avatar = Object.FindFirstObjectByType<AvatarView>();
            public readonly PlayLanguageSwitch Language = Object.FindFirstObjectByType<PlayLanguageSwitch>();

            /// <summary>
            /// 走一步并画出来。成交了就冒飘字，飘字和小人的动作都拨到一半；被拒时小人晃到一半；
            /// 不动时小人跳完、站着。
            /// </summary>
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
                Avatar.Place(env);
                Avatar.Play(AvatarAnimation.MotionFor(side, r), ActionCodec.Fraction(continuous));
                if (r.Traded)
                {
                    Wallet.StartFloat(side, Math.Abs(a.Cash - cashBefore));
                    Wallet.Advance(Wallet.FloatSeconds / 2);
                }
                Avatar.Advance(r.Traded || r.Rejected ? Avatar.MotionSeconds / 2 : Avatar.MotionSeconds + 0.01f);
            }

            /// <summary>换了语言以后重画所有的字（editor 里组件没有订阅语言切换）。</summary>
            public void Redraw()
            {
                Language.Redraw();
                Hud.Draw();
                Wallet.DrawLabels();
                Avatar.Redraw();
            }
        }
    }
}
