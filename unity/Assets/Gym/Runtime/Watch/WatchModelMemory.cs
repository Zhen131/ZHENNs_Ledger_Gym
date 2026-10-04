using System;
using System.IO;
using UnityEngine;

namespace Gym.Runtime.Watch
{
    /// <summary>
    /// 观战记住上一个模型的那份记录，存成一个小 JSON 文件。默认放在这个 Unity 工程自己的 UserSettings/ 下：
    /// Git 忽略它，也只属于这个工程。不用整台电脑共用的 EditorPrefs，否则在另一个工作目录里跑测试，会改掉
    /// 你自己那个工程记住的模型。测试和截图工具把 <see cref="Location"/> 改到临时文件。
    /// </summary>
    public static class WatchModelMemory
    {
        public const string FileName = "watch-model.json";

        static string location;

        /// <summary>&lt;Unity 工程&gt;/UserSettings/Gym/watch-model.json。</summary>
        public static string DefaultLocation =>
            Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "UserSettings", "Gym", FileName);

        /// <summary>记录文件放在哪；设成 null 回到 <see cref="DefaultLocation"/>。</summary>
        public static string Location
        {
            get => location ?? DefaultLocation;
            set => location = value;
        }

        /// <summary>读记录；没有记录、或者文件读不懂时返回 null（读不懂另打一条英文警告）。</summary>
        public static WatchModelRecord Read()
        {
            string path = Location;
            if (!File.Exists(path)) return null;
            try
            {
                var record = JsonUtility.FromJson<WatchModelRecord>(File.ReadAllText(path));
                if (record != null && !string.IsNullOrEmpty(record.model_asset)) return record;
            }
            catch (Exception e) when (e is IOException || e is ArgumentException || e is UnauthorizedAccessException)
            {
                Debug.LogWarning($"[Gym] could not read the remembered watch model {path}: {e.Message}");
                return null;
            }
            Debug.LogWarning($"[Gym] the remembered watch model {path} names no model; choose one again");
            return null;
        }

        public static void Write(WatchModelRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            string path = Location;
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            File.WriteAllText(path, JsonUtility.ToJson(record, true));
        }
    }
}
