using System;
using System.IO;
using NUnit.Framework;

namespace Gym.Tests.Runtime.Watch
{
    /// <summary>
    /// 要训练好的模型才能做的测试，从环境变量 GYM_WATCH_MODEL 拿 .onnx 的完整路径；没给就报告为忽略（Ignored）。
    /// 仓库里不放模型文件，所以平时这几项是忽略的；验收时给上模型实际跑一次。
    /// </summary>
    public static class ModelForTests
    {
        public const string Variable = "GYM_WATCH_MODEL";

        public static string PathOrIgnore()
        {
            string path = Environment.GetEnvironmentVariable(Variable);
            if (string.IsNullOrEmpty(path)) Assert.Ignore($"needs a trained model: set {Variable} to the full path of an .onnx file");
            path = Path.GetFullPath(path);
            Assert.IsTrue(File.Exists(path), $"{Variable} points to {path}, which does not exist");
            return path;
        }
    }
}
