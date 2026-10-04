using System;
using Gym.Core.Evaluation;
using Gym.Runtime.Agents;
using Unity.MLAgents;

namespace Gym.Runtime.Watch
{
    /// <summary>
    /// 观战真正推的东西：一步就是一次 <see cref="Academy.EnvironmentStep"/>，和评估包的走法一样（自动步进关掉，
    /// 由这里一步一步推）；重来就是让 Agent 结束这一局，它会回到段首。
    /// </summary>
    public sealed class AgentWatchTarget : IWatchTarget, IDisposable
    {
        readonly TradingAgent agent;

        public AgentWatchTarget(TradingAgent agent)
        {
            this.agent = agent ?? throw new ArgumentNullException(nameof(agent));
            agent.EpisodeFinished += OnEpisodeFinished;
        }

        public event Action Finished;

        public void Step() => Academy.Instance.EnvironmentStep();

        public void Restart() => agent.EpisodeInterrupted();

        public void Dispose() => agent.EpisodeFinished -= OnEpisodeFinished;

        void OnEpisodeFinished(TradingAgent source, EpisodeMetrics metrics) => Finished?.Invoke();
    }
}
