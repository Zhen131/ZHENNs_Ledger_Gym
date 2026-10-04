namespace Gym.Runtime.Play
{
    /// <summary>小人头上的提示字：买入、卖出写比例，被拒写「被拒」，不动和站着不写。</summary>
    public static class AvatarHint
    {
        /// <param name="fraction">这一步的下单比例（0～1），写法和读数面板「上一步」那一行相同，例如 25%、37.5%。</param>
        /// <returns>要显示的字；不显示时为 null。</returns>
        public static string Text(AvatarMotion motion, double fraction, PlayLanguage language)
        {
            switch (motion)
            {
                case AvatarMotion.Buy:
                    return PlayText.Format(PlayTextKey.AvatarBuy, language, HudReadout.Percent(fraction));
                case AvatarMotion.Sell:
                    return PlayText.Format(PlayTextKey.AvatarSell, language, HudReadout.Percent(fraction));
                case AvatarMotion.Rejected:
                    return PlayText.Get(PlayTextKey.AvatarRejected, language);
                default:
                    return null;
            }
        }
    }
}
