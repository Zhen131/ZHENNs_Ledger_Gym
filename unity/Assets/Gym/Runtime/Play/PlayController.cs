using Gym.Core.Env;
using Gym.Runtime.Agents;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using UnityEngine;

namespace Gym.Runtime.Play
{
    /// <summary>
    /// 键盘试玩：按一次键走一根 candle。Academy 的自动 step 关掉了；每个 action 键调用一次
    /// <see cref="Academy.EnvironmentStep"/>。
    ///
    /// 1 / 2 / 3 / 4 选 10 % / 25 % / 50 % / 100 %（不走 step）。B 买入，S 卖出，H 或 Space 不动
    /// （各走一个 step）。P 开关自动播放（每秒 5 次不动）。R 重新开始 episode。
    /// </summary>
    public class PlayController : MonoBehaviour, IActionSource
    {
        public static readonly double[] FractionChoices = { 0.10, 0.25, 0.50, 1.00 };

        [SerializeField] TradingAgent agent;
        [SerializeField] float autoPlayStepsPerSecond = 5f;
        [SerializeField] int selectedIndex = 1;

        TradeAction pendingAction = TradeAction.Hold;
        float pendingContinuous;
        float autoPlayTimer;

        public TradingAgent Agent
        {
            get => agent;
            set => agent = value;
        }

        public double SelectedFraction => FractionChoices[selectedIndex];
        public bool AutoPlay { get; private set; }
        public int StepsTaken { get; private set; }

        void Awake() => Attach();
        void OnEnable() => Attach();

        void OnDisable()
        {
            if (agent != null && ReferenceEquals(agent.ActionSource, this)) agent.ActionSource = null;
            if (Academy.IsInitialized) Academy.Instance.AutomaticSteppingEnabled = true;
        }

        void Attach()
        {
            if (agent != null) agent.ActionSource = this;
            Academy.Instance.AutomaticSteppingEnabled = false;
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) SelectFraction(0);
            if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) SelectFraction(1);
            if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3)) SelectFraction(2);
            if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4)) SelectFraction(3);

            if (Input.GetKeyDown(KeyCode.B)) PressBuy((float)SelectedFraction);
            else if (Input.GetKeyDown(KeyCode.S)) PressSell((float)SelectedFraction);
            else if (Input.GetKeyDown(KeyCode.H) || Input.GetKeyDown(KeyCode.Space)) PressHold();

            if (Input.GetKeyDown(KeyCode.P)) AutoPlay = !AutoPlay;
            if (Input.GetKeyDown(KeyCode.R)) Restart();

            if (!AutoPlay)
            {
                autoPlayTimer = 0;
                return;
            }
            autoPlayTimer += Time.unscaledDeltaTime;
            float period = 1f / Mathf.Max(autoPlayStepsPerSecond, 0.1f);
            while (autoPlayTimer >= period)
            {
                autoPlayTimer -= period;
                PressHold();
            }
        }

        public void SelectFraction(int index) => selectedIndex = Mathf.Clamp(index, 0, FractionChoices.Length - 1);

        public void PressBuy(float fraction) => Act(TradeAction.Buy, fraction);
        public void PressSell(float fraction) => Act(TradeAction.Sell, fraction);
        public void PressHold() => Act(TradeAction.Hold, (float)SelectedFraction);

        /// <summary>回到 playStart，全是现金。</summary>
        public void Restart()
        {
            if (agent != null) agent.EpisodeInterrupted();
        }

        void Act(TradeAction action, float fraction)
        {
            pendingAction = action;
            pendingContinuous = ActionCodec.FromFraction(fraction);
            try
            {
                Academy.Instance.EnvironmentStep();
                StepsTaken++;
            }
            finally
            {
                pendingAction = TradeAction.Hold;
            }
        }

        public void FillActions(in ActionBuffers actionsOut)
        {
            ActionSegment<int> discrete = actionsOut.DiscreteActions;
            ActionSegment<float> continuous = actionsOut.ContinuousActions;
            discrete[0] = (int)pendingAction;
            continuous[0] = pendingContinuous;
        }
    }
}
