using Gym.Runtime.Agents;

namespace Gym.Runtime.Watch
{
    /// <summary>
    /// 观战向谁要模型。Gym.Runtime 不引用推理引擎，看不见模型的类型，所以「按记录找到模型、挂到 Agent 上」由
    /// editor 程序集来做：它在 editor 加载时把自己登记到 <see cref="WatchController.ModelSource"/>。
    /// 打出来的包里没人登记，观战画面就只显示「还没有选模型」。
    /// </summary>
    public interface IWatchModelSource
    {
        /// <summary>
        /// 把 <paramref name="modelAsset"/>（工程里的 asset 路径）挂到 <paramref name="agent"/> 的
        /// BehaviorParameters 上。挂上了返回 true；挂不上（比如文件已经不在了）返回 false，原因写进
        /// <paramref name="problem"/>（英文，进日志）。
        /// </summary>
        bool TryAttach(TradingAgent agent, string modelAsset, out string problem);
    }
}
