namespace Gym.Runtime.Evaluation
{
    /// <summary>评估记录里提到路径时怎么写。</summary>
    public static class RecordPaths
    {
        /// <summary>路径的最后一段；不管在哪台机器上运行，/ 和 \ 都当分隔符。</summary>
        public static string FileNameOnly(string path) =>
            string.IsNullOrEmpty(path) ? "" : path.Substring(path.LastIndexOfAny(new[] { '/', '\\' }) + 1);

        /// <summary>
        /// 提交进仓库的评估记录怎样保存一个路径：相对路径照写，分隔符用 /；绝对路径（/…、\…、~…、C:…）
        /// 只留文件名，这样任何机器的文件夹和用户名都不会进仓库。
        /// </summary>
        public static string PathForRecords(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            bool absolute = path[0] == '/' || path[0] == '\\' || path[0] == '~' || (path.Length > 1 && path[1] == ':');
            return absolute ? FileNameOnly(path) : path.Replace('\\', '/');
        }
    }
}
