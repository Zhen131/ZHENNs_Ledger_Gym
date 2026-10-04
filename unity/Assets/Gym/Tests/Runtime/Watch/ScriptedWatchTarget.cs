using System;
using System.Collections.Generic;
using Gym.Runtime.Watch;

namespace Gym.Tests.Runtime.Watch
{
    /// <summary>
    /// 观战测试用的替身，代替模型和 Agent：每走一步位置加一；走到第 <see cref="FinishAt"/> 步时先报「走完了」，
    /// 再像真的环境那样当场回到段首并发出「一局开始」。每件事都按先后记进 <see cref="Log"/>。
    /// </summary>
    sealed class ScriptedWatchTarget : IWatchTarget
    {
        public ScriptedWatchTarget(int finishAt) => FinishAt = finishAt;

        public int FinishAt { get; }

        /// <summary>离段首走了几步。</summary>
        public int Position { get; private set; }

        public int Steps { get; private set; }
        public int Restarts { get; private set; }
        public List<string> Log { get; } = new List<string>();

        public event Action Finished;

        /// <summary>真的环境回到段首时发出的那个「一局开始」。</summary>
        public event Action EpisodeStarted;

        public void Step()
        {
            Steps++;
            Position++;
            Log.Add("step");
            if (Position < FinishAt) return;
            Log.Add("finished");
            Finished?.Invoke();
            StartOver();
        }

        public void Restart()
        {
            Restarts++;
            Log.Add("restart");
            StartOver();
        }

        void StartOver()
        {
            Position = 0;
            Log.Add("episode started");
            EpisodeStarted?.Invoke();
        }
    }
}
