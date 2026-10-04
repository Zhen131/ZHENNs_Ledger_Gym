using UnityEngine;

namespace Gym.Runtime.Play
{
    /// <summary>
    /// Play scene 新画面的颜色都在这里。钱少了、亏损用红，钱多了、盈利用绿；K 线和买卖三角的颜色在
    /// <see cref="CandleChartView"/> 上，不在这里。
    /// </summary>
    public static class PlayPalette
    {
        public static readonly Color PanelBackground = new Color(0.12f, 0.14f, 0.18f, 0.92f);
        public static readonly Color PanelBorder = new Color(0.30f, 0.34f, 0.42f, 1f);
        public static readonly Color ButtonBackground = new Color(0.18f, 0.24f, 0.34f, 1f);
        public static readonly Color Label = new Color(0.62f, 0.67f, 0.75f, 1f);
        public static readonly Color Value = new Color(0.93f, 0.95f, 0.98f, 1f);
        public static readonly Color GridLine = new Color(1f, 1f, 1f, 0.10f);
        public static readonly Color AxisText = new Color(0.62f, 0.67f, 0.75f, 1f);
        public static readonly Color Gain = new Color(0.25f, 0.85f, 0.45f, 1f);
        public static readonly Color Loss = new Color(0.98f, 0.33f, 0.33f, 1f);
        public static readonly Color Pocket = new Color(0.55f, 0.38f, 0.20f, 1f);
        public static readonly Color PocketDark = new Color(0.40f, 0.27f, 0.13f, 1f);
        public static readonly Color PocketStitch = new Color(0.85f, 0.70f, 0.45f, 1f);
    }
}
