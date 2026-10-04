using System;
using System.Globalization;

namespace Gym.Runtime.Play
{
    /// <summary>
    /// 画面上的美元金额怎么写：<c>$1,900.00</c>、<c>+$12.34</c>、<c>-$5.60</c>。减号用键盘上的 '-'，
    /// 别的减号字符系统字体里不一定有。正负按四舍五入到分以后的值判断，所以不会出现 "-$0.00"。
    /// </summary>
    public static class MoneyText
    {
        static readonly CultureInfo Numbers = CultureInfo.InvariantCulture;

        public static string Amount(double value) => "$" + Cents(value).ToString("N2", Numbers);

        /// <summary>带正负号；0 写成 "+$0.00"。</summary>
        public static string Signed(double value)
        {
            double cents = Cents(value);
            return (cents < 0 ? "-$" : "+$") + Math.Abs(cents).ToString("N2", Numbers);
        }

        /// <summary>盈亏用绿色（true）还是红色（false）：0 和正数是绿色。</summary>
        public static bool IsGainOrZero(double value) => Cents(value) >= 0;

        /// <summary>现金少了 <paramref name="amount"/>（买入）：写成 "-$…"。</summary>
        public static string Spent(double amount) => "-" + Amount(Math.Abs(amount));

        /// <summary>现金多了 <paramref name="amount"/>（卖出）：写成 "+$…"。</summary>
        public static string Received(double amount) => "+" + Amount(Math.Abs(amount));

        static double Cents(double value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }
}
