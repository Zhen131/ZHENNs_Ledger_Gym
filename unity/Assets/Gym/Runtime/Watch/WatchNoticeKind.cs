namespace Gym.Runtime.Watch
{
    /// <summary>观战画面上那段提示说的是哪件事。</summary>
    public enum WatchNoticeKind
    {
        /// <summary>还没选模型：告诉人去点哪个菜单。</summary>
        NoModel,
        /// <summary>这一段走完了：按 R 从头再看。</summary>
        EndOfSegment,
    }
}
