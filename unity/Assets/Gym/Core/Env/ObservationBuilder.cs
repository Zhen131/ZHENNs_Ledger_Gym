using System;
using Gym.Core.Accounting;
using Gym.Core.Market;

namespace Gym.Core.Env
{
    /// <summary>
    /// Agent 在决策下标 t 看到的数。只读到第 t 根 candle 为止（含第 t 根），不读 t 之后的任何一根。
    ///
    /// 三档都一样的前 35 个（base 档就是这 35 个）：
    ///   0～31：最近 32 根（价格窗口）的收盘价相对第 t 根：tanh(10 × (c[t − i] ÷ c[t] − 1))，i = 1 … 32；
    ///   32 仓位比例、33 未实现收益率 tanh(10 × r)、34 距上次交易的 step 数 ÷ 720（最多 1）。
    ///
    /// long 档再加 6 个（35～40）。σ 是过去 720 根小时对数收益 ln(c[i] ÷ c[i − 1])（i = t − 719 … t）的总体标准差，
    /// 小于 1e-6 时按 1e-6 算：
    ///   35～38：h = 24、168、720、2160 各一个，z = ln(c[t] ÷ c[t − h]) ÷ (σ × √h)，值 = tanh(z ÷ 3)；
    ///   39：tanh(ln(σ ÷ 0.0065))；
    ///   40：距过去 90 天最高收盘价的回撤 (max(c[t − 2159 … t]) − c[t]) ÷ max，落在 [0, 1)。
    ///
    /// long-ta 档再加 2 个（41、42），都在按天采样的序列 d[k] = c[t − 24k]（k = 0 … 89）上算：
    ///   41 MACD：从最旧（k = 89）到最新（k = 0）算 EMA12 和 EMA26，都用第一个样本初始化，平滑系数 2 ÷ (N + 1)；
    ///      值 = tanh(20 × (EMA12 − EMA26) ÷ c[t])；
    ///   42 RSI(14)：最近 14 个日变化 d[k] − d[k + 1]（k = 0 … 13）里涨幅的简单平均 G、跌幅绝对值的简单平均 L；
    ///      L = 0 且 G > 0 时 RSI = 100，G = L = 0 时 50，否则 100 − 100 ÷ (1 + G ÷ L)；值 = (RSI − 50) ÷ 50。
    ///
    /// long 和 long-ta 的历史长度是 2160（90 天）：决策下标必须 ≥ 2160。
    /// </summary>
    public static class ObservationBuilder
    {
        /// <summary>价格窗口：0～31 号是最近 32 根的收盘价。三档都是 32。</summary>
        public const int PriceWindow = 32;

        public const int PositionRatioIndex = PriceWindow;
        public const int UnrealizedReturnIndex = PriceWindow + 1;
        public const int StepsSinceTradeIndex = PriceWindow + 2;

        /// <summary>base 档的大小：价格窗口加仓位、浮盈、距上次交易三个数。</summary>
        public const int Size = PriceWindow + 3;

        /// <summary>base 档的历史长度：最早能决策的下标（价格窗口要读到 c[t − 32]）。</summary>
        public const int Lookback = PriceWindow;

        public const int StepsSinceTradeCap = 720;

        // ---- long 档（35～40）

        /// <summary>过去 24 小时收益（按波动率归一后压扁）。</summary>
        public const int Return1DayIndex = Size;
        /// <summary>过去 1 周（168 小时）。</summary>
        public const int Return1WeekIndex = Size + 1;
        /// <summary>过去 30 天（720 小时）。</summary>
        public const int Return30DayIndex = Size + 2;
        /// <summary>过去 90 天（2160 小时）。</summary>
        public const int Return90DayIndex = Size + 3;
        /// <summary>过去 30 天的小时波动率相对尺子。</summary>
        public const int VolatilityIndex = Size + 4;
        /// <summary>距过去 90 天最高收盘价的回撤。</summary>
        public const int DrawdownIndex = Size + 5;
        /// <summary>long 档的大小。</summary>
        public const int LongSize = Size + 6;

        /// <summary>35～38 号的四个时长（小时），和下标一一对应。</summary>
        public const int Hours1Day = 24;
        public const int Hours1Week = 168;
        public const int Hours30Days = 720;
        public const int Hours90Days = 2160;

        /// <summary>long 和 long-ta 的历史长度：90 天。决策下标必须 ≥ 它（35～38 号最远读到 c[t − 2160]）。</summary>
        public const int LongHistory = Hours90Days;

