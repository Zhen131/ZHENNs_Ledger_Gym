using Gym.Core.Env;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using UnityEngine;

namespace Gym.Runtime
{
    /// <summary>
    /// Keyboard play: one key press moves one candle. Automatic Academy stepping is
    /// switched off; every action key calls <see cref="Academy.EnvironmentStep"/> once.
    ///
    /// 1 / 2 / 3 / 4 pick 10 % / 25 % / 50 % / 100 % (no step). B buys, S sells,
    /// H or Space holds (each one step). P toggles auto-play (5 holds per second).
    /// R restarts the episode.
    /// </summary>
    public class PlayController : MonoBehaviour, IActionSource
    {
        public static readonly double[] FractionChoices = { 0.10, 0.25, 0.50, 1.00 };

        [SerializeField] TradingAgent agent;
        [SerializeField] float autoPlayStepsPerSecond = 5f;
        [SerializeField] int selectedIndex = 1;

        int pendingBranch = (int)TradeAction.Hold;
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

        public void PressBuy(float fraction) => Act((int)TradeAction.Buy, fraction);
        public void PressSell(float fraction) => Act((int)TradeAction.Sell, fraction);
        public void PressHold() => Act((int)TradeAction.Hold, (float)SelectedFraction);

        /// <summary>Back to playStart, all cash.</summary>
        public void Restart()
        {
            if (agent != null) agent.EpisodeInterrupted();
        }

        void Act(int branch, float fraction)
        {
            pendingBranch = branch;
            pendingContinuous = ActionCodec.FromFraction(fraction);
            try
            {
                Academy.Instance.EnvironmentStep();
                StepsTaken++;
            }
            finally
            {
                pendingBranch = (int)TradeAction.Hold;
            }
        }

        public void FillActions(in ActionBuffers actionsOut)
        {
            ActionSegment<int> discrete = actionsOut.DiscreteActions;
            ActionSegment<float> continuous = actionsOut.ContinuousActions;
            discrete[0] = pendingBranch;
            continuous[0] = pendingContinuous;
        }
    }
}
