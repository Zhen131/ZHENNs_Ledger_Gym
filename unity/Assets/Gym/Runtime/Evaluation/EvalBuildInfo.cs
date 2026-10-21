using System;

namespace Gym.Runtime.Evaluation
{
    /// <summary>BuildScript.BuildMacEval / BuildWindowsEval 写在数据旁边的 StreamingAssets/Gym/build-info.json 的内容。</summary>
    [Serializable]
    public class EvalBuildInfo
    {
        public string run_id;
        public string model_sha256;
        public string model_file;
        public string built_at_utc;
        public string unity_version;
        /// <summary>
        /// 打包时从模型读出的向量观测输入维数（第一轮的模型是 35）。评估包开工时拿它和当前观测集的大小比，
        /// 对不上就退出。13 批之前打的包没有这个字段，读出来是 0。
        /// </summary>
        public int vector_observation_size;
        /// <summary>
        /// 打包时从模型读出的连续数个数（下单模式的模型是 1，旋钮模式的是 0；0 是旋钮模式的正常值，不表示旧包）。
        /// 只有 <see cref="action_shape_recorded"/> 为 true 时才算数。
        /// </summary>
        public int continuous_actions;
        /// <summary>打包时从模型读出的每个离散分支的档数（下单模式 [3]，三档旋钮 [3]）。</summary>
        public int[] discrete_branches;
        /// <summary>
        /// 打包时记下了动作形状：24 批之后打的包为 true。之前打的包没有这个字段，读出来是 false，评估包就不查动作形状、只警告。
        /// </summary>
        public bool action_shape_recorded;
    }
}
