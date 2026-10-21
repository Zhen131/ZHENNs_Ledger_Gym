using System;
using System.Collections.Generic;

namespace Gym.Core.Env
{
    /// <summary>
    /// 一个动作形状：几个连续数，加上每个离散分支各有几档。模型的形状在打评估包时从模型里读出来（editor 里读，
    /// 写进 build-info.json）；一种动作模式要的形状由 <see cref="ActionCodec.ContinuousSizeFor"/>、
    /// <see cref="ActionCodec.BranchSizesFor"/> 给出（<see cref="For"/>），这里不另写一份。比较不碰模型，是纯函数。
    /// </summary>
    public sealed class ActionShape
    {
        readonly int[] branches;

        public ActionShape(int continuous, IReadOnlyList<int> branches)
        {
            if (continuous < 0) throw new ArgumentOutOfRangeException(nameof(continuous), continuous, "Must be >= 0.");
            Continuous = continuous;
            this.branches = new int[branches?.Count ?? 0];
            for (int i = 0; i < this.branches.Length; i++) this.branches[i] = branches[i];
        }

        /// <summary>连续数的个数。</summary>
        public int Continuous { get; }
        /// <summary>每个离散分支的档数。</summary>
        public IReadOnlyList<int> Branches => branches;

        /// <summary>动作模式 <paramref name="mode"/>（旋钮模式有 <paramref name="levelCount"/> 档）要的形状。</summary>
        public static ActionShape For(ActionMode mode, int levelCount) =>
            new ActionShape(ActionCodec.ContinuousSizeFor(mode), ActionCodec.BranchSizesFor(mode, levelCount));

        /// <summary>连续数个数相同、分支个数和每个分支的档数都相同。</summary>
        public bool SameAs(ActionShape other)
        {
            if (other == null || other.Continuous != Continuous || other.branches.Length != branches.Length) return false;
            for (int i = 0; i < branches.Length; i++)
                if (other.branches[i] != branches[i]) return false;
            return true;
        }

        /// <summary>写成「1 continuous + [3]」这样（报错和日志里用）。</summary>
        public override string ToString() => $"{Continuous} continuous + [{string.Join(", ", branches)}]";

        /// <summary>
        /// 模型的动作形状 <paramref name="model"/> 和动作模式 <paramref name="mode"/>（旋钮模式有 <paramref name="levelCount"/> 档）
        /// 对得上时返回 null，对不上时返回写明两边形状的报错（英文）。
        /// </summary>
        public static string ModelActionProblem(ActionShape model, ActionMode mode, int levelCount)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            ActionShape expected = For(mode, levelCount);
            if (model.SameAs(expected)) return null;
            string what = mode == ActionMode.Dial ? $"the action mode {mode.Name()} with {levelCount} levels" : $"the action mode {mode.Name()}";
            return $"the model's actions are {model} but {what} needs {expected}; " +
                   $"use the action mode the model was trained with, or a model trained in {mode.Name()} mode";
        }
    }
}
