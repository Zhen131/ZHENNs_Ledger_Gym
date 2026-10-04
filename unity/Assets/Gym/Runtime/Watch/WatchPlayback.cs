using System;
using System.Collections.Generic;
using System.Linq;

namespace Gym.Runtime.Watch
{
    /// <summary>
    /// 观战的播放：暂停、暂停时单步、在一张「每秒几步」的表里调快调慢、重来、走到头停住。只推一个
    /// <see cref="IWatchTarget"/>，不碰画面，所以测试能用替身。时钟由调用方拨（<see cref="Advance"/>）：
    /// 快慢由这里自己的计时决定，不被帧数卡住，每秒的步数比帧数多时一次 Advance 走好几步。
    /// </summary>
    public sealed class WatchPlayback : IDisposable
    {
        /// <summary>
        /// 一次 Advance 最多补走多少秒的步数。editor 卡了一下、或者在 Pause 里停过，回来时不会一口气冲出去一大截。
        /// 平时一帧只有几十毫秒，碰不到它。
        /// </summary>
        public const double MaxCatchUpSeconds = 0.25;

        /// <summary>
        /// 一帧帧加起来的小数有舍入误差（30 个 1/30 秒加起来是 0.99999…）；差这么一点也算够一步，
        /// 不然 5 步每秒拨 2 秒只走 9 步。
        /// </summary>
        const double RoundingSlack = 1e-9;

        readonly IWatchTarget target;
        readonly float[] speeds;
        double owedSteps;

        public WatchPlayback(IWatchTarget target, IReadOnlyList<float> stepsPerSecondChoices, int startSpeedIndex)
        {
            this.target = target ?? throw new ArgumentNullException(nameof(target));
            if (stepsPerSecondChoices == null || stepsPerSecondChoices.Count == 0)
                throw new ArgumentException("At least one speed is needed.", nameof(stepsPerSecondChoices));
            speeds = stepsPerSecondChoices.ToArray();
            for (int i = 0; i < speeds.Length; i++)
            {
                if (!(speeds[i] > 0) || float.IsInfinity(speeds[i]))
                    throw new ArgumentException($"Speed {speeds[i]} is not a positive number of steps per second.", nameof(stepsPerSecondChoices));
                if (i > 0 && !(speeds[i] > speeds[i - 1]))
                    throw new ArgumentException("Speeds must go from slow to fast.", nameof(stepsPerSecondChoices));
            }
            SpeedIndex = Clamp(startSpeedIndex);
            target.Finished += OnFinished;
        }

        public bool Paused { get; private set; }

        /// <summary>这一段走完了：不再往前走，直到 <see cref="Restart"/>。</summary>
        public bool Finished { get; private set; }

        /// <summary>
        /// 定格：走到头时打开，画面不理会紧接着的那次「一局开始」，数字留在走完那一刻；重来时先关掉它。
        /// </summary>
        public bool Frozen { get; private set; }

        public int SpeedIndex { get; private set; }
        public float StepsPerSecond => speeds[SpeedIndex];
        public IReadOnlyList<float> Speeds => speeds;

        /// <summary>从段首（或上次重来）起走了几步。</summary>
        public int StepsTaken { get; private set; }

        /// <summary>状态变了（暂停、换档、走到头、重来），画面该重写状态。</summary>
        public event Action Changed;

        /// <summary><see cref="Frozen"/> 变了，参数是新值。走到头时，它在环境回到段首之前触发。</summary>
        public event Action<bool> FrozenChanged;

        /// <summary>把时钟往前拨 <paramref name="seconds"/> 秒，按当前速度该走几步就走几步。暂停或走到头时不走。</summary>
        public void Advance(double seconds)
        {
            if (Paused || Finished || !(seconds > 0)) return;
            owedSteps += Math.Min(seconds, MaxCatchUpSeconds) * StepsPerSecond;
            while (owedSteps >= 1 - RoundingSlack && !Finished)
            {
                owedSteps -= 1;
                Step();
            }
        }

        public void TogglePause()
        {
            Paused = !Paused;
            owedSteps = 0;
            Changed?.Invoke();
        }

        /// <summary>暂停时走恰好一步；没暂停或已经走到头时什么都不做。返回走了没有。</summary>
        public bool StepOnce()
        {
            if (!Paused || Finished) return false;
            Step();
            return true;
        }

        /// <summary>换到表里快一档；已经最快时不动。</summary>
        public void Faster() => SetSpeedIndex(SpeedIndex + 1);

        /// <summary>换到表里慢一档；已经最慢时不动。</summary>
        public void Slower() => SetSpeedIndex(SpeedIndex - 1);

        /// <summary>回到段首重新开始。先关掉定格，画面才会接收接下来的「一局开始」。暂停与否不变。</summary>
        public void Restart()
        {
            owedSteps = 0;
            StepsTaken = 0;
            Finished = false;
            SetFrozen(false);
            target.Restart();
            Changed?.Invoke();
        }

        public void Dispose() => target.Finished -= OnFinished;

        void Step()
        {
            target.Step();
            StepsTaken++;
        }

        void SetSpeedIndex(int index)
        {
            int clamped = Clamp(index);
            if (clamped == SpeedIndex) return;
            SpeedIndex = clamped;
            owedSteps = 0;
            Changed?.Invoke();
        }

        int Clamp(int index) => Math.Max(0, Math.Min(speeds.Length - 1, index));

        void OnFinished()
        {
            Finished = true;
            owedSteps = 0;
            SetFrozen(true);
            Changed?.Invoke();
        }

        void SetFrozen(bool value)
        {
            if (Frozen == value) return;
            Frozen = value;
            FrozenChanged?.Invoke(value);
        }
    }
}
