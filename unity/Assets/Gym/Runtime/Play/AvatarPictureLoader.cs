using System;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Gym.Runtime.Play
{
    /// <summary>
    /// 去贴图文件夹找头像图：依次试 <see cref="FileNames"/>，用第一个能解码的。一个都没有就用占位图；
    /// 有文件但解码不了，打一条英文警告、跳过它，不抛异常。仓库里只放这个文件夹和它的说明文件，不放图。
    /// </summary>
    public static class AvatarPictureLoader
    {
        public static readonly string[] FileNames = { "avatar.png", "avatar.jpg", "avatar.jpeg" };

        /// <summary>贴图文件夹：StreamingAssets/Gym/avatar。</summary>
        public static string DefaultFolder => Path.Combine(Application.streamingAssetsPath, "Gym", "avatar");

        public static AvatarPicture Load(string folder)
        {
            foreach (string fileName in FileNames)
            {
                string path = Path.Combine(folder, fileName);
                if (!File.Exists(path)) continue;
                Texture2D texture = TryDecode(path);
                if (texture != null) return AvatarPicture.FromFile(texture, path);
                Debug.LogWarning($"[Gym] avatar picture '{path}' is not a PNG or JPEG image Unity can decode; skipping it");
            }
            return AvatarPicture.Placeholder();
        }

        /// <summary>读文件并解码成贴图；读不了或解码失败时返回 null。</summary>
        static Texture2D TryDecode(string path)
        {
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return null;
            }
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                name = "Avatar picture",
                hideFlags = HideFlags.DontSave,
                wrapMode = TextureWrapMode.Clamp,
            };
            if (bytes.Length > 0 && texture.LoadImage(bytes)) return texture;
            if (Application.isPlaying) Object.Destroy(texture);
            else Object.DestroyImmediate(texture);
            return null;
        }
    }
}
