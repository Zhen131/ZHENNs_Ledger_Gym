using Gym.Runtime.Agents;
using Gym.Runtime.Watch;
using Unity.InferenceEngine;
using Unity.MLAgents.Policies;
using UnityEditor;

namespace Gym.Editor
{
    /// <summary>
    /// 在 editor 里把记住的模型挂到 Watch scene 的 Agent 上。Gym.Runtime 看不见模型的类型，所以这一步放在 editor
    /// 程序集：editor 一加载（包括每次进入播放时重新加载脚本），就把自己登记到 <see cref="WatchController.ModelSource"/>。
    /// 推理设置（只推理、确定性、Burst）已经由场景生成器按评估包的那一份写进 Watch scene，这里只挂模型。
    /// </summary>
    [InitializeOnLoad]
    public sealed class WatchModelAttacher : IWatchModelSource
    {
        static WatchModelAttacher() => WatchController.ModelSource = new WatchModelAttacher();

        public bool TryAttach(TradingAgent agent, string modelAsset, out string problem)
        {
            var model = AssetDatabase.LoadAssetAtPath<ModelAsset>(modelAsset);
            if (model == null)
            {
                problem = $"the remembered watch model {modelAsset} is not in the project any more; choose one again with {WatchSetup.MenuPath}";
                return false;
            }
            agent.GetComponent<BehaviorParameters>().Model = model;
            problem = null;
            return true;
        }
    }
}
