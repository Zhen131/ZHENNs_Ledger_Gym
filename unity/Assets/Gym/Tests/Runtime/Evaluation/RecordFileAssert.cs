using System.IO;
using NUnit.Framework;

namespace Gym.Tests.Runtime.Evaluation
{
    public static class RecordFileAssert
    {
        // 提交进仓库的评估文件里不能带任何机器的路径。
        public static void NoMachinePath(string text, string what)
        {
            StringAssert.DoesNotContain("/Users/", text, what);
            StringAssert.DoesNotContain("/home/", text, what);
            StringAssert.DoesNotContain(":\\", text, what);   // 原样文本里的 C:\
            StringAssert.DoesNotContain("\\\\", text, what);  // JSON 转义后的任何反斜杠，\\server 也算
            StringAssert.DoesNotContain(Path.GetFullPath("."), text, what);
        }
    }
}
