using System;
using System.Collections.Generic;
using Gym.Core.Env;
using Unity.InferenceEngine;
using UnityEditor;

namespace Gym.Editor
{
    /// <summary>
    /// 从一个导入过的模型读出它的动作形状（几个连续数、每个离散分支几档），只在 editor 里用（运行时程序集不引用推理引擎）。
    /// 照 ML-Agents 的 SentisModelInfo 的做法自己写一份（那个类是 internal）：形状不在模型的输入里，而在两个输出
    /// continuous_action_output_shape、discrete_action_output_shape 里；喂全 0 的输入、用 CPU Worker 跑一次再读。
    /// mlagents 1.1.0 导出时连续数个数为 0 就不导出连续那组输出，所以缺 continuous_action_output_shape 算 0 个连续，
    /// 缺 discrete_action_output_shape 算 0 个分支；两样都缺就不是 ML-Agents 的动作模型，抛异常（打评估包就失败）。
    /// </summary>
    public static class ModelActions
    {
        public const string ContinuousShapeOutput = "continuous_action_output_shape";
        public const string DiscreteShapeOutput = "discrete_action_output_shape";

        /// <summary>
        /// 按 asset 路径（Assets/…/x.onnx）读；不是模型时抛异常。名字和下面那个不同：调用的程序集（例如测试）不用引用推理引擎。
        /// </summary>
        public static ActionShape ActionShapeAt(string modelAssetPath)
        {
            var asset = AssetDatabase.LoadAssetAtPath<ModelAsset>(modelAssetPath)
                ?? throw new InvalidOperationException($"{modelAssetPath} is not an imported model");
            return ActionShapeOf(asset);
        }

        /// <summary>模型的动作形状；两个形状输出都没有、或者读出来的数不是非负整数时抛异常。</summary>
        public static ActionShape ActionShapeOf(ModelAsset asset)
        {
            if (asset == null) throw new ArgumentNullException(nameof(asset));
            Model model = ModelLoader.Load(asset);
            bool hasContinuous = HasOutput(model, ContinuousShapeOutput);
            bool hasDiscrete = HasOutput(model, DiscreteShapeOutput);
            if (!hasContinuous && !hasDiscrete)
                throw new InvalidOperationException(
                    $"model {asset.name} has neither {ContinuousShapeOutput} nor {DiscreteShapeOutput}, so its action shape cannot be read; " +
                    $"its outputs are {string.Join(", ", model.outputs.ConvertAll(o => o.name))}");

            var inputs = new List<Tensor>();
            try
            {
                using (var worker = new Worker(model, DeviceType.CPU))
                {
                    foreach (Model.Input input in model.inputs)
                    {
                        Tensor zeros = ZeroInput(asset.name, input);
                        inputs.Add(zeros);
                        worker.SetInput(input.name, zeros);
                    }
                    worker.Schedule();
                    int continuous = 0;
                    if (hasContinuous)
                    {
                        float[] values = Read(worker, ContinuousShapeOutput);
                        if (values.Length != 1)
                            throw new InvalidOperationException($"model {asset.name}: {ContinuousShapeOutput} has {values.Length} values, expected 1");
                        continuous = WholeNumber(asset.name, ContinuousShapeOutput, values[0], 0);
                    }
                    var branches = new List<int>();
                    if (hasDiscrete)
                        foreach (float v in Read(worker, DiscreteShapeOutput))
                            branches.Add(WholeNumber(asset.name, DiscreteShapeOutput, v, 1));
                    return new ActionShape(continuous, branches);
                }
            }
            finally
            {
                foreach (Tensor t in inputs) t.Dispose();
            }
        }

        static bool HasOutput(Model model, string name)
        {
            foreach (Model.Output output in model.outputs)
                if (output.name == name) return true;
            return false;
        }

        /// <summary>按输入的形状建一个全 0 的张量；不定的维（批大小）取 1。</summary>
        static Tensor ZeroInput(string modelName, Model.Input input)
        {
            int[] dims = input.shape.ToIntArray()
                ?? throw new InvalidOperationException($"model {modelName}: input {input.name} has no fixed rank ({input.shape})");
            for (int i = 0; i < dims.Length; i++)
                if (dims[i] <= 0) dims[i] = 1;
            var shape = new TensorShape(dims);
            return input.dataType == DataType.Int ? new Tensor<int>(shape) : (Tensor)new Tensor<float>(shape);
        }

        /// <summary>一个输出的全部数（float 或 int 都读成 float）。</summary>
        static float[] Read(Worker worker, string name)
        {
            Tensor output = worker.PeekOutput(name);
            switch (output)
            {
                case Tensor<float> f: return f.DownloadToArray();
                case Tensor<int> n: return Array.ConvertAll(n.DownloadToArray(), x => (float)x);
                default: throw new InvalidOperationException($"output {name} is a {output?.GetType().Name ?? "null"}, expected a float or int tensor");
            }
        }

        static int WholeNumber(string modelName, string output, float value, int minimum)
        {
            int n = (int)Math.Round(value);
            if (Math.Abs(value - n) > 1e-6 || n < minimum)
                throw new InvalidOperationException($"model {modelName}: {output} holds {value}, expected a whole number >= {minimum}");
            return n;
        }
    }
}
