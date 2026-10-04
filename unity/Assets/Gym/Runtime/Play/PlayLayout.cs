using UnityEngine;

namespace Gym.Runtime.Play
{
    /// <summary>
    /// Play scene 的版面，按 16:9 设计，单位是世界坐标：正交相机的 orthographicSize 是 5，画面高 10
    /// （y 从 −5 到 5），16:9 时宽 17.78（x 从 −8.89 到 8.89）。更窄的窗口左右会被裁掉。
    /// 所有方框都写在这一处，谁也不压谁（测试会查）；图表上方那一条空着，留给以后站在最新那根 candle 上方的小人。
    /// </summary>
    public static class PlayLayout
    {
        public static readonly Rect Screen = Rect.MinMaxRect(-8.88f, -5f, 8.88f, 5f);

        /// <summary>左上角：切换语言的按钮。</summary>
        public static readonly Rect LanguageButton = Rect.MinMaxRect(-8.75f, 4.40f, -5.70f, 4.86f);

        /// <summary>左上：读数面板。</summary>
        public static readonly Rect Readout = Rect.MinMaxRect(-8.75f, -0.70f, -3.05f, 4.28f);

        /// <summary>左下：口袋、盈亏两行，以及口袋上方飘字走的那一段。</summary>
        public static readonly Rect Wallet = Rect.MinMaxRect(-8.75f, -4.86f, -3.05f, -0.90f);

        /// <summary>K 线图本身。</summary>
        public static readonly Rect Chart = Rect.MinMaxRect(-2.70f, -4.20f, 7.55f, 3.30f);

        /// <summary>图表右边：价格刻度的数字。</summary>
        public static readonly Rect PriceLabels = Rect.MinMaxRect(7.62f, -4.30f, 8.86f, 3.40f);

        /// <summary>图表下面：日期。比图表左右各宽一点，因为标签以 candle 为中心。</summary>
        public static readonly Rect TimeLabels = Rect.MinMaxRect(-2.98f, -4.86f, 7.58f, -4.36f);

        /// <summary>图表上方空着的一条，不放任何面板。</summary>
        public static readonly Rect AboveChart = Rect.MinMaxRect(-2.70f, 3.40f, 8.86f, 4.86f);

        /// <summary>面板和按钮的内边距。</summary>
        public const float Padding = 0.12f;

        /// <summary>
        /// 观战走到段尾时的提示：图表上方那一条的左边。小人总站在最右边那根最新的 candle 上，碰不到这里。
        /// </summary>
        public static readonly Rect WatchEndNotice = Rect.MinMaxRect(-2.70f, 3.95f, 4.20f, 4.80f);

        /// <summary>观战没给模型时的提示：图表正中（这时图表是空的）。</summary>
        public static readonly Rect WatchNoModelNotice = Rect.MinMaxRect(-1.95f, -1.75f, 6.80f, 0.85f);
    }
}
