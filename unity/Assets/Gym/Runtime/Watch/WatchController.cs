using System.Globalization;
using Gym.Core.Accounting;
using Gym.Runtime.Agents;
using Gym.Runtime.Configuration;
using Gym.Runtime.Play;
using Unity.MLAgents;
using UnityEngine;

namespace Gym.Runtime.Watch
{
    /// <summary>
    /// Watch scene：让训练好的模型来开试玩那套画面。Agent 在 scene 里默认不激活（只推理又没有模型时，ML-Agents
    /// 一启用就抛异常）；模型挂上以后，<see cref="StartWatching"/> 设好分段和费用、关掉自动步进，再激活 Agent。
    /// 之后按自己的时钟一步一步推，走法和评估包一样。
    ///
    /// P 暂停和继续；暂停时 N 走一步；[ 和 ] 在 <see cref="StepsPerSecondChoices"/> 里调慢、调快；R 回到段首重来；
    /// L（或左上角的按钮）切换语言，由 <see cref="PlayLanguageSwitch"/> 管。读数面板最下面写观战自己的按键和状态。
    /// 走到段尾就停住，画面定格在走完那一刻，提示按 R 重来。
    /// </summary>
    public class WatchController : MonoBehaviour, IReadoutControls
    {
        public static readonly float[] DefaultStepsPerSecond = { 1, 2, 5, 10, 20, 50, 100, 200, 500 };
        public const int DefaultSpeedIndex = 2;

        [SerializeField] TradingAgent agent;
        [SerializeField] PlayLanguageSwitch language;
        [SerializeField] HudView hud;
        [SerializeField] CandleChartView chart;
        [SerializeField] WalletView wallet;
        [SerializeField] AvatarView avatar;
        [SerializeField] WatchNotice notice;
        [Tooltip("Segment to watch. Keep the test segment for the final numbers; pick it only then.")]
        [SerializeField] EvaluationSegment segment = EvaluationSegment.Validation;
        [Tooltip("Fee per order as a share of the order (0.001 = 0.1 %).")]
        [SerializeField] double feeRate = CostModel.DefaultFeeRate;
        [Tooltip("Fixed fee per filled order, in USDT.")]
        [SerializeField] double fixedFee;
        [Tooltip("Price slippage against the trader (0.0005 = 5 bp).")]
        [SerializeField] double slippage;
        [Tooltip("The speeds, in steps per second, that the [ and ] keys move through, slowest first.")]
        [SerializeField] float[] stepsPerSecondChoices = (float[])DefaultStepsPerSecond.Clone();
        [Tooltip("Index in the speeds above to start at.")]
        [SerializeField] int startSpeedIndex = DefaultSpeedIndex;

        AgentWatchTarget target;

        public TradingAgent Agent { get => agent; set => agent = value; }
        public PlayLanguageSwitch Language { get => language; set => language = value; }
        public HudView Hud { get => hud; set => hud = value; }
        public CandleChartView Chart { get => chart; set => chart = value; }
        public WalletView Wallet { get => wallet; set => wallet = value; }
        public AvatarView Avatar { get => avatar; set => avatar = value; }
        public WatchNotice Notice { get => notice; set => notice = value; }
        public EvaluationSegment Segment { get => segment; set => segment = value; }
        public double FeeRate { get => feeRate; set => feeRate = value; }
        public double FixedFee { get => fixedFee; set => fixedFee = value; }
        public double Slippage { get => slippage; set => slippage = value; }
        public float[] StepsPerSecondChoices { get => stepsPerSecondChoices; set => stepsPerSecondChoices = value; }
        public int StartSpeedIndex { get => startSpeedIndex; set => startSpeedIndex = value; }

        /// <summary>开始观战以后才有；之前为 null。</summary>
        public WatchPlayback Playback { get; private set; }

        public bool IsWatching => Playback != null;

        /// <summary>为 true 时 Update 不拨播放的时钟，只有测试和截图工具拨它（按键照样处理）。</summary>
        public bool ManualClock { get; set; }

        void Awake()
        {
            if (hud != null) hud.Controls = this;
        }

        /// <summary>
        /// 模型已经挂在 Agent 上以后调用：把分段和费用交给 Agent、关掉自动步进，再激活 Agent。Agent 一启用就会
        /// 初始化并回到段首，各个画面跟着画出来。
        /// </summary>
        public void StartWatching()
        {
            if (IsWatching || agent == null) return;
            agent.SegmentOverride = segment;
            agent.CostOverride = new CostModel(feeRate, fixedFee, slippage);
            Academy.Instance.AutomaticSteppingEnabled = false;
            target = new AgentWatchTarget(agent);
            Playback = new WatchPlayback(target, stepsPerSecondChoices, startSpeedIndex);
            Playback.Changed += OnPlaybackChanged;
            Playback.FrozenChanged += SetViewsFrozen;
            agent.gameObject.SetActive(true);
            OnPlaybackChanged();
        }

        void Update()
        {
            if (!IsWatching) return;
            if (Input.GetKeyDown(KeyCode.P)) Playback.TogglePause();
            if (Input.GetKeyDown(KeyCode.N)) Playback.StepOnce();
            if (Input.GetKeyDown(KeyCode.RightBracket)) Playback.Faster();
            if (Input.GetKeyDown(KeyCode.LeftBracket)) Playback.Slower();
            if (Input.GetKeyDown(KeyCode.R)) Playback.Restart();
            if (!ManualClock) Playback.Advance(Time.unscaledDeltaTime);
        }

        void OnDestroy()
        {
            if (Playback != null)
            {
                Playback.Changed -= OnPlaybackChanged;
                Playback.FrozenChanged -= SetViewsFrozen;
                Playback.Dispose();
            }
            target?.Dispose();
            if (IsWatching && Academy.IsInitialized) Academy.Instance.AutomaticSteppingEnabled = true;
        }

        void SetViewsFrozen(bool frozen)
        {
            if (hud != null) hud.Frozen = frozen;
            if (chart != null) chart.Frozen = frozen;
            if (wallet != null) wallet.Frozen = frozen;
            if (avatar != null) avatar.Frozen = frozen;
        }

        void OnPlaybackChanged()
        {
            if (hud != null) hud.Draw();
            if (notice == null) return;
            if (Playback.Finished) notice.Show(WatchNoticeKind.EndOfSegment);
            else notice.Hide();
        }

        // ---- 读数面板最下面那几行（IReadoutControls）

        public string StatusLabel(PlayLanguage lang) =>
            PlayText.Format(PlayTextKey.WatchStatus, lang,
                PlayText.Get(segment == EvaluationSegment.Validation ? PlayTextKey.WatchSegmentValidation : PlayTextKey.WatchSegmentTest, lang));

        public string StatusValue(PlayLanguage lang)
        {
            if (Playback == null) return "-";
            if (Playback.Finished) return PlayText.Get(PlayTextKey.WatchFinished, lang);
            string speed = PlayText.Format(PlayTextKey.WatchSpeed, lang, Playback.StepsPerSecond.ToString("0.##", CultureInfo.InvariantCulture));
            return Playback.Paused ? speed + "  " + PlayText.Get(PlayTextKey.WatchPaused, lang) : speed;
        }

        public string KeysFirstLine(PlayLanguage lang) => PlayText.Get(PlayTextKey.WatchKeysFirst, lang);

        public string KeysSecondLine(PlayLanguage lang) => PlayText.Get(PlayTextKey.WatchKeysSecond, lang);
    }
}
