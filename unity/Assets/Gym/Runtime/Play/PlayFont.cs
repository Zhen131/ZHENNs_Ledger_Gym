using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gym.Runtime.Play
{
    /// <summary>
    /// Play scene 画字用的字体：按名字去找系统里装好的中文字体，仓库里不放字体文件（版权和体积）。
    /// 字体只在运行时（或截图工具调用时）造出来，不存进 scene：存进去的话下次打开就是丢失的引用。
    /// </summary>
    public static class PlayFont
    {
        /// <summary>
        /// 依次尝试的字体 family 名（系统里的名字等于它，或者以它加一个空格开头都算）。前五个是 macOS 自带的
        /// （苹方装在系统的另一个目录里，Unity 不一定找得到；STHeiti 那个文件在 Unity 里叫 Heiti SC），
        /// 后面是 Windows 和 Linux 上常见的。
        /// </summary>
        public static readonly string[] PreferredNames =
        {
            "PingFang SC", "Hiragino Sans GB", "STHeiti", "Heiti SC", "Songti SC", "Arial Unicode MS",
            "Microsoft YaHei", "SimHei", "Noto Sans CJK SC",
        };

        /// <summary>字形按多大的像素栅格化。画面上的字号由 <see cref="WorldText"/> 另外缩放。</summary>
        public const int RasterSize = 64;

        const string BuiltinFontName = "LegacyRuntime.ttf";

        static Font font;

        /// <summary>实际用上的字体名；还没找过时为 null。</summary>
        public static string ChosenName { get; private set; }

        public static Font Get()
        {
            if (font != null) return font;
            string installed = FindInstalled(PreferredNames, Font.GetOSInstalledFontNames());
            if (installed != null)
            {
                font = Font.CreateDynamicFontFromOSFont(installed, RasterSize);
                ChosenName = installed;
                Debug.Log($"[Gym] Play scene text uses the system font '{installed}'");
            }
            else
            {
                font = Resources.GetBuiltinResource<Font>(BuiltinFontName);
                ChosenName = font.name;
                Debug.LogWarning($"[Gym] none of the fonts {string.Join(", ", PreferredNames)} is installed; " +
                                 "the Play scene falls back to the default font and Chinese text may show as boxes");
            }
            font.hideFlags = HideFlags.DontSave;
            return font;
        }

        /// <summary>
        /// <paramref name="preferred"/> 里第一个装了的字体在系统里的完整名字；一个都没有时为 null。
        /// 例如想要 "STHeiti"，系统里叫 "STHeiti Medium"，也算找到。
        /// </summary>
        public static string FindInstalled(IReadOnlyList<string> preferred, IReadOnlyList<string> installed)
        {
            if (installed == null) return null;
            foreach (string want in preferred)
            {
                foreach (string name in installed)
                {
                    if (string.Equals(name, want, StringComparison.OrdinalIgnoreCase)) return name;
                }
                foreach (string name in installed)
                {
                    if (name != null && name.StartsWith(want + " ", StringComparison.OrdinalIgnoreCase)) return name;
                }
            }
            return null;
        }
    }
}
