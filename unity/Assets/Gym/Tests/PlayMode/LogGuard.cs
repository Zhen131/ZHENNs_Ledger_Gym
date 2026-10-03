using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TestTools;

namespace Gym.Tests.PlayMode
{
    /// <summary>
    /// 给 PlayMode 测试用的「没有错误日志」检查。LogAssert.NoUnexpectedReceived() 连普通的 Debug.Log 行
    /// 也会拒绝（ML-Agents 会打印好几行），所以这个守卫在生效期间记下每一条日志，把 Log 和 Warning
    /// 两类标成预期之内，再调用 NoUnexpectedReceived：任何 error、assert 或 exception 仍然会让测试失败。
    /// </summary>
    sealed class LogGuard : IDisposable
    {
        readonly List<(LogType type, string message)> seen = new List<(LogType, string)>();

        public LogGuard() => Application.logMessageReceived += OnLog;

        public int InfoCount { get; private set; }
        public IReadOnlyList<(LogType type, string message)> Seen => seen;

        void OnLog(string message, string stackTrace, LogType type) => seen.Add((type, message));

        public void AssertNoErrors()
        {
            Application.logMessageReceived -= OnLog;
            foreach ((LogType type, string message) in seen)
            {
                if (type != LogType.Log && type != LogType.Warning) continue;
                LogAssert.Expect(type, message);
                InfoCount++;
            }
            LogAssert.NoUnexpectedReceived();
        }

        public void Dispose() => Application.logMessageReceived -= OnLog;
    }
}
