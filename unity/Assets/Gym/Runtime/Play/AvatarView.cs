using Gym.Core.Env;
using Gym.Runtime.Agents;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Gym.Runtime.Play
{
    /// <summary>
    /// 头像框小人：站在最新那根 candle 的最高点上方，每走一步做一个动作（<see cref="AvatarMotion"/>），买入、卖出、
    /// 被拒时头上冒提示字。框里的图从贴图文件夹读（<see cref="AvatarPictureLoader"/>），没有就用占位图。
    /// 框横向不出图表的左右两边（右边是价格数字），也不压住脚下任何一根 K 线：前几根比最新那根高时，框抬到它们
    /// 上面，用一根细杆连着小脚，小脚尖指着最新那根的最高点。动作也不往右越过价格数字那一列：卖出往左歪，
    /// 左右晃得很小，提示字和框的右边对齐。
    /// 动作按 <see cref="Advance"/> 拨的时钟走；<see cref="ManualClock"/> 为 true 时只有测试和截图工具拨它。
    /// </summary>
    public class AvatarView : MonoBehaviour
    {
        /// <summary>框的边长（不含小脚），世界单位。</summary>
        const float FrameSize = 0.90f;
        const float BorderThickness = 0.05f;
        const float PictureMargin = 0.05f;
        const float FootHeight = 0.13f;
        const float FootHalfWidth = 0.09f;
        const float StalkHalfWidth = 0.015f;
        /// <summary>小脚尖离最高点多高、框底离脚下别的 K 线多高：要让开卖出时画在最高点上方的 ▼。</summary>
        const float FootGap = 0.22f;
        const float HintSize = 0.22f;
        const float HintGap = 0.06f;
        /// <summary>整个小人连同提示字离画面上边至少留多少。</summary>
        const float ScreenMargin = 0.05f;
        /// <summary>压在 K 线（0）和面板底色（10）上面，在字（20）下面。</summary>
        const int BackgroundOrder = 14;
        const int PictureOrder = 15;
        const int BorderOrder = 16;

        [SerializeField] TradingAgent agent;
        [SerializeField] CandleChartView chart;
        [SerializeField] PlayLanguageSwitch language;
        [Tooltip("Seconds one hop, lean or shake takes.")]
        [SerializeField] float motionSeconds = 0.6f;
        [Tooltip("How high, in world units, the avatar hops on every step.")]
        [SerializeField] float hopHeight = 0.30f;
        [Tooltip("How much bigger the avatar gets at the top of a buy hop (0.15 = 15 %).")]
        [SerializeField] float buyGrowth = 0.15f;
        [Tooltip("How far, in degrees, the avatar leans to the left at the top of a sell hop.")]
        [SerializeField] float sellLeanDegrees = 15f;
        [Tooltip("How far, in world units, the avatar shakes sideways when an order is rejected.")]
        [SerializeField] float shakeDistance = 0.06f;

        AvatarAnimation moves;
        Transform body;
        ShapeLayer background;
        ShapeLayer border;
        SpriteRenderer pictureRenderer;
        TextMesh hint;
        string pictureFolder;
        double hintFraction;
        float footOffset;
        float stalkLength;
        bool frozen;

        public TradingAgent Agent
        {
            get => agent;
            set => agent = value;
        }

        public CandleChartView Chart
        {
            get => chart;
            set => chart = value;
        }

        public PlayLanguageSwitch Language
        {
            get => language;
            set => language = value;
        }

        public float MotionSeconds => motionSeconds;

        /// <summary>为 true 时 Update 不拨时钟，动作只随 <see cref="Advance"/> 走。</summary>
        public bool ManualClock { get; set; }

        /// <summary>框里那张图；第一次站上去（或 <see cref="UsePictureFrom"/>）之前为 null。</summary>
        public AvatarPicture Picture { get; private set; }

        /// <summary>框里那张图画出来的宽和高，世界单位（站着不动时）。</summary>
        public Vector2 PictureSize { get; private set; }

        public AvatarMotion Motion => Moves.Motion;
        public float MotionProgress => Moves.Progress;
        public AvatarPose Pose => Moves.Pose;

        /// <summary>站上去过没有；没有时下面几项没有意义。</summary>
        public bool Placed { get; private set; }

        /// <summary>最新那根 candle 最高点的位置，小脚指着它。</summary>
        public Vector2 StandPoint { get; private set; }

        /// <summary>站着不动时框的位置（不含小脚），世界坐标。</summary>
        public Rect RestFrame { get; private set; }

        /// <summary>
        /// 做动作时框、细杆和小脚可能到的最大范围：站着的框加上跳高、变大、左右晃，以及往左歪（绕框底边中点逆时针转，
        /// 只往左边伸出去）。下边是小脚尖。
        /// </summary>
        public Rect ReachArea
        {
            get
            {
                float growth = FrameSize * buyGrowth;
                float right = Mathf.Max(shakeDistance, growth / 2);
                float left = Mathf.Max(right, FrameSize * Mathf.Sin(sellLeanDegrees * Mathf.Deg2Rad));
                Rect r = RestFrame;
                return Rect.MinMaxRect(r.xMin - left, r.yMin - stalkLength - FootHeight, r.xMax + right, r.yMax + hopHeight + growth);
            }
        }

        /// <summary>框底下那根细杆的长度：最新那根就是脚下最高的一根时为 0。</summary>
        public float StalkLength => stalkLength;

        /// <summary>头上的提示字；不显示时它的物体是关着的。</summary>
        public TextMesh HintText => hint;

        /// <summary>边框现在的颜色：平时是 <see cref="PlayPalette.AvatarFrame"/>，买入、卖出、被拒时和提示字同色。</summary>
        public Color BorderColor { get; private set; } = PlayPalette.AvatarFrame;

        /// <summary>
        /// 定格：为 true 时不理会 Agent 的「一局开始」，小人停在打开它那一刻（观战走到段尾时用，默认关）。
        /// 只在组件启用时切换才有效。
        /// </summary>
        public bool Frozen
        {
            get => frozen;
            set
            {
                if (frozen == value) return;
                frozen = value;
                if (agent == null || !isActiveAndEnabled) return;
                agent.EpisodeStarted -= OnEpisodeStarted;
                if (!frozen) agent.EpisodeStarted += OnEpisodeStarted;
            }
        }

        AvatarAnimation Moves => moves ??= new AvatarAnimation(new AvatarMotionStyle(motionSeconds, hopHeight, buyGrowth, sellLeanDegrees, shakeDistance));

        PlayLanguage CurrentLanguage => language != null ? language.Current : PlayLanguage.Chinese;

        static Rect FrameRect => new Rect(-FrameSize / 2, 0f, FrameSize, FrameSize);

        void OnEnable()
        {
            if (language != null) language.Changed += OnLanguageChanged;
            if (agent == null) return;
            if (!frozen) agent.EpisodeStarted += OnEpisodeStarted;
            agent.Stepped += OnStepped;
            if (agent.Env != null && agent.Env.Account != null) OnEpisodeStarted(agent);
        }

        void OnDisable()
        {
            if (language != null) language.Changed -= OnLanguageChanged;
            if (agent == null) return;
            agent.EpisodeStarted -= OnEpisodeStarted;
            agent.Stepped -= OnStepped;
        }

        void Start()
        {
            if (!Placed && agent != null && agent.Env != null && agent.Env.Account != null) OnEpisodeStarted(agent);
        }

        void Update()
        {
            if (!ManualClock) Advance(Time.unscaledDeltaTime);
        }

        void OnDestroy() => ReleasePicture();

        void OnLanguageChanged(PlayLanguage _) => DrawHint();

        void OnEpisodeStarted(TradingAgent source)
        {
            Moves.Stop();
            Place(source.Env);
        }

        void OnStepped(TradingAgent source)
        {
            Place(source.Env);
            Play(AvatarAnimation.MotionFor(source.LastAction, source.LastResult), ActionCodec.Fraction(source.LastContinuous));
        }

        /// <summary>站到 <paramref name="env"/> 当前这根 candle 上方（editor 的截图工具也用它）。</summary>
        public void Place(TradingEnv env)
        {
            if (chart == null || env == null || env.Account == null) return;
            BuildIfNeeded();
            Vector3 high = chart.LatestHighPosition(env);
            Rect area = chart.WorldArea;
            float half = FrameSize / 2;
            float centerX = Mathf.Clamp(high.x, area.xMin + half, area.xMax - half);
            StandPoint = high;
            RestFrame = new Rect(centerX - half, 0f, FrameSize, FrameSize);
            float footTip = high.y + FootGap;
            // 框底要高过框（连同动作）横跨的那几根 K 线里最高的一根，至少高过最新那根加上小脚。
            float underneath = chart.HighestHighY(env, ReachArea.xMin, ReachArea.xMax);
            float bottom = Mathf.Max(footTip + FootHeight, underneath + FootGap);
            // 可见范围最高的那根离图表顶还隔着一截边距，所以按现在的版面碰不到这个上限；留着兜底，保证整个小人在画面里。
            float highestBottom = PlayLayout.Screen.yMax - ScreenMargin - HintSize - HintGap - FrameSize * (1 + buyGrowth) - hopHeight;
            bottom = Mathf.Min(bottom, highestBottom);
            RestFrame = new Rect(centerX - half, bottom, FrameSize, FrameSize);
            stalkLength = Mathf.Max(0f, bottom - FootHeight - footTip);
            footOffset = Mathf.Clamp(high.x - centerX, -half + FootHalfWidth, half - FootHalfWidth);
            Placed = true;
            body.gameObject.SetActive(true);
            Refresh();
        }

        /// <summary>开始一个动作（editor 的截图工具也用它）。正在做的动作立刻换掉，从头开始。</summary>
        /// <param name="fraction">这一步的下单比例（0～1），写在买入、卖出的提示字里。</param>
        public void Play(AvatarMotion motion, double fraction)
        {
            BuildIfNeeded();
            Moves.Start(motion);
            hintFraction = fraction;
            Refresh();
        }

        /// <summary>把动作的时钟往前拨 <paramref name="seconds"/> 秒；做完了就回到站着不动。</summary>
        public void Advance(float seconds)
        {
            AvatarMotion before = Moves.Motion;
            Moves.Advance(seconds);
            if (Moves.Motion != before) Refresh();
            else ApplyPose();
        }

        /// <summary>马上回到站着不动，收掉提示字。</summary>
        public void StandStill()
        {
            Moves.Stop();
            Refresh();
        }

        /// <summary>按当前语言重画边框和提示字（editor 的截图工具换了语言以后调它；运行时跟着语言切换自己重画）。</summary>
        public void Redraw() => Refresh();

        /// <summary>改从 <paramref name="folder"/> 读头像图（测试和截图工具用临时文件夹），马上换上。</summary>
        public void UsePictureFrom(string folder)
        {
            pictureFolder = folder;
            if (body == null) BuildIfNeeded();
            else LoadPicture();
        }

        void Refresh()
        {
            BorderColor = ColorFor(Moves.Motion);
            DrawBorder();
            ApplyPose();
            DrawHint();
        }

        Color ColorFor(AvatarMotion motion)
        {
            switch (motion)
            {
                case AvatarMotion.Buy: return chart != null ? chart.BuyColor : PlayPalette.AvatarFrame;
                case AvatarMotion.Sell: return chart != null ? chart.SellColor : PlayPalette.AvatarFrame;
                case AvatarMotion.Rejected: return PlayPalette.Rejected;
                default: return PlayPalette.AvatarFrame;
            }
        }

        void ApplyPose()
        {
            if (body == null || !Placed) return;
            AvatarPose pose = Moves.Pose;
            body.position = new Vector3(RestFrame.center.x + pose.Offset.x, RestFrame.yMin + pose.Offset.y, 0f);
            body.localScale = new Vector3(pose.Scale, pose.Scale, 1f);
            body.rotation = Quaternion.Euler(0f, 0f, pose.TiltDegrees);
        }

        /// <summary>边框、细杆和小脚，按 body 的本地坐标画（原点在框底边的中点）。</summary>
        void DrawBorder()
        {
            if (border == null) return;
            border.Begin();
            border.Shapes.AddFrame(FrameRect, BorderThickness, BorderColor);
            if (stalkLength > 0)
                border.Shapes.AddQuad(footOffset - StalkHalfWidth, -stalkLength, footOffset + StalkHalfWidth, 0f, BorderColor);
            border.Shapes.AddTriangle(new Vector3(footOffset - FootHalfWidth, -stalkLength), new Vector3(footOffset + FootHalfWidth, -stalkLength),
                new Vector3(footOffset, -stalkLength - FootHeight), BorderColor);
            border.End();
        }

        /// <summary>
        /// 提示字放在框的上方、和框的右边对齐（字往左写，不会伸进右边的价格数字），高过跳到最高、变到最大时的框顶，
        /// 所以动作时不会压住它。
        /// </summary>
        void DrawHint()
        {
            if (hint == null) return;
            string text = AvatarHint.Text(Moves.Motion, hintFraction, CurrentLanguage);
            bool show = text != null && Placed;
            hint.gameObject.SetActive(show);
            if (!show) return;
            hint.text = text;
            hint.color = ColorFor(Moves.Motion);
            WorldText.Move(hint, new Vector2(RestFrame.xMax, RestFrame.yMax + hopHeight + FrameSize * buyGrowth + HintGap));
        }

        void BuildIfNeeded()
        {
            if (body != null) return;
            var bodyObject = new GameObject("Avatar body") { hideFlags = HideFlags.DontSaveInEditor };
            body = bodyObject.transform;
            body.SetParent(transform, false);
            body.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            background = new ShapeLayer(body, "Avatar background", 0f, BackgroundOrder);
            background.Begin();
            background.Shapes.AddRect(FrameRect, PlayPalette.AvatarBackground);
            background.End();

            var pictureObject = new GameObject("Avatar picture") { hideFlags = HideFlags.DontSaveInEditor };
            pictureObject.transform.SetParent(body, false);
            pictureObject.transform.localPosition = new Vector3(0f, FrameSize / 2, 0f);
            pictureRenderer = pictureObject.AddComponent<SpriteRenderer>();
            pictureRenderer.sortingOrder = PictureOrder;

            border = new ShapeLayer(body, "Avatar border", 0f, BorderOrder);
            hint = WorldText.Create(transform, "Avatar hint", Vector2.zero, HintSize, TextAnchor.LowerRight, PlayPalette.AvatarFrame);
            hint.gameObject.SetActive(false);

            LoadPicture();
            bodyObject.SetActive(Placed);
        }

        /// <summary>读头像图，按原比例缩进框里（长边贴着框的内边）。</summary>
        void LoadPicture()
        {
            ReleasePicture();
            Picture = AvatarPictureLoader.Load(pictureFolder ?? AvatarPictureLoader.DefaultFolder);
            Texture2D texture = Picture.Texture;
            float box = FrameSize - 2 * (BorderThickness + PictureMargin);
            float pixelsPerUnit = Mathf.Max(texture.width, texture.height) / box;
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f),
                pixelsPerUnit, 0, SpriteMeshType.FullRect);
            sprite.name = "Avatar picture";
            sprite.hideFlags = HideFlags.DontSave;
            pictureRenderer.sprite = sprite;
            PictureSize = sprite.bounds.size;
        }

        void ReleasePicture()
        {
            if (pictureRenderer != null && pictureRenderer.sprite != null)
            {
                DestroyNow(pictureRenderer.sprite);
                pictureRenderer.sprite = null;
            }
            if (Picture != null) DestroyNow(Picture.Texture);
            Picture = null;
        }

        static void DestroyNow(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }
    }
}