        /// <summary>σ 用的小时收益个数（30 天）。</summary>
        public const int VolatilityWindow = Hours30Days;

        /// <summary>σ 的下限：一段完全不动的价格的 σ 是 0，除以 0 之前先抬到这里。</summary>
        public const double MinVolatility = 1e-6;

        /// <summary>
        /// 39 号的尺子：BTC 小时收益滚动 720 根标准差的中位数，约 0.65 %。取自第一轮训练段 2017-08～2024-08 的中位数，
        /// 当作所有币共用的尺子，不随币、不随训练段变。
        /// </summary>
        public const double VolatilityScale = 0.0065;

        /// <summary>40 号回看的根数（90 天，含第 t 根）。</summary>
        public const int DrawdownWindow = Hours90Days;

        // ---- long-ta 档（41、42）

        public const int MacdIndex = LongSize;
        public const int RsiIndex = LongSize + 1;
        /// <summary>long-ta 档的大小。</summary>
        public const int LongTaSize = LongSize + 2;

        /// <summary>按天采样：每 24 根取一根，取 90 个（d[0] = c[t] … d[89] = c[t − 2136]）。</summary>
        public const int HoursPerDay = 24;
        public const int DailySamples = 90;
        public const int MacdFast = 12;
        public const int MacdSlow = 26;
        public const int RsiPeriod = 14;

        /// <summary>base 档：和 <see cref="Write(ObservationSet, CandleSeries, int, Account, int, float[], int)"/> 传 base 一样。</summary>
        public static void Write(CandleSeries series, int decisionIndex, Account account, int stepsSinceTrade, float[] dst, int offset = 0) =>
            Write(ObservationSet.Base, series, decisionIndex, account, stepsSinceTrade, dst, offset);

        /// <summary>按 <paramref name="set"/> 写 <see cref="ObservationSets.Size"/> 个数到 dst[offset …]。</summary>
        public static void Write(ObservationSet set, CandleSeries series, int decisionIndex, Account account, int stepsSinceTrade,
            float[] dst, int offset = 0)
        {
            if (series == null) throw new ArgumentNullException(nameof(series));
            if (account == null) throw new ArgumentNullException(nameof(account));
            if (dst == null) throw new ArgumentNullException(nameof(dst));
            int history = set.HistoryLength();
            int size = set.Size();
            if (decisionIndex < history || decisionIndex >= series.Count)
                throw new ArgumentOutOfRangeException(nameof(decisionIndex), decisionIndex, $"Must be in [{history}, {series.Count - 1}].");
            if (offset < 0 || dst.Length - offset < size)
                throw new ArgumentException($"Need {size} floats from offset {offset}.", nameof(dst));
            if (stepsSinceTrade < 0) throw new ArgumentOutOfRangeException(nameof(stepsSinceTrade));

            double now = series.CloseAt(decisionIndex);
            for (int i = 1; i <= PriceWindow; i++)
                dst[offset + i - 1] = (float)Math.Tanh(10 * (series.CloseAt(decisionIndex - i) / now - 1));
            dst[offset + PositionRatioIndex] = (float)account.PositionRatio(now);
            dst[offset + UnrealizedReturnIndex] = (float)Math.Tanh(10 * account.UnrealizedReturn(now));
            dst[offset + StepsSinceTradeIndex] = (float)Math.Min((double)stepsSinceTrade / StepsSinceTradeCap, 1.0);

            if (set == ObservationSet.Base) return;
            WriteLong(series, decisionIndex, dst, offset);
            if (set == ObservationSet.Long) return;
            dst[offset + MacdIndex] = (float)Math.Tanh(20 * Macd(series, decisionIndex));
            dst[offset + RsiIndex] = (float)((Rsi(series, decisionIndex) - 50) / 50);
        }

        /// <summary>35～40 号。</summary>
        static void WriteLong(CandleSeries series, int t, float[] dst, int offset)
        {
            double sigma = Math.Max(Volatility(series, t), MinVolatility);
            double now = series.CloseAt(t);
            dst[offset + Return1DayIndex] = (float)ScaledReturn(series, t, Hours1Day, sigma);
            dst[offset + Return1WeekIndex] = (float)ScaledReturn(series, t, Hours1Week, sigma);
            dst[offset + Return30DayIndex] = (float)ScaledReturn(series, t, Hours30Days, sigma);
            dst[offset + Return90DayIndex] = (float)ScaledReturn(series, t, Hours90Days, sigma);
            dst[offset + VolatilityIndex] = (float)Math.Tanh(Math.Log(sigma / VolatilityScale));
            double high = now;
            for (int i = t - DrawdownWindow + 1; i < t; i++)
                high = Math.Max(high, series.CloseAt(i));
            dst[offset + DrawdownIndex] = (float)((high - now) / high);
        }

