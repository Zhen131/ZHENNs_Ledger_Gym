using UnityEngine;

namespace Gym.Runtime.Play
{
    /// <summary>
    /// 没有头像图时的占位图：深色底上一个灰色剪影（圆头加肩膀），一眼看得出是占位。程序画的，
    /// 仓库里不放美术文件。
    /// </summary>
    public static class AvatarPlaceholder
    {
        public const int Size = 64;
        public const string TextureName = "Avatar placeholder";

        const float HeadCenterY = 40f;
        const float HeadRadius = 12f;
        const float ShoulderRadiusX = 23f;
        const float ShoulderRadiusY = 21f;
        const float ShoulderCenterY = 1f;

        public static Texture2D Create()
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                name = TextureName,
                hideFlags = HideFlags.DontSave,
                wrapMode = TextureWrapMode.Clamp,
            };
            Color32 figure = PlayPalette.AvatarPlaceholderFigure;
            Color32 background = PlayPalette.AvatarPlaceholderBackground;
            var pixels = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
                pixels[y * Size + x] = IsFigure(x + 0.5f, y + 0.5f) ? figure : background;
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        /// <summary>这个像素（中心坐标，y 向上）在剪影里没有：头是一个圆，肩膀是贴着底边的半个椭圆。</summary>
        static bool IsFigure(float x, float y)
        {
            float middle = Size / 2f;
            float hx = x - middle, hy = y - HeadCenterY;
            if (hx * hx + hy * hy <= HeadRadius * HeadRadius) return true;
            float sx = (x - middle) / ShoulderRadiusX, sy = (y - ShoulderCenterY) / ShoulderRadiusY;
            return sx * sx + sy * sy <= 1f;
        }
    }
}
