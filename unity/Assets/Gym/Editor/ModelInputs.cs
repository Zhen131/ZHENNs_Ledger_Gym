using System;
using System.Collections.Generic;
using Unity.InferenceEngine;
using UnityEditor;

namespace Gym.Editor
{
    /// <summary>
    /// 用推理引擎读一个导入过的模型的向量观测输入维数（ML-Agents 导出的模型里叫 obs_0，很老的叫 vector_observation）。
    /// 只在 editor 里用：运行时程序集不引用推理引擎，所以评估包靠打包时写进 build-info.json 的这个数，观战靠
    /// <see cref="WatchModelAttacher"/> 在 editor 里读。
    /// </summary>
    public static class ModelInputs
    {
        public const string ObservationInput = "obs_0";
        public const string LegacyObservationInput = "vector_observation";

        /// <summary>
        /// 按 asset 路径（Assets/…/x.onnx）读；不是模型时抛异常。名字和下面那个不同：调用的程序集（例如测试）不用引用推理引擎。
        /// </summary>
        public static int VectorObservationSizeAt(string modelAssetPath)
        {
            var asset = AssetDatabase.LoadAssetAtPath<ModelAsset>(modelAssetPath)
                ?? throw new InvalidOperationException($"{modelAssetPath} is not an imported model");
            return VectorObservationSize(asset);
        }

        /// <summary>
        /// 模型唯一的那个向量观测输入的最后一维。没有这样的输入、有不止一个、或者那一维不是定值时抛异常
        /// （这个 Agent 只有一个向量传感器）。
        /// </summary>
        public static int VectorObservationSize(ModelAsset asset)
        {
            if (asset == null) throw new ArgumentNullException(nameof(asset));
            Model model = ModelLoader.Load(asset);
            var found = new List<Model.Input>();
            foreach (Model.Input input in model.inputs)
                if (input.name == ObservationInput || input.name == LegacyObservationInput)
                    found.Add(input);
            if (found.Count != 1)
                throw new InvalidOperationException(
                    $"model {asset.name} has {found.Count} vector observation inputs ({ObservationInput} or {LegacyObservationInput}), expected 1; " +
                    $"its inputs are {string.Join(", ", model.inputs.ConvertAll(i => i.name))}");
            DynamicTensorShape shape = found[0].shape;
            int size = shape.isRankDynamic || shape.rank < 1 ? -1 : shape.Get(-1);
            if (size <= 0)
                throw new InvalidOperationException($"model {asset.name}: the size of input {found[0].name} is not fixed ({shape})");
            return size;
        }
    }
}
