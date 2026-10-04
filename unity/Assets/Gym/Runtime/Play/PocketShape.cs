using UnityEngine;

namespace Gym.Runtime.Play
{
    /// <summary>程序画的口袋：圆底的袋身，上面一道深色的袋口，袋口下面一排缝线。不用美术文件。</summary>
    public static class PocketShape
    {
        const int BottomSegments = 20;
        const float RimHeight = 0.14f;
        const float RimOverhang = 0.06f;
        const float StitchLength = 0.10f;
        const float StitchSpacing = 0.18f;
        const float StitchThickness = 0.024f;

        public static void Add(ColoredMeshBuilder shapes, Rect area)
        {
            float roundHeight = area.height * 0.25f;
            float bodyBottom = area.yMin + roundHeight;
            float rimBottom = area.yMax - RimHeight;
            shapes.AddQuad(area.xMin, bodyBottom, area.xMax, rimBottom, PlayPalette.Pocket);
            AddRoundBottom(shapes, new Vector3(area.center.x, bodyBottom), area.width / 2, roundHeight);
            shapes.AddQuad(area.xMin - RimOverhang, rimBottom, area.xMax + RimOverhang, area.yMax, PlayPalette.PocketDark);

            float y = rimBottom - 0.10f;
            for (float x = area.xMin + 0.12f; x + StitchLength <= area.xMax - 0.12f; x += StitchSpacing)
                shapes.AddQuad(x, y - StitchThickness / 2, x + StitchLength, y + StitchThickness / 2, PlayPalette.PocketStitch);
        }

        /// <summary>袋身下面的半个椭圆。</summary>
        static void AddRoundBottom(ColoredMeshBuilder shapes, Vector3 center, float radiusX, float radiusY)
        {
            for (int i = 0; i < BottomSegments; i++)
            {
                float from = Mathf.PI + Mathf.PI * i / BottomSegments;
                float to = Mathf.PI + Mathf.PI * (i + 1) / BottomSegments;
                shapes.AddTriangle(center,
                    new Vector3(center.x + radiusX * Mathf.Cos(from), center.y + radiusY * Mathf.Sin(from)),
                    new Vector3(center.x + radiusX * Mathf.Cos(to), center.y + radiusY * Mathf.Sin(to)),
                    PlayPalette.Pocket);
            }
        }
    }
}
