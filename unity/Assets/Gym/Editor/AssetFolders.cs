using System.IO;
using UnityEditor;

namespace Gym.Editor
{
    /// <summary>按打包工具和 scene 工具需要的方式建 asset 文件夹。</summary>
    public static class AssetFolders
    {
        /// <summary>通过 AssetDatabase 建这个文件夹和缺少的上级文件夹，这样每个文件夹都有自己的 .meta。</summary>
        public static void Ensure(string path)
        {
            path = path.Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            Ensure(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
