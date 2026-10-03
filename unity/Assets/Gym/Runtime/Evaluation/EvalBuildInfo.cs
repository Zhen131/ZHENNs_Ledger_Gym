using System;

namespace Gym.Runtime
{
    /// <summary>What BuildScript.BuildMacEval writes next to the data as StreamingAssets/Gym/build-info.json.</summary>
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
