using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TestTools;

namespace Gym.Tests.PlayMode
{
    /// <summary>
    /// "No error logs" for a PlayMode test. LogAssert.NoUnexpectedReceived() also
    /// rejects plain Debug.Log lines (ML-Agents prints several), so this guard records
    /// every log while it is active, marks the Log and Warning ones as expected, and
    /// then calls NoUnexpectedReceived: any error, assert or exception still fails.
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
