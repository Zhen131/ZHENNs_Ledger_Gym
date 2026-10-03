namespace Gym.Runtime.Evaluation
{
    /// <summary>How the evaluation records write the paths they mention.</summary>
    public static class RecordPaths
    {
        /// <summary>The last part of a path, splitting on both / and \ whatever machine this runs on.</summary>
        public static string FileNameOnly(string path) =>
            string.IsNullOrEmpty(path) ? "" : path.Substring(path.LastIndexOfAny(new[] { '/', '\\' }) + 1);

        /// <summary>
        /// A path as committed evaluation records keep it (05D M-2): a relative path as written,
        /// with /; an absolute one (/…, \…, ~…, C:…) only as its file name, so no machine's
        /// folders or user name end up in the repository.
        /// </summary>
        public static string PathForRecords(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            bool absolute = path[0] == '/' || path[0] == '\\' || path[0] == '~' || (path.Length > 1 && path[1] == ':');
            return absolute ? FileNameOnly(path) : path.Replace('\\', '/');
        }
    }
}
