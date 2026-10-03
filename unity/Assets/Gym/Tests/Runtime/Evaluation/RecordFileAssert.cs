using System.IO;
using NUnit.Framework;

namespace Gym.Tests.Editor
{
    public static class RecordFileAssert
    {
        // ---- 05D M-2: committed evaluation files must not carry a machine's paths
        public static void NoMachinePath(string text, string what)
        {
            StringAssert.DoesNotContain("/Users/", text, what);
            StringAssert.DoesNotContain("/home/", text, what);
            StringAssert.DoesNotContain(":\\", text, what);   // C:\ as raw text
            StringAssert.DoesNotContain("\\\\", text, what);  // any backslash once JSON-escaped, \\server too
            StringAssert.DoesNotContain(Path.GetFullPath("."), text, what);
        }
    }
}
