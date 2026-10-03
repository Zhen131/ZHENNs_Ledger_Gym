using System.IO;
using UnityEditor;

namespace Gym.Editor
{
    /// <summary>Creates asset folders the way the build and scene tools need them.</summary>
    public static class AssetFolders
    {
        /// <summary>Creates the folder and any missing parents through the AssetDatabase, so each one gets its .meta.</summary>
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
