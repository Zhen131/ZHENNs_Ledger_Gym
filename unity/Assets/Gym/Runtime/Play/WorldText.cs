using UnityEngine;

namespace Gym.Runtime.Play
{
    /// <summary>
    /// 用 TextMesh 在世界坐标里画字。它和 K 线一样由那台正交相机画，所以命令行截图截得到（IMGUI 截不到），
    /// 也不用另加相机或界面包。建出来的物体不存进 scene。
    /// </summary>
    public static class WorldText
    {
        /// <summary>字比图形离相机近，排序也靠后，保证压在面板底色和 K 线上面。</summary>
        public const float Depth = -2f;
        public const int SortingOrder = 20;

        /// <summary>TextMesh 的 characterSize 为 1 时，一个栅格像素是 0.1 个世界单位。</summary>
        const float UnitsPerRasterPixel = 0.1f;

        /// <param name="size">字号，世界单位：大约是一个汉字的高度。</param>
        public static TextMesh Create(Transform parent, string name, Vector2 position, float size, TextAnchor anchor, Color color)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.DontSaveInEditor };
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(new Vector3(position.x, position.y, Depth), Quaternion.identity);
            var text = go.AddComponent<TextMesh>();
            Font font = PlayFont.Get();
            text.font = font;
            text.fontSize = PlayFont.RasterSize;
            text.characterSize = size / (PlayFont.RasterSize * UnitsPerRasterPixel);
            text.anchor = anchor;
            text.alignment = AlignmentFor(anchor);
            text.richText = false;
            text.color = color;
            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            if (renderer == null) renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = font.material;
            renderer.sortingOrder = SortingOrder;
            return text;
        }

        public static void Move(TextMesh text, Vector2 position) =>
            text.transform.position = new Vector3(position.x, position.y, Depth);

        static TextAlignment AlignmentFor(TextAnchor anchor)
        {
            switch (anchor)
            {
                case TextAnchor.UpperCenter:
                case TextAnchor.MiddleCenter:
                case TextAnchor.LowerCenter:
                    return TextAlignment.Center;
                case TextAnchor.UpperRight:
                case TextAnchor.MiddleRight:
                case TextAnchor.LowerRight:
                    return TextAlignment.Right;
                default:
                    return TextAlignment.Left;
            }
        }
    }
}
