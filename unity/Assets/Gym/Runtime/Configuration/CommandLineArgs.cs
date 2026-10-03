using System;

namespace Gym.Runtime.Configuration
{
    /// <summary>Reads "-name value" pairs from a command line.</summary>
    public static class CommandLineArgs
    {
        /// <summary>The value after the first argument equal to <paramref name="name"/> in any letter case; null when there is none.</summary>
        public static string ValueOf(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            return null;
        }
    }
}
