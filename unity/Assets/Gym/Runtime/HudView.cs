using System;
using System.Globalization;
using System.Text;
using Gym.Core.Accounting;
using Gym.Core.Env;
using UnityEngine;

namespace Gym.Runtime
{
    /// <summary>The numbers the HUD shows, captured right after each step.</summary>
    public struct HudSnapshot
    {
        public bool Ready;
        public DateTime TimeUtc;
        public double Close;
        public double Cash;
        public long CoinUnits;
        public double Quantity;
        public double PositionRatio;
        public double Equity;
        public double Return;
        public double FeesPaid;
        public int Trades;
        public int Rejected;
        public string LastAction;
        public double FeeRate;
        public double FixedFee;
        public double Slippage;
        public int Step;
    }

    /// <summary>IMGUI read-out for the Play scene.</summary>
    public class HudView : MonoBehaviour
    {
        [SerializeField] TradingAgent agent;
        [SerializeField] PlayController controller;

        GUIStyle style;
        readonly StringBuilder text = new StringBuilder();

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

        public HudSnapshot Shown { get; private set; }

        void OnEnable()
        {
            if (agent == null) return;
            agent.EpisodeStarted += Refresh;
            agent.Stepped += Refresh;
            Refresh(agent);
        }

        void OnDisable()
        {
            if (agent == null) return;
            agent.EpisodeStarted -= Refresh;
            agent.Stepped -= Refresh;
        }

        void Start() => Refresh(agent);

        public void Refresh(TradingAgent source)
        {
            TradingEnv env = source != null ? source.Env : null;
            if (env == null || env.Account == null) return;
            Account a = env.Account;
            double close = env.CurrentClose;
            double initial = env.EquityCurve[0];
            Shown = new HudSnapshot
            {
                Ready = true,
                TimeUtc = env.Series.OpenTimeUtc(env.T),
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
                LastAction = DescribeLastAction(source),
                FeeRate = env.Cost.FeeRate,
                FixedFee = env.Cost.FixedFee,
                Slippage = env.Cost.Slippage,
                Step = env.StepCount,
            };
        }

        static string DescribeLastAction(TradingAgent source)
        {
            if (!source.HasStepped) return "-";
            StepResult r = source.LastResult;
            string pct = (ActionCodec.Fraction(source.LastContinuous) * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%";
            switch (source.LastBranch)
            {
                case (int)TradeAction.Buy: return r.Traded ? $"BUY {pct}: filled" : $"BUY {pct}: REJECTED";
                case (int)TradeAction.Sell: return r.Traded ? $"SELL {pct}: filled" : $"SELL {pct}: REJECTED";
                default: return "HOLD";
            }
        }

        void OnGUI()
        {
            HudSnapshot s = Shown;
            if (!s.Ready) return;
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.box)
                {
                    alignment = TextAnchor.UpperLeft,
                    fontSize = 14,
                    richText = false,
                    padding = new RectOffset(10, 10, 8, 8),
                };
            }

            CultureInfo inv = CultureInfo.InvariantCulture;
            text.Clear();
            text.AppendLine($"Time (UTC)     {s.TimeUtc.ToString("yyyy-MM-dd HH:mm", inv)}   step {s.Step}");
            text.AppendLine($"Close          {s.Close.ToString("F2", inv)}");
            text.AppendLine($"Cash           {s.Cash.ToString("F4", inv)} USDT");
            text.AppendLine($"Coin           {s.Quantity.ToString("F5", inv)} BTC  ({s.CoinUnits} units)");
            text.AppendLine($"Position       {(s.PositionRatio * 100).ToString("F2", inv)} %");
            text.AppendLine($"Equity         {s.Equity.ToString("F4", inv)} USDT");
            text.AppendLine($"Return         {(s.Return * 100).ToString("F4", inv)} %");
            text.AppendLine($"Fees paid      {s.FeesPaid.ToString("F4", inv)} USDT");
            text.AppendLine($"Trades         {s.Trades}    Rejected  {s.Rejected}");
            text.AppendLine($"Last action    {s.LastAction}");
            string selected = controller != null ? (controller.SelectedFraction * 100).ToString("0", inv) + " %" : "-";
            string auto = controller != null && controller.AutoPlay ? "   AUTO" : "";
            text.AppendLine($"Fraction       {selected}{auto}");
            text.AppendLine($"Fee rate       {(s.FeeRate * 100).ToString("0.###", inv)} %   fixed {s.FixedFee.ToString("0.##", inv)}   slip {(s.Slippage * 10000).ToString("0.#", inv)} bp");
            text.Append("Keys  1-4 fraction   B buy   S sell   H/Space hold   P auto   R restart");
            GUI.Box(new Rect(10, 10, 560, 250), text.ToString(), style);
        }
    }
}
