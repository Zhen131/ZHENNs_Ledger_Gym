using System;

namespace Gym.Core.Env
{
    /// <summary>
    /// 混训时一局用哪只币：只看这一局的 seed 和币的个数，没有状态，不从别的随机序列里抽数。
    ///
    /// 做法：<c>new System.Random(SeedMixer.Mix(episodeSeed, StreamIndex)).Next(count)</c>。两参数的
    /// <see cref="SeedMixer.Mix(int, int)"/> 会把 seed 再打散一遍，所以选到哪只币和训练起点、开局是否持币
    /// （<see cref="TradingEnv.ResetForTraining"/> 用的是 <c>new System.Random(SeedMixer.Mix(seed))</c>）互不相干。
    /// 不能用「Mix(seed) 再异或一个常量」：System.Random 对 seed 是线性的，异或常量后两条序列的差只有有限几种，
    /// 选币就会和起点绑在一起。
    /// </summary>
    public static class SymbolPicker
    {
        /// <summary>
        /// 选币这条随机流在 <see cref="SeedMixer.Mix(int, int)"/> 里的下标，固定不变（改了它，同一个 seed 选到的币就变了）。
        /// 取 "SYMB" 四个字母的 ASCII 码，只是为了和别处用到的小下标（例如 Agent 的编号）一眼分开。
        /// </summary>
        public const int StreamIndex = 0x53594D42;

        /// <summary>
        /// 第 <paramref name="episodeSeed"/> 局用第几只币，在 [0, <paramref name="count"/>) 里。
        /// <paramref name="count"/> 为 1 时恒为 0，不建随机数（只有一只币时和改动前完全一样）。
        /// </summary>
        public static int Pick(int episodeSeed, int count)
        {
            if (count < 1) throw new ArgumentOutOfRangeException(nameof(count), count, "Need at least one symbol.");
            if (count == 1) return 0;
            return new Random(SeedMixer.Mix(episodeSeed, StreamIndex)).Next(count);
        }
    }
}
