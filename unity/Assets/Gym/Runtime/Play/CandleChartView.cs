using System;
using System.Collections.Generic;
using Gym.Core.Env;
using Gym.Core.Market;
using Gym.Runtime.Agents;
using UnityEngine;

namespace Gym.Runtime.Play
{
    /// <summary>
    /// 把截止到当前这根的最后 64 根 candle 画成一个按顶点着色的 mesh：实体和影线，涨为绿、跌为红；
    /// 在订单成交的那根 candle 上，买入在下方画 ▲，卖出在上方画 ▼。
    /// 底下垫着价格刻度的横线（右边标价格）和每个 UTC 0 点的竖线（下面标「月-日」），可见范围变了就重算。
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class CandleChartView : MonoBehaviour
    {
        public const int VisibleCandles = 64;
        public const int DefaultPriceLines = 5;

        const float LabelSize = 0.17f;
        const float GridLineThickness = 0.012f;
        /// <summary>价格数字离图表右边、日期离图表下边的距离。</summary>
        const float LabelGap = 0.10f;

        [SerializeField] TradingAgent agent;
        [SerializeField] float width = 16f;
        [SerializeField] float height = 6.5f;
        [SerializeField] Color upColor = new Color(0.20f, 0.78f, 0.40f);
        [SerializeField] Color downColor = new Color(0.90f, 0.28f, 0.28f);
        [SerializeField] Color buyColor = new Color(0.30f, 0.65f, 1.00f);
        [SerializeField] Color sellColor = new Color(1.00f, 0.75f, 0.20f);
        [Tooltip("How many price lines to aim for; the 1-2-5 steps make the real count vary around it.")]
        [SerializeField] int desiredPriceLines = DefaultPriceLines;

        readonly ColoredMeshBuilder shapes = new ColoredMeshBuilder();
        readonly List<TextMesh> priceLabels = new List<TextMesh>();
        readonly List<TextMesh> timeLabels = new List<TextMesh>();
        readonly List<string> drawnPriceLabels = new List<string>();
        readonly List<DateTime> visibleTimes = new List<DateTime>();
        Mesh mesh;

        public TradingAgent Agent
        {
            get => agent;
            set => agent = value;
        }

        public float Width
        {
            get => width;
            set => width = value;
        }

        public float Height
        {
            get => height;
            set => height = value;
        }

        public int DesiredPriceLines
        {
            get => desiredPriceLines;
            set => desiredPriceLines = value;
        }

        public int DrawnCandles { get; private set; }
        public int DrawnMarkers { get; private set; }

        /// <summary>纵轴的上下限（可见 candle 的最低、最高价各留了一点边距）。</summary>
        public double VisibleLow { get; private set; }
        public double VisibleHigh { get; private set; }
        /// <summary>最旧那根可见 candle 的下标。</summary>
        public int FirstVisibleIndex { get; private set; }

        public PriceTicks DrawnPriceTicks { get; private set; }
        public IReadOnlyList<string> DrawnPriceLabels => drawnPriceLabels;
        public IReadOnlyList<TimeLabel> DrawnTimeLabels { get; private set; } = Array.Empty<TimeLabel>();

        /// <summary>买入 ▲、卖出 ▼ 的颜色；小人的提示字和边框跟着用。</summary>
        public Color BuyColor => buyColor;
        public Color SellColor => sellColor;

        /// <summary>图表在世界坐标里占的方框（图表不旋转、不缩放）。</summary>
        public Rect WorldArea => new Rect(transform.position.x - width / 2, transform.position.y - height / 2, width, height);

        /// <summary>
        /// <paramref name="env"/> 当前这根 candle 的最高点在世界坐标里的位置，小人站在它上方。可见范围按 env 自己算，
        /// 和 <see cref="Draw"/> 是同一套算法，所以不管这一帧图表先重画了没有，结果都和画出来的一致。
        /// </summary>
        public Vector3 LatestHighPosition(TradingEnv env)
        {
            int last = env.CurrentIndex;
            int first = Math.Max(0, last - VisibleCandles + 1);
            (double low, double high) = PriceRange(env.Series, first, last);
            return transform.TransformPoint(new Vector3(CandleX(last - first), PriceToY(env.Series[last].High, low, high)));
        }

        void Awake() => EnsureMesh();

        void OnEnable()
        {
            if (agent == null) return;
            agent.EpisodeStarted += Redraw;
            agent.Stepped += Redraw;
        }

        void OnDisable()
        {
            if (agent == null) return;
            agent.EpisodeStarted -= Redraw;
            agent.Stepped -= Redraw;
        }

        void Start() => Redraw(agent);

        void EnsureMesh()
        {
            if (mesh != null) return;
            mesh = new Mesh { name = "Candles" };
            mesh.MarkDynamic();
            GetComponent<MeshFilter>().sharedMesh = mesh;
        }

        public void Redraw(TradingAgent source) => Draw(source != null ? source.Env : null);

        /// <summary>直接按一个环境画图（editor 的快照工具也用它）。</summary>
        public void Draw(TradingEnv env)
        {
            if (env == null || env.Account == null) return;
            EnsureMesh();
            CandleSeries series = env.Series;
            int last = env.CurrentIndex;
            int first = Math.Max(0, last - VisibleCandles + 1);
            (double low, double high) = PriceRange(series, first, last);
            FirstVisibleIndex = first;
            VisibleLow = low;
            VisibleHigh = high;
            DrawnPriceTicks = PriceScale.Compute(low, high, desiredPriceLines);
            DrawnTimeLabels = TimeAxis.Pick(VisibleTimes(series, first, last));

            shapes.Clear();
            AddPriceLines(low, high);
            AddMidnightLines();
            AddCandles(series, first, last, low, high);
            DrawnCandles = last - first + 1;
            AddTradeMarkers(env, first, last, low, high);
            shapes.Upload(mesh);
            PlacePriceLabels(low, high);
            PlaceTimeLabels();
        }

        float SlotWidth => width / VisibleCandles;
        float LeftEdge => -width / 2;

        /// <summary>可见 candle 里最低的 low 和最高的 high，上下各留一点边距。</summary>
        static (double low, double high) PriceRange(CandleSeries series, int first, int last)
        {
            double low = double.MaxValue, high = double.MinValue;
            for (int i = first; i <= last; i++)
            {
                low = Math.Min(low, series[i].Low);
                high = Math.Max(high, series[i].High);
            }
            double pad = Math.Max((high - low) * 0.08, high * 1e-4);
            low -= pad;
            high += pad;
            return (low, high);
        }

        List<DateTime> VisibleTimes(CandleSeries series, int first, int last)
        {
            visibleTimes.Clear();
            for (int i = first; i <= last; i++) visibleTimes.Add(series.OpenTimeUtc(i));
            return visibleTimes;
        }

        float PriceToY(double price, double low, double high) => (float)((price - low) / (high - low) * height - height / 2);

        float CandleX(int offset) => LeftEdge + (offset + 0.5f) * SlotWidth;

        /// <summary>价格刻度的横线，先画，这样 candle 压在线上面。</summary>
        void AddPriceLines(double low, double high)
        {
            float half = GridLineThickness / 2;
            foreach (double price in DrawnPriceTicks.Values)
            {
                float y = PriceToY(price, low, high);
                shapes.AddQuad(LeftEdge, y - half, -LeftEdge, y + half, PlayPalette.GridLine);
            }
        }

        /// <summary>每个 UTC 0 点一条竖线，对着下面的日期。</summary>
        void AddMidnightLines()
        {
            float half = GridLineThickness / 2;
            foreach (TimeLabel label in DrawnTimeLabels)
            {
                float x = CandleX(label.Offset);
                shapes.AddQuad(x - half, -height / 2, x + half, height / 2, PlayPalette.GridLine);
            }
        }

        void AddCandles(CandleSeries series, int first, int last, double low, double high)
        {
            float slot = SlotWidth;
            for (int i = first; i <= last; i++)
            {
                Candle c = series[i];
                float x = CandleX(i - first);
                Color color = c.Close >= c.Open ? upColor : downColor;
                float top = PriceToY(Math.Max(c.Open, c.Close), low, high);
                float bottom = PriceToY(Math.Min(c.Open, c.Close), low, high);
                if (top - bottom < 0.02f) top = bottom + 0.02f;
                shapes.AddQuad(x - slot * 0.06f, PriceToY(c.Low, low, high), x + slot * 0.06f, PriceToY(c.High, low, high), color);
                shapes.AddQuad(x - slot * 0.32f, bottom, x + slot * 0.32f, top, color);
            }
        }

        /// <summary>每笔可见的买入在下方画 ▲，每笔可见的卖出在上方画 ▼；个数记在 DrawnMarkers 里。</summary>
        void AddTradeMarkers(TradingEnv env, int first, int last, double low, double high)
        {
            float slot = SlotWidth;
            CandleSeries series = env.Series;
            DrawnMarkers = 0;
            foreach (TradeRecord trade in env.Trades)
            {
                if (trade.CandleIndex < first || trade.CandleIndex > last) continue;
                float x = CandleX(trade.CandleIndex - first);
                float size = slot * 0.45f;
                if (trade.Side == TradeAction.Buy)
                {
                    float tip = PriceToY(series[trade.CandleIndex].Low, low, high) - 0.08f;
                    shapes.AddTriangle(new Vector3(x, tip), new Vector3(x - size, tip - size * 1.4f), new Vector3(x + size, tip - size * 1.4f), buyColor);
                }
                else
                {
                    float tip = PriceToY(series[trade.CandleIndex].High, low, high) + 0.08f;
                    shapes.AddTriangle(new Vector3(x, tip), new Vector3(x + size, tip + size * 1.4f), new Vector3(x - size, tip + size * 1.4f), sellColor);
                }
                DrawnMarkers++;
            }
        }

        /// <summary>每条横线的右边写价格，带千分位。</summary>
        void PlacePriceLabels(double low, double high)
        {
            IReadOnlyList<double> prices = DrawnPriceTicks.Values;
            drawnPriceLabels.Clear();
            for (int i = 0; i < prices.Count; i++)
            {
                string text = PriceScale.Label(prices[i], DrawnPriceTicks.Step);
                drawnPriceLabels.Add(text);
                Vector3 at = transform.TransformPoint(new Vector3(width / 2 + LabelGap, PriceToY(prices[i], low, high)));
                ShowLabel(priceLabels, i, "Price label", at, TextAnchor.MiddleLeft, text);
            }
            HideFrom(priceLabels, prices.Count);
        }

        /// <summary>每个 UTC 0 点的 candle 下面写「月-日」。</summary>
        void PlaceTimeLabels()
        {
            for (int i = 0; i < DrawnTimeLabels.Count; i++)
            {
                TimeLabel label = DrawnTimeLabels[i];
                Vector3 at = transform.TransformPoint(new Vector3(CandleX(label.Offset), -height / 2 - LabelGap));
                ShowLabel(timeLabels, i, "Time label", at, TextAnchor.UpperCenter, label.Text);
            }
            HideFrom(timeLabels, DrawnTimeLabels.Count);
        }

        /// <summary>标签按需要建，多出来的藏起来不删，下次重画接着用。</summary>
        void ShowLabel(List<TextMesh> pool, int index, string name, Vector3 at, TextAnchor anchor, string text)
        {
            if (index == pool.Count)
                pool.Add(WorldText.Create(transform, $"{name} {index}", at, LabelSize, anchor, PlayPalette.AxisText));
            TextMesh label = pool[index];
            label.gameObject.SetActive(true);
            WorldText.Move(label, at);
            label.text = text;
        }

        static void HideFrom(List<TextMesh> pool, int count)
        {
            for (int i = count; i < pool.Count; i++) pool[i].gameObject.SetActive(false);
        }
    }
}
