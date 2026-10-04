using Gym.Core.Accounting;
using Gym.Core.Env;
using Gym.Runtime.Agents;
using UnityEngine;

namespace Gym.Runtime.Play
{
    /// <summary>
    /// Play scene 左上角的读数面板：每个 step 之后抓一份 <see cref="HudSnapshot"/>，按当前语言画成
    /// 「名字　值」两列，最下面是按键提示。字用 TextMesh 画，命令行截图截得到。
    /// </summary>
    public class HudView : MonoBehaviour
    {
        const float TextSize = 0.19f;
        const float RowPitch = 0.30f;
        /// <summary>值那一列离名字那一列的左边多远；要放得下最长的英文名字 "Fee rate / fixed / slip"。</summary>
        const float ValueColumnOffset = 2.55f;
        /// <summary>按键提示从名字那一列往右缩进多少，让出「按键」两个字。</summary>
        const float KeysIndent = 0.80f;

        [SerializeField] TradingAgent agent;
        [SerializeField] PlayController controller;
        [SerializeField] PlayLanguageSwitch language;

        ShapeLayer background;
        TextMesh[] labels;
        TextMesh[] values;
        TextMesh keysLabel;
        TextMesh keysTrade;
        TextMesh keysOther;
        double shownFraction = -1;
        bool shownAutoPlay;

        public TradingAgent Agent
        {
            get => agent;
            set => agent = value;
        }

        public PlayController Controller
        {
            get => controller;
            set => controller = value;
        }

        public PlayLanguageSwitch Language
        {
            get => language;
            set => language = value;
        }

        public HudSnapshot Shown { get; private set; }

        IReadoutControls controls;
        bool frozen;

        /// <summary>
        /// 换掉面板最下面的按键提示和「下单比例」那一行（观战这类场景用，写自己的按键和状态）。
        /// 为 null 时（键盘试玩）面板照原样画。
        /// </summary>
        public IReadoutControls Controls
        {
            get => controls;
            set
            {
                controls = value;
                Draw();
            }
        }

        /// <summary>
        /// 定格：为 true 时不理会 Agent 的「一局开始」，面板停在打开它那一刻的数字（观战走到段尾时用，默认关）。
        /// 走到段尾时环境在同一步里就回到了段首，不定格的话数字会马上刷回段首。只在组件启用时切换才有效。
        /// </summary>
        public bool Frozen
        {
            get => frozen;
            set
            {
                if (frozen == value) return;
                frozen = value;
                if (agent == null || !isActiveAndEnabled) return;
                agent.EpisodeStarted -= Refresh;
                if (!frozen) agent.EpisodeStarted += Refresh;
            }
        }

        PlayLanguage CurrentLanguage => language != null ? language.Current : PlayLanguage.Chinese;

        void OnEnable()
        {
            if (language != null) language.Changed += OnLanguageChanged;
            if (agent == null) return;
            agent.EpisodeStarted += Refresh;
            agent.Stepped += Refresh;
            Refresh(agent);
        }

        void OnDisable()
        {
            if (language != null) language.Changed -= OnLanguageChanged;
            if (agent == null) return;
            agent.EpisodeStarted -= Refresh;
            agent.Stepped -= Refresh;
        }

        void Start() => Refresh(agent);

        /// <summary>选比例（1–4）和开关自动播放（P）不走 step，所以每帧看一眼要不要重画那一行。</summary>
        void Update()
        {
            if (controller == null || labels == null) return;
            if (controller.SelectedFraction != shownFraction || controller.AutoPlay != shownAutoPlay) Draw();
        }

        void OnLanguageChanged(PlayLanguage _) => Draw();

        public void Refresh(TradingAgent source)
        {
            TradingEnv env = source != null ? source.Env : null;
            if (env == null || env.Account == null) return;
            Show(Capture(env, source.HasStepped, source.LastAction, ActionCodec.Fraction(source.LastContinuous), source.LastResult.Traded));
        }

        /// <summary>按一个环境和它的上一步抓一份读数（editor 的截图工具也用它）。</summary>
        public static HudSnapshot Capture(TradingEnv env, bool hasStepped, TradeAction lastSide, double lastFraction, bool lastFilled)
        {
            Account a = env.Account;
            double close = env.CurrentClose;
            double initial = env.EquityCurve[0];
            return new HudSnapshot
            {
                Ready = true,
                TimeUtc = env.Series.OpenTimeUtc(env.CurrentIndex),
                Close = close,
                Cash = a.Cash,
                CoinUnits = a.CoinUnits,
                Quantity = a.Quantity,
                PositionRatio = a.PositionRatio(close),
                Equity = a.Equity(close),
                Return = a.Equity(close) / initial - 1,
                FeesPaid = a.FeesPaid,
                Trades = a.Trades,
                Rejected = a.Rejected,
                LastAction = DescribeLastAction(hasStepped, lastSide, lastFraction, lastFilled),
                FeeRate = env.Cost.FeeRate,
                FixedFee = env.Cost.FixedFee,
                Slippage = env.Cost.Slippage,
                Step = env.StepCount,
                HasStepped = hasStepped,
                LastSide = lastSide,
                LastFraction = lastFraction,
                LastFilled = lastFilled,
                RealizedPnl = a.RealizedPnl,
                UnrealizedPnl = a.UnrealizedPnl(close),
            };
        }

