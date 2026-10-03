using System;

namespace Gym.Runtime.Configuration
{
    /// <summary>从命令行里读 "-name value" 这样的参数对。</summary>
    public static class CommandLineArgs
    {
        /// <summary>第一个等于 <paramref name="name"/>（不分大小写）的参数后面的那个值；没有时为 null。</summary>
        public static string ValueOf(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            return null;
        }
    }
}
