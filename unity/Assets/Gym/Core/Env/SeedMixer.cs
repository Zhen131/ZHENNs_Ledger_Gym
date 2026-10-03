namespace Gym.Core.Env
{
    /// <summary>
    /// 在 seed 交给 <see cref="System.Random"/> 之前，先把它打散。
    ///
    /// .NET 里带 seed 的 <c>System.Random</c> 对 seed 是线性的：seed 为 s 和 s + 1 时，
    /// 序列里每个输出都相差同一个固定值（模 2³¹ − 1）。所以相邻的 seed 给出的是同一个序列
    /// 平移后的副本，不是互相独立的序列。先让每个 seed 过一遍 SplitMix64，就去掉了这种结构，
    /// 同时每个 seed 仍然能复现。
    /// </summary>
    public static class SeedMixer
    {
        /// <summary>
        /// 把 seed 当成 64 位无符号整数做 SplitMix64（负的 seed 做符号扩展，和
        /// <c>unchecked((ulong)seed)</c> 一样）；返回低 31 位，是一个非负的 int。
        /// </summary>
        public static int Mix(int seed) => Low31(SplitMix64(unchecked((ulong)seed)));

        /// <summary>
        /// 第 <paramref name="index"/> 项的 seed，基础 seed 是 <paramref name="seed"/>：高 32 位放 Mix(seed)，
        /// 低 32 位放 index 的 32 位二进制，再过一遍 SplitMix64。
        /// </summary>
        public static int Mix(int seed, int index) =>
            Low31(SplitMix64(((ulong)(uint)Mix(seed) << 32) | (uint)index));

        static ulong SplitMix64(ulong x)
        {
            unchecked
            {
                ulong z = x + 0x9E3779B97F4A7C15UL;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        static int Low31(ulong value) => (int)(value & 0x7FFFFFFFUL);
    }
}
