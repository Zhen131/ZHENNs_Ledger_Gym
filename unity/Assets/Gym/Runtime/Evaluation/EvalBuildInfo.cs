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
    }
}
