using System;

namespace Gym.Runtime.Evaluation
{
    /// <summary>BuildScript.BuildMacEval 写在数据旁边的 StreamingAssets/Gym/build-info.json 的内容。</summary>
    [Serializable]
    public class EvalBuildInfo
    {
        public string run_id;
        public string model_sha256;
        public string model_file;
        public string built_at_utc;
        public string unity_version;
    }
}
