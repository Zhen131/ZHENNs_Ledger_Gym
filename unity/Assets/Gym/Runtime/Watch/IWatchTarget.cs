using System;

namespace Gym.Runtime.Watch
{
    /// <summary>
    /// 观战推着走的那个东西：走一步、回到段首、走完时报一声。真的是 Agent 加 Academy（<see cref="AgentWatchTarget"/>），
    /// 测试里换成按脚本走的替身，这样不用模型也能测暂停、单步、快慢、重来和走到头。
    /// </summary>
    public interface IWatchTarget
    {
        /// <summary>走一步。</summary>
        void Step();

        /// <summary>回到段首重新开始。</summary>
        void Restart();

        /// <summary>这一段走完时触发；在环境自己回到段首（并发出「一局开始」）之前。</summary>
        event Action Finished;
    }
}
