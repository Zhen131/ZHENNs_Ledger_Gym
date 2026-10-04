using UnityEngine;

namespace Gym.Runtime.Play
{
    /// <summary>头像框里那张图：从贴图文件夹读到的图，或者程序画的占位图。</summary>
    public sealed class AvatarPicture
    {
        AvatarPicture(Texture2D texture, string sourceFile)
        {
            Texture = texture;
            SourceFile = sourceFile;
        }

        public Texture2D Texture { get; }

        /// <summary>图是从哪个文件读的；占位图为 null。</summary>
        public string SourceFile { get; }

        public bool IsPlaceholder => SourceFile == null;
        public int Width => Texture.width;
        public int Height => Texture.height;

        public static AvatarPicture FromFile(Texture2D texture, string sourceFile) => new AvatarPicture(texture, sourceFile);

        public static AvatarPicture Placeholder() => new AvatarPicture(AvatarPlaceholder.Create(), null);

        /// <summary>按原比例缩进一个边长为 <paramref name="box"/> 的正方形里：长的那条边正好等于 box，不拉伸。</summary>
        public Vector2 FitInto(float box)
        {
            float scale = box / Mathf.Max(Width, Height);
            return new Vector2(Width * scale, Height * scale);
        }
    }
}