        static string DescribeLastAction(bool hasStepped, TradeAction side, double fraction, bool filled)
        {
            if (!hasStepped) return "-";
            string pct = HudReadout.Percent(fraction);
            switch (side)
            {
                case TradeAction.Buy: return filled ? $"BUY {pct}: filled" : $"BUY {pct}: REJECTED";
                case TradeAction.Sell: return filled ? $"SELL {pct}: filled" : $"SELL {pct}: REJECTED";
                default: return "HOLD";
            }
        }

        public void Show(HudSnapshot snapshot)
        {
            Shown = snapshot;
            Draw();
        }

        /// <summary>按当前语言把 <see cref="Shown"/> 画出来；还没有读数时什么都不画。</summary>
        public void Draw()
        {
            if (!Shown.Ready) return;
            BuildIfNeeded();
            PlayLanguage lang = CurrentLanguage;
            double? fraction = controller != null ? controller.SelectedFraction : (double?)null;
            bool autoPlay = controller != null && controller.AutoPlay;
            for (int i = 0; i < HudReadout.Rows.Length; i++)
            {
                labels[i].text = HudReadout.Label(HudReadout.Rows[i], lang);
                values[i].text = HudReadout.Value(HudReadout.Rows[i], Shown, lang, fraction, autoPlay);
            }
            keysLabel.text = PlayText.Get(PlayTextKey.Keys, lang);
            keysTrade.text = PlayText.Get(PlayTextKey.KeysTrade, lang);
            keysOther.text = PlayText.Get(PlayTextKey.KeysOther, lang);
            shownFraction = fraction ?? -1;
            shownAutoPlay = autoPlay;
            if (controls != null) DrawControls(lang);
        }

        /// <summary>用 <see cref="Controls"/> 给的字盖掉「下单比例」那一行和两行按键提示。</summary>
        void DrawControls(PlayLanguage lang)
        {
            int row = System.Array.IndexOf(HudReadout.Rows, PlayTextKey.Fraction);
            labels[row].text = controls.StatusLabel(lang);
            values[row].text = controls.StatusValue(lang);
            keysTrade.text = controls.KeysFirstLine(lang);
            keysOther.text = controls.KeysSecondLine(lang);
        }

        void BuildIfNeeded()
        {
            if (labels != null) return;
            Rect area = PlayLayout.Readout;
            background = new ShapeLayer(transform, "Readout background", 0f, 10);
            background.Begin();
            background.Shapes.AddRect(area, PlayPalette.PanelBackground);
            background.Shapes.AddFrame(area, 0.02f, PlayPalette.PanelBorder);
            background.End();

            float left = area.xMin + PlayLayout.Padding;
            float top = area.yMax - PlayLayout.Padding - TextSize / 2;
            int rows = HudReadout.Rows.Length;
            labels = new TextMesh[rows];
            values = new TextMesh[rows];
            for (int i = 0; i < rows; i++)
            {
                float y = top - i * RowPitch;
                labels[i] = WorldText.Create(transform, $"Label {HudReadout.Rows[i]}", new Vector2(left, y), TextSize, TextAnchor.MiddleLeft, PlayPalette.Label);
                values[i] = WorldText.Create(transform, $"Value {HudReadout.Rows[i]}", new Vector2(left + ValueColumnOffset, y), TextSize, TextAnchor.MiddleLeft, PlayPalette.Value);
            }
            float keysY = top - rows * RowPitch;
            keysLabel = WorldText.Create(transform, "Keys label", new Vector2(left, keysY), TextSize, TextAnchor.MiddleLeft, PlayPalette.Label);
            keysTrade = WorldText.Create(transform, "Keys trade", new Vector2(left + KeysIndent, keysY), TextSize, TextAnchor.MiddleLeft, PlayPalette.Label);
            keysOther = WorldText.Create(transform, "Keys other", new Vector2(left + KeysIndent, keysY - RowPitch), TextSize, TextAnchor.MiddleLeft, PlayPalette.Label);
        }
    }
}
