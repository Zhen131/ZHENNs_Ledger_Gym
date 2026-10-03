using System;
using System.Collections.Generic;
using Gym.Core.Env;
using Gym.Core.Market;
using Gym.Runtime.Agents;
using UnityEngine;

namespace Gym.Runtime.Play
{
    /// <summary>
    /// Draws the last 64 candles ending at the current one as a single vertex-coloured
    /// mesh: bodies and wicks, green up and red down, with ▲ under a buy and ▼ over a
    /// sell on the candle where the order filled.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class CandleChartView : MonoBehaviour
    {
        public const int VisibleCandles = 64;

        [SerializeField] TradingAgent agent;
        [SerializeField] float width = 16f;
        [SerializeField] float height = 6.5f;
        [SerializeField] Color upColor = new Color(0.20f, 0.78f, 0.40f);
        [SerializeField] Color downColor = new Color(0.90f, 0.28f, 0.28f);
        [SerializeField] Color buyColor = new Color(0.30f, 0.65f, 1.00f);
        [SerializeField] Color sellColor = new Color(1.00f, 0.75f, 0.20f);

        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<Color> colors = new List<Color>();
        readonly List<int> triangles = new List<int>();
        Mesh mesh;

        public TradingAgent Agent
        {
            get => agent;
            set => agent = value;
        }

        public int DrawnCandles { get; private set; }
        public int DrawnMarkers { get; private set; }

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

        /// <summary>Draw straight from an environment (also used by the editor snapshot tool).</summary>
        public void Draw(TradingEnv env)
        {
            if (env == null || env.Account == null) return;
            EnsureMesh();
            CandleSeries series = env.Series;
            int last = env.CurrentIndex;
            int first = Math.Max(0, last - VisibleCandles + 1);
            (double low, double high) = PriceRange(series, first, last);

            vertices.Clear();
            colors.Clear();
            triangles.Clear();
            AddCandles(series, first, last, low, high);
            DrawnCandles = last - first + 1;
            AddTradeMarkers(env, first, last, low, high);
            UploadMesh();
        }

        float SlotWidth => width / VisibleCandles;
        float LeftEdge => -width / 2;

        /// <summary>The lowest low and highest high of the visible candles, with a margin above and below.</summary>
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

        float PriceToY(double price, double low, double high) => (float)((price - low) / (high - low) * height - height / 2);

        void AddCandles(CandleSeries series, int first, int last, double low, double high)
        {
            float slot = SlotWidth;
            float left = LeftEdge;
            for (int i = first; i <= last; i++)
            {
                Candle c = series[i];
                float x = left + (i - first + 0.5f) * slot;
                Color color = c.Close >= c.Open ? upColor : downColor;
                float top = PriceToY(Math.Max(c.Open, c.Close), low, high);
                float bottom = PriceToY(Math.Min(c.Open, c.Close), low, high);
                if (top - bottom < 0.02f) top = bottom + 0.02f;
                AddQuad(x - slot * 0.06f, PriceToY(c.Low, low, high), x + slot * 0.06f, PriceToY(c.High, low, high), color);
                AddQuad(x - slot * 0.32f, bottom, x + slot * 0.32f, top, color);
            }
        }

        /// <summary>▲ under each visible buy and ▼ over each visible sell; counts them in DrawnMarkers.</summary>
        void AddTradeMarkers(TradingEnv env, int first, int last, double low, double high)
        {
            float slot = SlotWidth;
            float left = LeftEdge;
            CandleSeries series = env.Series;
            DrawnMarkers = 0;
            foreach (TradeRecord trade in env.Trades)
            {
                if (trade.CandleIndex < first || trade.CandleIndex > last) continue;
                float x = left + (trade.CandleIndex - first + 0.5f) * slot;
                float size = slot * 0.45f;
                if (trade.Side == TradeAction.Buy)
                {
                    float tip = PriceToY(series[trade.CandleIndex].Low, low, high) - 0.08f;
                    AddTriangle(new Vector3(x, tip), new Vector3(x - size, tip - size * 1.4f), new Vector3(x + size, tip - size * 1.4f), buyColor);
                }
                else
                {
                    float tip = PriceToY(series[trade.CandleIndex].High, low, high) + 0.08f;
                    AddTriangle(new Vector3(x, tip), new Vector3(x + size, tip + size * 1.4f), new Vector3(x - size, tip + size * 1.4f), sellColor);
                }
                DrawnMarkers++;
            }
        }

        void UploadMesh()
        {
            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
        }

        void AddQuad(float x0, float y0, float x1, float y1, Color color)
        {
            int start = vertices.Count;
            vertices.Add(new Vector3(x0, y0));
            vertices.Add(new Vector3(x0, y1));
            vertices.Add(new Vector3(x1, y1));
            vertices.Add(new Vector3(x1, y0));
            for (int k = 0; k < 4; k++) colors.Add(color);
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
            triangles.Add(start);
            triangles.Add(start + 2);
            triangles.Add(start + 3);
        }

        void AddTriangle(Vector3 corner1, Vector3 corner2, Vector3 corner3, Color color)
        {
            int start = vertices.Count;
            vertices.Add(corner1);
            vertices.Add(corner2);
            vertices.Add(corner3);
            for (int k = 0; k < 3; k++) colors.Add(color);
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
        }
    }
}
