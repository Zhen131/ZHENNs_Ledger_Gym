using System.Collections.Generic;
using UnityEngine;

namespace Gym.Runtime.Play
{
    /// <summary>攒一批按顶点着色的方块和三角，再一次性写进一个 mesh。K 线图、面板底色、口袋都用它。</summary>
    public sealed class ColoredMeshBuilder
    {
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<Color> colors = new List<Color>();
        readonly List<int> triangles = new List<int>();

        public void Clear()
        {
            vertices.Clear();
            colors.Clear();
            triangles.Clear();
        }

        public void AddQuad(float x0, float y0, float x1, float y1, Color color)
        {
            int start = vertices.Count;
            vertices.Add(new Vector3(x0, y0));
            vertices.Add(new Vector3(x0, y1));
            vertices.Add(new Vector3(x1, y1));
            vertices.Add(new Vector3(x1, y0));
            for (int k = 0; k < 4; k++) colors.Add(color);
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
            triangles.Add(start);
            triangles.Add(start + 2);
            triangles.Add(start + 3);
        }

        public void AddRect(Rect area, Color color) => AddQuad(area.xMin, area.yMin, area.xMax, area.yMax, color);

        /// <summary>只画边框：四条宽 <paramref name="thickness"/> 的边，向内画。</summary>
        public void AddFrame(Rect area, float thickness, Color color)
        {
            AddQuad(area.xMin, area.yMin, area.xMax, area.yMin + thickness, color);
            AddQuad(area.xMin, area.yMax - thickness, area.xMax, area.yMax, color);
            AddQuad(area.xMin, area.yMin + thickness, area.xMin + thickness, area.yMax - thickness, color);
            AddQuad(area.xMax - thickness, area.yMin + thickness, area.xMax, area.yMax - thickness, color);
        }

        public void AddTriangle(Vector3 corner1, Vector3 corner2, Vector3 corner3, Color color)
        {
            int start = vertices.Count;
            vertices.Add(corner1);
            vertices.Add(corner2);
            vertices.Add(corner3);
            for (int k = 0; k < 3; k++) colors.Add(color);
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
        }

        public void Upload(Mesh mesh)
        {
            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
        }
    }
}
