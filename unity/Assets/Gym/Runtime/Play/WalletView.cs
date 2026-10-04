using System;
using Gym.Core.Accounting;
using Gym.Core.Env;
using Gym.Runtime.Agents;
using UnityEngine;

namespace Gym.Runtime.Play
{
    /// <summary>
    /// Play scene 左下角的口袋：口袋上写着现金，旁边两行是未实现盈亏和已实现盈亏（0 和正数绿、负数红）。
    /// 买入成交时口袋上方冒出红色的 "-$金额"，往上飘并淡出；卖出成交时冒出绿色的 "+$金额"，往下掉进口袋。
    /// 金额是这一步现金实际变了多少。同一时间只有一条飘字：新的出现时，旧的直接消失。
    /// 动画按 <see cref="Advance"/> 拨的时钟走；<see cref="ManualClock"/> 为 true 时只有测试和截图工具拨它。
    /// </summary>
    public class WalletView : MonoBehaviour
    {
        const float CashSize = 0.30f;
        const float CaptionSize = 0.16f;
        const float PnlLabelSize = 0.16f;
        const float PnlValueSize = 0.22f;
        const float FloatSize = 0.28f;
        /// <summary>口袋左右各离面板边多远、底边离面板底多远、有多高。</summary>
        const float PocketInset = 0.20f;
        const float PocketWidth = 2.60f;
        const float PocketHeight = 1.50f;
        /// <summary>飘字在口袋口上方多高的地方出现（或落到那里消失）。</summary>
        const float FloatGap = 0.25f;

        [SerializeField] TradingAgent agent;
        [SerializeField] PlayLanguageSwitch language;
        [Tooltip("Seconds a floating amount takes to rise and fade, or to fall into the pocket.")]
        [SerializeField] float floatSeconds = 1.2f;
        [Tooltip("How far, in world units, a floating amount travels.")]
        [SerializeField] float floatTravel = 1.5f;

        ShapeLayer shapes;
        TextMesh caption;
        TextMesh cash;
        TextMesh unrealizedLabel;
        TextMesh unrealizedValue;
        TextMesh realizedLabel;
        TextMesh realizedValue;
        TextMesh floatLabel;
        bool shown;
        double previousCash;

        public TradingAgent Agent
        {
            get => agent;
            set => agent = value;
        }

        public PlayLanguageSwitch Language
        {
            get => language;
            set => language = value;
        }

        public float FloatSeconds => floatSeconds;

        /// <summary>为 true 时 Update 不拨时钟，动画只随 <see cref="Advance"/> 走。</summary>
        public bool ManualClock { get; set; }

        public double ShownCash { get; private set; }
        public double ShownUnrealizedPnl { get; private set; }
        public double ShownRealizedPnl { get; private set; }

        /// <summary>正在飘的那条；没有时为 null。</summary>
        public FloatingAmount ActiveFloat { get; private set; }

        /// <summary>画在口袋上和口袋旁边的字（测试读它们）。</summary>
        public TextMesh CashText => cash;
        public TextMesh UnrealizedText => unrealizedValue;
        public TextMesh RealizedText => realizedValue;
        public TextMesh FloatText => floatLabel;

        /// <summary>口袋口的高度：卖出的飘字落到这里消失。</summary>
        public float PocketTop => PlayLayout.Wallet.yMin + PocketInset + PocketHeight;

        Rect PocketArea => new Rect(PlayLayout.Wallet.xMin + PocketInset, PlayLayout.Wallet.yMin + PocketInset, PocketWidth, PocketHeight);

        PlayLanguage CurrentLanguage => language != null ? language.Current : PlayLanguage.Chinese;

        void OnEnable()
        {
            if (language != null) language.Changed += OnLanguageChanged;
            if (agent == null) return;
            agent.EpisodeStarted += OnEpisodeStarted;
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
            if (agent != null && agent.Env != null && agent.Env.Account != null && !shown) OnEpisodeStarted(agent);
        }

        void Update()
        {
            if (!ManualClock) Advance(Time.unscaledDeltaTime);
        }

        void OnLanguageChanged(PlayLanguage _) => DrawLabels();

        void OnEpisodeStarted(TradingAgent source)
        {
            ClearFloat();
            ShowAccount(source.Env);
        }

        void OnStepped(TradingAgent source)
        {
            double before = previousCash;
            ShowAccount(source.Env);
            if (source.LastResult.Traded) StartFloat(source.LastAction, Math.Abs(ShownCash - before));
        }

        void ShowAccount(TradingEnv env)
        {
            Account a = env.Account;
            Show(a.Cash, a.UnrealizedPnl(env.CurrentClose), a.RealizedPnl);
        }

