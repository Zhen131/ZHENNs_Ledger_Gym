using System;

namespace Gym.Runtime.Watch
{
    /// <summary>观战记住的上一个模型：导入后在工程里的 asset 路径，和当初选的那个文件（只给人看）。</summary>
    [Serializable]
    public sealed class WatchModelRecord
    {
        /// <summary>导入后的 asset 路径，例如 Assets/Gym/Models/Imported/Watch-run-7.onnx。</summary>
        public string model_asset;

        /// <summary>当初选的 .onnx 文件的完整路径。</summary>
        public string source_file;
    }
}
