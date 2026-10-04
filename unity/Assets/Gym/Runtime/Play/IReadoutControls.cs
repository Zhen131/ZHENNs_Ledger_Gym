namespace Gym.Runtime.Play
{
    /// <summary>
    /// 读数面板最下面那几行由谁来写：键盘试玩的面板写的是试玩的按键和「下单比例」；观战这类场景实现这个接口，
    /// 换上自己的按键提示和状态（<see cref="HudView.Controls"/>）。
    /// </summary>
    public interface IReadoutControls
    {
        /// <summary>「下单比例」那一行换成的名字。</summary>
        string StatusLabel(PlayLanguage language);

        /// <summary>「下单比例」那一行换成的值。</summary>
        string StatusValue(PlayLanguage language);

        /// <summary>按键提示的第一行。</summary>
        string KeysFirstLine(PlayLanguage language);

        /// <summary>按键提示的第二行。</summary>
        string KeysSecondLine(PlayLanguage language);
    }
}