        /// <summary>按给定的数画口袋（editor 的截图工具也用它）。现金立刻换成新数，不做滚动效果。</summary>
        public void Show(double cashNow, double unrealizedPnl, double realizedPnl)
        {
            BuildIfNeeded();
            shown = true;
            previousCash = cashNow;
            ShownCash = cashNow;
            ShownUnrealizedPnl = unrealizedPnl;
            ShownRealizedPnl = realizedPnl;
            cash.text = MoneyText.Amount(cashNow);
            ShowSigned(unrealizedValue, unrealizedPnl);
            ShowSigned(realizedValue, realizedPnl);
            DrawLabels();
        }

        /// <summary>
        /// 为一笔成交冒一条飘字：买入是红色 "-$金额" 往上飘，卖出是绿色 "+$金额" 往下掉。不动不冒。
        /// 旧的那条还在飘时直接被换掉。
        /// </summary>
        public void StartFloat(TradeAction side, double cashChange)
        {
            if (side == TradeAction.Hold) return;
            BuildIfNeeded();
            float low = PocketTop + FloatGap;
            float high = low + floatTravel;
            ActiveFloat = side == TradeAction.Buy
                ? new FloatingAmount(MoneyText.Spent(cashChange), FloatDirection.RiseAndFade, low, high, floatSeconds)
                : new FloatingAmount(MoneyText.Received(cashChange), FloatDirection.FallIntoPocket, high, PocketTop, floatSeconds);
            floatLabel.text = ActiveFloat.Text;
            DrawFloat();
        }

        /// <summary>把动画时钟往前拨 <paramref name="seconds"/> 秒。</summary>
        public void Advance(float seconds)
        {
            if (ActiveFloat == null) return;
            ActiveFloat.Advance(seconds);
            if (ActiveFloat.Finished) ClearFloat();
            else DrawFloat();
        }

        void DrawFloat()
        {
            Color color = ActiveFloat.Direction == FloatDirection.RiseAndFade ? PlayPalette.Loss : PlayPalette.Gain;
            color.a = ActiveFloat.Alpha;
            floatLabel.color = color;
            WorldText.Move(floatLabel, new Vector2(PocketArea.center.x, ActiveFloat.Y));
            floatLabel.gameObject.SetActive(true);
        }

        /// <summary>马上收掉正在飘的那条。</summary>
        public void ClearFloat()
        {
            ActiveFloat = null;
            if (floatLabel != null) floatLabel.gameObject.SetActive(false);
        }

        static void ShowSigned(TextMesh text, double value)
        {
            text.text = MoneyText.Signed(value);
            text.color = MoneyText.IsGainOrZero(value) ? PlayPalette.Gain : PlayPalette.Loss;
        }

        /// <summary>按当前语言写口袋上和旁边的名字。</summary>
        public void DrawLabels()
        {
            if (caption == null) return;
            PlayLanguage lang = CurrentLanguage;
            caption.text = PlayText.Get(PlayTextKey.Cash, lang);
            unrealizedLabel.text = PlayText.Get(PlayTextKey.UnrealizedPnl, lang);
            realizedLabel.text = PlayText.Get(PlayTextKey.RealizedPnl, lang);
        }

        void BuildIfNeeded()
        {
            if (cash != null) return;
            Rect panel = PlayLayout.Wallet;
            Rect pocket = PocketArea;
            shapes = new ShapeLayer(transform, "Wallet shapes", 0f, 10);
            shapes.Begin();
            shapes.Shapes.AddRect(panel, PlayPalette.PanelBackground);
            shapes.Shapes.AddFrame(panel, 0.02f, PlayPalette.PanelBorder);
            PocketShape.Add(shapes.Shapes, pocket);
            shapes.End();

            float middle = pocket.center.x;
            caption = WorldText.Create(transform, "Wallet caption", new Vector2(middle, pocket.yMax - 0.42f), CaptionSize, TextAnchor.MiddleCenter, PlayPalette.PocketStitch);
            cash = WorldText.Create(transform, "Wallet cash", new Vector2(middle, pocket.yMax - 0.80f), CashSize, TextAnchor.MiddleCenter, PlayPalette.Value);

            float left = pocket.xMax + 0.25f;
            float top = pocket.yMax - 0.15f;
            unrealizedLabel = WorldText.Create(transform, "Unrealized label", new Vector2(left, top), PnlLabelSize, TextAnchor.MiddleLeft, PlayPalette.Label);
            unrealizedValue = WorldText.Create(transform, "Unrealized value", new Vector2(left, top - 0.30f), PnlValueSize, TextAnchor.MiddleLeft, PlayPalette.Gain);
            realizedLabel = WorldText.Create(transform, "Realized label", new Vector2(left, top - 0.70f), PnlLabelSize, TextAnchor.MiddleLeft, PlayPalette.Label);
            realizedValue = WorldText.Create(transform, "Realized value", new Vector2(left, top - 1.00f), PnlValueSize, TextAnchor.MiddleLeft, PlayPalette.Gain);

            floatLabel = WorldText.Create(transform, "Floating amount", new Vector2(middle, PocketTop + FloatGap), FloatSize, TextAnchor.MiddleCenter, PlayPalette.Loss);
            floatLabel.gameObject.SetActive(false);
        }
    }
}