        /// <summary>tanh(z ÷ 3)，z = ln(c[t] ÷ c[t − h]) ÷ (σ × √h)。</summary>
        static double ScaledReturn(CandleSeries series, int t, int hours, double sigma)
        {
            double z = Math.Log(series.CloseAt(t) / series.CloseAt(t - hours)) / (sigma * Math.Sqrt(hours));
            return Math.Tanh(z / 3);
        }

        /// <summary>
        /// 过去 720 根小时对数收益 ln(c[i] ÷ c[i − 1])（i = t − 719 … t）的总体标准差（除以 720，不是 719），
        /// 没有抬到下限。读 c[t − 720 … t]。
        /// </summary>
        public static double Volatility(CandleSeries series, int t)
        {
            if (series == null) throw new ArgumentNullException(nameof(series));
            if (t < VolatilityWindow || t >= series.Count)
                throw new ArgumentOutOfRangeException(nameof(t), t, $"Must be in [{VolatilityWindow}, {series.Count - 1}].");
            // 先求平均再求离差平方和（两遍），比「平方的平均减平均的平方」准；收益只算一次，放在栈上，不产生垃圾。
            Span<double> returns = stackalloc double[VolatilityWindow];
            double sum = 0;
            for (int k = 0; k < VolatilityWindow; k++)
            {
                int i = t - VolatilityWindow + 1 + k;
                returns[k] = Math.Log(series.CloseAt(i) / series.CloseAt(i - 1));
                sum += returns[k];
            }
            double mean = sum / VolatilityWindow;
            double squares = 0;
            for (int k = 0; k < VolatilityWindow; k++)
            {
                double deviation = returns[k] - mean;
                squares += deviation * deviation;
            }
            return Math.Sqrt(squares / VolatilityWindow);
        }

        /// <summary>按天采样的第 k 个样本 d[k] = c[t − 24k]。</summary>
        static double Daily(CandleSeries series, int t, int k) => series.CloseAt(t - HoursPerDay * k);

        /// <summary>(EMA12 − EMA26) ÷ c[t]，EMA 在 d[89] → d[0] 上算，都用 d[89] 初始化。</summary>
        public static double Macd(CandleSeries series, int t)
        {
            if (series == null) throw new ArgumentNullException(nameof(series));
            int oldest = DailySamples - 1;
            if (t - HoursPerDay * oldest < 0 || t >= series.Count)
                throw new ArgumentOutOfRangeException(nameof(t), t, $"Must be in [{HoursPerDay * oldest}, {series.Count - 1}].");
            double fastAlpha = 2.0 / (MacdFast + 1), slowAlpha = 2.0 / (MacdSlow + 1);
            double fast = Daily(series, t, oldest), slow = fast;
            for (int k = oldest - 1; k >= 0; k--)
            {
                double x = Daily(series, t, k);
                fast = fastAlpha * x + (1 - fastAlpha) * fast;
                slow = slowAlpha * x + (1 - slowAlpha) * slow;
            }
            return (fast - slow) / series.CloseAt(t);
        }

        /// <summary>RSI(14)，0～100，在最近 14 个日变化 d[k] − d[k + 1]（k = 0 … 13）上算；读 c[t − 336 … t]。</summary>
        public static double Rsi(CandleSeries series, int t)
        {
            if (series == null) throw new ArgumentNullException(nameof(series));
            if (t - HoursPerDay * RsiPeriod < 0 || t >= series.Count)
                throw new ArgumentOutOfRangeException(nameof(t), t, $"Must be in [{HoursPerDay * RsiPeriod}, {series.Count - 1}].");
            double gains = 0, losses = 0;
            for (int k = 0; k < RsiPeriod; k++)
            {
                double change = Daily(series, t, k) - Daily(series, t, k + 1);
                if (change > 0) gains += change;
                else losses -= change;
            }
            double g = gains / RsiPeriod, l = losses / RsiPeriod;
            if (l == 0) return g > 0 ? 100 : 50;
            return 100 - 100 / (1 + g / l);
        }
    }
}
