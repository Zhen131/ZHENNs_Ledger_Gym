using UnityEngine;

namespace Gym.Runtime.Play
{
    /// <summary>
    /// 一层按顶点着色的平面图形（面板底色、按钮框、口袋），按世界坐标画。挂在一个运行时才建的子物体上，
    /// 不存进 scene；材质也在运行时建，所以 scene 和材质文件都不用多一样东西。
    /// </summary>
    public sealed class ShapeLayer
    {
        /// <summary>和 K 线图同一个 shader：只按顶点颜色上色，带透明度。</summary>
        const string ShaderName = "Sprites/Default";

        static Material material;

        readonly Mesh mesh;

        public ShapeLayer(Transform parent, string name, float depth, int sortingOrder)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.DontSaveInEditor };
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(new Vector3(0, 0, depth), Quaternion.identity);
            mesh = new Mesh { name = name };
            mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = SharedMaterial;
            renderer.sortingOrder = sortingOrder;
        }
        public ColoredMeshBuilder Shapes { get; } = new ColoredMeshBuilder();

        /// <summary>清空，准备重画。</summary>
        public void Begin() => Shapes.Clear();

        /// <summary>把这次画的东西写进 mesh。</summary>
        public void End() => Shapes.Upload(mesh);

        static Material SharedMaterial
        {
            get
            {
                if (material == null)
                    material = new Material(Shader.Find(ShaderName)) { name = "Play shapes", hideFlags = HideFlags.DontSave };
                return material;
            }
        }
    }
}
