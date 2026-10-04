using System;
using System.Collections.Generic;
using System.Globalization;

namespace Gym.Runtime.Play
{
    /// <summary>
    /// K 线图的价格刻度怎么取：间隔只用 1、2、5 乘以 10 的整数次方（行情软件通用的做法），
    /// 每条线都是间隔的整数倍。纯函数，不碰画面。
    /// </summary>
    public static class PriceScale
    {
        static readonly int[] Mantissas = { 1, 2, 5 };

        /// <summary>
        /// 在 [<paramref name="low"/>, <paramref name="high"/>] 里挑刻度：在 1-2-5 的档位里，选条数离
        /// <paramref name="desiredLines"/> 最近的那一档，一样近时选间隔大的（线少一点）。档位之间差 2 到 2.5 倍，
        /// 所以条数会在想要的数上下浮动：想要 5 条时实际是 3～7 条。一条线都没有的档位不选。范围不合法时返回空的刻度。
        /// </summary>
        public static PriceTicks Compute(double low, double high, int desiredLines)
        {
            if (!(high > low) || double.IsInfinity(low) || double.IsInfinity(high) || desiredLines < 1)
                return new PriceTicks(0, Array.Empty<double>());

            int exponent = (int)Math.Floor(Math.Log10((high - low) / desiredLines));
            double bestStep = 0;
            List<double> bestValues = null;
            for (int e = exponent - 1; e <= exponent + 1; e++)
            {
                foreach (int mantissa in Mantissas)
                {
                    double step = StepOf(mantissa, e);
                    List<double> values = MultiplesInside(low, high, step);
                    if (values.Count == 0) continue; // 至少要有一条线
                    int distance = Math.Abs(values.Count - desiredLines);
                    int bestDistance = bestValues == null ? int.MaxValue : Math.Abs(bestValues.Count - desiredLines);
                    if (distance < bestDistance || (distance == bestDistance && step > bestStep))
                    {
                        bestStep = step;
                        bestValues = values;
                    }
                }
            }
            return new PriceTicks(bestStep, (IReadOnlyList<double>)bestValues ?? Array.Empty<double>());
        }

        /// <summary>[low, high] 里 step 的每个整数倍，从低到高。</summary>
        static List<double> MultiplesInside(double low, double high, double step)
        {
            var values = new List<double>();
            for (long k = (long)Math.Ceiling(low / step); ; k++)
            {
                double value = k * step;
                if (value > high) break;
                if (value >= low) values.Add(value);
            }
            return values;
        }

        /// <summary>刻度旁边的数字：带千分位，小数位数按间隔定（间隔 50 → "42,550"，间隔 0.5 → "1.5"）。</summary>
        public static string Label(double value, double step)
        {
            int decimals = step >= 1 ? 0 : (int)Math.Ceiling(-Math.Log10(step) - 1e-9);
            return value.ToString("N" + decimals, CultureInfo.InvariantCulture);
        }

        /// <summary>mantissa × 10^exponent；负次方用除法，这样 0.1、0.2 这类数尽量准。</summary>
        static double StepOf(int mantissa, int exponent) =>
            exponent >= 0 ? mantissa * Math.Pow(10, exponent) : mantissa / Math.Pow(10, -exponent);
    }
}
